# The `backup` sidecar: pg_dump for logical backups (PLAN §5, Layer 2) plus
# restic, which is what carries the uploads and the dumps off this machine
# (dev-plan 9.2 step 2).
#
# restic rather than a copy of the tarball, for three reasons that all
# matter here: it encrypts client-side always, which is what finally means
# the logical dumps do not leave in plaintext; it deduplicates, so the
# uploads volume can be backed up directly instead of shipping a freshly
# compressed archive whose bytes are new every time; and its own
# `forget --keep-last N --keep-within Dd` is the retention policy from the
# admin page, rather than a second rule that has to be kept in step.
FROM postgres:18

# Pinned like pgBackRest is, so an image rebuild cannot quietly change the
# tool that writes the backups.
ARG RESTIC_VERSION=0.18.0-1+b4
RUN apt-get update \
    && apt-get install -y --no-install-recommends "restic=${RESTIC_VERSION}" \
    && rm -rf /var/lib/apt/lists/*
