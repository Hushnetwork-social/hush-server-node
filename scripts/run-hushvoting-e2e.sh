#!/usr/bin/env bash
# HushVoting owns this runner; it never selects legacy HushNetwork scenarios.
set +x
set -euo pipefail
# Keep optional controlled inputs out of builds, provenance tools and ordinary test runs.
corpus_directory="${HUSH_TEST_KEYS_DIR-}"
corpus_password="${HUSH_TEST_DAT_PASSWORD-}"
corpus_inventory="${HUSH_TEST_CORPUS_INVENTORY-}"
export -n corpus_directory corpus_password corpus_inventory
unset HUSH_TEST_KEYS_DIR HUSH_TEST_DAT_PASSWORD HUSH_TEST_CORPUS_INVENTORY HUSHVOTING_CONTROLLED_CORPUS
# Crash/restart scenarios must never produce an OS core dump of credentials.
ulimit -c 0
server_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
workspace_root="$(dirname -- "$server_root")"
project="$server_root/Node/HushNode.IntegrationTests/HushNode.IntegrationTests.csproj"
export HUSHVOTING_E2E_CLIENT_ROOT="${HUSHVOTING_E2E_CLIENT_ROOT:-$workspace_root/hush-voting-web-client}"
export HUSH_SP07_RUST_WORKER_PATH="${HUSH_SP07_RUST_WORKER_PATH:-$server_root/Tools/HushSp07RustWorker/target/release/hush-sp07-rust-worker}"
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export TESTCONTAINERS_RYUK_DISABLED=true
export HUSHVOTING_E2E_RUN_ID="$(cat /proc/sys/kernel/random/uuid)"
export HUSHVOTING_E2E_OUTPUT="${HUSHVOTING_E2E_OUTPUT:-$server_root/TestResults/HushVoting/$HUSHVOTING_E2E_RUN_ID}"
build=false
list=false
all=false
qualifications=false
server_twins=false
controlled_corpus=false
group=""
while (($#)); do
  case "$1" in
    --build) build=true; shift ;;
    --list) list=true; shift ;;
    --all) all=true; shift ;;
    --qualifications) qualifications=true; shift ;;
    --server-twins) server_twins=true; shift ;;
    --controlled-corpus) controlled_corpus=true; shift ;;
    --group) group="${2:?--group needs a category}"; shift 2 ;;
    *) echo 'Usage: run-hushvoting-e2e.sh [--build] [--server-twins | --controlled-corpus] [--list | --group TAG | --all] | --qualifications' >&2; exit 2 ;;
  esac
done
if "$controlled_corpus"; then
  if "$server_twins" || "$all" || "$list" || "$qualifications" || [[ ! "$group" =~ ^HV-DAT-EXTERNAL-AC07[4-7]$ ]]; then
    echo 'Controlled corpus requires exactly one original AC074–AC077 group and the local operator inputs.' >&2; exit 2
  fi
  if [[ -z "$corpus_directory" || -z "$corpus_password" || -z "$corpus_inventory" ]]; then
    echo 'Controlled corpus runtime inputs are missing; no source was opened.' >&2; exit 2
  fi
  expected_corpus_output="$server_root/TestResults/HushVoting/$HUSHVOTING_E2E_RUN_ID"
  if [[ "$HUSHVOTING_E2E_OUTPUT" != "$expected_corpus_output" || "$(realpath -m -- "$HUSHVOTING_E2E_OUTPUT")" != "$expected_corpus_output" ]]; then
    echo 'Controlled corpus requires the owned default output directory.' >&2; exit 2
  fi
fi
if "$server_twins" && { "$all" || "$qualifications"; }; then
  echo 'Server Twins require their own --list or --group; Web catalogue and external gates are separate.' >&2; exit 2
fi
if [[ -z "$group" && "$list" == false && "$all" == false && "$qualifications" == false ]]; then
  echo 'Select --list, --group TAG, --all (Web scenarios), or --qualifications (external gate report).' >&2
  exit 2
fi
if [[ -n "$group" && ! "$group" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo 'Group must be a single category, not a filter expression.' >&2; exit 2
fi
selection_count=0
if "$list"; then selection_count=$((selection_count + 1)); fi
if "$all"; then selection_count=$((selection_count + 1)); fi
if "$qualifications"; then selection_count=$((selection_count + 1)); fi
if [[ -n "$group" ]]; then selection_count=$((selection_count + 1)); fi
if ((selection_count != 1)); then
  echo 'Choose exactly one of --list, --group TAG, --all, or --qualifications.' >&2; exit 2
fi
catalogue="$server_root/scripts/hushvoting-test-catalogue.py"
manifest="$server_root/Node/HushNode.IntegrationTests/HushVoting/migration-manifest.json"
if "$qualifications"; then
  if "$build"; then echo '--qualifications is a read-only report; omit --build.' >&2; exit 2; fi
  exec python3 "$catalogue" "$manifest" --qualifications
fi
if [[ -n "$group" ]]; then
  catalogue_layer=()
  if "$server_twins"; then catalogue_layer=(--server-twins); fi
  python3 "$catalogue" "$manifest" --check-group "$group" "${catalogue_layer[@]}"
fi
mkdir -p "$HUSHVOTING_E2E_OUTPUT"
chmod 700 "$HUSHVOTING_E2E_OUTPUT"
python3 "$catalogue" "$manifest" --qualifications > "$HUSHVOTING_E2E_OUTPUT/external-qualifications.json"
provenance="$server_root/scripts/hushvoting-e2e-provenance.py"
build_stamp="$server_root/Node/HushNode.IntegrationTests/bin/Debug/hushvoting-e2e-build.json"
mkdir -p "$(dirname -- "$build_stamp")"
# One owner of the shared production frontend and test build artifacts.
exec 9>"$build_stamp.lock"
flock -n 9 || { echo 'Another HushVoting build/test run owns these artifacts.' >&2; exit 1; }
provenance_args=(--server "$server_root" --client "$HUSHVOTING_E2E_CLIENT_ROOT" --stamp "$build_stamp")
cleanup() {
  local result=$?
  trap - EXIT
  # Only resources labelled with this invocation's unpredictable run ID.
  local ids
  ids="$(timeout 10s docker ps -aq --filter "label=hushvoting.e2e.run=$HUSHVOTING_E2E_RUN_ID")" || result=1
  if [[ -n "$ids" ]]; then
    while IFS= read -r id; do timeout 20s docker rm -fv "$id" >/dev/null || result=1; done <<< "$ids"
  fi
  rm -rf -- "${TMPDIR:-/tmp}/hushvoting-protocol-$HUSHVOTING_E2E_RUN_ID"
  exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
if "$build"; then
  python3 "$provenance" inputs "${provenance_args[@]}" > "$HUSHVOTING_E2E_OUTPUT/build-inputs.json"
  frontend_current="$(python3 "$provenance" frontend-current "${provenance_args[@]}")"
  if [[ "$frontend_current" != yes ]]; then
    (cd "$HUSHVOTING_E2E_CLIENT_ROOT" && timeout --kill-after=10s 5m npm run build:web)
    # Next.js intentionally leaves these runtime resources outside standalone.
    mkdir -p "$HUSHVOTING_E2E_CLIENT_ROOT/.next-web/standalone/.next-web" "$HUSHVOTING_E2E_CLIENT_ROOT/.next-web/standalone/src/app/api"
    cp -a "$HUSHVOTING_E2E_CLIENT_ROOT/.next-web/static" "$HUSHVOTING_E2E_CLIENT_ROOT/.next-web/standalone/.next-web/"
    cp -a "$HUSHVOTING_E2E_CLIENT_ROOT/public" "$HUSHVOTING_E2E_CLIENT_ROOT/.next-web/standalone/"
    cp -a "$HUSHVOTING_E2E_CLIENT_ROOT/src/app/api/protos" "$HUSHVOTING_E2E_CLIENT_ROOT/.next-web/standalone/src/app/api/"
  else
    echo 'Reusing verified HushVoting frontend build; rebuilding .NET changes.'
  fi
  timeout --kill-after=10s 5m dotnet build "$project" --no-restore --disable-build-servers -p:UseSharedCompilation=false -nodeReuse:false --verbosity quiet
  python3 "$provenance" stamp "${provenance_args[@]}" --before "$HUSHVOTING_E2E_OUTPUT/build-inputs.json"
fi
python3 "$provenance" verify "${provenance_args[@]}"
base_filter="$(python3 "$catalogue" "$manifest" --web-filter)"
layer='E2E'
if "$server_twins"; then
  base_filter='Category=HushVoting&Category=HV-SERVER-TWIN&Category!=HV-E2E'
  layer='backend Twin'
fi
if "$list"; then
  timeout --kill-after=10s 1m dotnet test "$project" --no-build --list-tests --filter "$base_filter"
  exit
fi
run_group() {
  local selected="$1"
  export HUSHVOTING_E2E_SELECTION="$selected"
  printf 'HushVoting %s: %s\n' "$layer" "$selected"
  python3 "$provenance" verify "${provenance_args[@]}" --receipt "$HUSHVOTING_E2E_OUTPUT/$selected.provenance.json"
  (
  if "$controlled_corpus"; then
    export HUSH_TEST_KEYS_DIR="$corpus_directory" HUSH_TEST_DAT_PASSWORD="$corpus_password" HUSH_TEST_CORPUS_INVENTORY="$corpus_inventory"
    export HUSHVOTING_CONTROLLED_CORPUS=1
  fi
  timeout --kill-after=10s 5m python3 "$server_root/scripts/hushvoting-artifact-guard.py" \
    --output "$HUSHVOTING_E2E_OUTPUT" --report "$HUSHVOTING_E2E_OUTPUT/$selected.artifacts.json" -- \
    dotnet test "$project" --no-build \
    --filter "$base_filter&Category=$selected" \
    --results-directory "$HUSHVOTING_E2E_OUTPUT" \
    --logger "trx;LogFileName=$selected.trx" --verbosity minimal
  )
  python3 "$provenance" verify "${provenance_args[@]}"
  python3 - "$HUSHVOTING_E2E_OUTPUT/$selected.trx" <<'PY'
import sys, xml.etree.ElementTree as ET
root=ET.parse(sys.argv[1]).getroot()
c=root.find('.//{*}Counters')
if c is None or int(c.attrib['total']) == 0 or c.attrib['passed'] != c.attrib['total']:
    raise SystemExit('HushVoting block must execute a nonzero number of tests with every test passing; skipped/pending is not green.')
PY
}
if "$all"; then
  # Validate the complete catalogue before running anything. Process substitution
  # would hide Python's failure and could silently execute only a partial list.
  python3 "$catalogue" "$manifest" --web-ids > "$HUSHVOTING_E2E_OUTPUT/scenario-ids.txt"
  mapfile -t groups < "$HUSHVOTING_E2E_OUTPUT/scenario-ids.txt"
  ((${#groups[@]})) || { echo 'No scenarios discovered' >&2; exit 1; }
  for selected in "${groups[@]}"; do run_group "$selected"; done
else
  run_group "$group"
fi
