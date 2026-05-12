# Protocol Package Promoter

`ProtocolPackagePromoter` promotes the working Protocol Omega / HushVoting v1 package source into
versioned official artifacts, website artifacts, the public package repository, and the runtime
package catalog used by `hush-server-node`.

The promoter owns package versioning in normal operation. Do not manually copy package folders or
hand-edit catalog entries.

## Default Command

From `hush-server-node`:

```powershell
dotnet run --project Node\scripts\ProtocolPackagePromoter\ProtocolPackagePromoter.csproj -- --workspace-root C:\myWork\HushNetworkOrg
```

Or through the wrapper:

```powershell
Node\scripts\promote-protocol-package.ps1 --workspace-root C:\myWork\HushNetworkOrg
```

When `--workspace-root` is omitted, the tool walks up from the current directory until it finds the
workspace containing `hush-memory-bank`, `hush-documents`, and `hush-server-node`.

## Inputs And Outputs

Working source:

```text
hush-memory-bank/Overview/ProtocolOmega/Protocol-Omega-HushVoting-v1-Artifacts/
```

Official HushDocuments output:

```text
hush-documents/PrivateServer_ElectronicVoting/Protocol-Omega-HushVoting-v1-Artifacts/vX.Y.Z/
```

Website mirror output:

```text
hush-website/public/protocol-omega/hushvoting-v1/vX.Y.Z/
```

Public package repository output:

```text
protocol-omega-packages/hushvoting-v1/vX.Y.Z/
```

Runtime catalog:

```text
hush-server-node/Node/Core/Elections/HushNode.Elections/ProtocolPackages/ApprovedProtocolPackageCatalog.json
```

Each generated version folder contains:

```text
vX.Y.Z/
  ChangeLog.md
  ProtocolOmegaPackageManifest.json
  Protocol-Specification-Package/
    PackageManifest.json
    PackageManifest.schema.json
    Protocol-Specification-Package.zip
    ...
  Protocol-Proof-And-Crypto-Review/
    PackageManifest.json
    PackageManifest.schema.json
    Protocol-Proof-And-Crypto-Review.zip
    ...
```

`ChangeLog.md` is a release-level artifact. It lives at the version root because one Protocol Omega
version can update files in both child packages. Child package manifests and ZIPs should not include
the release ChangeLog.

## Versioning Policy

Package versions are numeric only:

```text
vMAJOR.MINOR.PATCH
```

Do not use a `-dev` suffix.

The version meaning is:

| Segment | Meaning |
|---|---|
| `MAJOR` | Required package file-set changes |
| `MINOR` odd | Draft/internal package line |
| `MINOR` even | Complete approved package line |
| `PATCH` | Content/build counter inside the current line |

The promoter derives the version and generated timestamp when `--version` and `--generated-at` are
omitted.

## Completeness And Approval Status

Promotion status is derived from required package source files:

| Source state | Result status | Election opening |
|---|---|---|
| Missing required file | Promotion fails closed | Not cataloged |
| Any file contains `specified_not_implemented_yet` | `DraftPrivate` | Not allowed |
| All required files exist and no incomplete marker remains | `ApprovedInternal` | Allowed when latest and compatible |

The promoter also checks previous official artifacts. If a previous catalog entry was incorrectly
marked approved while its official artifacts still contain `specified_not_implemented_yet`, the
entry is downgraded to `DraftPrivate` and removed from latest/openable status.

## Auto-Bump Rules

Run without `--version` for normal promotion.

| Previous latest | Current source state | Content/file-set state | New version |
|---|---|---|---|
| none | incomplete | first generated package | `v1.1.0` |
| none | complete | first generated package | `v1.2.0` |
| `v1.0.0` | incomplete | previous artifacts contain incomplete markers | `v1.1.1` |
| `v1.1.1` | incomplete | content changed | `v1.1.2` |
| `v1.1.2` | incomplete | content changed | `v1.1.3` |
| odd minor draft | complete | all markers removed | next even minor, patch `0` |
| `v1.1.3` | complete | all markers removed | `v1.2.0` |
| even minor approved | complete | content changed | patch increments, e.g. `v1.2.0` to `v1.2.1` |
| any latest | any state | required file set changed | major increments |

Practical example:

1. Current package is `v1.1.1` and still has placeholder files.
2. You implement one specification file and leave other placeholders in place.
3. Run the promoter without `--version`.
4. The output becomes `v1.1.2` with status `DraftPrivate`.
5. When all required placeholders are implemented and no `specified_not_implemented_yet` marker
   remains, the next promotion becomes `v1.2.0` with status `ApprovedInternal`.

## Manual Version Override

Use `--version` only for recovery or controlled test scenarios.

Example: regenerate deleted official artifacts without bumping the build number:

```powershell
dotnet run --project Node\scripts\ProtocolPackagePromoter\ProtocolPackagePromoter.csproj -- --workspace-root C:\myWork\HushNetworkOrg --version v1.1.1
```

Manual override bypasses auto-bump selection, but it does not bypass completeness checks:

- missing required files still fail closed;
- incomplete markers still produce `DraftPrivate`;
- package-owned metadata such as markdown `Version:` and JSON `packageVersion` is normalized to the
  selected version.

## Optional Arguments

| Argument | Purpose |
|---|---|
| `--workspace-root <path>` | Workspace root containing the Hush repositories |
| `--version <vX.Y.Z>` | Manual version override for recovery/tests |
| `--generated-at <timestamp>` | Manual UTC timestamp override for deterministic tests |
| `--package-id <id>` | Package id override; defaults to `omega-hushvoting-v1` |
| `--public-base-url <url>` | Base URL used in access locations |
| `--scaffold` | Create missing required source files as incomplete skeletons |

## Expected Output

A successful run prints:

```text
Promoted Protocol Omega package v1.1.2
Approval status: DraftPrivate
Generated at: 2026-05-05T...
Incomplete files: 35
Specification hash: ...
Proof hash: ...
Release manifest hash: ...
Catalog: ...
Official artifacts: ...
Website artifacts: ...
Public package repository artifacts: ...
```

If `Incomplete files` is greater than `0`, the package is intentionally draft/private and must not
be used to open elections.

## Verification

After changing the promoter, run:

```powershell
dotnet test HushServerNode.Tests\HushServerNode.Tests.csproj --no-restore --filter "FullyQualifiedName~ProtocolPackagePromotionServiceTests" --verbosity minimal
```

For a quick compile check:

```powershell
dotnet build Node\scripts\ProtocolPackagePromoter\ProtocolPackagePromoter.csproj --no-restore
```

## Build-Time Resolver

`HushServerNode.csproj` calls `Node/scripts/resolve-protocol-package-for-build.ps1` during build and
publish unless `ResolveProtocolPackageOnBuild=false` is supplied.

Debug behavior:

- reads local artifacts from the sibling `hush-documents` repository;
- selects the latest local development package;
- writes the selected package under the HushServerNode output folder:

```text
Node/Release/ProtocolPackages/
  SelectedProtocolPackage.json
  ApprovedProtocolPackageCatalog.json
  vX.Y.Z/
    ProtocolOmegaPackageManifest.json
    ...
```

If the package already resolved in the output has the same version and hashes as the latest local
HushDocuments package, the resolver does nothing. This keeps normal Debug builds fast and avoids
rewriting output files unnecessarily.

Release behavior:

- reads the package from GitHub Releases, defaulting to releases in
  `Hushnetwork-social/protocol-omega-packages`;
- selects the latest published approved even-minor package release using tag convention
  `ProtocolOmega-HushVoting-v1-vX.Y.Z`;
- downloads asset `Protocol-Omega-HushVoting-v1-Artifacts-vX.Y.Z.zip`;
- fails closed if no approved release package is available;
- uses `HUSH_PROTOCOL_PACKAGE_GITHUB_TOKEN`, `GH_TOKEN`, or `GITHUB_TOKEN` when the GitHub repository
  needs authentication or unauthenticated GitHub API rate limits are not enough.

The public `protocol-omega-packages` repository is the authoritative distribution registry for
Release builds. Its release workflow validates approved even-minor packages under
`hushvoting-v1/vX.Y.Z/`, creates the immutable release tag, and uploads the ZIP asset consumed by
the build resolver. Private authoring still happens in `hush-documents` and `hush-memory-bank`.

Manual build examples:

```powershell
dotnet build Node\HushServerNode\HushServerNode.csproj --no-restore
dotnet publish Node\HushServerNode\HushServerNode.csproj -c Release
dotnet build Node\HushServerNode\HushServerNode.csproj /p:ResolveProtocolPackageOnBuild=false
```
