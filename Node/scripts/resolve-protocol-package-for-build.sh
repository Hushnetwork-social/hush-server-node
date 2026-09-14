#!/usr/bin/env bash
set -Eeuo pipefail

configuration="Debug"
server_repository_root=""
workspace_root=""
output_root=""
package_id="omega-hushvoting-v1"
local_artifacts_relative_path="PrivateServer_ElectronicVoting/Protocol-Omega-HushVoting-v1-Artifacts"
github_repository="Hushnetwork-social/protocol-omega-packages"
release_tag_prefix="ProtocolOmega-HushVoting-v1-"
release_asset_prefix="Protocol-Omega-HushVoting-v1-Artifacts"
github_token="${HUSH_PROTOCOL_PACKAGE_GITHUB_TOKEN:-${GH_TOKEN:-${GITHUB_TOKEN:-}}}"

temporary_root=""
cleanup() {
    if [[ -n "$temporary_root" && -d "$temporary_root" ]]; then
        rm -rf -- "$temporary_root"
    fi
}
trap cleanup EXIT

fail() {
    printf 'Protocol package resolver error: %s\n' "$*" >&2
    exit 1
}

warn() {
    printf 'Protocol package resolver warning: %s\n' "$*" >&2
}

usage() {
    cat <<'EOF'
Usage: resolve-protocol-package-for-build.sh [options]

Options:
  --configuration VALUE                 Build configuration (default: Debug)
  --server-repository-root PATH         hush-server-node repository root
  --workspace-root PATH                 HushNetwork workspace root
  --output-root PATH                    Protocol package output directory
  --package-id VALUE                    Expected package ID
  --local-artifacts-relative-path PATH  Path below the sibling hush-documents repository
  --github-repository OWNER/REPO        Release package repository
  --release-tag-prefix VALUE            Prefix for release tags
  --release-asset-prefix VALUE          Prefix for release ZIP assets
  --github-token VALUE                  GitHub API token (environment variables are preferred)
  --help                                Show this help
EOF
}

while (($# > 0)); do
    case "$1" in
        --configuration)
            (($# >= 2)) || fail "$1 requires a value."
            configuration="$2"
            shift 2
            ;;
        --server-repository-root)
            (($# >= 2)) || fail "$1 requires a value."
            server_repository_root="$2"
            shift 2
            ;;
        --workspace-root)
            (($# >= 2)) || fail "$1 requires a value."
            workspace_root="$2"
            shift 2
            ;;
        --output-root)
            (($# >= 2)) || fail "$1 requires a value."
            output_root="$2"
            shift 2
            ;;
        --package-id)
            (($# >= 2)) || fail "$1 requires a value."
            package_id="$2"
            shift 2
            ;;
        --local-artifacts-relative-path)
            (($# >= 2)) || fail "$1 requires a value."
            local_artifacts_relative_path="$2"
            shift 2
            ;;
        --github-repository)
            (($# >= 2)) || fail "$1 requires a value."
            github_repository="$2"
            shift 2
            ;;
        --release-tag-prefix)
            (($# >= 2)) || fail "$1 requires a value."
            release_tag_prefix="$2"
            shift 2
            ;;
        --release-asset-prefix)
            (($# >= 2)) || fail "$1 requires a value."
            release_asset_prefix="$2"
            shift 2
            ;;
        --github-token)
            (($# >= 2)) || fail "$1 requires a value."
            github_token="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            fail "Unknown argument '$1'."
            ;;
    esac
done

for command_name in curl jq realpath unzip; do
    command -v "$command_name" >/dev/null 2>&1 || fail "Required command '$command_name' is not installed."
done

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

find_server_repository_root() {
    if [[ -n "$server_repository_root" ]]; then
        realpath -m -- "$server_repository_root"
        return
    fi

    local current="$script_directory"
    while [[ "$current" != "/" ]]; do
        if [[ -f "$current/Node/HushServerNode/HushServerNode.csproj" ]]; then
            printf '%s\n' "$current"
            return
        fi
        current="$(dirname -- "$current")"
    done

    fail "Unable to resolve hush-server-node repository root. Pass --server-repository-root."
}

server_root="$(find_server_repository_root)"
[[ -f "$server_root/Node/HushServerNode/HushServerNode.csproj" ]] || \
    fail "Server repository root '$server_root' does not contain HushServerNode.csproj."

if [[ -z "$output_root" ]]; then
    output_root="$server_root/Node/Release/ProtocolPackages"
fi
output_root="$(realpath -m -- "$output_root")"

if [[ "$output_root" == "$server_root" || "$output_root" != "$server_root/"* ]]; then
    fail "Protocol package build output must stay inside the hush-server-node repository. OutputRoot='$output_root'."
fi

approval_status_name() {
    local manifest_path="$1"
    local status
    status="$(jq -r 'if .approvalStatus == null then "Unknown" else (.approvalStatus | tostring) end' "$manifest_path")"
    case "$status" in
        0) printf 'DraftPrivate\n' ;;
        1) printf 'ApprovedInternal\n' ;;
        2) printf 'Retired\n' ;;
        DraftPrivate|ApprovedInternal|Retired) printf '%s\n' "$status" ;;
        *) printf 'Unknown\n' ;;
    esac
}

parse_version() {
    local version="$1"
    [[ "$version" =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]
}

resolved_output_matches_candidate() {
    local version="$1"
    local manifest_path="$2"
    local metadata_path="$output_root/SelectedProtocolPackage.json"
    local catalog_path="$output_root/ApprovedProtocolPackageCatalog.json"
    local version_root="$output_root/$version"
    local copied_manifest="$version_root/ProtocolOmegaPackageManifest.json"

    [[ -f "$metadata_path" && -f "$catalog_path" && -d "$version_root" && -f "$copied_manifest" ]] || return 1
    jq empty "$metadata_path" "$catalog_path" >/dev/null 2>&1 || return 1

    local manifest_package_id manifest_version spec_hash proof_hash release_hash
    manifest_package_id="$(jq -r '.packageId // ""' "$manifest_path")"
    manifest_version="$(jq -r '.packageVersion // ""' "$manifest_path")"
    spec_hash="$(jq -r '.specPackageHash // ""' "$manifest_path")"
    proof_hash="$(jq -r '.proofPackageHash // ""' "$manifest_path")"
    release_hash="$(jq -r '.releaseManifestHash // ""' "$manifest_path")"

    jq -e \
        --arg package_id "$manifest_package_id" \
        --arg version "$manifest_version" \
        --arg spec_hash "$spec_hash" \
        --arg proof_hash "$proof_hash" \
        --arg release_hash "$release_hash" \
        '(.packageVersion == $version) and
         (.specPackageHash == $spec_hash) and
         (.proofPackageHash == $proof_hash) and
         (.releaseManifestHash == $release_hash)' \
        "$metadata_path" >/dev/null || return 1

    jq -e \
        --arg package_id "$manifest_package_id" \
        --arg version "$manifest_version" \
        --arg spec_hash "$spec_hash" \
        --arg proof_hash "$proof_hash" \
        --arg release_hash "$release_hash" \
        'any(.[];
             .packageId == $package_id and
             .packageVersion == $version and
             .isLatestForCompatibleProfiles == true and
             .specPackageHash == $spec_hash and
             .proofPackageHash == $proof_hash and
             .releaseManifestHash == $release_hash)' \
        "$catalog_path" >/dev/null
}

write_build_catalog() {
    local manifest_path="$1"
    local target_path="$2"
    local approval_name
    approval_name="$(approval_status_name "$manifest_path")"

    mkdir -p -- "$(dirname -- "$target_path")"
    jq --argjson is_approved "$( [[ "$approval_name" == "ApprovedInternal" ]] && printf true || printf false )" \
        '[{
            approvalStatus: .approvalStatus,
            approvedAt: .releasedAt,
            isLatestForCompatibleProfiles: true,
            externalReviewStatus: .externalReviewStatus,
            packageId: .packageId,
            packageVersion: .packageVersion,
            specPackageHash: .specPackageHash,
            proofPackageHash: .proofPackageHash,
            releaseManifestHash: .releaseManifestHash,
            compatibleProfileIds: .compatibleProfileIds,
            specAccessLocations: .specAccessLocations,
            proofAccessLocations: .proofAccessLocations,
            isApprovedForElectionOpen: $is_approved
        }]' "$manifest_path" >"$target_path"
}

write_selection_metadata() {
    local manifest_path="$1"
    local source="$2"
    local mode="$3"
    local target_path="$4"
    local approval_name resolved_at
    approval_name="$(approval_status_name "$manifest_path")"
    resolved_at="$(date -u +'%Y-%m-%dT%H:%M:%S.%NZ')"

    mkdir -p -- "$(dirname -- "$target_path")"
    jq \
        --arg resolved_at "$resolved_at" \
        --arg configuration "$configuration" \
        --arg mode "$mode" \
        --arg source "$source" \
        --arg approval_name "$approval_name" \
        '{
            resolvedAt: $resolved_at,
            buildConfiguration: $configuration,
            mode: $mode,
            source: $source,
            packageId: .packageId,
            packageVersion: .packageVersion,
            approvalStatus: $approval_name,
            externalReviewStatus: .externalReviewStatus,
            specPackageHash: .specPackageHash,
            proofPackageHash: .proofPackageHash,
            releaseManifestHash: .releaseManifestHash
        }' "$manifest_path" >"$target_path"
}

install_candidate() {
    local version="$1"
    local version_root="$2"
    local manifest_path="$3"
    local source="$4"
    local mode="$5"
    local build_kind="$6"
    local approval_name version_target

    approval_name="$(approval_status_name "$manifest_path")"
    rm -rf -- "$output_root"
    version_target="$output_root/$version"
    mkdir -p -- "$version_target"
    cp -a -- "$version_root/." "$version_target/"
    write_build_catalog "$manifest_path" "$output_root/ApprovedProtocolPackageCatalog.json"
    write_selection_metadata "$manifest_path" "$source" "$mode" "$output_root/SelectedProtocolPackage.json"
    printf "Resolved %s Protocol Omega package %s (%s) into '%s'.\n" \
        "$build_kind" "$version" "$approval_name" "$output_root"
}

github_headers=(
    -H 'Accept: application/vnd.github+json'
    -H 'User-Agent: HushServerNode-ProtocolPackageResolver'
    -H 'X-GitHub-Api-Version: 2022-11-28'
)
if [[ -n "$github_token" ]]; then
    github_headers+=(-H "Authorization: Bearer $github_token")
fi

resolve_release_candidate() {
    temporary_root="$(mktemp -d -t hush-protocol-package-release-XXXXXXXX)"
    local releases_path="$temporary_root/releases.json"
    local releases_url="https://api.github.com/repos/$github_repository/releases?per_page=100"

    if ! curl -fsSL "${github_headers[@]}" "$releases_url" -o "$releases_path"; then
        fail "Unable to read GitHub releases from '$github_repository'. Check that Protocol Omega packages have been released. Set HUSH_PROTOCOL_PACKAGE_GITHUB_TOKEN only for private repositories or API rate limits."
    fi

    local candidate_lines=()
    local tag_name asset_name asset_url version major minor patch expected_asset
    while IFS=$'\t' read -r tag_name asset_name asset_url; do
        [[ "$tag_name" == "$release_tag_prefix"* ]] || continue
        version="${tag_name#"$release_tag_prefix"}"
        if ! parse_version "$version"; then
            continue
        fi
        major="${BASH_REMATCH[1]}"
        minor="${BASH_REMATCH[2]}"
        patch="${BASH_REMATCH[3]}"
        ((10#$minor % 2 == 0)) || continue
        expected_asset="$release_asset_prefix-$version.zip"
        [[ "$asset_name" == "$expected_asset" ]] || continue
        candidate_lines+=("$version"$'\t'"$tag_name"$'\t'"$asset_name"$'\t'"$asset_url")
    done < <(jq -r '.[] | select(.draft == false and .prerelease == false) as $release |
                      $release.assets[]? |
                      [$release.tag_name, .name, .url] | @tsv' "$releases_path")

    ((${#candidate_lines[@]} > 0)) || \
        fail "No Protocol Omega GitHub release with an approved even-minor version and required ZIP asset was found."

    local selected_line
    selected_line="$(printf '%s\n' "${candidate_lines[@]}" | sort -t $'\t' -k1,1Vr | head -n 1)"
    IFS=$'\t' read -r version tag_name asset_name asset_url <<<"$selected_line"

    local zip_path="$temporary_root/$asset_name"
    local extract_root="$temporary_root/extracted"
    local asset_headers=("${github_headers[@]}")
    asset_headers[1]='Accept: application/octet-stream'
    if ! curl -fsSL "${asset_headers[@]}" "$asset_url" -o "$zip_path"; then
        fail "Unable to download GitHub release asset '$asset_name' from '$github_repository'."
    fi

    mkdir -p -- "$extract_root"
    unzip -q "$zip_path" -d "$extract_root" || fail "Unable to extract GitHub release asset '$asset_name'."

    local version_root="$extract_root/$version"
    [[ -d "$version_root" ]] || version_root="$extract_root"
    local manifest_path="$version_root/ProtocolOmegaPackageManifest.json"
    [[ -f "$manifest_path" ]] || \
        fail "GitHub release '$tag_name' asset '$asset_name' does not contain ProtocolOmegaPackageManifest.json."
    jq empty "$manifest_path" >/dev/null || fail "GitHub release '$tag_name' contains an invalid package manifest."

    local manifest_package_id manifest_version approval_name
    manifest_package_id="$(jq -r '.packageId // ""' "$manifest_path")"
    manifest_version="$(jq -r '.packageVersion // ""' "$manifest_path")"
    approval_name="$(approval_status_name "$manifest_path")"
    [[ "$manifest_package_id" == "$package_id" ]] || \
        fail "GitHub release '$tag_name' package id '$manifest_package_id' does not match expected '$package_id'."
    [[ "$manifest_version" == "$version" ]] || \
        fail "GitHub release '$tag_name' manifest version '$manifest_version' does not match release version '$version'."
    [[ "$approval_name" == "ApprovedInternal" ]] || \
        fail "GitHub release '$tag_name' is not approved. Manifest status is '$approval_name'."

    if resolved_output_matches_candidate "$version" "$manifest_path"; then
        printf "Protocol Omega package %s is already resolved in '%s'. No copy needed.\n" "$version" "$output_root"
        return
    fi

    install_candidate "$version" "$version_root" "$manifest_path" \
        "github-release" "release-github-release-approved" "Release"
}

resolve_debug_candidate() {
    local resolved_workspace_root="$workspace_root"
    if [[ -z "$resolved_workspace_root" ]]; then
        local parent
        parent="$(dirname -- "$server_root")"
        if [[ -d "$parent/hush-documents" ]]; then
            resolved_workspace_root="$parent"
        fi
    fi

    if [[ -z "$resolved_workspace_root" ]]; then
        warn "Skipping Debug Protocol Omega package resolution because a sibling hush-documents repository was not found."
        return
    fi
    resolved_workspace_root="$(realpath -m -- "$resolved_workspace_root")"

    local artifacts_root
    artifacts_root="$(realpath -m -- "$resolved_workspace_root/hush-documents/$local_artifacts_relative_path")"
    printf "Resolving Protocol Omega package for Debug from local artifacts '%s'.\n" "$artifacts_root"
    if [[ ! -d "$artifacts_root" ]]; then
        warn "Skipping Debug Protocol Omega package resolution because no local package artifacts were found."
        return
    fi

    local candidate_lines=()
    local directory version manifest_path manifest_package_id
    while IFS= read -r directory; do
        version="$(basename -- "$directory")"
        parse_version "$version" || continue
        manifest_path="$directory/ProtocolOmegaPackageManifest.json"
        [[ -f "$manifest_path" ]] || continue
        jq empty "$manifest_path" >/dev/null 2>&1 || fail "Local package manifest '$manifest_path' is invalid JSON."
        manifest_package_id="$(jq -r '.packageId // ""' "$manifest_path")"
        [[ "$manifest_package_id" == "$package_id" ]] || continue
        candidate_lines+=("$version"$'\t'"$directory")
    done < <(find "$artifacts_root" -mindepth 1 -maxdepth 1 -type d -print)

    if ((${#candidate_lines[@]} == 0)); then
        warn "Skipping Debug Protocol Omega package resolution because no local package artifacts were found."
        return
    fi

    local selected_line version_root
    selected_line="$(printf '%s\n' "${candidate_lines[@]}" | sort -t $'\t' -k1,1Vr | head -n 1)"
    IFS=$'\t' read -r version version_root <<<"$selected_line"
    manifest_path="$version_root/ProtocolOmegaPackageManifest.json"

    if resolved_output_matches_candidate "$version" "$manifest_path"; then
        printf "Protocol Omega package %s is already resolved in '%s'. No copy needed.\n" "$version" "$output_root"
        return
    fi

    install_candidate "$version" "$version_root" "$manifest_path" \
        "local" "debug-local-development" "Debug"
}

if [[ "$configuration" == "Release" ]]; then
    printf "Resolving Protocol Omega package for Release from GitHub releases in '%s'.\n" "$github_repository"
    resolve_release_candidate
else
    resolve_debug_candidate
fi
