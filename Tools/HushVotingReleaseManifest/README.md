# HushVoting Release Manifest

Generates canonical SP-08 `HushVotingReleaseManifest-v1.json` files from release-manifest input
JSON. CI workflows should call this tool after collecting immutable component evidence.

```powershell
dotnet run --project hush-server-node/Tools/HushVotingReleaseManifest/HushVotingReleaseManifest.csproj -- `
  --input path/to/release-manifest-input.json `
  --output path/to/HushVotingReleaseManifest-v1.json `
  --hash-output path/to/HushVotingReleaseManifest-v1.sha256
```

The tool validates `official_sp08` inputs for required components, immutable artifact references,
circuit/key evidence, lifecycle bindings, and public privacy boundaries. Development inputs must use
`development_placeholder` with `not_for_release_integrity_claims = true`.
