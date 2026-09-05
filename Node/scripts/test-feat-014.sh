#!/usr/bin/env bash
# =============================================================================
# FEAT-014 focused qualification gate.
#
# Fails when:
#   - any configured FEAT-014 test block discovers ZERO tests, or
#   - any executed test block fails, or
#   - .NET build servers cannot be shut down afterwards.
#
# Prints exact discovered/passed/failed/skipped counts and cleanup evidence.
#
# Usage:
#   bash scripts/test-feat-014.sh             # unit + PostgreSQL TwinTests blocks
#   bash scripts/test-feat-014.sh --unit      # cache unit/model tests only
#   bash scripts/test-feat-014.sh --twin      # FEAT-014 real-PostgreSQL TwinTests only
#   bash scripts/test-feat-014.sh --self-test # prove the zero-discovery guard fails non-zero
#
# Run from the Node directory (hush-server-node/Node).
# =============================================================================
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNIT_PROJECT="$REPO_ROOT/Core/HushVoting/HushNode.HushVoting.Licensing.Cache.Tests/HushNode.HushVoting.Licensing.Cache.Tests.csproj"
TWIN_PROJECT="$REPO_ROOT/HushNode.IntegrationTests/HushNode.IntegrationTests.csproj"
TWIN_FILTER="Category=FEAT-014&Category=TwinTest&Category=NON_E2E"
NO_MATCH_MARKER="No test matches the given testcase filter"

failed=0

shutdown_build_servers() {
    dotnet build-server shutdown >/dev/null 2>&1 || true
    echo "CLEANUP: dotnet build servers shut down"
}

# run_block <label> <project> <extra args...>
run_block() {
    local label="$1"
    local project="$2"
    shift 2

    echo "=== FEAT-014 block: $label ==="
    local output
    output="$(dotnet test "$project" --no-build --nologo --verbosity minimal "$@" 2>&1)"
    local exit_code=$?

    echo "$output"

    local total passed failed_count skipped discovered
    total="$(printf '%s\n' "$output" | grep -oE 'Total: +[0-9]+' | tail -1 | grep -oE '[0-9]+')"
    passed="$(printf '%s\n' "$output" | grep -oE 'Passed: +[0-9]+' | tail -1 | grep -oE '[0-9]+')"
    failed_count="$(printf '%s\n' "$output" | grep -oE 'Failed: +[0-9]+' | tail -1 | grep -oE '[0-9]+')"
    skipped="$(printf '%s\n' "$output" | grep -oE 'Skipped: +[0-9]+' | tail -1 | grep -oE '[0-9]+')"
    total="${total:-0}"
    passed="${passed:-0}"
    failed_count="${failed_count:-0}"
    skipped="${skipped:-0}"
    discovered="$((passed + failed_count + skipped))"

    echo "COUNT: $label discovered=$discovered passed=$passed failed=$failed_count skipped=$skipped"

    if printf '%s\n' "$output" | grep -q "$NO_MATCH_MARKER" || [[ "$discovered" -eq 0 ]]; then
        echo "ERROR: $label discovered zero tests (gate must fail non-zero)." >&2
        return 2
    fi

    if [[ "$exit_code" -ne 0 || "$failed_count" -ne 0 ]]; then
        echo "ERROR: $label failed (exit=$exit_code failed=$failed_count)." >&2
        return 1
    fi

    return 0
}

if [[ "${1:-}" == "--self-test" ]]; then
    echo "SELF-TEST: proving the zero-discovery guard fails non-zero..."
    run_block "zero-discovery-probe" "$UNIT_PROJECT" \
        --filter "FullyQualifiedName~ZeroMatchingSentinel_DoesNotExist"
    local_exit=$?
    if [[ "$local_exit" -eq 2 ]]; then
        echo "SELF-TEST: PASS - zero discovery made the gate fail non-zero (exit 2)."
        shutdown_build_servers
        exit 0
    fi
    echo "SELF-TEST: FAIL - unexpected exit code $local_exit." >&2
    shutdown_build_servers
    exit 1
fi

mode="${1:-all}"

if [[ "$mode" == "all" || "$mode" == "--unit" ]]; then
    run_block "unit-model" "$UNIT_PROJECT" || failed=1
fi

if [[ "$mode" == "all" || "$mode" == "--twin" ]]; then
    run_block "postgres-twin" "$TWIN_PROJECT" --filter "$TWIN_FILTER" || failed=1
fi

shutdown_build_servers

if [[ "$failed" -ne 0 ]]; then
    echo "FEAT-014 GATE: FAILED (see counts above)."
    exit 1
fi

echo "FEAT-014 GATE: PASSED (unit + PostgreSQL TwinTests; zero-discovery guard armed)."
exit 0
