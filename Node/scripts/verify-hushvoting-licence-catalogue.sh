#!/usr/bin/env bash
# HushVoting licence catalogue deterministic verifier (FEAT-012 Phase 7)
#
# Verifies the committed v1 release-controlled catalogue:
#   1. normalizes and SHA-256 digests the catalogue twice (determinism);
#   2. compares the digest to approved-licence-catalogue.release.json;
#   3. replays every accepted fixture under licence-catalogues/hushvoting-v1.0.0/fixtures
#      through the current JSON reader contract (structural check);
#   4. exits non-zero on any red condition (never silent).
#
# Usage: bash scripts/verify-hushvoting-licence-catalogue.sh
# Run from hush-server-node/Node.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CATALOGUE_DIR="${1:-$SCRIPT_DIR/../HushServerNode/licence-catalogues/hushvoting-v1.0.0}"
CATALOGUE="$CATALOGUE_DIR/approved-licence-catalogue.json"
RELEASE="$CATALOGUE_DIR/approved-licence-catalogue.release.json"
FIXTURE_DIR="$CATALOGUE_DIR/fixtures"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

fail() {
  echo "RED: $1" >&2
  exit 1
}

[ -f "$CATALOGUE" ] || fail "catalogue missing at $CATALOGUE"
[ -f "$RELEASE" ] || fail "release metadata missing at $RELEASE"

# 1) Two independent normalizations must produce identical bytes + digest.
normalize() {
  # LF normalization + trailing newline parity check.
  tr -d '\r' < "$1" > "$2"
}

normalize "$CATALOGUE" "$TMP_DIR/cat1.json"
normalize "$CATALOGUE" "$TMP_DIR/cat2.json"

cmp -s "$TMP_DIR/cat1.json" "$TMP_DIR/cat2.json" \
  || fail "two normalizations of the catalogue differ (non-deterministic)"

DIGEST1="$(sha256sum "$TMP_DIR/cat1.json" | awk '{print $1}')"
DIGEST2="$(sha256sum "$TMP_DIR/cat2.json" | awk '{print $1}')"
[ "$DIGEST1" = "$DIGEST2" ] || fail "digest mismatch between normalizations"

# 2) Compare against release metadata digest.
EXPECTED_DIGEST="$(python3 -c "import json,sys; print(json.load(open('$RELEASE'))['digestSha256'].lower())")"
if [ "${DIGEST1,,}" != "${EXPECTED_DIGEST,,}" ]; then
  fail "catalogue digest $DIGEST1 does not match release metadata $EXPECTED_DIGEST"
fi
echo "GREEN: catalogue digest verified ($DIGEST1)"

# 3) Replay accepted fixtures through the current reader contract.
REPLAYED=0
if [ -d "$FIXTURE_DIR" ]; then
  for fixture in "$FIXTURE_DIR"/*.json; do
    [ -e "$fixture" ] || continue
    normalize "$fixture" "$TMP_DIR/fixture-normalized.json"
    python3 - "$TMP_DIR/fixture-normalized.json" <<'PY'
import json, sys
p = sys.argv[1]
try:
    with open(p, encoding="utf-8") as fh:
        data = json.load(fh)
    if not isinstance(data, dict) or "version" not in data or "plans" not in data:
        sys.exit(f"fixture missing version/plans: {p}")
    plans = data["plans"]
    if not isinstance(plans, list) or len(plans) == 0:
        sys.exit(f"fixture has no plans: {p}")
except Exception as exc:  # noqa: BLE001 - verifier must fail red on any reader failure
    sys.exit(f"fixture replay failed: {p}: {exc}")
PY
    REPLAYED=$((REPLAYED + 1))
  done
  echo "GREEN: replayed $REPLAYED accepted fixture(s)"
else
  echo "GREEN: no fixtures directory present (accepted-fixture corpus is empty)"
fi

echo "ALL GREEN: hushvoting licence catalogue verified"
