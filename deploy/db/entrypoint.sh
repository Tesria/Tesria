#!/usr/bin/env bash
# The database container's entrypoint: PostgreSQL, plus the two things that
# have to happen inside this container and nowhere else.
#
# **1. pgBackRest's repo2 drop-in, before Postgres starts** (dev-plan 9.2
# step 1). archive_command runs in this container, so if the config that
# defines repo2 were written anywhere else there would be a window at every
# start where WAL went to the local repository only, and those segments would
# never reach the offsite one: a silent gap in point-in-time recovery, which
# is the failure 9.2 is most concerned with.
#
# **2. Point-in-time recovery, on request** (dev-plan 9.4). pgBackRest
# restores into the data directory with Postgres stopped, and nothing outside
# this container can stop Postgres: a sidecar would need the Docker socket,
# which no sidecar gets and none should. So this script supervises its own
# database. It starts the image's entrypoint as a child, forwards signals to
# it so `docker compose stop` behaves exactly as before, and watches one
# directory for a request.
#
# **The request is the authorization.** The directory lives on the `pgsocket`
# volume, which is shared by `db` and the `pgbackrest` sidecar and by nothing
# else. The web tier has no mount, no socket and no path to that file, so a
# restore can only be asked for by the sidecar that validated the job.
#
# **This script never stops the database from starting.** A backup target that
# is misconfigured is a problem; a database that will not boot is an outage.
# A failure is logged loudly and the missing WAL is caught by the archive-gap
# alert, rather than by refusing to run.

set -u

RESTORE_ROOT=/var/run/postgresql/tesria-restore
PGDATA_PATH="${PGDATA:-/var/lib/postgresql/18/docker}"
STANZA=main
# A request older than this belongs to a container that has already gone.
# Acting on one would restore a second time, days later, for a job nobody is
# waiting on, which is the worst thing this loop could do.
REQUEST_MAX_AGE=600
PG_PID=""
HANDLED=""

log() { echo "[db-entrypoint $(date -u +%FT%TZ)] $*"; }

# --- The secrets (dev-plan 25.1) --------------------------------------------

# The owner password, from the init service's file. The image's own
# entrypoint reads POSTGRES_PASSWORD_FILE, as root, and only on a new
# database; POSTGRES_PASSWORD set directly still works and takes precedence.
if [ -z "${POSTGRES_PASSWORD:-}" ] && [ -r /run/tesria/postgres-password/value ]; then
  export POSTGRES_PASSWORD_FILE=/run/tesria/postgres-password/value
fi

# The backup key, where archive_command will find it. Before Postgres
# starts, for the same reason as the repo2 drop-in below: a WAL segment
# pushed without it would fail.
# shellcheck source=../pgbackrest/cipher.sh
if [ -r /scripts/cipher.sh ]; then
  . /scripts/cipher.sh
  cipher_write_conf | while read -r line; do log "$line"; done
fi

# --- The repo2 drop-in ------------------------------------------------------

# shellcheck source=../pgbackrest/offsite.sh
if [ -r /scripts/offsite.sh ]; then
  . /scripts/offsite.sh
  if offsite_write_conf; then
    # A cloud repository that refuses this instance for good (a wrong
    # secret, a wrong passphrase, another cluster's repository) is left out
    # of WAL archiving until .env is fixed, so that the local backups carry
    # on (T8-008). The sidecar still has it, reports why on the cloud card,
    # and takes a new full cloud backup once it is back, since WAL from the
    # time it was left out never reached it. An outage is not a refusal: WAL
    # waits for the cloud, as before, behind archive-push-queue-max.
    state="$OFFSITE_STATE_DIR/.tesria-cloud-archiving"
    if [ -n "${OFFSITE_CLOUD_TYPE:-}" ] && reason="$(offsite_cloud_refusal "$PGDATA_PATH")"; then
      rm -f "$OFFSITE_CONF"
      log "WARNING: the cloud repository is left out of WAL archiving: $reason."
      log "WARNING: WAL goes to the local repository only until .env is fixed and this container restarts."
      printf 'excluded\n%s\n' "$reason" >"$state"
      [ -f "$OFFSITE_STATE_DIR/.tesria-cloud-gap" ] || date -u +%FT%TZ >"$OFFSITE_STATE_DIR/.tesria-cloud-gap"
    elif [ -n "${OFFSITE_CLOUD_TYPE:-}" ]; then
      printf 'included\n' >"$state"
    else
      rm -f "$state" "$OFFSITE_STATE_DIR/.tesria-cloud-gap"
    fi
  else
    log "WARNING: the offsite repository is configured but unusable;"
    log "WARNING: WAL will go to the local repository only until this is fixed."
  fi
else
  log "WARNING: /scripts/offsite.sh not mounted; no offsite repository."
fi

# --- Supervising Postgres ---------------------------------------------------

start_postgres() {
  # The image's own entrypoint, unchanged: it runs initdb on an empty volume,
  # skips it when PG_VERSION is already there (which is every start after a
  # restore), and execs postgres.
  /usr/local/bin/docker-entrypoint.sh "$@" &
  PG_PID=$!
  log "postgres started (pid ${PG_PID})"
}

# Fast shutdown, not smart. Postgres reads SIGTERM as a *smart* shutdown,
# which waits for every client to disconnect of its own accord: with the app
# holding a connection pool that means waiting for the ten-second timeout and
# then being killed, at every single `docker compose stop`. SIGINT is the fast
# shutdown, which rolls back open transactions and exits cleanly in about a
# second. Interposing here is what makes that choice available at all, and it
# is strictly better than what this container did before.
stop_postgres() {
  [ -n "$PG_PID" ] || return 0
  kill -INT "$PG_PID" 2>/dev/null
  local waited=0
  while kill -0 "$PG_PID" 2>/dev/null && [ "$waited" -lt 60 ]; do
    sleep 0.5
    waited=$(( waited + 1 ))
  done
  kill -0 "$PG_PID" 2>/dev/null && { log "postgres did not stop; forcing it"; kill -QUIT "$PG_PID" 2>/dev/null; sleep 2; }
  PG_PID=""
}

# `docker compose stop` and `docker compose down` arrive here. Forwarded and
# waited on, so the container stops as quickly and as cleanly as it did
# before this script existed.
on_signal() {
  log "shutting down"
  stop_postgres
  exit 0
}
trap on_signal TERM INT

# --- The restore request ----------------------------------------------------

status() { printf '%s %s\n' "$(date -u +%FT%TZ)" "$*" >>"$RESTORE_ROOT/status"; log "restore: $*"; }

# A request is a two-line file: the job id, then the pgBackRest arguments the
# sidecar decided on. This script does not parse a time or choose a backup
# set; it stops the database, runs what it was handed, and starts it again.
# Deciding what to restore is the sidecar's job, and it has already checked
# the bounds and taken the safety backup by the time this file appears.
handle_request() {
  local file="$RESTORE_ROOT/request" job args age

  age=$(( $(date +%s) - $(stat -c %Y "$file" 2>/dev/null || echo 0) ))
  job="$(sed -n 1p "$file" 2>/dev/null)"
  args="$(sed -n 2p "$file" 2>/dev/null)"
  rm -f "$file"

  if [ -z "$job" ]; then
    log "ignoring a restore request with no job id"
    return 0
  fi
  # Two guards against restoring twice, which is the one mistake here that
  # cannot be undone by hand: a request this container has already acted on,
  # and a request left behind by a container that has since restarted.
  case " $HANDLED " in *" $job "*) log "ignoring restore request $job: already handled"; return 0 ;; esac
  if [ "$age" -gt "$REQUEST_MAX_AGE" ]; then
    log "ignoring restore request $job: it is ${age}s old and belongs to an earlier run"
    return 0
  fi
  HANDLED="$HANDLED $job"

  : > "$RESTORE_ROOT/status"
  status "stopping the database"
  stop_postgres

  status "restoring the cluster"
  # shellcheck disable=SC2086  # the sidecar's arguments are deliberately a word list
  if gosu postgres pgbackrest --stanza="$STANZA" --pg1-path="$PGDATA_PATH" $args restore; then
    status "starting the database"
    start_postgres "$@"
    local waited=0
    until gosu postgres pg_isready -h /var/run/postgresql -q; do
      sleep 2
      waited=$(( waited + 2 ))
      if [ "$waited" -gt 1800 ]; then
        status "failed: the database did not come back within 30 minutes"
        printf 'failed\n' >"$RESTORE_ROOT/done"
        return 0
      fi
    done
    status "done"
    printf 'ok\n' >"$RESTORE_ROOT/done"
  else
    status "failed: pgbackrest could not restore the cluster"
    # Start it again regardless. A failed restore usually leaves the data
    # directory as it was, and a database that is up is what lets somebody
    # look at what went wrong.
    start_postgres "$@"
    printf 'failed\n' >"$RESTORE_ROOT/done"
  fi
}

# --- After a restore by hand: this machine's owner password ----------------

# deploy/pgbackrest/restore.sh leaves this file behind. A restored cluster
# keeps the role passwords it had when it was backed up, which after a
# restore onto a new machine are the old machine's, so nothing could sign in
# (T8-018). Once the database is open for writing, the owner's password is
# set to the one stored on this machine; migrate sets the app's, as it does
# on every start. Over the local socket, which the image trusts, so the old
# password is not needed. The database reports healthy only once this is
# done (the healthcheck looks for the file), so migrate does not try first.
RESET_MARKER=/var/lib/postgresql/.tesria-reset-owner-password
reset_owner_password() {
  [ -f "$RESET_MARKER" ] || return 0
  local user="${POSTGRES_USER:-postgres}" file=/run/tesria/postgres-password/value waited=0
  until [ "$(psql -h /var/run/postgresql -U "$user" -d postgres -XtAc 'SELECT NOT pg_is_in_recovery()' 2>/dev/null)" = t ]; do
    sleep 2
    waited=$(( waited + 2 ))
    # Recovery can take a long while on a large cluster; give up only after a
    # day, and say how to do it by hand.
    if [ "$waited" -gt 86400 ]; then
      log "ERROR: the restored database never opened for writing; the owner password was not reset"
      return 1
    fi
  done
  if [ -n "${POSTGRES_PASSWORD:-}" ] || [ -r "$file" ]; then
    # The password never goes on a command line: psql reads it from the file
    # (or the environment) itself.
    if PGPW_FILE="$file" psql -h /var/run/postgresql -U "$user" -d postgres -X -q -v ON_ERROR_STOP=1 \
         -v owner="$user" >/dev/null 2>&1 <<'SQL'
\set pw `if [ -n "$POSTGRES_PASSWORD" ]; then printf '%s' "$POSTGRES_PASSWORD"; else cat "$PGPW_FILE"; fi`
ALTER ROLE :"owner" PASSWORD :'pw';
SQL
    then
      log "restored cluster: the owner's password is now this machine's"
      rm -f "$RESET_MARKER"
    else
      log "ERROR: could not set the owner's password on the restored database; see docs/backup-recovery.md"
      return 1
    fi
  else
    log "WARNING: no owner password is stored here; the restored database keeps its own"
    rm -f "$RESET_MARKER"
  fi
}

# --- The loop ---------------------------------------------------------------

mkdir -p "$RESTORE_ROOT"
chmod 770 "$RESTORE_ROOT" 2>/dev/null || true
# A request written before this container started is from an earlier run.
rm -f "$RESTORE_ROOT/request" "$RESTORE_ROOT/done"

start_postgres "$@"
reset_owner_password &

while true; do
  # `wait -n` would be tidier, but the trap has to be able to interrupt this,
  # and a short sleep is what lets a signal arrive promptly.
  sleep 2 &
  wait $! 2>/dev/null

  if [ -n "$PG_PID" ] && ! kill -0 "$PG_PID" 2>/dev/null; then
    # Postgres exited on its own: a crash, or an administrator stopping it
    # from inside. Exiting lets compose restart the container, which is the
    # behavior this container had before it was supervised.
    wait "$PG_PID" 2>/dev/null
    log "postgres exited; stopping the container so it is restarted"
    exit 1
  fi

  [ -f "$RESTORE_ROOT/request" ] && handle_request "$@"
done
