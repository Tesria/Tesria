# PostgreSQL 18 with pgBackRest installed, for continuous WAL archiving and
# point-in-time recovery (PLAN §5). The base image is unchanged except for the
# pgBackRest binary; archiving is enabled via the `command:` in docker-compose,
# and the repository lives on a dedicated volume shared with the backup sidecar.
FROM postgres:18

# Pinned, and the version matters (dev-plan 9.2). Before 2.59,
# archive-push-queue-max did not take effect while archive-push was erroring
# (pgbackrest#2629), which is exactly the case it exists for: an offsite
# repository that has gone away. On 2.58 a dead remote could fill pg_wal with
# the limit set and apparently working. The PGDG apt repository the base image
# already configures carries it.
ARG PGBACKREST_VERSION=2.59.1-1.pgdg13+1
RUN apt-get update \
    && apt-get install -y --no-install-recommends "pgbackrest=${PGBACKREST_VERSION}" \
    && rm -rf /var/lib/apt/lists/* \
    # Directories used by pgBackRest for the async archive spool and logs, owned
    # by the postgres user so archive_command (which runs as postgres) can write.
    && mkdir -p /var/spool/pgbackrest /var/log/pgbackrest \
    && chown -R postgres:postgres /var/spool/pgbackrest /var/log/pgbackrest

# Tesria's license and notice travel with the image (the review's LIC-01).
COPY LICENSE NOTICE /usr/share/doc/tesria/
