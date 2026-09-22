# The stand-in for an unconfigured path target

`docker-compose.yml` mounts this directory as `/mnt/nas` and `/mnt/removable`
when `OFFSITE_NAS_PATH` or `OFFSITE_REMOVABLE_PATH` is not set, because
Compose has no way to leave a mount out.

Nothing is ever written here. A path target is only used when it carries a
`.tesria-backup-target` sentinel file, and this directory deliberately has
none, so an instance with no network drive and no removable disk reports
both as absent and copies nothing.

See `deploy/backup/claim-target.sh` and dev-plan 9.2 step 3.
