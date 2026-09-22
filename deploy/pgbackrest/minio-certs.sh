#!/usr/bin/env bash
# Gives the test MinIO a self-signed certificate (dev-plan 9.2 step 1).
#
# pgBackRest always speaks TLS to an S3 endpoint and has no plain-HTTP
# option: against a plain-HTTP MinIO it fails with "TLS error ... wrong
# version number". repo2-storage-verify-tls=n turns off certificate
# *checking*, not TLS, so the server still has to present one.
#
# Test harness only. Never point a real instance at a certificate nobody
# verifies: the backup is leaving the building.
set -euo pipefail
cd "$(dirname "$0")/../.."

project="${COMPOSE_PROJECT_NAME:-$(basename "$PWD" | tr '[:upper:]' '[:lower:]')}"
volume="${project}_minio_certs"

echo "Generating a self-signed certificate in volume ${volume}..."
docker volume create "$volume" >/dev/null
# postgres:18 is already built here and carries openssl, so this pulls nothing.
docker run --rm -v "${volume}:/certs" --entrypoint /bin/sh postgres:18 -c '
  openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout /certs/private.key -out /certs/public.crt \
    -subj "/CN=minio" \
    -addext "subjectAltName=DNS:minio,DNS:localhost,IP:127.0.0.1" 2>/dev/null
  chmod 0644 /certs/public.crt && chmod 0600 /certs/private.key
  echo "  wrote public.crt and private.key"'

echo "Restarting MinIO so it picks the certificate up..."
docker compose --profile offsite-test up -d --force-recreate minio >/dev/null
echo "Done. MinIO serves https://minio:9000 on the compose network."
