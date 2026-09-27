# shellcheck shell=bash
# The backup encryption key, as a pgBackRest config drop-in (dev-plan 25.1).
#
# Sourced by the db entrypoint and the pgbackrest sidecar's run.sh, which
# both run as root. Until 0.8.0 the key was an environment variable,
# PGBACKREST_REPO1_CIPHER_PASS, set by Compose from .env. It is now a file
# written by the init service, and a file in pgBackRest's include directory
# is what every pgBackRest command reads: archive_command, the sidecar's
# loop, and anything run later with `docker compose exec`, which would never
# see a variable this script exported.
#
# The environment variable still wins if something sets it: pgBackRest takes
# the environment over its config files.

CIPHER_KEY_FILE="${CIPHER_KEY_FILE:-/run/tesria/backup-key/value}"
CIPHER_CONF="${CIPHER_CONF:-/etc/pgbackrest/conf.d/tesria-cipher.conf}"

cipher_write_conf() {
  if [ ! -r "$CIPHER_KEY_FILE" ]; then
    echo "WARNING: no backup key at $CIPHER_KEY_FILE; pgBackRest cannot encrypt or read backups"
    return 1
  fi
  mkdir -p "$(dirname "$CIPHER_CONF")"
  # Readable by the postgres user, which runs every pgBackRest command.
  ( umask 027
    { echo "[global]"
      echo "repo1-cipher-type=aes-256-cbc"
      printf 'repo1-cipher-pass=%s\n' "$(cat "$CIPHER_KEY_FILE")"
    } >"$CIPHER_CONF.tmp" )
  chown root:postgres "$CIPHER_CONF.tmp"
  mv -f "$CIPHER_CONF.tmp" "$CIPHER_CONF"
  echo "backup key: written to pgBackRest's configuration"
}
