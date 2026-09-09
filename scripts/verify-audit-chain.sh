#!/usr/bin/env bash
# Verifies the audit log's hash chain (dev-plan 3.1) through the API.
#
#   scripts/verify-audit-chain.sh https://wiki.example.com "$TESRIA_ADMIN_TOKEN"
#
# The token must belong to an administrator (Profile -> API tokens). Exit
# status is 0 when every link holds, 1 when the chain is broken, 2 on any
# other failure — suitable for cron. The check is also run by the app itself
# daily and on demand from Admin -> Security.
#
# Honest limit: this asks the app to check itself. An attacker who controls
# the app binary could lie here; the stdout copy of every audit row (the
# `Tesria.Audit` log category in `docker compose logs app`) is the record
# that does not depend on the app being honest after the fact.
set -euo pipefail

BASE="${1:?usage: $0 <base-url> <admin-api-token>}"
TOKEN="${2:?usage: $0 <base-url> <admin-api-token>}"

RESPONSE=$(curl -fsS -X POST "${BASE%/}/api/admin/audit/verify" \
  -H "Authorization: Bearer ${TOKEN}" -H "Content-Length: 0") || {
  echo "verify-audit-chain: request failed" >&2; exit 2; }

echo "${RESPONSE}"
if echo "${RESPONSE}" | grep -q '"ok":true'; then
  exit 0
else
  echo "verify-audit-chain: CHAIN BROKEN" >&2
  exit 1
fi
