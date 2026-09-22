#!/usr/bin/env bash
# Wraps the postgres image's own entrypoint so that pgBackRest's repo2
# drop-in exists before archive_command can run (dev-plan 9.2 step 1).
#
# It has to happen here. archive_command runs inside this container, so if
# the config that defines repo2 is written anywhere else (the sidecar, a
# host step) there is a window at every start where Postgres archives WAL
# to repo1 only, and those segments never reach the offsite repository: a
# silent gap in point-in-time recovery, which is the failure 9.2 is most
# concerned with.
#
# **This script never stops the database from starting.** A backup target
# that is misconfigured is a problem; a database that will not boot is an
# outage. A failure is logged loudly here, and the missing WAL is caught by
# the archive-gap alert rather than by refusing to run.

set -u

# shellcheck source=../pgbackrest/offsite.sh
if [ -r /scripts/offsite.sh ]; then
  . /scripts/offsite.sh
  if offsite_write_conf; then
    :
  else
    echo "db-entrypoint: WARNING: the offsite repository is configured but unusable;" >&2
    echo "db-entrypoint: WAL will go to the local repository only until this is fixed." >&2
  fi
else
  echo "db-entrypoint: WARNING: /scripts/offsite.sh not mounted; no offsite repository." >&2
fi

exec /usr/local/bin/docker-entrypoint.sh "$@"
