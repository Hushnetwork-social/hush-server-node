# HushVotingPublicationProofPoc

Local synthetic SP-07 publication-proof PoC harness.

This tool exists to test the Hush/BabyJubJub vector-ballot publication-proof adapter before FEAT-117
touches the production election close path.

## Safety Boundary

- No server connection.
- No blockchain writes.
- No real voter data.
- No real vote secrets.
- No production private keys.
- Generated `private-witness.json` files are PoC/test-only and must not become audit package
  artifacts.

## Commands

```powershell
dotnet run --project Tools\HushVotingPublicationProofPoc\HushVotingPublicationProofPoc.csproj -- --help
dotnet run --project Tools\HushVotingPublicationProofPoc\HushVotingPublicationProofPoc.csproj -- run --output artifacts\sp07-poc
dotnet run --project Tools\HushVotingPublicationProofPoc\HushVotingPublicationProofPoc.csproj -- verify --input artifacts\sp07-poc
```

Custom benchmark vectors can be generated with explicit dimensions:

```powershell
dotnet build Tools\HushVotingPublicationProofPoc\HushVotingPublicationProofPoc.csproj -c Release --no-restore
dotnet Tools\HushVotingPublicationProofPoc\bin\Release\net9.0\HushVotingPublicationProofPoc.dll generate `
  --vector sp07-bg-hush-valid-n20-k4-v1 `
  --ballots 20 `
  --slots 4 `
  --output artifacts\sp07-benchmark-custom `
  --force
```

The `hotbench` command runs the matched C# language baseline used by `Tools/HushSp07RustWorker`.
It benchmarks only the component aggregation shape, not the full SP-07 proof:

```powershell
dotnet Tools\HushVotingPublicationProofPoc\bin\Release\net9.0\HushVotingPublicationProofPoc.dll hotbench `
  --ballots 1000 `
  --slots 8 `
  --rounds 2 `
  --mode windowed `
  --output artifacts\sp07-language-bench\csharp-n1000-k8.json
```

Use `--mode pippenger --window-bits 6` to test the C# BigInteger/Pippenger lane:

```powershell
dotnet Tools\HushVotingPublicationProofPoc\bin\Release\net9.0\HushVotingPublicationProofPoc.dll hotbench `
  --ballots 1000 `
  --slots 8 `
  --rounds 2 `
  --mode pippenger `
  --window-bits 6 `
  --output artifacts\sp07-language-bench\csharp-pippenger-n1000-k8.json
```

## Current Scope

The first version covers:

- deterministic synthetic accepted ballot generation;
- deterministic hidden ballot permutation;
- independent per-slot rerandomization;
- BabyJubJub point validation;
- direct hash-to-BabyJubJub commitment-key harness;
- accepted/published hash binding;
- private-witness relation checks;
- shared SP-07 proof library generation for the `m=1` matrix profile;
- profiled proof-generation timing output in `publication-proof-profile.json`;
- public proof bytes embedded in `publication-proof-transcript.json`;
- public `PUB-005` verification without loading `private-witness.json`;
- public privacy scan;
- tally replay hash binding.

Valid vectors now report:

```text
PASS
```

This is still a PoC harness, not the production close-path integration. The implemented public
proof slice supports the `matrix_m_1_publication_proof_v1` profile: outer shuffle commitments,
single-value product argument, and Hush vectorized multi-exponentiation argument. Future FEAT-117
work must still wire the shared library into production publication sessions and decide whether a
larger `m > 1` profile is needed for performance.

The current proof engine uses projective BabyJubJub arithmetic, faster field inversion,
incremental scalar powers, a windowed path for full-size scalar multiplication, slot-level
parallelism for vector exponentiation, fixed-point precomputation for commitment keys and the
election public key, parallel commitment accumulation for larger vectors, and parallel/projective
witness relation checks. The PoC harness also parallelizes synthetic ballot generation,
rerandomization, commitment-key generation, and private diagnostic witness checks.

The `generate` command includes synthetic artifact creation and local JSON IO, so it must not be
treated as the exact production close-worker cost. Larger chunk sizes and production proof-worker
hardware still need separate benchmark evidence.

Extended benchmark evidence is documented in:

```text
hush-memory-bank/Overview/ProtocolOmega/Protocol-Omega-HushVoting-v1-Artifacts/Protocol-Proof-And-Crypto-Review/SP-07-Hush-Benchmark-Evidence-2026-05-06.md
```

The verifier treats `private-witness.json` as optional. If it is removed, `PVA-006` is reported as
`NOT_APPLICABLE`, but `PUB-005` can still pass from public artifacts only.

## Default Vectors

| Vector | Ballots | Slots |
|---|---:|---:|
| `sp07-bg-hush-valid-n3-k2-v1` | 3 | 2 |
| `sp07-bg-hush-valid-n5-k3-v1` | 5 | 3 |
| `sp07-bg-hush-valid-n12-k4-v1` | 12 | 4 |

Generated output is written under `artifacts/sp07-poc` by default. The repository ignores
`artifacts/`, so generated PoC evidence does not get committed by accident.
