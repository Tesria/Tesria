# PostgreSQL 18 with pgBackRest installed, for continuous WAL archiving and
# point-in-time recovery (PLAN §5). The base image is unchanged except for the
# pgBackRest binary; archiving is enabled via the `command:` in docker-compose,
# and the repository lives on a dedicated volume shared with the backup sidecar.
FROM postgres:18

RUN apt-get update \
    && apt-get install -y --no-install-recommends pgbackrest \
    && rm -rf /var/lib/apt/lists/* \
    # Directories used by pgBackRest for the async archive spool and logs, owned
    # by the postgres user so archive_command (which runs as postgres) can write.
    && mkdir -p /var/spool/pgbackrest /var/log/pgbackrest \
    && chown -R postgres:postgres /var/spool/pgbackrest /var/log/pgbackrest
