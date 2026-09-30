#!/usr/bin/env bash
# Checks deploy/init/init.sh (dev-plan 25.1): that it generates each secret
# once and never again, takes .env over what it stored, refuses a placeholder
# on a new install, and refuses to invent a secret for a database or backups
# that already exist.
#
#   scripts/test-init.sh
#
# Runs itself inside the db image's base (postgres:18), where init runs in
# production, against scratch directories standing in for the volumes.
set -eu

if [ "${1:-}" != "--inside" ]; then
  here="$(cd "$(dirname "$0")/.." && pwd)"
  exec docker run --rm -v "$here/deploy/init:/init:ro" -v "$here/scripts/test-init.sh:/test.sh:ro" \
    postgres:18 bash /test.sh --inside
fi
# From here a non-zero exit is often the thing being checked.
set +e

FAILED=0
pass() { echo "ok   $*"; }
fail() { echo "FAIL $*"; FAILED=1; }
check() { if eval "$2"; then pass "$1"; else fail "$1"; fi; }

T=""
fresh() {
  T="$(mktemp -d)"
  mkdir -p "$T/secrets" "$T/pgdata" "$T/repo" "$T/install"
  chown 1000:1000 "$T/install"
}
run() {
  env -i PATH="$PATH" SECRETS_ROOT="$T/secrets" PGDATA_ROOT="$T/pgdata" REPO_ROOT="$T/repo" \
    INSTALL_ROOT="$T/install" "$@" bash /init/init.sh >"$T/out" 2>&1
}
# The same, with an init command (use-this-folder, check-folder) after the settings.
cmd() {
  local c="$1"; shift
  env -i PATH="$PATH" SECRETS_ROOT="$T/secrets" PGDATA_ROOT="$T/pgdata" REPO_ROOT="$T/repo" \
    INSTALL_ROOT="$T/install" "$@" bash /init/init.sh "$c" >"$T/out" 2>&1
}
val() { cat "$T/secrets/$1/value"; }
mode() { stat -c '%a %u:%g' "$1"; }

echo "== a new install, nothing set"
fresh
run; check "exits 0" '[ $? -eq 0 ]'
for s in postgres-password app-db-password backup-key collab-secret pdf-secret; do
  check "$s generated" '[ -s "$T/secrets/'$s'/value" ]'
  check "$s is 0440 root:10203" '[ "$(mode "$T/secrets/'$s'/value")" = "440 0:10203" ]'
done
check "the owner password is 48 hex characters" '[[ "$(val postgres-password)" =~ ^[0-9a-f]{48}$ ]]'
check "the backup key is 64 hex characters" '[[ "$(val backup-key)" =~ ^[0-9a-f]{64}$ ]]'
check "the secrets differ" '[ "$(val postgres-password)" != "$(val app-db-password)" ]'
check "pgpass is 0400 postgres" '[ "$(mode "$T/secrets/postgres-password/pgpass")" = "400 999:999" ]'
check "pgpass holds the owner password" '[ "$(cat "$T/secrets/postgres-password/pgpass")" = "*:*:*:*:$(val postgres-password)" ]'
check "backup-key.txt holds the key" 'grep -qx "$(val backup-key)" "$T/install/backup-key.txt"'
check "backup-key.txt belongs to the folder owner" '[ "$(mode "$T/install/backup-key.txt")" = "600 1000:1000" ]'
check "the status says generated" '[ "$(cat "$T/secrets/status/backup-key-source")" = generated ]'
check "no placeholder flag" '[ ! -e "$T/secrets/status/placeholders" ]'
check "no secret in the log" '! grep -q "$(val backup-key)" "$T/out" && ! grep -q "$(val postgres-password)" "$T/out"'

echo "== started again: nothing changes"
before="$(cat "$T"/secrets/*/value | sha256sum)"
keyfile="$(sha256sum <"$T/install/backup-key.txt")"
run; check "exits 0" '[ $? -eq 0 ]'
check "every secret is the same" '[ "$(cat "$T"/secrets/*/value | sha256sum)" = "$before" ]'
check "backup-key.txt is untouched" '[ "$(sha256sum <"$T/install/backup-key.txt")" = "$keyfile" ]'
check "no second key file" '[ "$(ls "$T/install" | wc -l)" -eq 1 ]'
check "the log says kept" 'grep -q "POSTGRES_PASSWORD: kept" "$T/out"'
touch "$T/pgdata/PG_VERSION"; mkdir -p "$T/repo/archive/main"
run; check "and again once the database and backups exist" '[ $? -eq 0 ] && [ "$(cat "$T"/secrets/*/value | sha256sum)" = "$before" ]'

echo "== show-backup-key"
key="$(val backup-key)"
env -i PATH="$PATH" SECRETS_ROOT="$T/secrets" bash /init/init.sh show-backup-key >"$T/shown"
check "prints the stored key" '[ "$(cat "$T/shown")" = "$key" ]'

echo "== a value in .env wins"
run APP_DB_PASSWORD=from-env-1 BACKUP_ENCRYPTION_KEY="$key"
check "exits 0" '[ $? -eq 0 ]'
check "the app password is the .env one" '[ "$(val app-db-password)" = from-env-1 ]'
check "the key source is now provided" '[ "$(cat "$T/secrets/status/backup-key-source")" = provided ]'
run
check "and is kept when the line is removed" '[ "$(val app-db-password)" = from-env-1 ]'

echo "== a placeholder on a new install is refused"
fresh
run POSTGRES_PASSWORD=change-me-to-a-long-random-string; code=$?
check "exits non-zero" '[ "$code" -ne 0 ]'
check "names the setting" 'grep -q "POSTGRES_PASSWORD in .env is still the example value" "$T/out"'
check "stores nothing" '[ ! -s "$T/secrets/postgres-password/value" ]'

echo "== a placeholder on an existing install starts, flagged"
fresh; touch "$T/pgdata/PG_VERSION"
run POSTGRES_PASSWORD=change-me-to-a-long-random-string BACKUP_ENCRYPTION_KEY=change-me-to-a-long-random-passphrase
check "exits 0" '[ $? -eq 0 ]'
check "flags both settings" '[ "$(cat "$T/secrets/status/placeholders")" = "$(printf "POSTGRES_PASSWORD\nBACKUP_ENCRYPTION_KEY")" ]'
check "warns in the log" 'grep -q "WARNING: POSTGRES_PASSWORD is still the example value" "$T/out"'
check "no backup-key.txt for a key from .env" '[ ! -e "$T/install/backup-key.txt" ]'
run POSTGRES_PASSWORD=real-one BACKUP_ENCRYPTION_KEY=real-key
check "the flag clears once they are changed" '[ ! -e "$T/secrets/status/placeholders" ]'

echo "== an existing database with no password known is refused"
fresh; touch "$T/pgdata/PG_VERSION"
cmd use-this-folder
run; code=$?
check "exits non-zero" '[ "$code" -ne 0 ]'
check "says which setting" 'grep -q "Set POSTGRES_PASSWORD in .env" "$T/out"'

echo "== existing backups with no key known are refused"
fresh; mkdir -p "$T/repo/backup/main"
run; code=$?
check "exits non-zero" '[ "$code" -ne 0 ]'
check "says which setting" 'grep -q "Set BACKUP_ENCRYPTION_KEY in .env" "$T/out"'
check "generates no key" '[ ! -s "$T/secrets/backup-key/value" ]'

echo "== a new install over an old backup-key.txt keeps the old one"
fresh; echo "the old key" >"$T/install/backup-key.txt"
run
check "exits 0" '[ $? -eq 0 ]'
check "the old file is kept" 'grep -qx "the old key" "$T"/install/backup-key.old-*.txt'
check "the new file holds the new key" 'grep -qx "$(val backup-key)" "$T/install/backup-key.txt"'

echo "== pgpass escapes : and \\"
fresh
run 'POSTGRES_PASSWORD=a:b\c'
check "escaped" '[ "$(cat "$T/secrets/postgres-password/pgpass")" = "*:*:*:*:a\\:b\\\\c" ]'

echo "== a drive path that double quotes in .env mangled is refused (WIN-005)"
fresh
run "OFFSITE_REMOVABLE_PATH=C:$(printf '\t')esria-qa-win\\usb"; code=$?
check "exits non-zero" '[ "$code" -ne 0 ]'
check "names the setting and the fix" 'grep -q "OFFSITE_REMOVABLE_PATH in .env has a tab or a line break" "$T/out" && grep -q "OFFSITE_REMOVABLE_PATH=C:/TesriaBackups" "$T/out"'
check "stores nothing" '[ ! -s "$T/secrets/postgres-password/value" ]'
fresh
run $'OFFSITE_NAS_PATH=E:\\Backups\new'; code=$?
check "a line break too" '[ "$code" -ne 0 ] && grep -q "OFFSITE_NAS_PATH in .env has a tab" "$T/out"'
fresh
run 'OFFSITE_REMOVABLE_PATH=C:\TesriaBackups' 'OFFSITE_NAS_PATH=D:/My Backups/Tesria'
check "a plain Windows path is fine" '[ $? -eq 0 ]'

echo "== the folder check: a new install"
fresh
cmd check-folder; code=$?
check "check-folder before the first start says nothing is there yet (R-005)" '[ "$code" -eq 0 ] && grep -q "has not started yet" "$T/out"'
run
check "the first start claims the folder" '[ -s "$T/install/.tesria-install" ] && [ -s "$T/secrets/status/install-id" ]'
cmd check-folder
check "check-folder then says it is this folder" 'grep -q "This folder is the one" "$T/out"'

echo "== an install last started before 0.8.2, from another folder (R-001)"
fresh; run; touch "$T/pgdata/PG_VERSION"
rm -f "$T/secrets/status/install-id" "$T/install/.tesria-install"
key="$(val backup-key)"; pw="$(val postgres-password)"
mv "$T/install/backup-key.txt" "$T/keyfile"
run; code=$?
check "a folder without its key file or password is refused" '[ "$code" -ne 0 ] && grep -q "cannot show it is its own" "$T/out" && grep -q "may now be stopped" "$T/out"'
check "and nothing is claimed" '[ ! -e "$T/install/.tesria-install" ] && [ ! -e "$T/secrets/status/install-id" ]'
cmd check-folder; code=$?
check "check-folder there says it cannot tell" '[ "$code" -eq 2 ]'
run BACKUP_ENCRYPTION_KEY=not-the-key; code=$?
check "a different backup key in .env is no proof" '[ "$code" -ne 0 ] && [ ! -e "$T/install/.tesria-install" ]'

echo "== the same install from its own folder, after the upgrade"
cp "$T/keyfile" "$T/install/backup-key.txt"
cmd check-folder; code=$?
check "check-folder recognizes its backup-key.txt" '[ "$code" -eq 0 ] && grep -q "almost certainly its own folder" "$T/out"'
run; code=$?
check "its backup-key.txt lets it claim the folder" '[ "$code" -eq 0 ] && [ -s "$T/install/.tesria-install" ]'
rm -f "$T/secrets/status/install-id" "$T/install/.tesria-install" "$T/install/backup-key.txt"
run "POSTGRES_PASSWORD=$pw"
check "so does its password in .env" '[ $? -eq 0 ] && [ -s "$T/install/.tesria-install" ]'
rm -f "$T/secrets/status/install-id" "$T/install/.tesria-install"
run "BACKUP_ENCRYPTION_KEY=$key"
check "and its backup key in .env" '[ $? -eq 0 ] && [ -s "$T/install/.tesria-install" ]'
rm -f "$T/secrets/status/install-id" "$T/install/.tesria-install"
cmd use-this-folder
check "use-this-folder claims it on purpose" '[ $? -eq 0 ] && [ -s "$T/install/.tesria-install" ]'
run
check "and it then starts" '[ $? -eq 0 ]'

[ "$FAILED" -eq 0 ] && echo "all passed" || { echo "some checks failed"; exit 1; }
