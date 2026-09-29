#!/usr/bin/env bash
# The `init` service (dev-plan 25.1): Tesria's secrets, made on first start.
#
# Every other Tesria service waits for this one to finish. For each secret it
# takes, in order:
#
#   1. the value set in .env, if there is one;
#   2. the value stored by an earlier run, if there is one;
#   3. a new random value.
#
# **A stored value is never replaced by a generated one.** Postgres keeps the
# owner password it was created with, and pgBackRest keeps the key its
# backups were encrypted with; a second, different value would lock the stack
# out of its own database and backups. So a value is generated only when the
# thing it protects does not exist yet, and when it does exist and no value
# is known, this refuses to start and says which setting to fill in.
#
# **Each secret lives on its own volume**, mounted only into the services that
# already had it (the matrix in dev-plan 25.1). The app never gets the owner
# password or the backup key. Files are 0440, root-owned, in group
# SECRETS_GID, which the non-root services join with `group_add`.
#
# **The backup key reaches the owner as a file**, backup-key.txt in the
# folder Tesria was installed in, written only when the key is generated.
# The app cannot read it, and cannot read the key.
#
# `docker compose run --rm init show-backup-key` prints the key.

set -eu
umask 077

SECRETS_ROOT="${SECRETS_ROOT:-/run/tesria}"
STATUS_DIR="${STATUS_DIR:-$SECRETS_ROOT/status}"
PGDATA_ROOT="${PGDATA_ROOT:-/pgdata}"
REPO_ROOT="${REPO_ROOT:-/pgbackrest}"
INSTALL_ROOT="${INSTALL_ROOT:-/install}"
SECRETS_GID="${SECRETS_GID:-10203}"
# The postgres user in the db and backup images, which libpq's password file
# must belong to: it ignores one that its group or anyone else can read.
POSTGRES_UID="${POSTGRES_UID:-999}"

log() { echo "[init] $*"; }
fail() {
  echo "[init] ERROR: $*" >&2
  echo "[init] Tesria has not started. Fix the setting above in .env and run: docker compose up -d" >&2
  exit 1
}

value_of() { local f="$SECRETS_ROOT/$1/value"; [ -r "$f" ] && cat "$f"; }

if [ "${1:-}" = "show-backup-key" ]; then
  key="${BACKUP_ENCRYPTION_KEY:-$(value_of backup-key || true)}"
  [ -n "$key" ] || { echo "No backup key is stored yet: start Tesria once with docker compose up -d." >&2; exit 1; }
  printf '%s\n' "$key"
  exit 0
fi

# --- Which folder this install belongs to (0.8.2, WIN-001) -------------------
#
# docker-compose.yml names its project `tesria`, and installs made before
# 0.8.2 keep that name so they keep their volumes (tesria_pgdata and the
# rest). Its cost: a second copy of Tesria unzipped into another folder is
# the same Compose project, and without this check it silently took over the
# first one's containers and data, so that a `docker compose down -v` in the
# test folder deleted the real wiki.
#
# So the install remembers its folder. The first start writes one line,
# "<project> <id>", to .tesria-install in the folder, and the same id to the
# project's status volume. A later start from a folder whose line is missing,
# or names another project or id, is refused before any secret or data is
# written.
# `use-this-folder` makes the folder it runs in the install's own, for an
# owner who moved or re-downloaded Tesria on purpose; `check-folder` only
# reports, for the uninstall steps.
PROJECT="${TESRIA_PROJECT:-tesria}"
MARKER="$INSTALL_ROOT/.tesria-install"
STORED_ID="$STATUS_DIR/install-id"

folder_id() {
  [ -r "$MARKER" ] || return 0
  # One line, "<project> <id>"; a Windows editor may have added a \r.
  tr -d '\r' <"$MARKER" | awk -v p="$PROJECT" '$1 == p { print $2; exit }'
}
stored_id() { [ -r "$STORED_ID" ] && tr -d ' \r\n' <"$STORED_ID"; }

claim_folder() {
  mkdir -p "$STATUS_DIR"
  chmod 0755 "$STATUS_DIR"
  printf '%s\n' "$1" >"$STATUS_DIR/.install-id.tmp"
  chmod 0444 "$STATUS_DIR/.install-id.tmp"
  mv -f "$STATUS_DIR/.install-id.tmp" "$STORED_ID"
  printf '%s %s\n' "$PROJECT" "$1" >"$MARKER"
  chmod 0644 "$MARKER"
  chown "$(stat -c '%u:%g' "$INSTALL_ROOT")" "$MARKER" 2>/dev/null || true
}

refuse_folder() {
  cat >&2 <<EOF
[init] ERROR: this folder is not the one Tesria "$PROJECT" was installed from.
[init]
[init] Docker Compose already has a Tesria named "$PROJECT", started from another
[init] folder. Its data has not been touched, but it may now be stopped: to
[init] bring it back, run docker compose up -d in its own folder.
[init]
[init] Do not run docker compose down -v here: it would delete that wiki.
[init]
[init] To run a second, separate Tesria from this folder, give it its own name,
[init] network and ports in this folder's .env, then run docker compose up -d:
[init]     COMPOSE_PROJECT_NAME=tesria2
[init]     TESRIA_SUBNET=10.204.0.0/24
[init]     TESRIA_HTTP_PORT=8080
[init]     TESRIA_HTTPS_PORT=8443
[init]
[init] If this folder is meant to replace the old one (you moved Tesria, or
[init] unzipped it again somewhere else), run this here, then docker compose up -d:
[init]     docker compose run --rm init use-this-folder
[init] See the docs page Uninstalling and moving.
EOF
  exit 1
}

if [ -d "$INSTALL_ROOT" ]; then
  stored="$(stored_id || true)"
  here="$(folder_id || true)"
  case "${1:-}" in
    use-this-folder)
      # A new id, so the folder it replaces no longer starts this install.
      claim_folder "$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')"
      log "this folder now runs the Tesria named \"$PROJECT\"; start it with: docker compose up -d"
      exit 0
      ;;
    check-folder)
      if [ -z "$stored" ]; then
        echo "Tesria \"$PROJECT\" has not started since 0.8.2, so it cannot tell which folder it belongs to. Run docker compose ls instead: its CONFIG FILES column names the folder it was last started from." >&2
        exit 2
      elif [ "$here" = "$stored" ]; then
        echo "This folder is the one Tesria \"$PROJECT\" was installed from."
        exit 0
      fi
      echo "This folder is NOT the one Tesria \"$PROJECT\" was installed from. Do not run docker compose down -v here: it would delete that wiki." >&2
      exit 1
      ;;
  esac
  if [ -z "$stored" ]; then
    # A new install, or the first start of one made before 0.8.2: this
    # folder is where it lives from now on.
    claim_folder "$(head -c 16 /dev/urandom | od -An -tx1 | tr -d ' \n')"
    log "remembered this folder as Tesria \"$PROJECT\"'s own"
  elif [ "$here" != "$stored" ]; then
    refuse_folder
  fi
fi

# The database exists once initdb has run. Checked for the version file two
# levels down, where postgres:18 puts its data directory (18/docker).
db_exists() { [ -n "$(find "$PGDATA_ROOT" -maxdepth 3 -name PG_VERSION -print -quit 2>/dev/null)" ]; }
# pgBackRest's repository exists once the stanza has been created.
repo_exists() { [ -d "$REPO_ROOT/archive/main" ] || [ -d "$REPO_ROOT/backup/main" ]; }
# The values .env.example shipped with until 0.8.0.
is_placeholder() { case "$1" in change-me*) return 0 ;; *) return 1 ;; esac; }
generate() { head -c "$1" /dev/urandom | od -An -tx1 | tr -d ' \n'; }

prepare_dir() {
  mkdir -p "$1"
  chown "0:$SECRETS_GID" "$1"
  chmod 0750 "$1"
}

write_value() {
  local dir="$SECRETS_ROOT/$1"
  printf '%s' "$2" >"$dir/.value.tmp"
  chown "0:$SECRETS_GID" "$dir/.value.tmp"
  chmod 0440 "$dir/.value.tmp"
  mv -f "$dir/.value.tmp" "$dir/value"
}

PLACEHOLDERS=""
# Sets SOURCE to provided, stored or generated.
#   $1 the secret's directory   $2 the .env setting   $3 its value
#   $4 a check that is true when the thing it protects already exists
#   $5 what to say when it does and no value is known   $6 bytes to generate
resolve() {
  local name="$1" setting="$2" value="$3" exists="$4" missing="$5" bytes="$6"
  prepare_dir "$SECRETS_ROOT/$name"
  if [ -n "$value" ]; then
    if is_placeholder "$value"; then
      db_exists || fail "$setting in .env is still the example value from .env.example. Delete that line (Tesria then makes its own) or set a long random value."
      PLACEHOLDERS="$PLACEHOLDERS $setting"
    fi
    [ "$(value_of "$name" || true)" = "$value" ] || write_value "$name" "$value"
    SOURCE=provided
  elif [ -s "$SECRETS_ROOT/$name/value" ]; then
    SOURCE=stored
  elif $exists; then
    fail "$missing"
  else
    write_value "$name" "$(generate "$bytes")"
    SOURCE=generated
  fi
  log "$setting: $(case "$SOURCE" in provided) echo "from .env" ;; stored) echo "kept" ;; generated) echo "generated" ;; esac)"
}

never() { return 1; }

resolve postgres-password POSTGRES_PASSWORD "${POSTGRES_PASSWORD:-}" db_exists \
  "The database already exists, but POSTGRES_PASSWORD is not set and no password is stored. Set POSTGRES_PASSWORD in .env to the password the database was created with." 24

# libpq's password file, for pg_dump and psql in the backup sidecars. They
# run commands through `docker compose exec`, which sees the container's
# environment and nothing a startup script exported, so the file is named in
# PGPASSFILE rather than read into PGPASSWORD. Backslash and colon are the
# two characters the format escapes.
owner_password="$(value_of postgres-password)"
escaped="$(printf '%s' "$owner_password" | sed -e 's/\\/\\\\/g' -e 's/:/\\:/g')"
printf '*:*:*:*:%s\n' "$escaped" >"$SECRETS_ROOT/postgres-password/.pgpass.tmp"
chown "$POSTGRES_UID:$POSTGRES_UID" "$SECRETS_ROOT/postgres-password/.pgpass.tmp"
chmod 0400 "$SECRETS_ROOT/postgres-password/.pgpass.tmp"
mv -f "$SECRETS_ROOT/postgres-password/.pgpass.tmp" "$SECRETS_ROOT/postgres-password/pgpass"
unset owner_password escaped

# The app role's password is set by migrate on every pass (ALTER ROLE), so a
# new one is safe even on an existing database. A running migrate rereads
# this file every few seconds and runs a pass when it changed, and the app
# and collab read it for each new connection, so nothing needs restarting.
resolve app-db-password APP_DB_PASSWORD "${APP_DB_PASSWORD:-}" never "" 24

resolve backup-key BACKUP_ENCRYPTION_KEY "${BACKUP_ENCRYPTION_KEY:-}" repo_exists \
  "Backups already exist, encrypted with a key that is not set. Set BACKUP_ENCRYPTION_KEY in .env to that key. It is in backup-key.txt if Tesria made it, or wherever you saved it." 32
key_source="$SOURCE"

resolve collab-secret COLLAB_SHARED_SECRET "${COLLAB_SHARED_SECRET:-}" never "" 32
resolve pdf-secret PDF_SHARED_SECRET "${PDF_SHARED_SECRET:-}" never "" 32

# --- What the app may know about this, and nothing secret ------------------

mkdir -p "$STATUS_DIR"
chmod 0755 "$STATUS_DIR"
status_file() { printf '%s\n' "$2" >"$STATUS_DIR/.$1.tmp"; chmod 0444 "$STATUS_DIR/.$1.tmp"; mv -f "$STATUS_DIR/.$1.tmp" "$STATUS_DIR/$1"; }

# "generated" until the key comes from .env, which means the owner has it.
case "$key_source" in
  generated) status_file backup-key-source generated ;;
  provided) status_file backup-key-source provided ;;
esac

if [ -n "$PLACEHOLDERS" ]; then
  status_file placeholders "$(printf '%s\n' $PLACEHOLDERS)"
  for s in $PLACEHOLDERS; do
    log "WARNING: $s is still the example value from .env.example, which anyone can read. See the docs page Security hardening, Changing a secret."
  done
else
  rm -f "$STATUS_DIR/placeholders"
fi

# --- The backup key, for the owner to save ----------------------------------

if [ "$key_source" = generated ]; then
  if [ -d "$INSTALL_ROOT" ]; then
    target="$INSTALL_ROOT/backup-key.txt"
    # An earlier install's key, left behind. Its backups may still be on a
    # network drive or in the cloud, so it is kept, never overwritten.
    if [ -e "$target" ]; then
      old="$INSTALL_ROOT/backup-key.old-$(date -u +%Y%m%dT%H%M%SZ).txt"
      mv "$target" "$old"
      log "kept the previous backup key as $(basename "$old")"
    fi
    {
      echo "Tesria backup key"
      echo "================="
      echo
      value_of backup-key
      echo
      echo
      echo "This key encrypts your Tesria backups. Without it, no backup can be"
      echo "restored, by anyone, including you."
      echo
      echo "Save it somewhere that is not this computer, such as a password manager."
      echo "Then tell Tesria you have: Administration, Backups, \"I saved it\"."
      echo
      echo "Once it is saved you may delete this file. While this computer is running,"
      echo "this command prints the key again:"
      echo
      echo "    docker compose run --rm init show-backup-key"
    } >"$target"
    chmod 0600 "$target"
    # Owned by whoever owns the folder, so they can open it without sudo.
    chown "$(stat -c '%u:%g' "$INSTALL_ROOT")" "$target" 2>/dev/null || true
    log "wrote the new backup key to backup-key.txt in the Tesria folder: save it somewhere safe"
  else
    log "the Tesria folder is not mounted; print the backup key with: docker compose run --rm init show-backup-key"
  fi
fi

log "done"
