#!/usr/bin/env bash
set -euo pipefail

test_filter='Category!=Performance&Category!=PerformanceTest&Category!=E2E&Category!=HS-INT-087-CROSS-RUNTIME-PROOF'
dotnet_test_args=("$@")

test_projects=(
  "Core/Feeds/HushNode.Feeds.Tests/HushNode.Feeds.Tests.csproj"
  "Core/Identity/HushNode.Identity.Tests/HushNode.Identity.Tests.csproj"
  "Core/Idempotency/HushNode.Idempotency.Tests/HushNode.Idempotency.Tests.csproj"
  "Core/Reactions/HushNode.Reactions.Tests/HushNode.Reactions.Tests.csproj"
  "Core/UrlMetadata/HushNode.UrlMetadata.Tests/HushNode.UrlMetadata.Tests.csproj"
  "Infrastructure/HushNode.Caching.Tests/HushNode.Caching.Tests.csproj"
  "Infrastructure/HushNode.Notifications.Tests/HushNode.Notifications.Tests.csproj"
  "Infrastructure/HushNode.PushNotifications.Tests/HushNode.PushNotifications.Tests.csproj"
  "HushServerNode.Tests/HushServerNode.Tests.csproj"
  "HushNode.IntegrationTests/HushNode.IntegrationTests.csproj"
)

if [[ ! -f "HushServerNode.sln" ]]; then
  echo "::error::run-dotnet-test-projects.sh must be run from the Node directory."
  exit 1
fi

for test_project in "${test_projects[@]}"; do
  echo "::group::dotnet test ${test_project}"
  if ! dotnet test "${test_project}" "${dotnet_test_args[@]}" --no-build --verbosity normal --filter "${test_filter}"; then
    echo "::endgroup::"
    exit 1
  fi
  echo "::endgroup::"
done
