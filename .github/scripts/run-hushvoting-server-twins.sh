#!/usr/bin/env bash
set -euo pipefail
server_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
groups=(
  HV-ID-INGRESS-TWIN HV-ID-ADMISSION-TWIN HV-ID-ENCODING-TWIN
  HV-ID-CACHE-TWIN HV-ID-CACHE-OUTAGE-TWIN HV-ID-FIELD-TWIN
  HV-ID-SHAPE-TWIN HV-ID-NULL-TWIN HV-ID-BINDING-WAIT-TWIN
  HV-NODE-RESTART-TWIN HV-NODE-RESET-TWIN
  HV-ID-CREATE-SECURITY-005 HV-RW-SECURITY-007 HV-DAT-SECURITY-AC081
)
build=(--build)
for group in "${groups[@]}"; do
  timeout --signal=TERM --kill-after=20s 15m bash "$server_root/scripts/run-hushvoting-e2e.sh" \
    "${build[@]}" --server-twins --group "$group"
  build=()
done
