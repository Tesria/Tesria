#!/usr/bin/env bash
# Dependency audit — the release gate (dev-plan 3.6).
#
#   scripts/audit.sh
#
# Runs the three audits this repo has (web SPA, collab sidecar, .NET API,
# transitive packages included) and exits non-zero if any reports a
# vulnerability. Run it before tagging a release and after touching any
# package manifest; CI can run it as-is.
set -uo pipefail
cd "$(dirname "$0")/.."
status=0

echo "== src/web (npm, production dependencies)"
(cd src/web && npm audit --omit=dev) || status=1

echo; echo "== collab (npm, production dependencies)"
(cd collab && npm audit --omit=dev) || status=1

echo; echo "== src/Api (.NET, transitive included)"
out=$(cd src/Api && dotnet list package --vulnerable --include-transitive 2>&1)
echo "$out"
if echo "$out" | grep -q "has the following vulnerable packages"; then status=1; fi

echo
if [ "$status" -eq 0 ]; then echo "audit: clean"; else echo "audit: FINDINGS — see above" >&2; fi
exit "$status"
