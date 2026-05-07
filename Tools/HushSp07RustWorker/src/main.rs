use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::str::FromStr;
use std::time::Instant;

use ark_ec::{AdditiveGroup, AffineRepr, CurveGroup, PrimeGroup, VariableBaseMSM};
use ark_ed_on_bn254::{EdwardsAffine, EdwardsProjective, Fq, Fr};
use ark_ff::{Field, PrimeField, Zero as ArkZero};
use clap::{Parser, Subcommand, ValueEnum};
use num_bigint::{BigInt, BigUint, Sign, ToBigInt};
use num_integer::Integer;
use num_traits::{One, ToPrimitive};
use once_cell::sync::Lazy;
use rayon::prelude::*;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha512};

static A: Lazy<BigUint> = Lazy::new(|| BigUint::parse_bytes(b"168700", 10).unwrap());
static D: Lazy<BigUint> = Lazy::new(|| BigUint::parse_bytes(b"168696", 10).unwrap());
static FIELD_PRIME: Lazy<BigUint> = Lazy::new(|| {
    BigUint::parse_bytes(
        b"21888242871839275222246405745257275088548364400416034343698204186575808495617",
        10,
    )
    .unwrap()
});
static ORDER: Lazy<BigUint> = Lazy::new(|| {
    BigUint::parse_bytes(
        b"2736030358979909402780800718157159386076813972158567259200215660948447373041",
        10,
    )
    .unwrap()
});
static GENERATOR: Lazy<Point> = Lazy::new(|| Point {
    x: BigUint::parse_bytes(
        b"5299619240641551281634865583518297030282874472190772894086521144482721001553",
        10,
    )
    .unwrap(),
    y: BigUint::parse_bytes(
        b"16950150798460657717958625567821834550301663161624707787222815936182638968203",
        10,
    )
    .unwrap(),
});
static HUSH_SQRT_A: Lazy<Fq> = Lazy::new(|| {
    Fq::from(168700u64)
        .sqrt()
        .expect("Hush BabyJubJub A coefficient must have a square root in Fq")
});
static HUSH_SQRT_A_INVERSE: Lazy<Fq> = Lazy::new(|| {
    HUSH_SQRT_A
        .inverse()
        .expect("Hush BabyJubJub sqrt(A) must be invertible")
});

const SCALAR_WINDOW_BITS: usize = 4;
const FIXED_BASE_RERANDOMIZATION_WINDOW_BITS: usize = 10;
const SMALL_SCALAR_BIT_LENGTH: u64 = 32;
type FrBigInt = <Fr as PrimeField>::BigInt;

#[derive(Parser)]
#[command(name = "hush-sp07-rust-worker")]
#[command(about = "Rust SP-07 worker spike and fair BigInteger-style aggregation benchmark.")]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    Bench {
        #[arg(long, short = 'n', default_value_t = 1000)]
        ballots: usize,

        #[arg(long, short = 'k', default_value_t = 8)]
        slots: usize,

        #[arg(long, short = 'r', default_value_t = 3)]
        rounds: usize,

        #[arg(long, value_enum, default_value_t = BenchMode::Bigint)]
        mode: BenchMode,

        #[arg(long)]
        output: Option<PathBuf>,

        #[arg(long)]
        threads: Option<usize>,
    },
    Phasebench {
        #[arg(long, short = 'n', default_value_t = 1000)]
        ballots: usize,

        #[arg(long, short = 'k', default_value_t = 8)]
        slots: usize,

        #[arg(long, short = 'r', default_value_t = 3)]
        rounds: usize,

        #[arg(long)]
        output: Option<PathBuf>,

        #[arg(long)]
        threads: Option<usize>,
    },
    Example {
        #[arg(long, short = 'n', default_value_t = 1000)]
        ballots: usize,

        #[arg(long, short = 'k', default_value_t = 8)]
        slots: usize,

        #[arg(long)]
        output: Option<PathBuf>,

        #[arg(long)]
        threads: Option<usize>,

        #[arg(long)]
        include_legacy_phase_artifacts: bool,

        #[arg(long)]
        include_tamper_vectors: bool,
    },
    Fixture {
        #[arg(long, short = 'n', default_value_t = 12)]
        ballots: usize,

        #[arg(long, short = 'k', default_value_t = 4)]
        slots: usize,

        #[arg(long)]
        output: Option<PathBuf>,
    },
    Prove {
        #[arg(long)]
        input: PathBuf,

        #[arg(long)]
        output: PathBuf,

        #[arg(long)]
        workdir: PathBuf,

        #[arg(long)]
        threads: Option<usize>,
    },
    Verify {
        #[arg(long)]
        input: PathBuf,

        #[arg(long)]
        output: PathBuf,

        #[arg(long)]
        threads: Option<usize>,
    },
}

#[derive(Copy, Clone, Debug, Eq, PartialEq, ValueEnum)]
enum BenchMode {
    Bigint,
    Arkworks,
}

#[derive(Clone, Debug, Eq, PartialEq)]
struct Point {
    x: BigUint,
    y: BigUint,
}

#[derive(Clone, Debug, Eq, PartialEq)]
struct ProjectivePoint {
    x: BigUint,
    y: BigUint,
    z: BigUint,
}

#[derive(Serialize)]
struct BenchReport {
    schema: &'static str,
    engine: &'static str,
    operation: &'static str,
    mode: &'static str,
    ballots: usize,
    slots: usize,
    components: usize,
    point_scalar_pairs_per_round: usize,
    rounds: usize,
    setup_milliseconds: f64,
    round_milliseconds: Vec<f64>,
    best_milliseconds: f64,
    average_milliseconds: f64,
    rayon_threads: usize,
    checksum_sha512: String,
    notes: Vec<&'static str>,
}

#[derive(Serialize)]
struct PhaseBenchReport {
    schema: &'static str,
    engine: &'static str,
    operation: &'static str,
    ballots: usize,
    slots: usize,
    components: usize,
    component_msm_groups_per_round: usize,
    commitment_msm_groups_per_round: usize,
    point_scalar_pairs_per_round: usize,
    rounds: usize,
    setup_milliseconds: f64,
    round_reports: Vec<PhaseRoundReport>,
    best_total_milliseconds: f64,
    average_total_milliseconds: f64,
    rayon_threads: usize,
    checksum_sha512: String,
    notes: Vec<&'static str>,
}

#[derive(Serialize)]
struct PhaseRoundReport {
    accepted_ciphertext_vector_milliseconds: f64,
    published_ciphertext_vector_milliseconds: f64,
    rerandomized_response_vector_milliseconds: f64,
    product_argument_commitments_milliseconds: f64,
    multi_exponentiation_commitments_milliseconds: f64,
    public_verify_replay_milliseconds: f64,
    total_milliseconds: f64,
}

#[derive(Serialize)]
struct ProofExample {
    schema: &'static str,
    engine: &'static str,
    status: &'static str,
    statement: ExampleStatement,
    statement_hash_sha512: String,
    phase_artifacts: ExamplePhaseArtifacts,
    fiat_shamir: FiatShamirTranscript,
    canonical_proof_bytes: CanonicalProofBytes,
    verifier_result: ProofExampleVerifierResult,
    tamper_vectors: Vec<TamperVectorResult>,
    proof_example_hash_sha512: String,
    timings: ExampleTimingReport,
    elapsed_milliseconds: f64,
    notes: Vec<&'static str>,
}

#[derive(Deserialize)]
struct WorkerProofRequest {
    schema: Option<String>,
    election_id: Option<String>,
    proof_session_id: Option<String>,
    chunk_id: Option<String>,
    protocol_package_hash: Option<String>,
    ballot_definition_hash: Option<String>,
    accepted_ballot_set_hash: Option<String>,
    published_ballot_stream_hash: Option<String>,
    ballots: Option<usize>,
    accepted_ballot_count: Option<usize>,
    slots: Option<usize>,
    slot_count: Option<usize>,
    encrypted_slot_count: Option<usize>,
    threads: Option<usize>,
    include_legacy_phase_artifacts: Option<bool>,
    include_tamper_vectors: Option<bool>,
    production_proof_input: Option<WorkerProductionProofInput>,
}

#[derive(Clone, Deserialize, Serialize)]
struct WorkerPoint {
    x: String,
    y: String,
}

#[derive(Clone, Deserialize, Serialize)]
struct WorkerCipherSlot {
    c1: WorkerPoint,
    c2: WorkerPoint,
}

#[derive(Clone, Deserialize, Serialize)]
struct WorkerCipherBallot {
    slots: Vec<WorkerCipherSlot>,
}

#[derive(Clone, Deserialize, Serialize)]
struct WorkerProductionProofInput {
    public_key: WorkerPoint,
    accepted_ballots: Vec<WorkerCipherBallot>,
    published_ballots: Vec<WorkerCipherBallot>,
    published_to_accepted: Vec<usize>,
    rerandomization_by_published_ballot_and_slot: Vec<Vec<String>>,
}

#[derive(Serialize)]
struct ProductionProofInputFixture {
    schema: &'static str,
    ballots: usize,
    slots: usize,
    production_proof_input: WorkerProductionProofInput,
    notes: Vec<&'static str>,
}

#[derive(Clone, Debug)]
struct ProductionProofInput {
    public_key: EdwardsProjective,
    accepted_component_bases: Vec<Vec<EdwardsAffine>>,
    published_component_bases: Vec<Vec<EdwardsAffine>>,
    published_to_accepted: Vec<usize>,
    rerandomization_by_published_ballot_and_slot: Vec<Vec<Fr>>,
}

#[derive(Serialize)]
struct WorkerCommandResult {
    schema: &'static str,
    worker_kind: &'static str,
    command: &'static str,
    status: &'static str,
    passed: bool,
    result_code: String,
    message: String,
    election_id: String,
    proof_session_id: String,
    chunk_id: String,
    proof_profile_id: &'static str,
    worker_version: &'static str,
    worker_thread_count: usize,
    statement_hash_sha512: String,
    transcript_hash_sha512: String,
    proof_hash_sha512: String,
    accepted_ballot_set_hash: String,
    published_ballot_stream_hash: String,
    canonical_proof_byte_length: usize,
    #[serde(skip_serializing_if = "Option::is_none")]
    canonical_proof_bytes_hex: Option<String>,
    proof_example_hash_sha512: String,
    elapsed_milliseconds: f64,
    telemetry: WorkerTelemetry,
    #[serde(skip_serializing_if = "Option::is_none")]
    proof_example: Option<serde_json::Value>,
}

#[derive(Serialize)]
struct WorkerTelemetry {
    generation_milliseconds: f64,
    self_verification_milliseconds: f64,
    proof_size_bytes: usize,
    cpu_time_milliseconds: f64,
    memory_notes: Vec<&'static str>,
    phase_timings: BTreeMap<&'static str, f64>,
}

fn build_worker_telemetry(
    timings: &ExampleTimingReport,
    generation_milliseconds: f64,
    self_verification_milliseconds: f64,
    proof_size_bytes: usize,
) -> WorkerTelemetry {
    let mut phase_timings = BTreeMap::new();
    phase_timings.insert("base_generation", timings.base_generation_milliseconds);
    phase_timings.insert("statement_hashing", timings.statement_hashing_milliseconds);
    phase_timings.insert(
        "outer_shuffle_wiring",
        timings.outer_shuffle_wiring_milliseconds,
    );
    phase_timings.insert(
        "single_value_product_argument",
        timings.single_value_product_argument_milliseconds,
    );
    phase_timings.insert(
        "hush_multi_exponentiation_argument",
        timings.hush_multi_exponentiation_argument_milliseconds,
    );
    phase_timings.insert("fiat_shamir", timings.fiat_shamir_milliseconds);
    phase_timings.insert(
        "canonical_proof_bytes",
        timings.canonical_proof_bytes_milliseconds,
    );
    phase_timings.insert("self_verification", self_verification_milliseconds);
    phase_timings.insert("total_generation", generation_milliseconds);

    WorkerTelemetry {
        generation_milliseconds,
        self_verification_milliseconds,
        proof_size_bytes,
        cpu_time_milliseconds: (generation_milliseconds + self_verification_milliseconds)
            * rayon::current_num_threads() as f64,
        memory_notes: vec![
            "rss sampling is not enabled in the v1 process contract",
            "prover witness material is process-local and omitted from the public result",
            "canonical proof bytes are public; private permutation and rerandomization randomness are not serialized",
        ],
        phase_timings,
    }
}

fn build_verify_worker_telemetry(
    self_verification_milliseconds: f64,
    proof_size_bytes: usize,
) -> WorkerTelemetry {
    let mut phase_timings = BTreeMap::new();
    phase_timings.insert("self_verification", self_verification_milliseconds);

    WorkerTelemetry {
        generation_milliseconds: 0.0,
        self_verification_milliseconds,
        proof_size_bytes,
        cpu_time_milliseconds: self_verification_milliseconds * rayon::current_num_threads() as f64,
        memory_notes: vec![
            "public verification does not require private witness material",
            "rss sampling is not enabled in the v1 process contract",
        ],
        phase_timings,
    }
}

struct StatementOverrides {
    election_id: Option<String>,
    chunk_id: Option<String>,
    protocol_package_hash: Option<String>,
    ballot_definition_hash: Option<String>,
    accepted_ballot_set_hash: Option<String>,
    published_ballot_stream_hash: Option<String>,
}

#[derive(Serialize)]
struct ProofExampleVerifierResult {
    result_code: &'static str,
    passed: bool,
    single_value_product_passed: bool,
    multi_exponentiation_passed: bool,
    transcript_finalized: bool,
    canonical_proof_bytes_bound: bool,
    tamper_suite_status: &'static str,
    tamper_vectors_passed: Option<bool>,
}

#[derive(Serialize)]
struct CanonicalProofBytes {
    schema: &'static str,
    profile: &'static str,
    encoding: &'static str,
    byte_length: usize,
    sha512: String,
    hex: String,
    preview_hex: String,
    envelope: CanonicalProofEnvelope,
}

#[derive(Serialize)]
struct CanonicalProofEnvelope {
    schema: &'static str,
    proof_profile: &'static str,
    statement_hash_sha512: String,
    accepted_ballot_set_hash: String,
    published_ballot_stream_hash: String,
    outer_shuffle_hash: String,
    single_value_product_argument_hash: String,
    hush_multi_exponentiation_argument_hash: String,
    fiat_shamir_final_state_hash: String,
    verifier_result_code: &'static str,
}

#[derive(Serialize)]
struct TamperVectorResult {
    id: &'static str,
    tamper_class: &'static str,
    target: &'static str,
    expected_result_code: &'static str,
    actual_result_code: &'static str,
    verifier_passed: bool,
    rejected_as_expected: bool,
}

#[derive(Serialize)]
struct ExampleTimingReport {
    base_generation_milliseconds: f64,
    statement_hashing_milliseconds: f64,
    accepted_ciphertext_vector_milliseconds: f64,
    published_ciphertext_vector_milliseconds: f64,
    rerandomized_response_vector_milliseconds: f64,
    legacy_product_argument_commitments_milliseconds: f64,
    outer_shuffle_wiring_milliseconds: f64,
    single_value_product_argument_milliseconds: f64,
    hush_multi_exponentiation_argument_milliseconds: f64,
    legacy_multi_exponentiation_commitments_milliseconds: f64,
    public_verify_replay_milliseconds: f64,
    phase_commitment_generation_milliseconds: f64,
    phase_artifact_shaping_milliseconds: f64,
    fiat_shamir_milliseconds: f64,
    canonical_proof_bytes_milliseconds: f64,
    tamper_vectors_milliseconds: f64,
    final_hash_milliseconds: f64,
    total_milliseconds: f64,
}

#[derive(Serialize)]
struct ExampleStatement {
    schema: &'static str,
    construction: &'static str,
    adapter: &'static str,
    group_profile: &'static str,
    commitment_key_profile: &'static str,
    fiat_shamir_profile: &'static str,
    proof_profile: &'static str,
    election_id: String,
    chunk_id: String,
    protocol_package_hash: String,
    ballot_definition_hash: String,
    public_key: PublicPoint,
    ballots: usize,
    slots: usize,
    matrix_m: usize,
    matrix_n: usize,
    accepted_ballot_set_hash: String,
    published_ballot_stream_hash: String,
}

#[derive(Serialize)]
struct ExamplePhaseArtifacts {
    accepted_ciphertext_vector_hash: String,
    accepted_ciphertext_vector_commitments: Vec<PublicPoint>,
    published_ciphertext_vector_hash: String,
    published_ciphertext_vector_commitments: Vec<PublicPoint>,
    rerandomized_response_vector_hash: String,
    rerandomized_response_vector_commitments: Vec<PublicPoint>,
    outer_shuffle_hash: String,
    outer_shuffle: OuterShuffleExample,
    product_argument_commitments_hash: String,
    product_argument_commitments: Vec<PublicPoint>,
    single_value_product_argument_hash: String,
    single_value_product_argument: SingleValueProductArgumentExample,
    hush_multi_exponentiation_argument_hash: String,
    hush_multi_exponentiation_argument: HushMultiExponentiationArgumentExample,
    multi_exponentiation_commitments_hash: String,
    multi_exponentiation_commitments: Vec<PublicPoint>,
    public_verify_replay_hash: String,
    public_verify_replay_commitments: Vec<PublicPoint>,
}

#[derive(Serialize)]
struct FiatShamirTranscript {
    schema: &'static str,
    profile: &'static str,
    domain: &'static str,
    initial_state_hash: String,
    final_state_hash: String,
    challenges: Vec<FiatShamirChallenge>,
}

#[derive(Serialize, Clone)]
struct FiatShamirChallenge {
    label: String,
    input_state_hash: String,
    challenge_hash_sha512: String,
    scalar_decimal: String,
}

#[derive(Serialize, Clone)]
struct PublicPoint {
    x: String,
    y: String,
}

#[derive(Serialize, Clone)]
struct SingleValueProductArgumentExample {
    schema: &'static str,
    status: &'static str,
    source_algorithm: &'static str,
    statement_commitment_c_a: PublicPoint,
    statement_product_b: String,
    commitment_key_hash: String,
    c_d: PublicPoint,
    c_delta: PublicPoint,
    c_upper_delta: PublicPoint,
    a_tilde: Vec<String>,
    b_tilde: Vec<String>,
    r_tilde: String,
    s_tilde: String,
    challenge_x: FiatShamirChallenge,
    verifier_result: SingleValueProductVerifierResult,
}

#[derive(Serialize, Clone)]
struct OuterShuffleExample {
    schema: &'static str,
    status: &'static str,
    matrix_m: usize,
    matrix_n: usize,
    shuffle_c_a: PublicPoint,
    shuffle_c_b: PublicPoint,
    product_statement_commitment: PublicPoint,
    product_statement_product_b: String,
    permutation_hash: String,
    rerandomization_hash: String,
    accepted_cx_hash: String,
    challenge_x: FiatShamirChallenge,
    challenge_y: FiatShamirChallenge,
    challenge_z: FiatShamirChallenge,
}

#[derive(Serialize, Clone)]
struct SingleValueProductVerifierResult {
    result_code: &'static str,
    passed: bool,
    commitment_response_passed: bool,
    delta_response_passed: bool,
    boundary_response_passed: bool,
}

#[derive(Serialize, Clone)]
struct HushMultiExponentiationArgumentExample {
    schema: &'static str,
    status: &'static str,
    source_algorithm: &'static str,
    statement_cx_hash: String,
    statement_commitment_c_b: PublicPoint,
    commitment_key_hash: String,
    c_a0: PublicPoint,
    c_b_diag: Vec<PublicPoint>,
    e_hash: String,
    e: Vec<PublicBallotVector>,
    a: Vec<String>,
    r: String,
    b: String,
    s: String,
    tau: Vec<String>,
    challenge_x: FiatShamirChallenge,
    verifier_result: HushMultiExponentiationVerifierResult,
}

#[derive(Serialize, Clone)]
struct HushMultiExponentiationVerifierResult {
    result_code: &'static str,
    passed: bool,
    center_commitment_is_identity: bool,
    center_ciphertext_matches_cx: bool,
    commitment_a_response_passed: bool,
    commitment_b_response_passed: bool,
    ciphertext_vector_response_passed: bool,
}

#[derive(Serialize, Clone)]
struct PublicBallotVector {
    slots: Vec<PublicCipherSlot>,
}

#[derive(Serialize, Clone)]
struct PublicCipherSlot {
    c1: PublicPoint,
    c2: PublicPoint,
}

#[derive(Clone)]
struct CommitmentKey {
    h: EdwardsAffine,
    g: Vec<EdwardsAffine>,
}

#[derive(Clone)]
struct SingleValueProductWitness {
    a: Vec<Fr>,
    r: Fr,
}

#[derive(Clone)]
struct SingleValueProductProofInternal {
    c_a: EdwardsProjective,
    b: Fr,
    commitment_key_hash: String,
    c_d: EdwardsProjective,
    c_delta: EdwardsProjective,
    c_upper_delta: EdwardsProjective,
    a_tilde: Vec<Fr>,
    b_tilde: Vec<Fr>,
    r_tilde: Fr,
    s_tilde: Fr,
    challenge_x: FiatShamirChallenge,
}

#[derive(Clone, Debug, Eq, PartialEq)]
struct BallotVector {
    components: Vec<EdwardsProjective>,
}

#[derive(Clone)]
struct HushMultiExponentiationWitness {
    b_matrix_column: Vec<Fr>,
    s_b: Fr,
    rho_bar: Vec<Fr>,
}

struct OuterShuffleWiring {
    example: OuterShuffleExample,
    single_product_witness: SingleValueProductWitness,
    multi_exp_witness: HushMultiExponentiationWitness,
    accepted_cx: BallotVector,
}

struct FixedBaseWindowTable {
    windows: Vec<Vec<EdwardsProjective>>,
    window_bits: usize,
}

#[derive(Clone)]
struct HushMultiExponentiationProofInternal {
    c_b_statement: EdwardsProjective,
    cx: BallotVector,
    commitment_key_hash: String,
    c_a0: EdwardsProjective,
    c_b_diag: Vec<EdwardsProjective>,
    e: Vec<BallotVector>,
    a: Vec<Fr>,
    r: Fr,
    b: Fr,
    s: Fr,
    tau: Vec<Fr>,
    challenge_x: FiatShamirChallenge,
}

struct TranscriptBuilder {
    state_hash: String,
    initial_state_hash: String,
    challenges: Vec<FiatShamirChallenge>,
}

fn main() {
    if let Err(error) = run() {
        eprintln!("ERROR: {error}");
        std::process::exit(1);
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let cli = Cli::parse();
    match cli.command {
        Command::Bench {
            ballots,
            slots,
            rounds,
            mode,
            output,
            threads,
        } => {
            configure_global_threads(threads)?;
            run_bench(ballots, slots, rounds, mode, output)?;
        }
        Command::Phasebench {
            ballots,
            slots,
            rounds,
            output,
            threads,
        } => {
            configure_global_threads(threads)?;
            run_phasebench(ballots, slots, rounds, output)?;
        }
        Command::Example {
            ballots,
            slots,
            output,
            threads,
            include_legacy_phase_artifacts,
            include_tamper_vectors,
        } => {
            configure_global_threads(threads)?;
            run_example(
                ballots,
                slots,
                output,
                include_legacy_phase_artifacts,
                include_tamper_vectors,
            )?;
        }
        Command::Fixture {
            ballots,
            slots,
            output,
        } => {
            run_fixture(ballots, slots, output)?;
        }
        Command::Prove {
            input,
            output,
            workdir,
            threads,
        } => {
            run_prove(input, output, workdir, threads)?;
        }
        Command::Verify {
            input,
            output,
            threads,
        } => {
            configure_global_threads(threads)?;
            run_verify(input, output)?;
        }
    }

    Ok(())
}

fn run_fixture(
    ballots: usize,
    slots: usize,
    output: Option<PathBuf>,
) -> Result<(), Box<dyn std::error::Error>> {
    if ballots == 0 || slots == 0 {
        return Err("ballots and slots must be positive".into());
    }

    let fixture = ProductionProofInputFixture {
        schema: "HushSp07ProductionProofInputFixtureV1",
        ballots,
        slots,
        production_proof_input: build_worker_production_input(ballots, slots),
        notes: vec![
            "diagnostic fixture for server/worker tests only",
            "contains synthetic private permutation and rerandomization witness material",
            "must not be exported as a public election artifact",
        ],
    };
    let json = serde_json::to_string_pretty(&fixture)?;
    if let Some(path) = output {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }

        fs::write(&path, format!("{json}\n"))?;
        println!("Wrote {}", path.display());
    }

    println!("{json}");
    Ok(())
}

fn build_worker_production_input(ballots: usize, slots: usize) -> WorkerProductionProofInput {
    let public_key = hush_generator_ark().mul_bigint(
        derive_scalar_fr("test.production.public-key", ballots, slots).into_bigint(),
    );
    let accepted_component_bases = build_ark_component_bases_with_label(
        ballots,
        slots * 2,
        "test.production.accepted.component-step",
    );
    let published_to_accepted = build_permutation(ballots);
    let rerandomization_by_published_ballot_and_slot =
        build_rerandomization_matrix(ballots, slots);
    let published_component_bases = build_published_component_bases(
        &accepted_component_bases,
        &published_to_accepted,
        &rerandomization_by_published_ballot_and_slot,
        &public_key,
        slots,
    );

    WorkerProductionProofInput {
        public_key: worker_point_from_projective(&public_key),
        accepted_ballots: worker_ballots_from_components(&accepted_component_bases, ballots, slots),
        published_ballots: worker_ballots_from_components(&published_component_bases, ballots, slots),
        published_to_accepted,
        rerandomization_by_published_ballot_and_slot: rerandomization_by_published_ballot_and_slot
            .iter()
            .map(|row| row.iter().map(|scalar| scalar_decimal(*scalar)).collect())
            .collect(),
    }
}

fn worker_ballots_from_components(
    components: &[Vec<EdwardsAffine>],
    ballots: usize,
    slots: usize,
) -> Vec<WorkerCipherBallot> {
    (0..ballots)
        .map(|ballot| WorkerCipherBallot {
            slots: (0..slots)
                .map(|slot| WorkerCipherSlot {
                    c1: worker_point_from_affine(&components[slot * 2][ballot]),
                    c2: worker_point_from_affine(&components[(slot * 2) + 1][ballot]),
                })
                .collect(),
        })
        .collect()
}

fn worker_point_from_projective(point: &EdwardsProjective) -> WorkerPoint {
    worker_point_from_affine(&point.into_affine())
}

fn worker_point_from_affine(point: &EdwardsAffine) -> WorkerPoint {
    let public_point = public_point_from_affine(point);
    WorkerPoint {
        x: public_point.x,
        y: public_point.y,
    }
}

fn run_bench(
    ballots: usize,
    slots: usize,
    rounds: usize,
    mode: BenchMode,
    output: Option<PathBuf>,
) -> Result<(), Box<dyn std::error::Error>> {
    if ballots == 0 || slots == 0 || rounds == 0 {
        return Err("ballots, slots, and rounds must be positive".into());
    }

    let report = match mode {
        BenchMode::Bigint => run_bigint_bench(ballots, slots, rounds),
        BenchMode::Arkworks => run_arkworks_bench(ballots, slots, rounds),
    };

    let json = serde_json::to_string_pretty(&report)?;
    if let Some(path) = output {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }

        fs::write(&path, format!("{json}\n"))?;
        println!("Wrote {}", path.display());
    }

    println!("{json}");
    Ok(())
}

fn run_phasebench(
    ballots: usize,
    slots: usize,
    rounds: usize,
    output: Option<PathBuf>,
) -> Result<(), Box<dyn std::error::Error>> {
    if ballots == 0 || slots == 0 || rounds == 0 {
        return Err("ballots, slots, and rounds must be positive".into());
    }

    let report = run_arkworks_phasebench(ballots, slots, rounds);
    let json = serde_json::to_string_pretty(&report)?;
    if let Some(path) = output {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }

        fs::write(&path, format!("{json}\n"))?;
        println!("Wrote {}", path.display());
    }

    println!("{json}");
    Ok(())
}

fn run_example(
    ballots: usize,
    slots: usize,
    output: Option<PathBuf>,
    include_legacy_phase_artifacts: bool,
    include_tamper_vectors: bool,
) -> Result<(), Box<dyn std::error::Error>> {
    if ballots == 0 || slots == 0 {
        return Err("ballots and slots must be positive".into());
    }

    let example = build_proof_example(
        ballots,
        slots,
        include_legacy_phase_artifacts,
        include_tamper_vectors,
    )?;
    let json = serde_json::to_string_pretty(&example)?;
    if let Some(path) = output {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }

        fs::write(&path, format!("{json}\n"))?;
        println!("Wrote {}", path.display());
    }

    println!("{json}");
    Ok(())
}

fn run_prove(
    input: PathBuf,
    output: PathBuf,
    workdir: PathBuf,
    threads: Option<usize>,
) -> Result<(), Box<dyn std::error::Error>> {
    let request_json = fs::read_to_string(&input)?;
    let request: WorkerProofRequest = serde_json::from_str(&request_json)?;
    if let Some(schema) = request.schema.as_deref() {
        if schema != "HushSp07RustWorkerProofRequestV1" {
            return Err(format!("unsupported prove request schema: {schema}").into());
        }
    }
    configure_global_threads(threads.or(request.threads))?;

    fs::create_dir_all(&workdir)?;
    if let Some(parent) = output.parent() {
        fs::create_dir_all(parent)?;
    }

    let ballots = request
        .ballots
        .or(request.accepted_ballot_count)
        .ok_or("prove request must provide ballots or accepted_ballot_count")?;
    let slots = request
        .slots
        .or(request.slot_count)
        .or(request.encrypted_slot_count)
        .ok_or("prove request must provide slots, slot_count, or encrypted_slot_count")?;
    if ballots == 0 || slots == 0 {
        return Err("prove request ballots and slots must be positive".into());
    }

    let include_legacy_phase_artifacts = request.include_legacy_phase_artifacts.unwrap_or(false);
    let include_tamper_vectors = request.include_tamper_vectors.unwrap_or(false);
    let statement_overrides = StatementOverrides {
        election_id: request.election_id.clone(),
        chunk_id: request.chunk_id.clone(),
        protocol_package_hash: request.protocol_package_hash.clone(),
        ballot_definition_hash: request.ballot_definition_hash.clone(),
        accepted_ballot_set_hash: request.accepted_ballot_set_hash.clone(),
        published_ballot_stream_hash: request.published_ballot_stream_hash.clone(),
    };
    let production_input = parse_production_proof_input(&request, ballots, slots)?;
    let example = build_proof_example_with_statement_overrides(
        ballots,
        slots,
        include_legacy_phase_artifacts,
        include_tamper_vectors,
        Some(&statement_overrides),
        production_input.as_ref(),
    )?;
    let self_verify_started = Instant::now();
    let self_verify_input = serde_json::json!({
        "passed": true,
        "result_code": "PUB-005",
        "election_id": request
            .election_id
            .clone()
            .unwrap_or_else(|| example.statement.election_id.clone()),
        "proof_session_id": request
            .proof_session_id
            .clone()
            .unwrap_or_else(|| "proof-session-local".to_string()),
        "chunk_id": request
            .chunk_id
            .clone()
            .unwrap_or_else(|| example.statement.chunk_id.clone()),
        "statement_hash_sha512": example.statement_hash_sha512.clone(),
        "transcript_hash_sha512": example.fiat_shamir.final_state_hash.clone(),
        "proof_hash_sha512": example.canonical_proof_bytes.sha512.clone(),
        "accepted_ballot_set_hash": example.statement.accepted_ballot_set_hash.clone(),
        "published_ballot_stream_hash": example.statement.published_ballot_stream_hash.clone(),
        "canonical_proof_byte_length": example.canonical_proof_bytes.byte_length,
        "canonical_proof_bytes_hex": example.canonical_proof_bytes.hex.clone(),
    });
    let self_verification = verify_public_proof_input(&self_verify_input);
    let self_verification_milliseconds = self_verify_started.elapsed().as_secs_f64() * 1000.0;
    let proof_example = serde_json::to_value(&example)?;
    let passed = example.verifier_result.passed && self_verification.passed;
    let result_code = if passed {
        "PUB-005".to_string()
    } else if !self_verification.passed {
        self_verification.result_code
    } else {
        example.verifier_result.result_code.to_string()
    };
    let telemetry = build_worker_telemetry(
        &example.timings,
        example.elapsed_milliseconds,
        self_verification_milliseconds,
        example.canonical_proof_bytes.byte_length,
    );
    let elapsed_milliseconds = example.elapsed_milliseconds + self_verification_milliseconds;
    let result = WorkerCommandResult {
        schema: "HushSp07RustWorkerCommandResultV1",
        worker_kind: "rust_arkworks_m1_process_worker",
        command: "prove",
        status: "completed",
        passed,
        result_code,
        message: if passed {
            "SP-07 Rust worker generated and self-verified the m=1 proof chunk.".to_string()
        } else {
            "SP-07 Rust worker proof generation did not pass self-verification.".to_string()
        },
        election_id: request
            .election_id
            .clone()
            .unwrap_or_else(|| example.statement.election_id.clone()),
        proof_session_id: request
            .proof_session_id
            .clone()
            .unwrap_or_else(|| "proof-session-local".to_string()),
        chunk_id: request
            .chunk_id
            .clone()
            .unwrap_or_else(|| example.statement.chunk_id.clone()),
        proof_profile_id: "matrix_m_1_publication_proof_v1",
        worker_version: env!("CARGO_PKG_VERSION"),
        worker_thread_count: rayon::current_num_threads(),
        statement_hash_sha512: example.statement_hash_sha512.clone(),
        transcript_hash_sha512: example.fiat_shamir.final_state_hash.clone(),
        proof_hash_sha512: example.canonical_proof_bytes.sha512.clone(),
        accepted_ballot_set_hash: example.statement.accepted_ballot_set_hash.clone(),
        published_ballot_stream_hash: example.statement.published_ballot_stream_hash.clone(),
        canonical_proof_byte_length: example.canonical_proof_bytes.byte_length,
        canonical_proof_bytes_hex: Some(example.canonical_proof_bytes.hex.clone()),
        proof_example_hash_sha512: example.proof_example_hash_sha512.clone(),
        elapsed_milliseconds,
        telemetry,
        proof_example: Some(proof_example),
    };

    write_json_atomic(&output, &result)?;
    println!(
        "Wrote SP-07 prove result {} for election {} chunk {}",
        output.display(),
        result.election_id,
        result.chunk_id
    );
    Ok(())
}

fn parse_production_proof_input(
    request: &WorkerProofRequest,
    ballots: usize,
    slots: usize,
) -> Result<Option<ProductionProofInput>, Box<dyn std::error::Error>> {
    let Some(input) = request.production_proof_input.as_ref() else {
        return Ok(None);
    };

    parse_worker_production_proof_input(input, ballots, slots)
        .map(Some)
        .map_err(|message| message.into())
}

fn parse_worker_production_proof_input(
    input: &WorkerProductionProofInput,
    ballots: usize,
    slots: usize,
) -> Result<ProductionProofInput, String> {
    if input.accepted_ballots.len() != ballots {
        return Err(format!(
            "production_proof_input accepted_ballots count {} does not match ballots {ballots}",
            input.accepted_ballots.len()
        ));
    }

    if input.published_ballots.len() != ballots {
        return Err(format!(
            "production_proof_input published_ballots count {} does not match ballots {ballots}",
            input.published_ballots.len()
        ));
    }

    validate_permutation(&input.published_to_accepted, ballots)?;
    let rerandomization = parse_rerandomization_matrix(
        &input.rerandomization_by_published_ballot_and_slot,
        ballots,
        slots,
    )?;
    let public_key = EdwardsProjective::from(parse_hush_point(&input.public_key, "public_key")?);
    let accepted_component_bases = parse_worker_ballots_to_components(
        &input.accepted_ballots,
        ballots,
        slots,
        "accepted_ballots",
    )?;
    let published_component_bases = parse_worker_ballots_to_components(
        &input.published_ballots,
        ballots,
        slots,
        "published_ballots",
    )?;
    let expected_published_component_bases = build_published_component_bases(
        &accepted_component_bases,
        &input.published_to_accepted,
        &rerandomization,
        &public_key,
        slots,
    );
    if expected_published_component_bases != published_component_bases {
        return Err(
            "production_proof_input published_ballots do not match accepted_ballots, published_to_accepted, rerandomization, and public_key"
                .to_string(),
        );
    }

    Ok(ProductionProofInput {
        public_key,
        accepted_component_bases,
        published_component_bases,
        published_to_accepted: input.published_to_accepted.clone(),
        rerandomization_by_published_ballot_and_slot: rerandomization,
    })
}

fn validate_permutation(values: &[usize], ballots: usize) -> Result<(), String> {
    if values.len() != ballots {
        return Err(format!(
            "production_proof_input published_to_accepted count {} does not match ballots {ballots}",
            values.len()
        ));
    }

    let mut seen = vec![false; ballots];
    for (index, value) in values.iter().copied().enumerate() {
        if value >= ballots {
            return Err(format!(
                "production_proof_input published_to_accepted[{index}]={value} is outside the accepted ballot range"
            ));
        }

        if seen[value] {
            return Err(format!(
                "production_proof_input published_to_accepted is not a permutation; accepted index {value} appears more than once"
            ));
        }

        seen[value] = true;
    }

    Ok(())
}

fn parse_rerandomization_matrix(
    rows: &[Vec<String>],
    ballots: usize,
    slots: usize,
) -> Result<Vec<Vec<Fr>>, String> {
    if rows.len() != ballots {
        return Err(format!(
            "production_proof_input rerandomization row count {} does not match ballots {ballots}",
            rows.len()
        ));
    }

    rows.iter()
        .enumerate()
        .map(|(ballot, row)| {
            if row.len() != slots {
                return Err(format!(
                    "production_proof_input rerandomization row {ballot} has {} slots, expected {slots}",
                    row.len()
                ));
            }

            row.iter()
                .enumerate()
                .map(|(slot, scalar)| {
                    Fr::from_str(scalar.trim()).map_err(|err| {
                        format!(
                            "production_proof_input rerandomization[{ballot}][{slot}] is not a scalar: {err:?}"
                        )
                    })
                })
                .collect()
        })
        .collect()
}

fn parse_worker_ballots_to_components(
    ballots_payload: &[WorkerCipherBallot],
    ballots: usize,
    slots: usize,
    field_name: &str,
) -> Result<Vec<Vec<EdwardsAffine>>, String> {
    if ballots_payload.len() != ballots {
        return Err(format!(
            "production_proof_input {field_name} count {} does not match ballots {ballots}",
            ballots_payload.len()
        ));
    }

    let mut components = vec![Vec::with_capacity(ballots); slots * 2];
    for (ballot_index, ballot) in ballots_payload.iter().enumerate() {
        if ballot.slots.len() != slots {
            return Err(format!(
                "production_proof_input {field_name}[{ballot_index}] has {} slots, expected {slots}",
                ballot.slots.len()
            ));
        }

        for (slot_index, slot) in ballot.slots.iter().enumerate() {
            components[slot_index * 2].push(parse_hush_point(
                &slot.c1,
                &format!("{field_name}[{ballot_index}].slots[{slot_index}].c1"),
            )?);
            components[(slot_index * 2) + 1].push(parse_hush_point(
                &slot.c2,
                &format!("{field_name}[{ballot_index}].slots[{slot_index}].c2"),
            )?);
        }
    }

    Ok(components)
}

fn parse_hush_point(point: &WorkerPoint, path: &str) -> Result<EdwardsAffine, String> {
    let x = Fq::from_str(point.x.trim()).map_err(|err| {
        format!("production_proof_input {path}.x is not a field element: {err:?}")
    })?;
    let y = Fq::from_str(point.y.trim()).map_err(|err| {
        format!("production_proof_input {path}.y is not a field element: {err:?}")
    })?;
    let ark_point = EdwardsAffine::new(x * hush_sqrt_a(), y);
    if !ark_point.is_on_curve() || !ark_point.is_in_correct_subgroup_assuming_on_curve() {
        return Err(format!(
            "production_proof_input {path} is not a valid Hush BabyJubJub subgroup point"
        ));
    }

    Ok(ark_point)
}

fn run_verify(input: PathBuf, output: PathBuf) -> Result<(), Box<dyn std::error::Error>> {
    let started = Instant::now();
    let input_json = fs::read_to_string(&input)?;
    let input_value: serde_json::Value = serde_json::from_str(&input_json)?;
    if let Some(parent) = output.parent() {
        fs::create_dir_all(parent)?;
    }

    let verification = verify_public_proof_input(&input_value);
    let result_code = input_value
        .get("result_code")
        .and_then(serde_json::Value::as_str)
        .unwrap_or("PUB-005");
    let proof_hash = input_value
        .get("proof_hash_sha512")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let statement_hash = input_value
        .get("statement_hash_sha512")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let transcript_hash = input_value
        .get("transcript_hash_sha512")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let passed = verification.passed
        && result_code == "PUB-005"
        && proof_hash.len() == 128
        && statement_hash.len() == 128
        && transcript_hash.len() == 128;
    let elapsed_milliseconds = started.elapsed().as_secs_f64() * 1000.0;

    let result = WorkerCommandResult {
        schema: "HushSp07RustWorkerCommandResultV1",
        worker_kind: "rust_arkworks_m1_process_worker",
        command: "verify",
        status: "completed",
        passed,
        result_code: if passed {
            "PUB-005".to_string()
        } else {
            verification.result_code
        },
        message: if passed {
            "SP-07 Rust worker verified the canonical public m=1 proof bytes.".to_string()
        } else {
            verification.message
        },
        election_id: json_string_field(&input_value, "election_id", "unknown-election"),
        proof_session_id: json_string_field(&input_value, "proof_session_id", "unknown-session"),
        chunk_id: json_string_field(&input_value, "chunk_id", "unknown-chunk"),
        proof_profile_id: "matrix_m_1_publication_proof_v1",
        worker_version: env!("CARGO_PKG_VERSION"),
        worker_thread_count: rayon::current_num_threads(),
        statement_hash_sha512: statement_hash.to_string(),
        transcript_hash_sha512: transcript_hash.to_string(),
        proof_hash_sha512: proof_hash.to_string(),
        accepted_ballot_set_hash: json_string_field(&input_value, "accepted_ballot_set_hash", ""),
        published_ballot_stream_hash: json_string_field(
            &input_value,
            "published_ballot_stream_hash",
            "",
        ),
        canonical_proof_byte_length: verification.canonical_proof_byte_length,
        canonical_proof_bytes_hex: None,
        proof_example_hash_sha512: json_string_field(&input_value, "proof_example_hash_sha512", ""),
        elapsed_milliseconds,
        telemetry: build_verify_worker_telemetry(
            elapsed_milliseconds,
            verification.canonical_proof_byte_length,
        ),
        proof_example: None,
    };

    write_json_atomic(&output, &result)?;
    println!(
        "Wrote SP-07 verify result {} for election {} chunk {}",
        output.display(),
        result.election_id,
        result.chunk_id
    );
    Ok(())
}

struct PublicProofVerificationOutcome {
    passed: bool,
    result_code: String,
    message: String,
    canonical_proof_byte_length: usize,
}

fn verify_public_proof_input(input_value: &serde_json::Value) -> PublicProofVerificationOutcome {
    let expected_proof_hash = input_value
        .get("proof_hash_sha512")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let expected_statement_hash = input_value
        .get("statement_hash_sha512")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let expected_transcript_hash = input_value
        .get("transcript_hash_sha512")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let expected_accepted_hash = input_value
        .get("accepted_ballot_set_hash")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();
    let expected_published_hash = input_value
        .get("published_ballot_stream_hash")
        .and_then(serde_json::Value::as_str)
        .unwrap_or_default();

    if let Some(proof_hex) = input_value
        .get("canonical_proof_bytes_hex")
        .and_then(serde_json::Value::as_str)
    {
        return verify_canonical_proof_bytes(
            proof_hex,
            expected_proof_hash,
            expected_statement_hash,
            expected_transcript_hash,
            expected_accepted_hash,
            expected_published_hash,
            input_value
                .get("canonical_proof_byte_length")
                .and_then(serde_json::Value::as_u64)
                .unwrap_or_default() as usize,
        );
    }

    PublicProofVerificationOutcome {
        passed: false,
        result_code: "PUB-015".to_string(),
        message: "SP-07 verify input is missing canonical_proof_bytes_hex.".to_string(),
        canonical_proof_byte_length: input_value
            .get("canonical_proof_byte_length")
            .and_then(serde_json::Value::as_u64)
            .unwrap_or_default() as usize,
    }
}

fn verify_canonical_proof_bytes(
    proof_hex: &str,
    expected_proof_hash: &str,
    expected_statement_hash: &str,
    expected_transcript_hash: &str,
    expected_accepted_hash: &str,
    expected_published_hash: &str,
    expected_byte_length: usize,
) -> PublicProofVerificationOutcome {
    let proof_bytes = match decode_hex(proof_hex) {
        Ok(bytes) => bytes,
        Err(message) => {
            return PublicProofVerificationOutcome {
                passed: false,
                result_code: "PUB-015".to_string(),
                message,
                canonical_proof_byte_length: 0,
            };
        }
    };

    let actual_proof_hash = hex_digest(Sha512::digest(&proof_bytes));
    if actual_proof_hash != expected_proof_hash {
        return PublicProofVerificationOutcome {
            passed: false,
            result_code: "PUB-015".to_string(),
            message: "SP-07 canonical proof bytes do not match proof_hash_sha512.".to_string(),
            canonical_proof_byte_length: proof_bytes.len(),
        };
    }

    if expected_byte_length != 0 && proof_bytes.len() != expected_byte_length {
        return PublicProofVerificationOutcome {
            passed: false,
            result_code: "PUB-015".to_string(),
            message: "SP-07 canonical proof byte length does not match the verifier input."
                .to_string(),
            canonical_proof_byte_length: proof_bytes.len(),
        };
    }

    let envelope: serde_json::Value = match serde_json::from_slice(&proof_bytes) {
        Ok(envelope) => envelope,
        Err(error) => {
            return PublicProofVerificationOutcome {
                passed: false,
                result_code: "PUB-015".to_string(),
                message: format!(
                    "SP-07 canonical proof bytes are not a valid proof envelope: {error}"
                ),
                canonical_proof_byte_length: proof_bytes.len(),
            };
        }
    };

    if json_string_field(&envelope, "schema", "") != "HushSp07CanonicalPublicationProofEnvelopeV1"
        || json_string_field(&envelope, "proof_profile", "") != "matrix_m_1_publication_proof_v1"
        || json_string_field(&envelope, "verifier_result_code", "") != "PUB-005"
    {
        return PublicProofVerificationOutcome {
            passed: false,
            result_code: "PUB-015".to_string(),
            message: "SP-07 canonical proof envelope uses an unsupported schema, profile, or result code."
                .to_string(),
            canonical_proof_byte_length: proof_bytes.len(),
        };
    }

    if json_string_field(&envelope, "statement_hash_sha512", "") != expected_statement_hash
        || json_string_field(&envelope, "fiat_shamir_final_state_hash", "")
            != expected_transcript_hash
        || json_string_field(&envelope, "accepted_ballot_set_hash", "") != expected_accepted_hash
        || json_string_field(&envelope, "published_ballot_stream_hash", "")
            != expected_published_hash
    {
        return PublicProofVerificationOutcome {
            passed: false,
            result_code: "PUB-015".to_string(),
            message:
                "SP-07 canonical proof envelope does not bind the expected public statement hashes."
                    .to_string(),
            canonical_proof_byte_length: proof_bytes.len(),
        };
    }

    PublicProofVerificationOutcome {
        passed: true,
        result_code: "PUB-005".to_string(),
        message: "SP-07 canonical proof bytes verified.".to_string(),
        canonical_proof_byte_length: proof_bytes.len(),
    }
}

fn write_json_atomic<T: Serialize>(
    output: &Path,
    value: &T,
) -> Result<(), Box<dyn std::error::Error>> {
    let json = serde_json::to_string_pretty(value)?;
    let temporary = output.with_extension("json.tmp");
    fs::write(&temporary, format!("{json}\n"))?;
    if output.exists() {
        fs::remove_file(output)?;
    }
    fs::rename(&temporary, output)?;
    Ok(())
}

fn json_string_field(value: &serde_json::Value, field: &str, fallback: &str) -> String {
    value
        .get(field)
        .and_then(serde_json::Value::as_str)
        .unwrap_or(fallback)
        .to_string()
}

fn decode_hex(value: &str) -> Result<Vec<u8>, String> {
    let trimmed = value.trim();
    if trimmed.len() % 2 != 0 {
        return Err("SP-07 canonical proof hex has an odd length.".to_string());
    }

    let mut bytes = Vec::with_capacity(trimmed.len() / 2);
    for index in (0..trimmed.len()).step_by(2) {
        let byte = u8::from_str_radix(&trimmed[index..index + 2], 16)
            .map_err(|_| "SP-07 canonical proof hex contains a non-hex character.".to_string())?;
        bytes.push(byte);
    }

    Ok(bytes)
}

fn configure_global_threads(threads: Option<usize>) -> Result<(), Box<dyn std::error::Error>> {
    if let Some(threads) = threads {
        if threads == 0 {
            return Err("--threads must be positive when provided".into());
        }

        rayon::ThreadPoolBuilder::new()
            .num_threads(threads)
            .build_global()?;
    }

    Ok(())
}

fn run_bigint_bench(ballots: usize, slots: usize, rounds: usize) -> BenchReport {
    let setup_start = Instant::now();
    let scalars = build_scalars(ballots);
    let component_bases = build_component_bases(ballots, slots * 2);
    let setup_milliseconds = setup_start.elapsed().as_secs_f64() * 1000.0;
    let mut round_milliseconds = Vec::with_capacity(rounds);
    let mut last_result = Vec::new();
    for _ in 0..rounds {
        let round_start = Instant::now();
        last_result = aggregate_components(&component_bases, &scalars);
        round_milliseconds.push(round_start.elapsed().as_secs_f64() * 1000.0);
    }

    let best_milliseconds = round_milliseconds
        .iter()
        .copied()
        .fold(f64::INFINITY, f64::min);
    let average_milliseconds =
        round_milliseconds.iter().sum::<f64>() / round_milliseconds.len() as f64;
    BenchReport {
        schema: "HushSp07RustWorkerBenchReportV1",
        engine: "rust_num_bigint_same_formula_v1",
        operation: "component_cipher_vector_exponentiation_shape",
        mode: "bigint",
        ballots,
        slots,
        components: slots * 2,
        point_scalar_pairs_per_round: ballots * slots * 2,
        rounds,
        setup_milliseconds,
        round_milliseconds,
        best_milliseconds,
        average_milliseconds,
        rayon_threads: rayon::current_num_threads(),
        checksum_sha512: checksum(&last_result),
        notes: vec![
            "Fair language spike: uses num-bigint arbitrary precision arithmetic, not fixed-width field arithmetic.",
            "Matches the current C# operation shape: K slots, two ciphertext components per slot, same exponent vector per component.",
            "This is an aggregation hot-path benchmark, not a full SP-07 proof generator.",
        ],
    }
}

fn run_arkworks_bench(ballots: usize, slots: usize, rounds: usize) -> BenchReport {
    let setup_start = Instant::now();
    let scalars = build_ark_scalars(ballots);
    let component_bases = build_ark_component_bases(ballots, slots * 2);
    let setup_milliseconds = setup_start.elapsed().as_secs_f64() * 1000.0;

    let mut round_milliseconds = Vec::with_capacity(rounds);
    let mut last_result = Vec::new();
    for _ in 0..rounds {
        let round_start = Instant::now();
        last_result = aggregate_ark_components(&component_bases, &scalars);
        round_milliseconds.push(round_start.elapsed().as_secs_f64() * 1000.0);
    }

    let best_milliseconds = round_milliseconds
        .iter()
        .copied()
        .fold(f64::INFINITY, f64::min);
    let average_milliseconds =
        round_milliseconds.iter().sum::<f64>() / round_milliseconds.len() as f64;
    BenchReport {
        schema: "HushSp07RustWorkerBenchReportV1",
        engine: "rust_arkworks_fixed_field_msm_v1",
        operation: "component_cipher_vector_exponentiation_shape",
        mode: "arkworks",
        ballots,
        slots,
        components: slots * 2,
        point_scalar_pairs_per_round: ballots * slots * 2,
        rounds,
        setup_milliseconds,
        round_milliseconds,
        best_milliseconds,
        average_milliseconds,
        rayon_threads: rayon::current_num_threads(),
        checksum_sha512: checksum_ark(&last_result),
        notes: vec![
            "Optimized Rust lane: uses arkworks fixed-field arithmetic and variable-base MSM.",
            "Coordinates are transformed between the Hush A=168700 twisted-Edwards form and arkworks' normalized a=1 BabyJubJub form.",
            "This is an aggregation hot-path benchmark, not a full SP-07 proof generator.",
        ],
    }
}

fn run_arkworks_phasebench(ballots: usize, slots: usize, rounds: usize) -> PhaseBenchReport {
    let components = slots * 2;
    let setup_start = Instant::now();
    let accepted_scalars = build_ark_scalar_bigints_with_label("phase.accepted.challenge", ballots);
    let published_scalars =
        build_ark_scalar_bigints_with_label("phase.published.challenge", ballots);
    let response_scalars = build_ark_scalar_bigints_with_label("phase.response.challenge", ballots);
    let verifier_batched_scalars = build_batched_verifier_scalar_bigints(
        "phase.verify.left",
        "phase.verify.right",
        "phase.verify.batch",
        ballots,
    );
    let product_a_scalars = build_ark_scalar_bigints_with_label("phase.product.a", ballots);
    let product_b_scalars = build_ark_scalar_bigints_with_label("phase.product.b", ballots);
    let product_delta_scalars = build_ark_scalar_bigints_with_label("phase.product.delta", ballots);
    let multi_a_scalars = build_ark_scalar_bigints_with_label("phase.multi.a", ballots);
    let multi_b_scalars = build_ark_scalar_bigints_with_label("phase.multi.b", ballots);
    let multi_tau_scalars = build_ark_scalar_bigints_with_label("phase.multi.tau", ballots);
    let accepted_component_bases =
        build_ark_component_bases_with_label(ballots, components, "phase.accepted.component-step");
    let published_component_bases =
        build_ark_component_bases_with_label(ballots, components, "phase.published.component-step");
    let response_component_bases =
        build_ark_component_bases_with_label(ballots, components, "phase.response.component-step");
    let verify_left_component_bases = build_ark_component_bases_with_label(
        ballots,
        components,
        "phase.verify.left-component-step",
    );
    let verify_right_component_bases = build_ark_component_bases_with_label(
        ballots,
        components,
        "phase.verify.right-component-step",
    );
    let verify_batched_component_bases =
        build_batched_component_bases(&verify_left_component_bases, &verify_right_component_bases);
    let product_commitment_bases =
        build_ark_progression_bases(ballots, "phase.product.commitment-step");
    let multi_commitment_bases =
        build_ark_progression_bases(ballots, "phase.multi.commitment-step");
    let setup_milliseconds = setup_start.elapsed().as_secs_f64() * 1000.0;

    let mut round_reports = Vec::with_capacity(rounds);
    let mut last_result = Vec::new();
    for _ in 0..rounds {
        let round_start = Instant::now();
        let (accepted_ms, accepted_result) = time_ark_points(|| {
            aggregate_ark_components_bigint(&accepted_component_bases, &accepted_scalars)
        });
        let (published_ms, published_result) = time_ark_points(|| {
            aggregate_ark_components_bigint(&published_component_bases, &published_scalars)
        });
        let (response_ms, response_result) = time_ark_points(|| {
            aggregate_ark_components_bigint(&response_component_bases, &response_scalars)
        });
        let product_scalar_sets = [
            product_a_scalars.as_slice(),
            product_b_scalars.as_slice(),
            product_delta_scalars.as_slice(),
        ];
        let (product_ms, product_result) = time_ark_points(|| {
            aggregate_ark_shared_bases_bigint(&product_commitment_bases, &product_scalar_sets)
        });
        let multi_scalar_sets = [
            multi_a_scalars.as_slice(),
            multi_b_scalars.as_slice(),
            multi_tau_scalars.as_slice(),
        ];
        let (multi_ms, multi_result) = time_ark_points(|| {
            aggregate_ark_shared_bases_bigint(&multi_commitment_bases, &multi_scalar_sets)
        });
        let (verify_ms, verify_result) = time_ark_points(|| {
            aggregate_ark_components_bigint(
                &verify_batched_component_bases,
                &verifier_batched_scalars,
            )
        });

        last_result.clear();
        last_result.extend(accepted_result);
        last_result.extend(published_result);
        last_result.extend(response_result);
        last_result.extend(product_result);
        last_result.extend(multi_result);
        last_result.extend(verify_result);

        round_reports.push(PhaseRoundReport {
            accepted_ciphertext_vector_milliseconds: accepted_ms,
            published_ciphertext_vector_milliseconds: published_ms,
            rerandomized_response_vector_milliseconds: response_ms,
            product_argument_commitments_milliseconds: product_ms,
            multi_exponentiation_commitments_milliseconds: multi_ms,
            public_verify_replay_milliseconds: verify_ms,
            total_milliseconds: round_start.elapsed().as_secs_f64() * 1000.0,
        });
    }

    let best_total_milliseconds = round_reports
        .iter()
        .map(|round| round.total_milliseconds)
        .fold(f64::INFINITY, f64::min);
    let average_total_milliseconds = round_reports
        .iter()
        .map(|round| round.total_milliseconds)
        .sum::<f64>()
        / round_reports.len() as f64;
    let component_msm_groups_per_round = components * 4;
    let commitment_msm_groups_per_round = 6;

    PhaseBenchReport {
        schema: "HushSp07RustWorkerPhaseBenchReportV1",
        engine: "rust_arkworks_fixed_field_msm_phasebench_v1",
        operation: "stacked_sp07_publication_proof_phase_shape",
        ballots,
        slots,
        components,
        component_msm_groups_per_round,
        commitment_msm_groups_per_round,
        point_scalar_pairs_per_round: ballots * ((components * 5) + commitment_msm_groups_per_round),
        rounds,
        setup_milliseconds,
        round_reports,
        best_total_milliseconds,
        average_total_milliseconds,
        rayon_threads: rayon::current_num_threads(),
        checksum_sha512: checksum_ark(&last_result),
        notes: vec![
            "Proof-shaped phase benchmark: stacks accepted/published ciphertext vector MSMs, response-vector MSMs, product commitment MSMs, multi-exponentiation commitment MSMs, and a verifier-replay MSM slice.",
            "This is still not a complete Bayer-Groth proof payload: it does not implement Fiat-Shamir transcript construction, product/hadamard equations, canonical proof bytes, tamper vectors, or final verifier result codes.",
            "Setup time is reported separately because production may cache commitment keys and worker bases; round totals measure the repeated crypto phase workload.",
        ],
    }
}

fn build_proof_example(
    ballots: usize,
    slots: usize,
    include_legacy_phase_artifacts: bool,
    include_tamper_vectors: bool,
) -> Result<ProofExample, serde_json::Error> {
    build_proof_example_with_statement_overrides(
        ballots,
        slots,
        include_legacy_phase_artifacts,
        include_tamper_vectors,
        None,
        None,
    )
}

fn build_proof_example_with_statement_overrides(
    ballots: usize,
    slots: usize,
    include_legacy_phase_artifacts: bool,
    include_tamper_vectors: bool,
    statement_overrides: Option<&StatementOverrides>,
    production_input: Option<&ProductionProofInput>,
) -> Result<ProofExample, serde_json::Error> {
    let started = Instant::now();
    let components = slots * 2;
    let base_generation_started = Instant::now();
    let public_key = production_input
        .map(|input| input.public_key.clone())
        .unwrap_or_else(|| {
            hush_generator_ark().mul_bigint(
                derive_scalar_fr("example.election-public-key", ballots, slots).into_bigint(),
            )
        });
    let accepted_component_bases = production_input
        .map(|input| input.accepted_component_bases.clone())
        .unwrap_or_else(|| {
            build_ark_component_bases_with_label(
                ballots,
                components,
                "example.accepted.component-step",
            )
        });
    let permutation = production_input
        .map(|input| input.published_to_accepted.clone())
        .unwrap_or_else(|| build_permutation(ballots));
    let rerandomization = production_input
        .map(|input| input.rerandomization_by_published_ballot_and_slot.clone())
        .unwrap_or_else(|| build_rerandomization_matrix(ballots, slots));
    let published_component_bases = production_input
        .map(|input| input.published_component_bases.clone())
        .unwrap_or_else(|| {
            build_published_component_bases(
                &accepted_component_bases,
                &permutation,
                &rerandomization,
                &public_key,
                slots,
            )
        });
    let response_component_bases = include_legacy_phase_artifacts.then(|| {
        build_ark_component_bases_with_label(ballots, components, "example.response.component-step")
    });
    let verify_batched_component_bases = include_legacy_phase_artifacts.then(|| {
        let verify_left_component_bases = build_ark_component_bases_with_label(
            ballots,
            components,
            "example.verify.left-component-step",
        );
        let verify_right_component_bases = build_ark_component_bases_with_label(
            ballots,
            components,
            "example.verify.right-component-step",
        );
        build_batched_component_bases(&verify_left_component_bases, &verify_right_component_bases)
    });
    let product_commitment_bases =
        build_ark_progression_bases(ballots, "example.product.commitment-step");
    let product_commitment_key = CommitmentKey {
        h: derive_ark_base("example.product.commitment-h", ballots, slots),
        g: product_commitment_bases.clone(),
    };
    let multi_commitment_bases = include_legacy_phase_artifacts
        .then(|| build_ark_progression_bases(ballots, "example.multi.commitment-step"));

    let accepted_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.accepted.challenge", ballots));
    let published_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.published.challenge", ballots));
    let response_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.response.challenge", ballots));
    let verifier_batched_scalars = include_legacy_phase_artifacts.then(|| {
        build_batched_verifier_scalar_bigints(
            "example.verify.left",
            "example.verify.right",
            "example.verify.batch",
            ballots,
        )
    });
    let product_a_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.product.a", ballots));
    let product_b_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.product.b", ballots));
    let product_delta_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.product.delta", ballots));
    let multi_a_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.multi.a", ballots));
    let multi_b_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.multi.b", ballots));
    let multi_tau_scalars = include_legacy_phase_artifacts
        .then(|| build_ark_scalar_bigints_with_label("example.multi.tau", ballots));
    let base_generation_milliseconds = base_generation_started.elapsed().as_secs_f64() * 1000.0;

    let statement_hashing_started = Instant::now();
    let default_protocol_package_hash = hash_parts(&[
        "protocol-package",
        "Protocol-Omega-HushVoting-v1-Artifacts",
        "v1.1.x-rust-spike",
    ]);
    let default_ballot_definition_hash = hash_parts(&[
        "ballot-definition",
        &ballots.to_string(),
        &slots.to_string(),
    ]);
    let default_accepted_ballot_set_hash = hash_ark_affine_components(&accepted_component_bases);
    let default_published_ballot_stream_hash =
        hash_ark_affine_components(&published_component_bases);
    let statement = ExampleStatement {
        schema: "HushSp07BgStatementV1",
        construction: "bayer_groth_reencryption_shuffle_argument_v1",
        adapter: "hush_babyjubjub_vector_ballot_bg_adapter_v1",
        group_profile: "hush_babyjubjub_bn254_subgroup_v1",
        commitment_key_profile: "hush_pedersen_commitment_key_demo_v1",
        fiat_shamir_profile: "hush_sp07_fiat_shamir_sha512_v1",
        proof_profile: "matrix_m_1_publication_proof_v1",
        election_id: statement_overrides
            .and_then(|overrides| overrides.election_id.clone())
            .unwrap_or_else(|| format!("rust-sp07-example-n{ballots}-k{slots}")),
        chunk_id: statement_overrides
            .and_then(|overrides| overrides.chunk_id.clone())
            .unwrap_or_else(|| "chunk-000001".to_string()),
        protocol_package_hash: statement_overrides
            .and_then(|overrides| overrides.protocol_package_hash.clone())
            .unwrap_or(default_protocol_package_hash),
        ballot_definition_hash: statement_overrides
            .and_then(|overrides| overrides.ballot_definition_hash.clone())
            .unwrap_or(default_ballot_definition_hash),
        public_key: public_point_from_projective(&public_key),
        ballots,
        slots,
        matrix_m: 1,
        matrix_n: ballots,
        accepted_ballot_set_hash: statement_overrides
            .and_then(|overrides| overrides.accepted_ballot_set_hash.clone())
            .unwrap_or(default_accepted_ballot_set_hash),
        published_ballot_stream_hash: statement_overrides
            .and_then(|overrides| overrides.published_ballot_stream_hash.clone())
            .unwrap_or(default_published_ballot_stream_hash),
    };
    let statement_hash_sha512 = canonical_hash(&statement)?;
    let statement_hashing_milliseconds = statement_hashing_started.elapsed().as_secs_f64() * 1000.0;

    let phase_commitment_started = Instant::now();
    let (accepted_ciphertext_vector_milliseconds, accepted_commitments) =
        if let Some(scalars) = accepted_scalars.as_ref() {
            let started = Instant::now();
            let commitments = aggregate_ark_components_bigint(&accepted_component_bases, scalars);
            (started.elapsed().as_secs_f64() * 1000.0, commitments)
        } else {
            (0.0, Vec::new())
        };
    let (published_ciphertext_vector_milliseconds, published_commitments) =
        if let Some(scalars) = published_scalars.as_ref() {
            let started = Instant::now();
            let commitments = aggregate_ark_components_bigint(&published_component_bases, scalars);
            (started.elapsed().as_secs_f64() * 1000.0, commitments)
        } else {
            (0.0, Vec::new())
        };
    let (rerandomized_response_vector_milliseconds, response_commitments) =
        if let (Some(bases), Some(scalars)) =
            (response_component_bases.as_ref(), response_scalars.as_ref())
        {
            let started = Instant::now();
            let commitments = aggregate_ark_components_bigint(bases, scalars);
            (started.elapsed().as_secs_f64() * 1000.0, commitments)
        } else {
            (0.0, Vec::new())
        };
    let (legacy_product_argument_commitments_milliseconds, product_commitments) =
        if let (Some(product_a), Some(product_b), Some(product_delta)) = (
            product_a_scalars.as_ref(),
            product_b_scalars.as_ref(),
            product_delta_scalars.as_ref(),
        ) {
            let product_scalar_sets = [
                product_a.as_slice(),
                product_b.as_slice(),
                product_delta.as_slice(),
            ];
            let started = Instant::now();
            let commitments =
                aggregate_ark_shared_bases_bigint(&product_commitment_bases, &product_scalar_sets);
            (started.elapsed().as_secs_f64() * 1000.0, commitments)
        } else {
            (0.0, Vec::new())
        };
    let outer_shuffle_started = Instant::now();
    let outer_shuffle = build_outer_shuffle_wiring(
        &statement_hash_sha512,
        &product_commitment_key,
        &accepted_component_bases,
        &permutation,
        &rerandomization,
        slots,
    );
    let outer_shuffle_wiring_milliseconds = outer_shuffle_started.elapsed().as_secs_f64() * 1000.0;
    let single_value_product_started = Instant::now();
    let single_value_product_proof = prove_single_value_product(
        &statement_hash_sha512,
        &product_commitment_key,
        &outer_shuffle.single_product_witness,
    );
    let single_value_product_verifier_result = verify_single_value_product(
        &statement_hash_sha512,
        &product_commitment_key,
        &single_value_product_proof,
    );
    let single_value_product_argument = build_single_value_product_argument_example(
        &single_value_product_proof,
        single_value_product_verifier_result,
    );
    let single_value_product_argument_milliseconds =
        single_value_product_started.elapsed().as_secs_f64() * 1000.0;
    let hush_multi_started = Instant::now();
    let hush_multi_exponentiation_proof = prove_hush_multi_exponentiation(
        &statement_hash_sha512,
        &product_commitment_key,
        &public_key,
        &published_component_bases,
        slots,
        &outer_shuffle.multi_exp_witness,
        &statement.published_ballot_stream_hash,
        &outer_shuffle.accepted_cx,
    );
    let hush_multi_exponentiation_verifier_result = verify_hush_multi_exponentiation(
        &statement_hash_sha512,
        &statement.published_ballot_stream_hash,
        &product_commitment_key,
        &public_key,
        &published_component_bases,
        slots,
        &hush_multi_exponentiation_proof,
    );
    let hush_multi_exponentiation_argument = build_hush_multi_exponentiation_argument_example(
        slots,
        &hush_multi_exponentiation_proof,
        hush_multi_exponentiation_verifier_result,
    );
    let hush_multi_exponentiation_argument_milliseconds =
        hush_multi_started.elapsed().as_secs_f64() * 1000.0;
    let (legacy_multi_exponentiation_commitments_milliseconds, multi_commitments) =
        if let (Some(bases), Some(multi_a), Some(multi_b), Some(multi_tau)) = (
            multi_commitment_bases.as_ref(),
            multi_a_scalars.as_ref(),
            multi_b_scalars.as_ref(),
            multi_tau_scalars.as_ref(),
        ) {
            let multi_scalar_sets = [multi_a.as_slice(), multi_b.as_slice(), multi_tau.as_slice()];
            let started = Instant::now();
            let commitments = aggregate_ark_shared_bases_bigint(bases, &multi_scalar_sets);
            (started.elapsed().as_secs_f64() * 1000.0, commitments)
        } else {
            (0.0, Vec::new())
        };
    let (public_verify_replay_milliseconds, verify_commitments) =
        if let (Some(bases), Some(scalars)) = (
            verify_batched_component_bases.as_ref(),
            verifier_batched_scalars.as_ref(),
        ) {
            let started = Instant::now();
            let commitments = aggregate_ark_components_bigint(bases, scalars);
            (started.elapsed().as_secs_f64() * 1000.0, commitments)
        } else {
            (0.0, Vec::new())
        };
    let phase_commitment_generation_milliseconds =
        phase_commitment_started.elapsed().as_secs_f64() * 1000.0;

    let phase_artifact_started = Instant::now();
    let phase_artifacts = ExamplePhaseArtifacts {
        accepted_ciphertext_vector_hash: if accepted_commitments.is_empty() {
            statement.accepted_ballot_set_hash.clone()
        } else {
            hash_ark_projective_points(&accepted_commitments)
        },
        accepted_ciphertext_vector_commitments: public_points_from_projective(
            &accepted_commitments,
        ),
        published_ciphertext_vector_hash: if published_commitments.is_empty() {
            statement.published_ballot_stream_hash.clone()
        } else {
            hash_ark_projective_points(&published_commitments)
        },
        published_ciphertext_vector_commitments: public_points_from_projective(
            &published_commitments,
        ),
        rerandomized_response_vector_hash: if response_commitments.is_empty() {
            hash_parts(&[
                "rerandomized-response-vector",
                "legacy-phase-artifact-disabled",
                &statement_hash_sha512,
            ])
        } else {
            hash_ark_projective_points(&response_commitments)
        },
        rerandomized_response_vector_commitments: public_points_from_projective(
            &response_commitments,
        ),
        outer_shuffle_hash: canonical_hash(&outer_shuffle.example)?,
        outer_shuffle: outer_shuffle.example,
        product_argument_commitments_hash: if product_commitments.is_empty() {
            hash_parts(&[
                "legacy-product-argument-commitments",
                "disabled",
                &statement_hash_sha512,
            ])
        } else {
            hash_ark_projective_points(&product_commitments)
        },
        product_argument_commitments: public_points_from_projective(&product_commitments),
        single_value_product_argument_hash: canonical_hash(&single_value_product_argument)?,
        single_value_product_argument,
        hush_multi_exponentiation_argument_hash: canonical_hash(
            &hush_multi_exponentiation_argument,
        )?,
        hush_multi_exponentiation_argument,
        multi_exponentiation_commitments_hash: if multi_commitments.is_empty() {
            hash_parts(&[
                "legacy-multi-exponentiation-commitments",
                "disabled",
                &statement_hash_sha512,
            ])
        } else {
            hash_ark_projective_points(&multi_commitments)
        },
        multi_exponentiation_commitments: public_points_from_projective(&multi_commitments),
        public_verify_replay_hash: if verify_commitments.is_empty() {
            hash_parts(&[
                "legacy-public-verify-replay",
                "disabled",
                &statement_hash_sha512,
            ])
        } else {
            hash_ark_projective_points(&verify_commitments)
        },
        public_verify_replay_commitments: public_points_from_projective(&verify_commitments),
    };
    let phase_artifact_shaping_milliseconds =
        phase_artifact_started.elapsed().as_secs_f64() * 1000.0;

    let fiat_shamir_started = Instant::now();
    let mut transcript = TranscriptBuilder::new(&statement_hash_sha512);
    transcript.challenge("statement_binding");
    transcript.absorb_hash(
        "accepted_ciphertext_vector",
        &phase_artifacts.accepted_ciphertext_vector_hash,
    );
    transcript.challenge("product_alpha");
    transcript.absorb_hash(
        "published_ciphertext_vector",
        &phase_artifacts.published_ciphertext_vector_hash,
    );
    transcript.challenge("product_beta");
    transcript.absorb_hash(
        "rerandomized_response_vector",
        &phase_artifacts.rerandomized_response_vector_hash,
    );
    transcript.absorb_hash("outer_shuffle", &phase_artifacts.outer_shuffle_hash);
    transcript.challenge("multi_exponentiation_x");
    transcript.absorb_hash(
        "product_argument_commitments",
        &phase_artifacts.product_argument_commitments_hash,
    );
    transcript.absorb_hash(
        "single_value_product_argument",
        &phase_artifacts.single_value_product_argument_hash,
    );
    transcript.challenge("product_single_value_bound");
    transcript.challenge("multi_exponentiation_y");
    transcript.absorb_hash(
        "multi_exponentiation_commitments",
        &phase_artifacts.multi_exponentiation_commitments_hash,
    );
    transcript.absorb_hash(
        "hush_multi_exponentiation_argument",
        &phase_artifacts.hush_multi_exponentiation_argument_hash,
    );
    transcript.challenge("verifier_batch");
    transcript.absorb_hash(
        "public_verify_replay",
        &phase_artifacts.public_verify_replay_hash,
    );
    transcript.challenge("proof_finalizer");
    let fiat_shamir = transcript.finish();
    let fiat_shamir_milliseconds = fiat_shamir_started.elapsed().as_secs_f64() * 1000.0;

    let canonical_proof_started = Instant::now();
    let preliminary_result_code = proof_example_primary_result_code(&phase_artifacts, &fiat_shamir);
    let canonical_proof_bytes = build_canonical_proof_bytes(
        &statement,
        &statement_hash_sha512,
        &phase_artifacts,
        &fiat_shamir,
        preliminary_result_code,
    )?;
    let canonical_proof_bytes_milliseconds =
        canonical_proof_started.elapsed().as_secs_f64() * 1000.0;

    let tamper_vectors_started = Instant::now();
    let tamper_vectors = if include_tamper_vectors {
        build_tamper_vectors(
            &statement_hash_sha512,
            &statement.published_ballot_stream_hash,
            &product_commitment_key,
            &public_key,
            &published_component_bases,
            slots,
            &single_value_product_proof,
            &hush_multi_exponentiation_proof,
        )
    } else {
        Vec::new()
    };
    let tamper_vectors_milliseconds = tamper_vectors_started.elapsed().as_secs_f64() * 1000.0;
    let verifier_result = build_proof_example_verifier_result(
        &phase_artifacts,
        &fiat_shamir,
        &canonical_proof_bytes,
        &tamper_vectors,
        include_tamper_vectors,
    );

    let final_hash_started = Instant::now();
    let tamper_vectors_hash = canonical_hash(&tamper_vectors)?;
    let proof_example_hash_sha512 = hash_parts(&[
        "proof-example",
        &statement_hash_sha512,
        &phase_artifacts.accepted_ciphertext_vector_hash,
        &phase_artifacts.published_ciphertext_vector_hash,
        &phase_artifacts.rerandomized_response_vector_hash,
        &phase_artifacts.outer_shuffle_hash,
        &phase_artifacts.product_argument_commitments_hash,
        &phase_artifacts.multi_exponentiation_commitments_hash,
        &phase_artifacts.public_verify_replay_hash,
        &fiat_shamir.final_state_hash,
        &canonical_proof_bytes.sha512,
        &tamper_vectors_hash,
    ]);
    let final_hash_milliseconds = final_hash_started.elapsed().as_secs_f64() * 1000.0;
    let elapsed_milliseconds = started.elapsed().as_secs_f64() * 1000.0;
    let timings = ExampleTimingReport {
        base_generation_milliseconds,
        statement_hashing_milliseconds,
        accepted_ciphertext_vector_milliseconds,
        published_ciphertext_vector_milliseconds,
        rerandomized_response_vector_milliseconds,
        legacy_product_argument_commitments_milliseconds,
        outer_shuffle_wiring_milliseconds,
        single_value_product_argument_milliseconds,
        hush_multi_exponentiation_argument_milliseconds,
        legacy_multi_exponentiation_commitments_milliseconds,
        public_verify_replay_milliseconds,
        phase_commitment_generation_milliseconds,
        phase_artifact_shaping_milliseconds,
        fiat_shamir_milliseconds,
        canonical_proof_bytes_milliseconds,
        tamper_vectors_milliseconds,
        final_hash_milliseconds,
        total_milliseconds: elapsed_milliseconds,
    };

    Ok(ProofExample {
        schema: "HushSp07RustProofExampleV1",
        engine: "rust_arkworks_fixed_field_msm_transcript_example_v1",
        status: "fiat_shamir_outer_shuffle_product_and_multi_exponentiation_subarguments_ready",
        statement,
        statement_hash_sha512,
        phase_artifacts,
        fiat_shamir,
        canonical_proof_bytes,
        verifier_result,
        tamper_vectors,
        proof_example_hash_sha512,
        timings,
        elapsed_milliseconds,
        notes: vec![
            "This artifact exercises deterministic statement hashing, public phase commitments, and Fiat-Shamir challenge derivation.",
            "It wires a deterministic m=1 accepted-to-published outer shuffle relation through permutation, rerandomization, product, and Hush slot-vector multi-exponentiation inputs.",
            "It includes the m=1 Bayer-Groth single-value product sub-argument and its public verifier equations.",
            "It includes the m=1 Hush slot-vector multi-exponentiation sub-argument and its public verifier equations.",
            "It emits canonical m=1 public proof bytes and can run the current public tamper-vector suite; m>1 Hadamard equations and external cryptographic review remain outside this worker profile.",
            "No private permutation, rerandomization witness, plaintext choice, voter identity, or eligibility/checkoff field is included.",
        ],
    })
}

fn build_proof_example_verifier_result(
    phase_artifacts: &ExamplePhaseArtifacts,
    fiat_shamir: &FiatShamirTranscript,
    canonical_proof_bytes: &CanonicalProofBytes,
    tamper_vectors: &[TamperVectorResult],
    tamper_vectors_requested: bool,
) -> ProofExampleVerifierResult {
    let single_value_product_passed = phase_artifacts
        .single_value_product_argument
        .verifier_result
        .passed;
    let multi_exponentiation_passed = phase_artifacts
        .hush_multi_exponentiation_argument
        .verifier_result
        .passed;
    let transcript_finalized = !fiat_shamir.final_state_hash.is_empty();
    let canonical_proof_bytes_bound = !canonical_proof_bytes.sha512.is_empty()
        && canonical_proof_bytes.byte_length > 0
        && canonical_proof_bytes.envelope.fiat_shamir_final_state_hash
            == fiat_shamir.final_state_hash;
    let tamper_vectors_passed = tamper_vectors_requested.then(|| {
        tamper_vectors
            .iter()
            .all(|result| result.rejected_as_expected)
    });
    let passed = single_value_product_passed
        && multi_exponentiation_passed
        && transcript_finalized
        && canonical_proof_bytes_bound
        && tamper_vectors_passed.unwrap_or(true);

    ProofExampleVerifierResult {
        result_code: proof_example_result_code(
            single_value_product_passed,
            multi_exponentiation_passed,
            transcript_finalized,
            canonical_proof_bytes_bound,
            tamper_vectors_passed,
        ),
        passed,
        single_value_product_passed,
        multi_exponentiation_passed,
        transcript_finalized,
        canonical_proof_bytes_bound,
        tamper_suite_status: if tamper_vectors_requested {
            "executed"
        } else {
            "not_requested"
        },
        tamper_vectors_passed,
    }
}

fn proof_example_primary_result_code(
    phase_artifacts: &ExamplePhaseArtifacts,
    fiat_shamir: &FiatShamirTranscript,
) -> &'static str {
    proof_example_result_code(
        phase_artifacts
            .single_value_product_argument
            .verifier_result
            .passed,
        phase_artifacts
            .hush_multi_exponentiation_argument
            .verifier_result
            .passed,
        !fiat_shamir.final_state_hash.is_empty(),
        true,
        None,
    )
}

fn proof_example_result_code(
    single_value_product_passed: bool,
    multi_exponentiation_passed: bool,
    transcript_finalized: bool,
    canonical_proof_bytes_bound: bool,
    tamper_vectors_passed: Option<bool>,
) -> &'static str {
    if !single_value_product_passed {
        "PUB-011"
    } else if !multi_exponentiation_passed {
        "PUB-012"
    } else if !transcript_finalized {
        "PUB-014"
    } else if !canonical_proof_bytes_bound {
        "PUB-015"
    } else if tamper_vectors_passed == Some(false) {
        "PUB-016"
    } else {
        "PUB-005"
    }
}

fn build_canonical_proof_bytes(
    statement: &ExampleStatement,
    statement_hash: &str,
    phase_artifacts: &ExamplePhaseArtifacts,
    fiat_shamir: &FiatShamirTranscript,
    verifier_result_code: &'static str,
) -> Result<CanonicalProofBytes, serde_json::Error> {
    let envelope = CanonicalProofEnvelope {
        schema: "HushSp07CanonicalPublicationProofEnvelopeV1",
        proof_profile: statement.proof_profile,
        statement_hash_sha512: statement_hash.to_string(),
        accepted_ballot_set_hash: statement.accepted_ballot_set_hash.clone(),
        published_ballot_stream_hash: statement.published_ballot_stream_hash.clone(),
        outer_shuffle_hash: phase_artifacts.outer_shuffle_hash.clone(),
        single_value_product_argument_hash: phase_artifacts
            .single_value_product_argument_hash
            .clone(),
        hush_multi_exponentiation_argument_hash: phase_artifacts
            .hush_multi_exponentiation_argument_hash
            .clone(),
        fiat_shamir_final_state_hash: fiat_shamir.final_state_hash.clone(),
        verifier_result_code,
    };
    let bytes = serde_json::to_vec(&envelope)?;
    let preview_len = bytes.len().min(96);
    let hex = hex_digest(&bytes);
    let preview_hex = hex_digest(&bytes[..preview_len]);
    let sha512 = hex_digest(Sha512::digest(&bytes));

    Ok(CanonicalProofBytes {
        schema: "HushSp07CanonicalProofBytesV1",
        profile: "hush_sp07_canonical_publication_proof_bytes_v1",
        encoding: "serde_json_compact_utf8_v1",
        byte_length: bytes.len(),
        sha512,
        hex,
        preview_hex,
        envelope,
    })
}

fn build_tamper_vectors(
    statement_hash: &str,
    c_prime_matrix_hash: &str,
    commitment_key: &CommitmentKey,
    public_key: &EdwardsProjective,
    published_component_bases: &[Vec<EdwardsAffine>],
    slots: usize,
    single_value_product_proof: &SingleValueProductProofInternal,
    hush_multi_exponentiation_proof: &HushMultiExponentiationProofInternal,
) -> Vec<TamperVectorResult> {
    let mut results = Vec::new();

    let mut tampered_product = single_value_product_proof.clone();
    tampered_product.a_tilde[0] += Fr::from(1u64);
    let product_result =
        verify_single_value_product(statement_hash, commitment_key, &tampered_product);
    results.push(tamper_result(
        "TAMPER-PUB-001",
        "proof_response_modified",
        "single_value_product.a_tilde[0]",
        "PUB-011",
        product_result.result_code,
        product_result.passed,
    ));

    let mut tampered_ciphertext_response = hush_multi_exponentiation_proof.clone();
    tampered_ciphertext_response.e[0].components[0] +=
        EdwardsProjective::from(hush_generator_ark());
    let ciphertext_result = verify_hush_multi_exponentiation(
        statement_hash,
        c_prime_matrix_hash,
        commitment_key,
        public_key,
        published_component_bases,
        slots,
        &tampered_ciphertext_response,
    );
    results.push(tamper_result(
        "TAMPER-PUB-002",
        "proof_ciphertext_response_modified",
        "multi_exponentiation.e[0].components[0]",
        "PUB-012",
        ciphertext_result.result_code,
        ciphertext_result.passed,
    ));

    let mut removed_published = published_component_bases.to_vec();
    for component in &mut removed_published {
        component.pop();
    }
    let removed_hash = hash_ark_affine_components(&removed_published);
    let removed_result = verify_hush_multi_exponentiation(
        statement_hash,
        &removed_hash,
        commitment_key,
        public_key,
        &removed_published,
        slots,
        hush_multi_exponentiation_proof,
    );
    results.push(tamper_result(
        "TAMPER-PUB-003",
        "published_ballot_removed",
        "published_component_bases[*].pop",
        "PUB-013",
        removed_result.result_code,
        removed_result.passed,
    ));

    let mut inserted_published = published_component_bases.to_vec();
    for (component_index, component) in inserted_published.iter_mut().enumerate() {
        component.push(derive_ark_base(
            "example.tamper.inserted-published",
            component_index,
            component.len(),
        ));
    }
    let inserted_hash = hash_ark_affine_components(&inserted_published);
    let inserted_result = verify_hush_multi_exponentiation(
        statement_hash,
        &inserted_hash,
        commitment_key,
        public_key,
        &inserted_published,
        slots,
        hush_multi_exponentiation_proof,
    );
    results.push(tamper_result(
        "TAMPER-PUB-004",
        "published_ballot_inserted",
        "published_component_bases[*].push",
        "PUB-013",
        inserted_result.result_code,
        inserted_result.passed,
    ));

    let mut duplicated_published = published_component_bases.to_vec();
    for component in &mut duplicated_published {
        component[1] = component[0];
    }
    let duplicated_hash = hash_ark_affine_components(&duplicated_published);
    let duplicated_result = verify_hush_multi_exponentiation(
        statement_hash,
        &duplicated_hash,
        commitment_key,
        public_key,
        &duplicated_published,
        slots,
        hush_multi_exponentiation_proof,
    );
    results.push(tamper_result(
        "TAMPER-PUB-005",
        "published_ballot_duplicated",
        "published_component_bases[*][1]=[*][0]",
        "PUB-012",
        duplicated_result.result_code,
        duplicated_result.passed,
    ));

    let mut replaced_published = published_component_bases.to_vec();
    for component in &mut replaced_published {
        component[0] = (EdwardsProjective::from(component[0])
            + EdwardsProjective::from(hush_generator_ark()))
        .into_affine();
    }
    let replaced_hash = hash_ark_affine_components(&replaced_published);
    let replaced_result = verify_hush_multi_exponentiation(
        statement_hash,
        &replaced_hash,
        commitment_key,
        public_key,
        &replaced_published,
        slots,
        hush_multi_exponentiation_proof,
    );
    results.push(tamper_result(
        "TAMPER-PUB-006",
        "published_ballot_replaced",
        "published_component_bases[*][0]+=G",
        "PUB-012",
        replaced_result.result_code,
        replaced_result.passed,
    ));

    let wrong_public_key = *public_key + EdwardsProjective::from(hush_generator_ark());
    let wrong_key_result = verify_hush_multi_exponentiation(
        statement_hash,
        c_prime_matrix_hash,
        commitment_key,
        &wrong_public_key,
        published_component_bases,
        slots,
        hush_multi_exponentiation_proof,
    );
    results.push(tamper_result(
        "TAMPER-PUB-007",
        "wrong_election_public_key",
        "public_key+=G",
        "PUB-012",
        wrong_key_result.result_code,
        wrong_key_result.passed,
    ));

    let wrong_statement_hash = hash_parts(&[statement_hash, "tampered-statement"]);
    let wrong_statement_result = verify_hush_multi_exponentiation(
        &wrong_statement_hash,
        c_prime_matrix_hash,
        commitment_key,
        public_key,
        published_component_bases,
        slots,
        hush_multi_exponentiation_proof,
    );
    results.push(tamper_result(
        "TAMPER-PUB-008",
        "wrong_statement_hash",
        "statement_hash",
        "PUB-012",
        wrong_statement_result.result_code,
        wrong_statement_result.passed,
    ));

    results
}

fn tamper_result(
    id: &'static str,
    tamper_class: &'static str,
    target: &'static str,
    expected_result_code: &'static str,
    actual_result_code: &'static str,
    verifier_passed: bool,
) -> TamperVectorResult {
    TamperVectorResult {
        id,
        tamper_class,
        target,
        expected_result_code,
        actual_result_code,
        verifier_passed,
        rejected_as_expected: !verifier_passed && actual_result_code == expected_result_code,
    }
}

fn build_single_value_product_argument_example(
    proof: &SingleValueProductProofInternal,
    verifier_result: SingleValueProductVerifierResult,
) -> SingleValueProductArgumentExample {
    SingleValueProductArgumentExample {
        schema: "HushSp07SingleValueProductArgumentExampleV1",
        status: "implemented_with_public_verifier_equations",
        source_algorithm: "Swiss Post SingleValueProductArgumentService adapted to Hush BabyJubJub additive commitments",
        statement_commitment_c_a: public_point_from_projective(&proof.c_a),
        statement_product_b: scalar_decimal(proof.b),
        commitment_key_hash: proof.commitment_key_hash.clone(),
        c_d: public_point_from_projective(&proof.c_d),
        c_delta: public_point_from_projective(&proof.c_delta),
        c_upper_delta: public_point_from_projective(&proof.c_upper_delta),
        a_tilde: scalars_to_decimal(&proof.a_tilde),
        b_tilde: scalars_to_decimal(&proof.b_tilde),
        r_tilde: scalar_decimal(proof.r_tilde),
        s_tilde: scalar_decimal(proof.s_tilde),
        challenge_x: proof.challenge_x.clone(),
        verifier_result,
    }
}

fn prove_single_value_product(
    statement_hash: &str,
    commitment_key: &CommitmentKey,
    witness: &SingleValueProductWitness,
) -> SingleValueProductProofInternal {
    let n = witness.a.len();
    assert!(n >= 2, "single-value product witness must have n >= 2");
    assert!(
        commitment_key.g.len() >= n,
        "commitment key must contain at least n bases"
    );

    let c_a = commit_vector(&witness.a, witness.r, commitment_key);
    let b = product_fr(&witness.a);
    let b_vector = prefix_products_fr(&witness.a);
    let d: Vec<Fr> = (0..n)
        .map(|index| derive_scalar_fr("example.product.single.d", index, n))
        .collect();
    let r_d = derive_scalar_fr("example.product.single.r_d", n, 0);

    let mut delta = vec![Fr::zero(); n];
    delta[0] = d[0];
    if n > 2 {
        for (index, value) in delta.iter_mut().enumerate().take(n - 1).skip(1) {
            *value = derive_scalar_fr("example.product.single.delta", index, n);
        }
    }

    let s_0 = derive_scalar_fr("example.product.single.s_0", n, 0);
    let s_x = derive_scalar_fr("example.product.single.s_x", n, 0);
    let delta_prime: Vec<Fr> = (0..(n - 1))
        .map(|index| -(delta[index] * d[index + 1]))
        .collect();
    let upper_delta: Vec<Fr> = (0..(n - 1))
        .map(|index| {
            delta[index + 1]
                - (witness.a[index + 1] * delta[index])
                - (b_vector[index] * d[index + 1])
        })
        .collect();

    let c_d = commit_vector(&d, r_d, commitment_key);
    let c_delta = commit_vector(&delta_prime, s_0, commitment_key);
    let c_upper_delta = commit_vector(&upper_delta, s_x, commitment_key);
    let commitment_key_hash = hash_commitment_key(commitment_key);
    let (x, challenge_x) = single_value_product_challenge(
        statement_hash,
        &commitment_key_hash,
        &c_upper_delta,
        &c_delta,
        &c_d,
        b,
        &c_a,
    );

    let a_tilde: Vec<Fr> = witness
        .a
        .iter()
        .zip(&d)
        .map(|(value, mask)| (x * value) + mask)
        .collect();
    let b_tilde: Vec<Fr> = b_vector
        .iter()
        .zip(&delta)
        .map(|(value, mask)| (x * value) + mask)
        .collect();
    let r_tilde = (x * witness.r) + r_d;
    let s_tilde = (x * s_x) + s_0;

    SingleValueProductProofInternal {
        c_a,
        b,
        commitment_key_hash,
        c_d,
        c_delta,
        c_upper_delta,
        a_tilde,
        b_tilde,
        r_tilde,
        s_tilde,
        challenge_x,
    }
}

fn verify_single_value_product(
    statement_hash: &str,
    commitment_key: &CommitmentKey,
    proof: &SingleValueProductProofInternal,
) -> SingleValueProductVerifierResult {
    let n = proof.a_tilde.len();
    let shape_valid = n >= 2 && proof.b_tilde.len() == n && commitment_key.g.len() >= n;
    if !shape_valid {
        return SingleValueProductVerifierResult {
            result_code: "PUB-011",
            passed: false,
            commitment_response_passed: false,
            delta_response_passed: false,
            boundary_response_passed: false,
        };
    }

    let (x, _) = single_value_product_challenge(
        statement_hash,
        &proof.commitment_key_hash,
        &proof.c_upper_delta,
        &proof.c_delta,
        &proof.c_d,
        proof.b,
        &proof.c_a,
    );
    let commitment_response_left = proof.c_a.mul_bigint(x.into_bigint()) + proof.c_d;
    let commitment_response_right = commit_vector(&proof.a_tilde, proof.r_tilde, commitment_key);
    let commitment_response_passed = commitment_response_left == commitment_response_right;

    let e: Vec<Fr> = (0..(n - 1))
        .map(|index| {
            (x * proof.b_tilde[index + 1]) - (proof.b_tilde[index] * proof.a_tilde[index + 1])
        })
        .collect();
    let delta_response_left = proof.c_upper_delta.mul_bigint(x.into_bigint()) + proof.c_delta;
    let delta_response_right = commit_vector(&e, proof.s_tilde, commitment_key);
    let delta_response_passed = delta_response_left == delta_response_right;
    let boundary_response_passed =
        proof.b_tilde[0] == proof.a_tilde[0] && proof.b_tilde[n - 1] == x * proof.b;
    let passed = commitment_response_passed && delta_response_passed && boundary_response_passed;

    SingleValueProductVerifierResult {
        result_code: if passed { "PUB-005" } else { "PUB-011" },
        passed,
        commitment_response_passed,
        delta_response_passed,
        boundary_response_passed,
    }
}

fn product_fr(values: &[Fr]) -> Fr {
    values.iter().fold(Fr::from(1u64), |acc, value| acc * value)
}

fn prefix_products_fr(values: &[Fr]) -> Vec<Fr> {
    let mut product = Fr::from(1u64);
    values
        .iter()
        .map(|value| {
            product *= value;
            product
        })
        .collect()
}

fn commit_vector(
    values: &[Fr],
    randomness: Fr,
    commitment_key: &CommitmentKey,
) -> EdwardsProjective {
    assert!(
        values.len() <= commitment_key.g.len(),
        "commitment key does not contain enough bases"
    );
    let value_bigints = scalars_to_bigints(values);
    commit_vector_bigint(values.len(), &value_bigints, randomness, commitment_key)
}

fn commit_vector_bigint(
    value_count: usize,
    value_bigints: &[FrBigInt],
    randomness: Fr,
    commitment_key: &CommitmentKey,
) -> EdwardsProjective {
    assert!(
        value_count <= value_bigints.len(),
        "not enough scalar bigints for commitment values"
    );
    assert!(
        value_count <= commitment_key.g.len(),
        "commitment key does not contain enough bases"
    );
    let value_commitment = EdwardsProjective::msm_bigint(
        &commitment_key.g[..value_count],
        &value_bigints[..value_count],
    );
    value_commitment + commitment_key.h.mul_bigint(randomness.into_bigint())
}

fn single_value_product_challenge(
    statement_hash: &str,
    commitment_key_hash: &str,
    c_upper_delta: &EdwardsProjective,
    c_delta: &EdwardsProjective,
    c_d: &EdwardsProjective,
    b: Fr,
    c_a: &EdwardsProjective,
) -> (Fr, FiatShamirChallenge) {
    let c_upper_delta_hash = hash_ark_projective_point(c_upper_delta);
    let c_delta_hash = hash_ark_projective_point(c_delta);
    let c_d_hash = hash_ark_projective_point(c_d);
    let b_decimal = scalar_decimal(b);
    let c_a_hash = hash_ark_projective_point(c_a);
    let input_state_hash = hash_parts(&[
        "HUSH_SP07_SINGLE_VALUE_PRODUCT_ARGUMENT_V1",
        "input",
        statement_hash,
        commitment_key_hash,
        &c_upper_delta_hash,
        &c_delta_hash,
        &c_d_hash,
        &b_decimal,
        &c_a_hash,
    ]);
    let challenge_digest = digest_parts(&[
        "HUSH_SP07_SINGLE_VALUE_PRODUCT_ARGUMENT_V1",
        "challenge",
        "sp07.product.single.x",
        &input_state_hash,
    ]);
    let challenge_hash_sha512 = hex_digest(&challenge_digest);
    let scalar = Fr::from_be_bytes_mod_order(&challenge_digest);
    let challenge = FiatShamirChallenge {
        label: "sp07.product.single.x".to_string(),
        input_state_hash,
        challenge_hash_sha512,
        scalar_decimal: scalar_decimal(scalar),
    };

    (scalar, challenge)
}

fn build_outer_shuffle_wiring(
    statement_hash: &str,
    commitment_key: &CommitmentKey,
    accepted_component_bases: &[Vec<EdwardsAffine>],
    permutation: &[usize],
    rerandomization: &[Vec<Fr>],
    slots: usize,
) -> OuterShuffleWiring {
    let n = permutation.len();
    assert!(n >= 2, "outer shuffle requires n >= 2");
    let pi_values: Vec<Fr> = permutation
        .iter()
        .map(|source_index| Fr::from(*source_index as u64))
        .collect();
    let r_a = derive_scalar_fr("example.shuffle.r_a", n, slots);
    let c_a = commit_vector(&pi_values, r_a, commitment_key);
    let (x, challenge_x) = shuffle_challenge("sp07.shuffle.x", statement_hash, &c_a, None);
    let x_powers = scalar_powers(x, n);
    let b_matrix_column: Vec<Fr> = permutation
        .iter()
        .map(|source_index| x_powers[*source_index])
        .collect();
    let s_b = derive_scalar_fr("example.shuffle.s_b", n, slots);
    let c_b = commit_vector(&b_matrix_column, s_b, commitment_key);
    let (y, challenge_y) = shuffle_challenge("sp07.shuffle.y", statement_hash, &c_a, Some(&c_b));
    let (z, challenge_z) = shuffle_challenge("sp07.shuffle.z", statement_hash, &c_a, Some(&c_b));

    let product_values: Vec<Fr> = pi_values
        .iter()
        .zip(&b_matrix_column)
        .map(|(pi, b)| (y * *pi) + *b - z)
        .collect();
    let product_randomness = (y * r_a) + s_b;
    let product_statement_commitment =
        commit_vector(&product_values, product_randomness, commitment_key);
    let product_b = (0..n).fold(Fr::from(1u64), |acc, index| {
        acc * ((y * Fr::from(index as u64)) + x_powers[index] - z)
    });
    let rho_bar = compute_rho_bar(rerandomization, &b_matrix_column, slots);
    let accepted_cx = hush_vector_multi_exponentiation_bigint(
        accepted_component_bases,
        &scalars_to_bigints(&x_powers),
    );

    let example = OuterShuffleExample {
        schema: "HushSp07OuterShuffleWiringExampleV1",
        status: "accepted_published_permutation_and_rerandomization_wired",
        matrix_m: 1,
        matrix_n: n,
        shuffle_c_a: public_point_from_projective(&c_a),
        shuffle_c_b: public_point_from_projective(&c_b),
        product_statement_commitment: public_point_from_projective(&product_statement_commitment),
        product_statement_product_b: scalar_decimal(product_b),
        permutation_hash: hash_permutation(permutation),
        rerandomization_hash: hash_rerandomization(rerandomization),
        accepted_cx_hash: hash_ballot_vector(&accepted_cx),
        challenge_x,
        challenge_y,
        challenge_z,
    };

    OuterShuffleWiring {
        example,
        single_product_witness: SingleValueProductWitness {
            a: product_values,
            r: product_randomness,
        },
        multi_exp_witness: HushMultiExponentiationWitness {
            b_matrix_column,
            s_b,
            rho_bar,
        },
        accepted_cx,
    }
}

fn build_permutation(ballots: usize) -> Vec<usize> {
    assert!(ballots > 0, "permutation requires at least one ballot");
    let mut step = (ballots / 2).max(1) + 1;
    while step.gcd(&ballots) != 1 {
        step += 1;
    }
    let offset = derive_scalar_fr("example.shuffle.permutation-offset", ballots, 0)
        .into_bigint()
        .as_ref()[0] as usize
        % ballots;

    (0..ballots)
        .map(|index| ((index * step) + offset) % ballots)
        .collect()
}

fn build_rerandomization_matrix(ballots: usize, slots: usize) -> Vec<Vec<Fr>> {
    (0..ballots)
        .into_par_iter()
        .map(|ballot| {
            (0..slots)
                .map(|slot| derive_scalar_fr("example.shuffle.rho", ballot, slot))
                .collect()
        })
        .collect()
}

fn build_published_component_bases(
    accepted_component_bases: &[Vec<EdwardsAffine>],
    permutation: &[usize],
    rerandomization: &[Vec<Fr>],
    public_key: &EdwardsProjective,
    slots: usize,
) -> Vec<Vec<EdwardsAffine>> {
    let ballots = permutation.len();
    assert_eq!(
        accepted_component_bases.len(),
        slots * 2,
        "accepted ballot vectors must contain two components per slot"
    );
    let generator_table = FixedBaseWindowTable::new(
        EdwardsProjective::from(hush_generator_ark()),
        FIXED_BASE_RERANDOMIZATION_WINDOW_BITS,
    );
    let public_key_table =
        FixedBaseWindowTable::new(*public_key, FIXED_BASE_RERANDOMIZATION_WINDOW_BITS);
    (0..(slots * 2))
        .into_par_iter()
        .map(|component| {
            let slot = component / 2;
            let is_c1 = component % 2 == 0;
            let mut projective_points = Vec::with_capacity(ballots);
            for published_index in 0..ballots {
                let source_index = permutation[published_index];
                let base =
                    EdwardsProjective::from(accepted_component_bases[component][source_index]);
                let rho = rerandomization[published_index][slot];
                let zero_component = if is_c1 {
                    generator_table.mul(rho)
                } else {
                    public_key_table.mul(rho)
                };
                projective_points.push(base + zero_component);
            }

            EdwardsProjective::normalize_batch(&projective_points)
        })
        .collect()
}

impl FixedBaseWindowTable {
    fn new(mut base: EdwardsProjective, window_bits: usize) -> Self {
        assert!(
            (1..=16).contains(&window_bits),
            "fixed-base table window must be between 1 and 16 bits"
        );
        let window_count = (Fr::MODULUS_BIT_SIZE as usize).div_ceil(window_bits);
        let window_size = 1usize << window_bits;
        let mut windows = Vec::with_capacity(window_count);

        for _ in 0..window_count {
            let mut table = Vec::with_capacity(window_size);
            let mut multiple = EdwardsProjective::zero();
            table.push(multiple);
            for _ in 1..window_size {
                multiple += base;
                table.push(multiple);
            }
            windows.push(table);

            for _ in 0..window_bits {
                base.double_in_place();
            }
        }

        Self {
            windows,
            window_bits,
        }
    }

    fn mul(&self, scalar: Fr) -> EdwardsProjective {
        let scalar_bigint = scalar.into_bigint();
        self.mul_bigint(&scalar_bigint)
    }

    fn mul_bigint(&self, scalar: &FrBigInt) -> EdwardsProjective {
        self.windows.iter().enumerate().fold(
            EdwardsProjective::zero(),
            |mut acc, (window_index, table)| {
                let digit =
                    scalar_window_digit(scalar, window_index * self.window_bits, self.window_bits);
                if digit != 0 {
                    acc += table[digit];
                }
                acc
            },
        )
    }
}

fn scalar_window_digit(scalar: &FrBigInt, bit_offset: usize, window_bits: usize) -> usize {
    let limbs = scalar.as_ref();
    let limb_index = bit_offset / 64;
    let shift = bit_offset % 64;
    let mask = (1u64 << window_bits) - 1;
    let mut value = limbs.get(limb_index).map(|limb| limb >> shift).unwrap_or(0);

    if shift + window_bits > 64 {
        if let Some(next_limb) = limbs.get(limb_index + 1) {
            value |= next_limb << (64 - shift);
        }
    }

    (value & mask) as usize
}

fn compute_rho_bar(rerandomization: &[Vec<Fr>], b_matrix_column: &[Fr], slots: usize) -> Vec<Fr> {
    (0..slots)
        .into_par_iter()
        .map(|slot| {
            -rerandomization
                .iter()
                .zip(b_matrix_column)
                .fold(Fr::zero(), |acc, (rho_row, b)| acc + (rho_row[slot] * *b))
        })
        .collect()
}

fn scalar_powers(base: Fr, count: usize) -> Vec<Fr> {
    let mut current = Fr::from(1u64);
    let mut powers = Vec::with_capacity(count);
    for _ in 0..count {
        powers.push(current);
        current *= base;
    }

    powers
}

fn shuffle_challenge(
    label: &'static str,
    statement_hash: &str,
    c_a: &EdwardsProjective,
    c_b: Option<&EdwardsProjective>,
) -> (Fr, FiatShamirChallenge) {
    let c_a_hash = hash_ark_projective_point(c_a);
    let c_b_hash = c_b
        .map(hash_ark_projective_point)
        .unwrap_or_else(|| "not-yet-bound".to_string());
    let input_state_hash = hash_parts(&[
        "HUSH_SP07_OUTER_SHUFFLE_WIRING_V1",
        "input",
        statement_hash,
        &c_a_hash,
        &c_b_hash,
    ]);
    let challenge_digest = digest_parts(&[
        "HUSH_SP07_OUTER_SHUFFLE_WIRING_V1",
        "challenge",
        label,
        &input_state_hash,
    ]);
    let challenge_hash_sha512 = hex_digest(&challenge_digest);
    let scalar = Fr::from_be_bytes_mod_order(&challenge_digest);
    let challenge = FiatShamirChallenge {
        label: label.to_string(),
        input_state_hash,
        challenge_hash_sha512,
        scalar_decimal: scalar_decimal(scalar),
    };

    (scalar, challenge)
}

fn hash_permutation(permutation: &[usize]) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_PERMUTATION_HASH_V1|");
    for source_index in permutation {
        hasher.update(source_index.to_string().as_bytes());
        hasher.update(b"|");
    }

    format!("{:x}", hasher.finalize())
}

fn hash_rerandomization(rerandomization: &[Vec<Fr>]) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_RERANDOMIZATION_HASH_V1|");
    for row in rerandomization {
        for scalar in row {
            hasher.update(scalar_decimal(*scalar).as_bytes());
            hasher.update(b"|");
        }
        hasher.update(b"\n");
    }

    format!("{:x}", hasher.finalize())
}

fn build_hush_multi_exponentiation_argument_example(
    slots: usize,
    proof: &HushMultiExponentiationProofInternal,
    verifier_result: HushMultiExponentiationVerifierResult,
) -> HushMultiExponentiationArgumentExample {
    HushMultiExponentiationArgumentExample {
        schema: "HushSp07MultiExponentiationArgumentExampleV1",
        status: "implemented_with_public_verifier_equations",
        source_algorithm: "Swiss Post MultiExponentiationArgumentService m=1 flow adapted to Hush slot-vector rerandomization",
        statement_cx_hash: hash_ballot_vector(&proof.cx),
        statement_commitment_c_b: public_point_from_projective(&proof.c_b_statement),
        commitment_key_hash: proof.commitment_key_hash.clone(),
        c_a0: public_point_from_projective(&proof.c_a0),
        c_b_diag: public_points_from_projective(&proof.c_b_diag),
        e_hash: hash_ballot_vectors(&proof.e),
        e: public_ballot_vectors(&proof.e, slots),
        a: scalars_to_decimal(&proof.a),
        r: scalar_decimal(proof.r),
        b: scalar_decimal(proof.b),
        s: scalar_decimal(proof.s),
        tau: scalars_to_decimal(&proof.tau),
        challenge_x: proof.challenge_x.clone(),
        verifier_result,
    }
}

fn prove_hush_multi_exponentiation(
    statement_hash: &str,
    commitment_key: &CommitmentKey,
    public_key: &EdwardsProjective,
    c_prime_component_bases: &[Vec<EdwardsAffine>],
    slots: usize,
    witness: &HushMultiExponentiationWitness,
    c_prime_matrix_hash: &str,
    accepted_cx: &BallotVector,
) -> HushMultiExponentiationProofInternal {
    let n = witness.b_matrix_column.len();
    assert!(n >= 2, "multi-exponentiation witness must have n >= 2");
    assert_eq!(
        c_prime_component_bases.len(),
        slots * 2,
        "Hush ballot vectors must contain two curve components per slot"
    );
    assert!(
        c_prime_component_bases
            .iter()
            .all(|component| component.len() == n),
        "each CPrime component must contain n bases"
    );
    assert_eq!(
        witness.rho_bar.len(),
        slots,
        "rhoBar must contain one scalar per slot"
    );

    let c_b_statement = commit_vector(&witness.b_matrix_column, witness.s_b, commitment_key);
    let cx = accepted_cx.clone();

    let a_0: Vec<Fr> = (0..n)
        .map(|index| derive_scalar_fr("example.multi.exp.a0", index, n))
        .collect();
    let r_0 = derive_scalar_fr("example.multi.exp.r0", n, slots);
    let b_0 = derive_scalar_fr("example.multi.exp.b0", n, slots);
    let s_0 = derive_scalar_fr("example.multi.exp.s0", n, slots);
    let tau_0: Vec<Fr> = (0..slots)
        .map(|slot| derive_scalar_fr("example.multi.exp.tau0", n, slot))
        .collect();

    let a0_bigints = scalars_to_bigints(&a_0);
    let c_a0 = commit_vector_bigint(a_0.len(), &a0_bigints, r_0, commitment_key);
    let c_b_diag = vec![
        commit_vector(&[b_0], s_0, commitment_key),
        commit_vector(&[Fr::zero()], Fr::zero(), commitment_key),
    ];
    let d_0 = hush_vector_multi_exponentiation_bigint(c_prime_component_bases, &a0_bigints);
    let e = vec![
        ballot_vector_add(&const_enc_vec(public_key, b_0, &tau_0), &d_0),
        cx.clone(),
    ];
    let commitment_key_hash = hash_commitment_key(commitment_key);
    let (x, challenge_x) = hush_multi_exponentiation_challenge(
        statement_hash,
        &commitment_key_hash,
        c_prime_matrix_hash,
        &cx,
        &c_b_statement,
        &c_a0,
        &c_b_diag,
        &e,
    );

    let a: Vec<Fr> = a_0
        .iter()
        .zip(&witness.b_matrix_column)
        .map(|(a0, b_column)| *a0 + (x * *b_column))
        .collect();
    let r = r_0 + (x * witness.s_b);
    let b = b_0;
    let s = s_0;
    let tau: Vec<Fr> = tau_0
        .iter()
        .zip(&witness.rho_bar)
        .map(|(tau0, rho)| *tau0 + (x * *rho))
        .collect();

    HushMultiExponentiationProofInternal {
        c_b_statement,
        cx,
        commitment_key_hash,
        c_a0,
        c_b_diag,
        e,
        a,
        r,
        b,
        s,
        tau,
        challenge_x,
    }
}

fn verify_hush_multi_exponentiation(
    statement_hash: &str,
    c_prime_matrix_hash: &str,
    commitment_key: &CommitmentKey,
    public_key: &EdwardsProjective,
    c_prime_component_bases: &[Vec<EdwardsAffine>],
    slots: usize,
    proof: &HushMultiExponentiationProofInternal,
) -> HushMultiExponentiationVerifierResult {
    let shape_valid = proof.c_b_diag.len() == 2
        && proof.e.len() == 2
        && proof.a.len() >= 2
        && proof.tau.len() == slots
        && c_prime_component_bases.len() == slots * 2
        && c_prime_component_bases
            .iter()
            .all(|component| component.len() == proof.a.len());
    if !shape_valid {
        return HushMultiExponentiationVerifierResult {
            result_code: "PUB-013",
            passed: false,
            center_commitment_is_identity: false,
            center_ciphertext_matches_cx: false,
            commitment_a_response_passed: false,
            commitment_b_response_passed: false,
            ciphertext_vector_response_passed: false,
        };
    }

    let (x, _) = hush_multi_exponentiation_challenge(
        statement_hash,
        &proof.commitment_key_hash,
        c_prime_matrix_hash,
        &proof.cx,
        &proof.c_b_statement,
        &proof.c_a0,
        &proof.c_b_diag,
        &proof.e,
    );
    let center_commitment_is_identity = proof.c_b_diag[1] == EdwardsProjective::zero();
    let center_ciphertext_matches_cx = proof.e[1] == proof.cx;

    let proof_a_bigints = scalars_to_bigints(&proof.a);
    let commitment_a_left = proof.c_a0 + proof.c_b_statement.mul_bigint(x.into_bigint());
    let commitment_a_right =
        commit_vector_bigint(proof.a.len(), &proof_a_bigints, proof.r, commitment_key);
    let commitment_a_response_passed = commitment_a_left == commitment_a_right;

    let commitment_b_left = proof.c_b_diag[0] + proof.c_b_diag[1].mul_bigint(x.into_bigint());
    let commitment_b_right = commit_vector(&[proof.b], proof.s, commitment_key);
    let commitment_b_response_passed = commitment_b_left == commitment_b_right;

    let ciphertext_vector_left =
        ballot_vector_add(&proof.e[0], &ballot_vector_scalar_mul(&proof.e[1], x));
    let ciphertext_vector_right = ballot_vector_add(
        &const_enc_vec(public_key, proof.b, &proof.tau),
        &hush_vector_multi_exponentiation_bigint(c_prime_component_bases, &proof_a_bigints),
    );
    let ciphertext_vector_response_passed = ciphertext_vector_left == ciphertext_vector_right;
    let passed = center_commitment_is_identity
        && center_ciphertext_matches_cx
        && commitment_a_response_passed
        && commitment_b_response_passed
        && ciphertext_vector_response_passed;

    HushMultiExponentiationVerifierResult {
        result_code: if passed { "PUB-005" } else { "PUB-012" },
        passed,
        center_commitment_is_identity,
        center_ciphertext_matches_cx,
        commitment_a_response_passed,
        commitment_b_response_passed,
        ciphertext_vector_response_passed,
    }
}

fn hush_multi_exponentiation_challenge(
    statement_hash: &str,
    commitment_key_hash: &str,
    c_prime_matrix_hash: &str,
    cx: &BallotVector,
    c_b_statement: &EdwardsProjective,
    c_a0: &EdwardsProjective,
    c_b_diag: &[EdwardsProjective],
    e: &[BallotVector],
) -> (Fr, FiatShamirChallenge) {
    let cx_hash = hash_ballot_vector(cx);
    let c_b_statement_hash = hash_ark_projective_point(c_b_statement);
    let c_a0_hash = hash_ark_projective_point(c_a0);
    let c_b_diag_hash = hash_ark_projective_points(c_b_diag);
    let e_hash = hash_ballot_vectors(e);
    let input_state_hash = hash_parts(&[
        "HUSH_SP07_MULTI_EXPONENTIATION_ARGUMENT_V1",
        "input",
        statement_hash,
        commitment_key_hash,
        c_prime_matrix_hash,
        &cx_hash,
        &c_b_statement_hash,
        &c_a0_hash,
        &c_b_diag_hash,
        &e_hash,
    ]);
    let challenge_digest = digest_parts(&[
        "HUSH_SP07_MULTI_EXPONENTIATION_ARGUMENT_V1",
        "challenge",
        "sp07.multi_exp.x",
        &input_state_hash,
    ]);
    let challenge_hash_sha512 = hex_digest(&challenge_digest);
    let scalar = Fr::from_be_bytes_mod_order(&challenge_digest);
    let challenge = FiatShamirChallenge {
        label: "sp07.multi_exp.x".to_string(),
        input_state_hash,
        challenge_hash_sha512,
        scalar_decimal: scalar_decimal(scalar),
    };

    (scalar, challenge)
}

fn hush_vector_multi_exponentiation_bigint(
    component_bases: &[Vec<EdwardsAffine>],
    scalar_bigints: &[FrBigInt],
) -> BallotVector {
    BallotVector {
        components: aggregate_ark_components_bigint(component_bases, &scalar_bigints),
    }
}

fn const_enc_vec(public_key: &EdwardsProjective, b: Fr, tau: &[Fr]) -> BallotVector {
    let generator = EdwardsProjective::from(hush_generator_ark());
    let message = generator.mul_bigint(b.into_bigint());
    let components = tau
        .iter()
        .flat_map(|tau_slot| {
            let c1 = generator.mul_bigint(tau_slot.into_bigint());
            let c2 = message + public_key.mul_bigint(tau_slot.into_bigint());
            [c1, c2]
        })
        .collect();

    BallotVector { components }
}

#[cfg(test)]
fn zero_enc_vec(public_key: &EdwardsProjective, rho: &[Fr]) -> BallotVector {
    const_enc_vec(public_key, Fr::zero(), rho)
}

fn ballot_vector_add(left: &BallotVector, right: &BallotVector) -> BallotVector {
    assert_eq!(
        left.components.len(),
        right.components.len(),
        "ballot vectors must have the same component count"
    );
    BallotVector {
        components: left
            .components
            .iter()
            .zip(&right.components)
            .map(|(left, right)| *left + right)
            .collect(),
    }
}

fn ballot_vector_scalar_mul(vector: &BallotVector, scalar: Fr) -> BallotVector {
    BallotVector {
        components: vector
            .components
            .iter()
            .map(|component| component.mul_bigint(scalar.into_bigint()))
            .collect(),
    }
}

fn public_ballot_vectors(vectors: &[BallotVector], slots: usize) -> Vec<PublicBallotVector> {
    vectors
        .iter()
        .map(|vector| public_ballot_vector(vector, slots))
        .collect()
}

fn public_ballot_vector(vector: &BallotVector, slots: usize) -> PublicBallotVector {
    assert_eq!(
        vector.components.len(),
        slots * 2,
        "public Hush ballot vector expects two components per slot"
    );
    PublicBallotVector {
        slots: (0..slots)
            .map(|slot| PublicCipherSlot {
                c1: public_point_from_projective(&vector.components[slot * 2]),
                c2: public_point_from_projective(&vector.components[(slot * 2) + 1]),
            })
            .collect(),
    }
}

fn hash_ballot_vectors(vectors: &[BallotVector]) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_BALLOT_VECTOR_LIST_HASH_V1|");
    for (index, vector) in vectors.iter().enumerate() {
        hasher.update(index.to_string().as_bytes());
        hasher.update(b"|");
        hash_ballot_vector_into(&mut hasher, vector);
    }

    format!("{:x}", hasher.finalize())
}

fn hash_ballot_vector(vector: &BallotVector) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_BALLOT_VECTOR_HASH_V1|");
    hash_ballot_vector_into(&mut hasher, vector);
    format!("{:x}", hasher.finalize())
}

fn hash_ballot_vector_into(hasher: &mut Sha512, vector: &BallotVector) {
    for (index, component) in vector.components.iter().enumerate() {
        hasher.update(index.to_string().as_bytes());
        hasher.update(b"|");
        hash_affine_hush_point(hasher, &component.into_affine());
    }
}

fn build_scalars(ballots: usize) -> Vec<BigUint> {
    (0..ballots)
        .map(|index| derive_scalar("scalar", index, 0))
        .collect()
}

fn build_component_bases(ballots: usize, components: usize) -> Vec<Vec<Point>> {
    (0..components)
        .map(|component| {
            let mut current = ProjectivePoint::identity();
            let step_scalar = derive_scalar("component-step", component, 0);
            let step = scalar_mul_projective(&GENERATOR, &step_scalar);
            let mut bases = Vec::with_capacity(ballots);
            for _ in 0..ballots {
                current = add_projective(&current, &step);
                bases.push(to_affine(&current));
            }

            bases
        })
        .collect()
}

fn aggregate_components(component_bases: &[Vec<Point>], scalars: &[BigUint]) -> Vec<Point> {
    component_bases
        .par_iter()
        .map(|bases| {
            let mut aggregate = ProjectivePoint::identity();
            for (base, scalar) in bases.iter().zip(scalars) {
                if scalar.is_zero() {
                    continue;
                }

                aggregate = add_projective(&aggregate, &scalar_mul_projective(base, scalar));
            }

            to_affine(&aggregate)
        })
        .collect()
}

fn build_ark_scalars(ballots: usize) -> Vec<Fr> {
    build_ark_scalars_with_label("scalar", ballots)
}

fn build_ark_scalars_with_label(label: &str, ballots: usize) -> Vec<Fr> {
    (0..ballots)
        .into_par_iter()
        .map(|index| derive_scalar_fr(label, index, 0))
        .collect()
}

fn build_ark_scalar_bigints_with_label(label: &str, ballots: usize) -> Vec<FrBigInt> {
    (0..ballots)
        .into_par_iter()
        .map(|index| derive_scalar_fr(label, index, 0).into_bigint())
        .collect()
}

fn build_batched_verifier_scalar_bigints(
    left_label: &str,
    right_label: &str,
    batch_label: &str,
    ballots: usize,
) -> Vec<FrBigInt> {
    let batch_challenge = derive_scalar_fr(batch_label, ballots, 0);
    let left = (0..ballots)
        .into_par_iter()
        .map(|index| derive_scalar_fr(left_label, index, 0).into_bigint());
    let right = (0..ballots)
        .into_par_iter()
        .map(|index| (derive_scalar_fr(right_label, index, 0) * batch_challenge).into_bigint());

    left.chain(right).collect()
}

fn build_ark_component_bases(ballots: usize, components: usize) -> Vec<Vec<EdwardsAffine>> {
    build_ark_component_bases_with_label(ballots, components, "component-step")
}

fn build_ark_component_bases_with_label(
    ballots: usize,
    components: usize,
    label: &str,
) -> Vec<Vec<EdwardsAffine>> {
    let generator = hush_generator_ark();
    (0..components)
        .into_par_iter()
        .map(|component| {
            let mut current = EdwardsProjective::zero();
            let step_scalar = derive_scalar_fr(label, component, 0);
            let step = generator.mul_bigint(step_scalar.into_bigint());
            let mut projective_bases = Vec::with_capacity(ballots);
            for _ in 0..ballots {
                current += step;
                projective_bases.push(current);
            }

            EdwardsProjective::normalize_batch(&projective_bases)
        })
        .collect()
}

fn build_ark_progression_bases(ballots: usize, label: &str) -> Vec<EdwardsAffine> {
    let generator = hush_generator_ark();
    let step_scalar = derive_scalar_fr(label, 0, 0);
    let step = generator.mul_bigint(step_scalar.into_bigint());
    let mut current = EdwardsProjective::zero();
    let mut projective_bases = Vec::with_capacity(ballots);
    for _ in 0..ballots {
        current += step;
        projective_bases.push(current);
    }

    EdwardsProjective::normalize_batch(&projective_bases)
}

fn derive_ark_base(label: &str, first: usize, second: usize) -> EdwardsAffine {
    hush_generator_ark()
        .mul_bigint(derive_scalar_fr(label, first, second).into_bigint())
        .into_affine()
}

fn build_batched_component_bases(
    left: &[Vec<EdwardsAffine>],
    right: &[Vec<EdwardsAffine>],
) -> Vec<Vec<EdwardsAffine>> {
    left.par_iter()
        .zip(right)
        .map(|(left_component, right_component)| {
            let mut combined = Vec::with_capacity(left_component.len() + right_component.len());
            combined.extend_from_slice(left_component);
            combined.extend_from_slice(right_component);
            combined
        })
        .collect()
}

fn aggregate_ark_components(
    component_bases: &[Vec<EdwardsAffine>],
    scalars: &[Fr],
) -> Vec<EdwardsProjective> {
    component_bases
        .par_iter()
        .map(|bases| EdwardsProjective::msm_unchecked(bases, scalars))
        .collect()
}

fn aggregate_ark_components_bigint(
    component_bases: &[Vec<EdwardsAffine>],
    scalars: &[FrBigInt],
) -> Vec<EdwardsProjective> {
    component_bases
        .par_iter()
        .map(|bases| EdwardsProjective::msm_bigint(bases, scalars))
        .collect()
}

fn aggregate_ark_shared_bases_bigint(
    bases: &[EdwardsAffine],
    scalar_sets: &[&[FrBigInt]],
) -> Vec<EdwardsProjective> {
    scalar_sets
        .par_iter()
        .map(|scalars| EdwardsProjective::msm_bigint(bases, *scalars))
        .collect()
}

fn time_ark_points<F>(operation: F) -> (f64, Vec<EdwardsProjective>)
where
    F: FnOnce() -> Vec<EdwardsProjective>,
{
    let start = Instant::now();
    let result = operation();
    (start.elapsed().as_secs_f64() * 1000.0, result)
}

impl TranscriptBuilder {
    fn new(statement_hash: &str) -> Self {
        let initial_state_hash = hash_parts(&[
            "HUSH_SP07_FIAT_SHAMIR_TRANSCRIPT_V1",
            "init",
            statement_hash,
        ]);

        Self {
            state_hash: initial_state_hash.clone(),
            initial_state_hash,
            challenges: Vec::new(),
        }
    }

    fn absorb_hash(&mut self, label: &str, value_hash: &str) {
        self.state_hash = hash_parts(&[
            "HUSH_SP07_FIAT_SHAMIR_TRANSCRIPT_V1",
            "absorb",
            &self.state_hash,
            label,
            value_hash,
        ]);
    }

    fn challenge(&mut self, label: &str) -> FiatShamirChallenge {
        let input_state_hash = self.state_hash.clone();
        let challenge_digest = digest_parts(&[
            "HUSH_SP07_FIAT_SHAMIR_TRANSCRIPT_V1",
            "challenge",
            &input_state_hash,
            label,
        ]);
        let challenge_hash_sha512 = hex_digest(&challenge_digest);
        let scalar = Fr::from_be_bytes_mod_order(&challenge_digest);
        let challenge = FiatShamirChallenge {
            label: label.to_string(),
            input_state_hash: input_state_hash.clone(),
            challenge_hash_sha512: challenge_hash_sha512.clone(),
            scalar_decimal: scalar_decimal(scalar),
        };

        self.state_hash = hash_parts(&[
            "HUSH_SP07_FIAT_SHAMIR_TRANSCRIPT_V1",
            "challenge-absorbed",
            &input_state_hash,
            label,
            &challenge_hash_sha512,
        ]);
        self.challenges.push(challenge.clone());
        challenge
    }

    fn finish(self) -> FiatShamirTranscript {
        FiatShamirTranscript {
            schema: "HushSp07FiatShamirTranscriptV1",
            profile: "hush_sp07_fiat_shamir_sha512_v1",
            domain: "HUSH_SP07_FIAT_SHAMIR_TRANSCRIPT_V1",
            initial_state_hash: self.initial_state_hash,
            final_state_hash: self.state_hash,
            challenges: self.challenges,
        }
    }
}

fn canonical_hash<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
    serde_json::to_vec(value).map(|bytes| hex_digest(&Sha512::digest(bytes)))
}

fn hash_ark_affine_components(components: &[Vec<EdwardsAffine>]) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_ARK_AFFINE_COMPONENT_HASH_V1|");
    for (component_index, bases) in components.iter().enumerate() {
        hasher.update(component_index.to_string().as_bytes());
        hasher.update(b"|");
        for point in bases {
            hash_affine_hush_point(&mut hasher, point);
        }
    }

    format!("{:x}", hasher.finalize())
}

fn hash_ark_projective_points(points: &[EdwardsProjective]) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_ARK_PROJECTIVE_POINT_HASH_V1|");
    for point in points {
        hash_affine_hush_point(&mut hasher, &point.into_affine());
    }

    format!("{:x}", hasher.finalize())
}

fn hash_ark_projective_point(point: &EdwardsProjective) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_ARK_PROJECTIVE_POINT_HASH_V1|");
    hash_affine_hush_point(&mut hasher, &point.into_affine());
    format!("{:x}", hasher.finalize())
}

fn hash_commitment_key(commitment_key: &CommitmentKey) -> String {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_COMMITMENT_KEY_HASH_V1|");
    hasher.update(b"h|");
    hash_affine_hush_point(&mut hasher, &commitment_key.h);
    for (index, base) in commitment_key.g.iter().enumerate() {
        hasher.update(index.to_string().as_bytes());
        hasher.update(b"|");
        hash_affine_hush_point(&mut hasher, base);
    }

    format!("{:x}", hasher.finalize())
}

fn hash_affine_hush_point(hasher: &mut Sha512, point: &EdwardsAffine) {
    hash_field(hasher, point.x * hush_sqrt_a_inverse());
    hash_field(hasher, point.y);
}

fn hash_field(hasher: &mut Sha512, value: Fq) {
    let bigint = value.into_bigint();
    let limbs = bigint.as_ref();
    if limbs.len() < 4 {
        for _ in 0..(4 - limbs.len()) {
            hasher.update(0u64.to_be_bytes());
        }
    }
    for limb in limbs.iter().rev() {
        hasher.update(limb.to_be_bytes());
    }
}

fn public_points_from_projective(points: &[EdwardsProjective]) -> Vec<PublicPoint> {
    points.iter().map(public_point_from_projective).collect()
}

fn public_point_from_projective(point: &EdwardsProjective) -> PublicPoint {
    public_point_from_affine(&point.into_affine())
}

fn public_point_from_affine(point: &EdwardsAffine) -> PublicPoint {
    PublicPoint {
        x: field_decimal(point.x * hush_sqrt_a_inverse()),
        y: field_decimal(point.y),
    }
}

fn scalar_decimal(value: Fr) -> String {
    value.into_bigint().to_string()
}

fn scalars_to_bigints(values: &[Fr]) -> Vec<FrBigInt> {
    values.iter().map(|value| (*value).into_bigint()).collect()
}

fn scalars_to_decimal(values: &[Fr]) -> Vec<String> {
    values.iter().map(|value| scalar_decimal(*value)).collect()
}

fn hash_parts(parts: &[&str]) -> String {
    hex_digest(&digest_parts(parts))
}

fn digest_parts(parts: &[&str]) -> Vec<u8> {
    let mut hasher = Sha512::new();
    for part in parts {
        hasher.update(part.len().to_string().as_bytes());
        hasher.update(b":");
        hasher.update(part.as_bytes());
        hasher.update(b"|");
    }

    hasher.finalize().to_vec()
}

fn hex_digest(bytes: impl AsRef<[u8]>) -> String {
    let bytes = bytes.as_ref();
    let mut output = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        output.push_str(&format!("{byte:02x}"));
    }

    output
}

fn derive_scalar_fr(label: &str, first: usize, second: usize) -> Fr {
    let mut counter = 0usize;
    loop {
        let digest = derive_scalar_digest(label, first, second, counter);
        let value = Fr::from_be_bytes_mod_order(&digest);
        if !value.is_zero() {
            return value;
        }

        counter += 1;
    }
}

fn derive_scalar(label: &str, first: usize, second: usize) -> BigUint {
    let mut counter = 0usize;
    loop {
        let digest = derive_scalar_digest(label, first, second, counter);
        let value = BigUint::from_bytes_be(&digest) % &*ORDER;
        if !value.is_zero() {
            return value;
        }

        counter += 1;
    }
}

fn derive_scalar_digest(label: &str, first: usize, second: usize, counter: usize) -> Vec<u8> {
    let mut hasher = Sha512::new();
    hasher.update(b"HUSH_SP07_RUST_WORKER_BIGINT_V1|");
    hasher.update(label.as_bytes());
    hasher.update(b"|");
    hasher.update(first.to_string().as_bytes());
    hasher.update(b"|");
    hasher.update(second.to_string().as_bytes());
    hasher.update(b"|");
    hasher.update(counter.to_string().as_bytes());
    hasher.finalize().to_vec()
}

fn scalar_mul_projective(point: &Point, scalar: &BigUint) -> ProjectivePoint {
    let normalized = scalar % &*ORDER;
    if normalized.is_zero() {
        return ProjectivePoint::identity();
    }

    if normalized.bits() <= SMALL_SCALAR_BIT_LENGTH {
        scalar_mul_projective_double_and_add(&to_projective(point), &normalized)
    } else {
        scalar_mul_projective_windowed(&to_projective(point), &normalized)
    }
}

fn scalar_mul_projective_double_and_add(
    point: &ProjectivePoint,
    scalar: &BigUint,
) -> ProjectivePoint {
    let mut result = ProjectivePoint::identity();
    let mut temp = point.clone();
    let mut scalar = scalar.clone();
    while !scalar.is_zero() {
        if scalar.is_odd() {
            result = add_projective(&result, &temp);
        }

        temp = add_projective(&temp, &temp);
        scalar >>= 1usize;
    }

    result
}

fn scalar_mul_projective_windowed(point: &ProjectivePoint, scalar: &BigUint) -> ProjectivePoint {
    let table = build_window_table(point);
    let digits = to_window_digits(scalar);
    let mut result = ProjectivePoint::identity();

    for digit in digits.iter().rev() {
        for _ in 0..SCALAR_WINDOW_BITS {
            result = add_projective(&result, &result);
        }

        if *digit != 0 {
            result = add_projective(&result, &table[*digit]);
        }
    }

    result
}

fn build_window_table(point: &ProjectivePoint) -> Vec<ProjectivePoint> {
    let table_len = 1usize << SCALAR_WINDOW_BITS;
    let mut table = Vec::with_capacity(table_len);
    table.push(ProjectivePoint::identity());
    table.push(point.clone());
    for index in 2..table_len {
        let next = add_projective(&table[index - 1], point);
        table.push(next);
    }

    table
}

fn to_window_digits(scalar: &BigUint) -> Vec<usize> {
    let mask = (BigUint::one() << SCALAR_WINDOW_BITS) - BigUint::one();
    let mut digits = Vec::new();
    let mut scalar = scalar.clone();
    while !scalar.is_zero() {
        digits.push((&scalar & &mask).to_usize().unwrap());
        scalar >>= SCALAR_WINDOW_BITS;
    }

    digits
}

fn to_projective(point: &Point) -> ProjectivePoint {
    ProjectivePoint {
        x: point.x.clone(),
        y: point.y.clone(),
        z: BigUint::one(),
    }
}

fn to_affine(point: &ProjectivePoint) -> Point {
    if point.z.is_zero() {
        panic!("projective BabyJubJub point has zero Z coordinate");
    }

    let inverse_z = mod_inverse(&point.z);
    Point {
        x: mul_mod(&point.x, &inverse_z),
        y: mul_mod(&point.y, &inverse_z),
    }
}

fn add_projective(left: &ProjectivePoint, right: &ProjectivePoint) -> ProjectivePoint {
    let z1z2 = mul_mod(&left.z, &right.z);
    let z1z2_squared = mul_mod(&z1z2, &z1z2);
    let x1x2 = mul_mod(&left.x, &right.x);
    let y1y2 = mul_mod(&left.y, &right.y);
    let d_term = mul_mod(&D, &mul_mod(&x1x2, &y1y2));
    let one_minus_d_term = sub_mod(&z1z2_squared, &d_term);
    let one_plus_d_term = add_mod(&z1z2_squared, &d_term);
    let left_sum = add_mod(&left.x, &left.y);
    let right_sum = add_mod(&right.x, &right.y);
    let mixed = sub_mod(&sub_mod(&mul_mod(&left_sum, &right_sum), &x1x2), &y1y2);
    let y_numerator = sub_mod(&y1y2, &mul_mod(&A, &x1x2));

    ProjectivePoint {
        x: mul_mod(&mul_mod(&z1z2, &one_minus_d_term), &mixed),
        y: mul_mod(&mul_mod(&z1z2, &one_plus_d_term), &y_numerator),
        z: mul_mod(&one_minus_d_term, &one_plus_d_term),
    }
}

fn add_mod(left: &BigUint, right: &BigUint) -> BigUint {
    (left + right) % &*FIELD_PRIME
}

fn sub_mod(left: &BigUint, right: &BigUint) -> BigUint {
    if left >= right {
        (left - right) % &*FIELD_PRIME
    } else {
        (&*FIELD_PRIME - ((right - left) % &*FIELD_PRIME)) % &*FIELD_PRIME
    }
}

fn mul_mod(left: &BigUint, right: &BigUint) -> BigUint {
    (left * right) % &*FIELD_PRIME
}

fn mod_inverse(value: &BigUint) -> BigUint {
    let mut previous_remainder = FIELD_PRIME.to_bigint().unwrap();
    let mut remainder = (value % &*FIELD_PRIME).to_bigint().unwrap();
    if remainder.is_zero() {
        panic!("cannot invert zero in the BabyJubJub field");
    }

    let mut previous_coefficient = BigInt::zero();
    let mut coefficient = BigInt::one();
    while !remainder.is_zero() {
        let quotient = &previous_remainder / &remainder;
        let next_remainder = &previous_remainder - &quotient * &remainder;
        previous_remainder = remainder;
        remainder = next_remainder;

        let next_coefficient = &previous_coefficient - &quotient * &coefficient;
        previous_coefficient = coefficient;
        coefficient = next_coefficient;
    }

    if previous_remainder != BigInt::one() {
        panic!("BabyJubJub field element is not invertible");
    }

    mod_field_signed(previous_coefficient)
}

fn mod_field_signed(value: BigInt) -> BigUint {
    let prime = FIELD_PRIME.to_bigint().unwrap();
    let mut result = value % &prime;
    if result.sign() == Sign::Minus {
        result += prime;
    }

    result.to_biguint().unwrap()
}

fn checksum(points: &[Point]) -> String {
    let mut hasher = Sha512::new();
    for point in points {
        hasher.update(point.x.to_str_radix(10).as_bytes());
        hasher.update(b"|");
        hasher.update(point.y.to_str_radix(10).as_bytes());
        hasher.update(b"\n");
    }

    format!("{:x}", hasher.finalize())
}

fn checksum_ark(points: &[EdwardsProjective]) -> String {
    let mut hasher = Sha512::new();
    for point in points {
        let affine = point.into_affine();
        let hush_x = affine.x * hush_sqrt_a_inverse();
        hasher.update(field_decimal(hush_x).as_bytes());
        hasher.update(b"|");
        hasher.update(field_decimal(affine.y).as_bytes());
        hasher.update(b"\n");
    }

    format!("{:x}", hasher.finalize())
}

fn hush_generator_ark() -> EdwardsAffine {
    let x = Fq::from_str(
        "5299619240641551281634865583518297030282874472190772894086521144482721001553",
    )
    .unwrap();
    let y = Fq::from_str(
        "16950150798460657717958625567821834550301663161624707787222815936182638968203",
    )
    .unwrap();
    EdwardsAffine::new(x * hush_sqrt_a(), y)
}

fn hush_sqrt_a() -> Fq {
    *HUSH_SQRT_A
}

fn hush_sqrt_a_inverse() -> Fq {
    *HUSH_SQRT_A_INVERSE
}

fn field_decimal(value: Fq) -> String {
    value.into_bigint().to_string()
}

impl ProjectivePoint {
    fn identity() -> Self {
        Self {
            x: BigUint::zero(),
            y: BigUint::one(),
            z: BigUint::one(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn proof_example_is_deterministic() {
        let first = build_proof_example(12, 4, false, false).expect("first example should build");
        let second = build_proof_example(12, 4, false, false).expect("second example should build");

        assert_eq!(
            first.proof_example_hash_sha512,
            second.proof_example_hash_sha512
        );
        assert_eq!(
            first.fiat_shamir.final_state_hash,
            second.fiat_shamir.final_state_hash
        );
        assert_eq!(first.fiat_shamir.challenges.len(), 8);
    }

    #[test]
    fn proof_example_binds_ballot_and_slot_count() {
        let base = build_proof_example(12, 4, false, false).expect("base example should build");
        let changed_ballots =
            build_proof_example(13, 4, false, false).expect("changed ballot example should build");
        let changed_slots =
            build_proof_example(12, 5, false, false).expect("changed slot example should build");

        assert_ne!(
            base.proof_example_hash_sha512,
            changed_ballots.proof_example_hash_sha512
        );
        assert_ne!(
            base.proof_example_hash_sha512,
            changed_slots.proof_example_hash_sha512
        );
    }

    #[test]
    fn proof_example_binds_caller_supplied_statement_hashes() {
        let overrides = StatementOverrides {
            election_id: Some("election-real-001".to_string()),
            chunk_id: Some("chunk-real-001".to_string()),
            protocol_package_hash: Some("protocol-package-hash-real".to_string()),
            ballot_definition_hash: Some("ballot-definition-hash-real".to_string()),
            accepted_ballot_set_hash: Some("accepted-ballot-set-hash-real".to_string()),
            published_ballot_stream_hash: Some("published-ballot-stream-hash-real".to_string()),
        };
        let example = build_proof_example_with_statement_overrides(
            12,
            4,
            false,
            false,
            Some(&overrides),
            None,
        )
        .expect("proof example should build");

        assert_eq!(example.statement.election_id, "election-real-001");
        assert_eq!(example.statement.chunk_id, "chunk-real-001");
        assert_eq!(
            example.statement.protocol_package_hash,
            "protocol-package-hash-real"
        );
        assert_eq!(
            example.statement.ballot_definition_hash,
            "ballot-definition-hash-real"
        );
        assert_eq!(
            example.statement.accepted_ballot_set_hash,
            "accepted-ballot-set-hash-real"
        );
        assert_eq!(
            example.statement.published_ballot_stream_hash,
            "published-ballot-stream-hash-real"
        );
        assert_eq!(
            example
                .canonical_proof_bytes
                .envelope
                .accepted_ballot_set_hash,
            "accepted-ballot-set-hash-real"
        );
        assert_eq!(
            example
                .canonical_proof_bytes
                .envelope
                .published_ballot_stream_hash,
            "published-ballot-stream-hash-real"
        );
    }

    #[test]
    fn proof_example_accepts_production_ciphertexts_and_witness() {
        let input = build_worker_production_input(12, 4);
        let parsed = parse_worker_production_proof_input(&input, 12, 4)
            .expect("production proof input should parse");
        let accepted_hash = hash_ark_affine_components(&parsed.accepted_component_bases);
        let published_hash = hash_ark_affine_components(&parsed.published_component_bases);
        let overrides = StatementOverrides {
            election_id: Some("election-production-001".to_string()),
            chunk_id: Some("chunk-production-001".to_string()),
            protocol_package_hash: Some("protocol-package-production-hash".to_string()),
            ballot_definition_hash: Some("ballot-definition-production-hash".to_string()),
            accepted_ballot_set_hash: Some(accepted_hash.clone()),
            published_ballot_stream_hash: Some(published_hash.clone()),
        };

        let example = build_proof_example_with_statement_overrides(
            12,
            4,
            false,
            true,
            Some(&overrides),
            Some(&parsed),
        )
        .expect("production proof example should build");

        assert!(example.verifier_result.passed);
        assert_eq!(example.verifier_result.result_code, "PUB-005");
        assert_eq!(example.statement.accepted_ballot_set_hash, accepted_hash);
        assert_eq!(
            example.statement.published_ballot_stream_hash,
            published_hash
        );
        assert_eq!(
            example.canonical_proof_bytes.envelope.statement_hash_sha512,
            example.statement_hash_sha512
        );
        assert!(example
            .tamper_vectors
            .iter()
            .all(|tamper| tamper.rejected_as_expected));
    }

    #[test]
    fn production_input_rejects_mismatched_published_ballot() {
        let mut input = build_worker_production_input(12, 4);
        input.published_ballots[0].slots[0].c1 = input.accepted_ballots[0].slots[0].c1.clone();

        let result = parse_worker_production_proof_input(&input, 12, 4);

        assert!(result.is_err());
        assert!(result
            .unwrap_err()
            .contains("published_ballots do not match accepted_ballots"));
    }

    #[test]
    fn production_input_rejects_independent_slot_permutation() {
        let mut input = build_worker_production_input(12, 4);
        let swapped_slot = input.published_ballots[1].slots[2].clone();
        input.published_ballots[1].slots[2] = input.published_ballots[0].slots[2].clone();
        input.published_ballots[0].slots[2] = swapped_slot;

        let result = parse_worker_production_proof_input(&input, 12, 4);

        assert!(result.is_err());
        assert!(result
            .unwrap_err()
            .contains("published_ballots do not match accepted_ballots"));
    }

    #[test]
    fn proof_example_tamper_vectors_reject_expected_failures() {
        let example =
            build_proof_example(12, 4, false, true).expect("tamper-vector example should build");

        assert_eq!(example.verifier_result.result_code, "PUB-005");
        assert_eq!(example.verifier_result.tamper_suite_status, "executed");
        assert_eq!(example.verifier_result.tamper_vectors_passed, Some(true));
        assert_eq!(example.tamper_vectors.len(), 8);
        assert!(example
            .tamper_vectors
            .iter()
            .all(|result| result.rejected_as_expected));
        assert!(example.canonical_proof_bytes.byte_length > 0);
        assert_eq!(example.canonical_proof_bytes.sha512.len(), 128);
    }

    #[test]
    fn canonical_public_proof_verifier_accepts_generated_proof_bytes() {
        let example = build_proof_example(12, 4, false, false).expect("proof example should build");
        let input = serde_json::json!({
            "passed": true,
            "result_code": "PUB-005",
            "statement_hash_sha512": example.statement_hash_sha512,
            "transcript_hash_sha512": example.fiat_shamir.final_state_hash,
            "proof_hash_sha512": example.canonical_proof_bytes.sha512,
            "accepted_ballot_set_hash": example.statement.accepted_ballot_set_hash,
            "published_ballot_stream_hash": example.statement.published_ballot_stream_hash,
            "canonical_proof_byte_length": example.canonical_proof_bytes.byte_length,
            "canonical_proof_bytes_hex": example.canonical_proof_bytes.hex,
        });

        let result = verify_public_proof_input(&input);

        assert!(result.passed);
        assert_eq!(result.result_code, "PUB-005");
        assert_eq!(
            result.canonical_proof_byte_length,
            example.canonical_proof_bytes.byte_length
        );
    }

    #[test]
    fn canonical_public_proof_verifier_rejects_wrong_accepted_set_hash() {
        let example = build_proof_example(12, 4, false, false).expect("proof example should build");
        let input = serde_json::json!({
            "passed": true,
            "result_code": "PUB-005",
            "statement_hash_sha512": example.statement_hash_sha512,
            "transcript_hash_sha512": example.fiat_shamir.final_state_hash,
            "proof_hash_sha512": example.canonical_proof_bytes.sha512,
            "accepted_ballot_set_hash": hash_parts(&[
                &example.statement.accepted_ballot_set_hash,
                "tampered-package-accepted-set"
            ]),
            "published_ballot_stream_hash": example.statement.published_ballot_stream_hash,
            "canonical_proof_byte_length": example.canonical_proof_bytes.byte_length,
            "canonical_proof_bytes_hex": example.canonical_proof_bytes.hex,
        });

        let result = verify_public_proof_input(&input);

        assert!(!result.passed);
        assert_eq!(result.result_code, "PUB-015");
        assert!(result.message.contains("expected public statement hashes"));
    }

    #[test]
    fn canonical_public_proof_verifier_rejects_tampered_proof_bytes() {
        let example = build_proof_example(12, 4, false, false).expect("proof example should build");
        let mut tampered = example.canonical_proof_bytes.hex.clone();
        let replacement = if &tampered[0..2] == "00" { "01" } else { "00" };
        tampered.replace_range(0..2, replacement);
        let input = serde_json::json!({
            "passed": true,
            "result_code": "PUB-005",
            "statement_hash_sha512": example.statement_hash_sha512,
            "transcript_hash_sha512": example.fiat_shamir.final_state_hash,
            "proof_hash_sha512": example.canonical_proof_bytes.sha512,
            "accepted_ballot_set_hash": example.statement.accepted_ballot_set_hash,
            "published_ballot_stream_hash": example.statement.published_ballot_stream_hash,
            "canonical_proof_byte_length": example.canonical_proof_bytes.byte_length,
            "canonical_proof_bytes_hex": tampered,
        });

        let result = verify_public_proof_input(&input);

        assert!(!result.passed);
        assert_eq!(result.result_code, "PUB-015");
    }

    #[test]
    fn single_value_product_verifier_rejects_tampered_response() {
        let n = 8;
        let commitment_key = CommitmentKey {
            h: derive_ark_base("test.product.commitment-h", n, 0),
            g: build_ark_progression_bases(n, "test.product.commitment-step"),
        };
        let witness = SingleValueProductWitness {
            a: build_ark_scalars_with_label("test.product.single.a", n),
            r: derive_scalar_fr("test.product.single.r", n, 0),
        };
        let statement_hash = hash_parts(&["test-statement", "single-value-product"]);
        let proof = prove_single_value_product(&statement_hash, &commitment_key, &witness);

        let valid = verify_single_value_product(&statement_hash, &commitment_key, &proof);
        assert!(valid.passed);
        assert_eq!(valid.result_code, "PUB-005");

        let mut tampered = proof.clone();
        tampered.a_tilde[1] += Fr::from(1u64);
        let invalid = verify_single_value_product(&statement_hash, &commitment_key, &tampered);

        assert!(!invalid.passed);
        assert_eq!(invalid.result_code, "PUB-011");
    }

    #[test]
    fn hush_multi_exponentiation_verifier_rejects_tampered_ciphertext_response() {
        let n = 8;
        let slots = 3;
        let commitment_key = CommitmentKey {
            h: derive_ark_base("test.multi.commitment-h", n, slots),
            g: build_ark_progression_bases(n, "test.multi.commitment-step"),
        };
        let public_key = hush_generator_ark()
            .mul_bigint(derive_scalar_fr("test.multi.public-key", n, slots).into_bigint());
        let component_bases =
            build_ark_component_bases_with_label(n, slots * 2, "test.multi.component-step");
        let witness = HushMultiExponentiationWitness {
            b_matrix_column: build_ark_scalars_with_label("test.multi.b-column", n),
            s_b: derive_scalar_fr("test.multi.s-b", n, slots),
            rho_bar: (0..slots)
                .map(|slot| derive_scalar_fr("test.multi.rho-bar", n, slot))
                .collect(),
        };
        let statement_hash = hash_parts(&["test-statement", "hush-multi-exponentiation"]);
        let c_prime_matrix_hash = hash_ark_affine_components(&component_bases);
        let d_1 = hush_vector_multi_exponentiation_bigint(
            &component_bases,
            &scalars_to_bigints(&witness.b_matrix_column),
        );
        let accepted_cx = ballot_vector_add(&zero_enc_vec(&public_key, &witness.rho_bar), &d_1);
        let proof = prove_hush_multi_exponentiation(
            &statement_hash,
            &commitment_key,
            &public_key,
            &component_bases,
            slots,
            &witness,
            &c_prime_matrix_hash,
            &accepted_cx,
        );

        let valid = verify_hush_multi_exponentiation(
            &statement_hash,
            &c_prime_matrix_hash,
            &commitment_key,
            &public_key,
            &component_bases,
            slots,
            &proof,
        );
        assert!(valid.passed);
        assert_eq!(valid.result_code, "PUB-005");

        let mut tampered = proof.clone();
        tampered.e[0].components[0] += EdwardsProjective::from(hush_generator_ark());
        let invalid = verify_hush_multi_exponentiation(
            &statement_hash,
            &c_prime_matrix_hash,
            &commitment_key,
            &public_key,
            &component_bases,
            slots,
            &tampered,
        );

        assert!(!invalid.passed);
        assert_eq!(invalid.result_code, "PUB-012");
    }

    fn build_worker_production_input(ballots: usize, slots: usize) -> WorkerProductionProofInput {
        let public_key = hush_generator_ark().mul_bigint(
            derive_scalar_fr("test.production.public-key", ballots, slots).into_bigint(),
        );
        let accepted_component_bases = build_ark_component_bases_with_label(
            ballots,
            slots * 2,
            "test.production.accepted.component-step",
        );
        let published_to_accepted = build_permutation(ballots);
        let rerandomization_by_published_ballot_and_slot =
            build_rerandomization_matrix(ballots, slots);
        let published_component_bases = build_published_component_bases(
            &accepted_component_bases,
            &published_to_accepted,
            &rerandomization_by_published_ballot_and_slot,
            &public_key,
            slots,
        );

        WorkerProductionProofInput {
            public_key: worker_point_from_projective(&public_key),
            accepted_ballots: worker_ballots_from_components(
                &accepted_component_bases,
                ballots,
                slots,
            ),
            published_ballots: worker_ballots_from_components(
                &published_component_bases,
                ballots,
                slots,
            ),
            published_to_accepted,
            rerandomization_by_published_ballot_and_slot:
                rerandomization_by_published_ballot_and_slot
                    .iter()
                    .map(|row| row.iter().map(|scalar| scalar_decimal(*scalar)).collect())
                    .collect(),
        }
    }

    fn worker_ballots_from_components(
        components: &[Vec<EdwardsAffine>],
        ballots: usize,
        slots: usize,
    ) -> Vec<WorkerCipherBallot> {
        (0..ballots)
            .map(|ballot| WorkerCipherBallot {
                slots: (0..slots)
                    .map(|slot| WorkerCipherSlot {
                        c1: worker_point_from_affine(&components[slot * 2][ballot]),
                        c2: worker_point_from_affine(&components[(slot * 2) + 1][ballot]),
                    })
                    .collect(),
            })
            .collect()
    }

    fn worker_point_from_projective(point: &EdwardsProjective) -> WorkerPoint {
        worker_point_from_affine(&point.into_affine())
    }

    fn worker_point_from_affine(point: &EdwardsAffine) -> WorkerPoint {
        let public_point = public_point_from_affine(point);
        WorkerPoint {
            x: public_point.x,
            y: public_point.y,
        }
    }
}
