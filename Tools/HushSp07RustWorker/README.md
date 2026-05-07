# HushSp07RustWorker

Rust SP-07 proof-worker PoC and production extraction point.

## PoC Outcome

PoC result: SUCCESS - production implementation approved for `m=1` Rust/arkworks proof worker,
external crypto review pending.

This means the Rust lane is no longer only a language/performance spike. It is the approved
production implementation path for the first `matrix_m_1_publication_proof_v1` worker profile. The
remaining work is production integration and hardening: canonical request/result contracts,
verifier-from-canonical-proof-bytes, parser validation, HushServerNode lifecycle orchestration,
witness custody/deletion, package export, CI packaging, and approved worker hardware profiling.

## Current Scope

The current implementation contains two benchmark modes:

### `--mode bigint`

Fair language benchmark, not the optimized Rust crypto worker.

It uses:

- `num-bigint` arbitrary-precision integers;
- the same BabyJubJub constants as the C# PoC;
- the same projective twisted-Edwards addition formula;
- the same 4-bit windowed scalar multiplication shape;
- the same `K` slots / two ciphertext components per slot aggregation shape.

It does **not** use:

- arkworks;
- fixed-width Montgomery field arithmetic;
- Pippenger/MSM;
- batch affine inversion;
- GPU acceleration;
- a production SP-07 proof generator.

That boundary matters. This tool answers: "does Rust alone, with equivalent BigInteger-style
arithmetic, improve the hot aggregation path?" It does not answer whether an optimized Rust crypto
engine can beat the C# implementation.

### `--mode arkworks`

Optimized Rust crypto benchmark.

It uses:

- `ark-ed-on-bn254` fixed-field arithmetic;
- arkworks variable-base MSM;
- coordinate conversion between Hush's `A=168700,D=168696` twisted-Edwards form and arkworks'
  normalized `a=1,d=168696/168700` BabyJubJub form;
- the same deterministic scalar source and aggregation shape as the C# hotbench.

It is still a hot-path benchmark, not a production SP-07 proof generator.

### `phasebench`

Optimized Rust proof-shaped phase benchmark.

It stacks several SP-07-like crypto phases in one round:

- accepted ciphertext-vector MSMs;
- published ciphertext-vector MSMs;
- rerandomized response-vector MSMs;
- product-argument commitment MSMs;
- multi-exponentiation commitment MSMs;
- a transcript-batched verifier-replay MSM slice.

This is closer to the publication-proof close/counting workload than the single `bench --mode
arkworks` aggregation. It is still not a complete Bayer-Groth proof payload: it does not implement
Fiat-Shamir transcript construction, product/Hadamard equations, canonical proof bytes, tamper
vectors, or final verifier result codes.

### `example`

Deterministic Rust proof-example artifact.

It adds the first non-benchmark proof file shape around the optimized phase work:

- `HushSp07BgStatementV1` fields;
- statement hash;
- deterministic `m=1` accepted-to-published outer shuffle wiring;
- the `m=1` single-value product sub-argument and verifier equations;
- the `m=1` Hush slot-vector multi-exponentiation sub-argument and verifier equations;
- deterministic Fiat-Shamir transcript state;
- challenge labels and scalar values;
- top-level verifier result-code mapping for the implemented subarguments;
- canonical public proof-byte envelope hash and byte length;
- optional tamper-vector replay with `--include-tamper-vectors`;
- proof-example hash.

It is still not a complete production publication proof. It does not yet implement the `m > 1`
Hadamard path or server/package integration.

The default `example` mode is lean and avoids the older placeholder phase commitments. Use
`--include-legacy-phase-artifacts` only when comparing against the earlier stacked example output.
The stacked crypto workload remains owned by `phasebench`.

## Commands

```powershell
cargo build --release

target\release\hush-sp07-rust-worker.exe bench `
  --ballots 1000 `
  --slots 8 `
  --rounds 2 `
  --mode bigint `
  --output ..\..\artifacts\sp07-language-bench\rust-n1000-k8.json
```

Optimized arkworks mode:

```powershell
target\release\hush-sp07-rust-worker.exe bench `
  --ballots 1000 `
  --slots 8 `
  --rounds 2 `
  --mode arkworks `
  --output ..\..\artifacts\sp07-language-bench\rust-arkworks-n1000-k8.json
```

Proof-shaped phase benchmark:

```powershell
target\release\hush-sp07-rust-worker.exe phasebench `
  --ballots 1000 `
  --slots 8 `
  --rounds 3 `
  --threads 2 `
  --output ..\..\artifacts\sp07-language-bench\rust-arkworks-phase-n1000-k8.json
```

Proof example artifact:

```powershell
target\release\hush-sp07-rust-worker.exe example `
  --ballots 1000 `
  --slots 8 `
  --threads 8 `
  --output ..\..\artifacts\sp07-rust-proof-example\rust-example-n1000-k8.json
```

Proof example with negative verifier replay:

```powershell
target\release\hush-sp07-rust-worker.exe example `
  --ballots 1000 `
  --slots 8 `
  --threads 8 `
  --include-tamper-vectors `
  --output ..\..\artifacts\sp07-rust-proof-example\rust-example-n1000-k8-tamper.json
```

Production-shaped process worker command:

```powershell
target\release\hush-sp07-rust-worker.exe prove `
  --input ..\..\artifacts\sp07-worker-job\proof-request.json `
  --output ..\..\artifacts\sp07-worker-job\proof-result.json `
  --workdir ..\..\artifacts\sp07-worker-job `
  --threads 8
```

Production-shaped public verifier command:

```powershell
target\release\hush-sp07-rust-worker.exe verify `
  --input ..\..\artifacts\sp07-worker-job\proof-result.json `
  --output ..\..\artifacts\sp07-worker-job\verify-result.json
```

HushServerNode production integration must call `prove` and `verify`. The `bench`, `phasebench`,
and `example` commands remain available as PoC/research tools.

In GitHub CI/CD the release worker is built before .NET tests and exposed through:

```text
HUSH_SP07_RUST_WORKER_PATH=Tools/HushSp07RustWorker/target/release/hush-sp07-rust-worker
```

In the AWS runtime container the worker is copied into:

```text
/app/tools/hush-sp07/hush-sp07-rust-worker
```

HushServerNode registers the worker client and chunk coordinator lazily. The process adapter reads:

```text
HUSH_SP07_RUST_WORKER_PATH
HUSH_SP07_RUST_WORKER_THREADS
HUSH_SP07_RUST_WORKER_TIMEOUT_SECONDS
```

The chunk coordinator can be configured through:

```text
Elections:Sp07PublicationProof:WorkRoot
Elections:Sp07PublicationProof:MaxParallelWorkers
Elections:Sp07PublicationProof:VerifyAfterProve
```

Run the matching C# hot-path benchmark from the `hush-server-node` root:

```powershell
dotnet Tools\HushVotingPublicationProofPoc\bin\Release\net9.0\HushVotingPublicationProofPoc.dll hotbench `
  --ballots 1000 `
  --slots 8 `
  --rounds 2 `
  --mode pippenger `
  --output artifacts\sp07-language-bench\csharp-n1000-k8.json
```

The checksums must match for the same `N,K` pair. Matching checksums prove that both tools used the
same deterministic base points, scalar vector, and group formula for the benchmark operation.

## Initial Evidence

On the current development workstation:

| Tool | N | K | Best round | Average round | Checksum |
|---|---:|---:|---:|---:|---|
| C# `System.Numerics.BigInteger` | 20 | 8 | 66.932ms | 66.932ms | matched |
| Rust `num-bigint` | 20 | 8 | 57.532ms | 57.532ms | matched |
| C# `System.Numerics.BigInteger` | 112 | 8 | 304.107ms | 400.017ms | matched |
| Rust `num-bigint` | 112 | 8 | 419.666ms | 434.823ms | matched |
| C# `System.Numerics.BigInteger` | 1000 | 8 | 2871.425ms | 2882.104ms | matched |
| Rust `num-bigint` | 1000 | 8 | 3438.088ms | 3647.821ms | matched |
| C# `BigInteger` + Pippenger | 112 | 8 | 72.344ms | 77.133ms | matched |
| Rust arkworks MSM | 112 | 8 | 17.293ms | 19.073ms | matched |
| C# `BigInteger` + Pippenger | 1000 | 8 | 323.834ms | 327.823ms | matched |
| Rust arkworks MSM | 1000 | 8 | 35.745ms | 36.694ms | matched |
| C# `BigInteger` + Pippenger | 2000 | 8 | 583.242ms | 595.861ms | matched |
| Rust arkworks MSM | 2000 | 8 | 48.486ms | 50.017ms | matched |

Interpretation: switching language without changing the arithmetic/MSM strategy does not provide the
needed improvement. Algorithmic MSM improves the C# hot path substantially. Rust becomes clearly
faster only when it uses a real crypto backend with fixed-field arithmetic and optimized MSM.

## Phase Benchmark Evidence

The `phasebench` command stacks multiple arkworks MSM groups per round. For `K=8`, each round
processes:

- `80` component MSM groups;
- `6` commitment MSM groups;
- `N * 86` point-scalar pairs.

On the current development workstation:

| Tool | N | K | Point-scalar pairs/round | Best round | Average round |
|---|---:|---:|---:|---:|---:|
| Rust arkworks phasebench, `--threads 2` | 1000 | 8 | 86,000 | 56.209ms | 58.598ms |
| Rust arkworks phasebench, `--threads 2` | 2000 | 8 | 172,000 | 98.404ms | 104.331ms |
| Rust arkworks phasebench, `--threads 2` | 5000 | 8 | 430,000 | 216.958ms | 226.590ms |

Interpretation: the optimized Rust lane remains comfortably under the 5-second phase target even
when the benchmark is expanded from a single ciphertext-vector aggregation into a stacked
proof-shaped workload. This is strong evidence for a Rust proof-worker lane, but it is not yet
evidence that the complete SP-07 public proof generator is finished.

## Proof Example Evidence

The `example` command produced deterministic transcript artifacts:

| Tool | N | K | Elapsed | Status |
|---|---:|---:|---:|---|
| Rust arkworks wired example, `--threads 8` | 1000 | 8 | 119.007ms | `PUB-005`, outer + product + multi-exp + canonical bytes pass |
| Rust arkworks wired example, `--threads 8` | 2000 | 8 | 195.627ms | `PUB-005`, outer + product + multi-exp + canonical bytes pass |
| Rust arkworks wired example, `--threads 8` | 5000 | 8 | 397.852ms | `PUB-005`, outer + product + multi-exp + canonical bytes pass |
| Rust arkworks wired example + tamper replay, `--threads 8` | 1000 | 8 | 239.392ms | `PUB-005`, 8/8 tamper vectors rejected |
| Rust arkworks wired example + tamper replay, `--threads 8` | 5000 | 8 | 774.868ms | `PUB-005`, 8/8 tamper vectors rejected |
| Rust arkworks legacy example, `--threads 8` | 5000 | 8 | 934.828ms | product + multi-exp pass with legacy placeholder work enabled |

Artifacts:

```text
hush-server-node/artifacts/sp07-rust-proof-example/rust-example-n12-k4.json
hush-server-node/artifacts/sp07-rust-proof-example/rust-example-n1000-k8.json
hush-server-node/artifacts/sp07-rust-proof-example/rust-example-n2000-k8-optimized-serial2.json
hush-server-node/artifacts/sp07-rust-proof-example/rust-example-n5000-k8-optimized-serial.json
```

Interpretation: the Rust lane now has a stable statement/transcript artifact shape, deterministic
accepted-to-published `m=1` outer shuffle wiring, two real publicly verified subarguments, and a
top-level `PUB-005` verifier result for the implemented path. It now also binds a canonical public
proof-byte envelope and can run optional insert/remove/duplicate/replace-style tamper replay. The
next step is server/package integration and then the generalized `m > 1` path if selected for larger
chunks.

The current `example` timing includes synthetic base generation and JSON artifact shaping. It is not
a pure proof-equation benchmark. The artifact now reports timing buckets so that setup, statement
hashing, subargument generation/verification, legacy phase work, Fiat-Shamir derivation, and final
hashing can be tracked separately. The older placeholder phase artifacts are disabled by default
because they double-count work already covered by `phasebench`.

The `--threads 2` phasebench profile is intentionally recorded because this nested MSM workload is
faster with a small Rayon pool than with all logical processors. More threads improve some setup
steps, but they slow the repeated MSM phase through scheduling and memory contention. Production
workers should benchmark and pin their own worker-thread profile instead of assuming "all cores" is
fastest.
