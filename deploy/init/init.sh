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
# new one is safe even on an existing database.
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
