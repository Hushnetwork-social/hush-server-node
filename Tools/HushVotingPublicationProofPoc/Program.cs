using System.Globalization;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HushShared.Elections.PublicationProof;

internal static class Program
{
    private const string DefaultOutput = "artifacts/sp07-poc";
    private static readonly JsonSerializerOptions WriteJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static readonly JsonSerializerOptions ReadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static int Main(string[] args) => Run(args);

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            ShowUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = CliOptions.Parse(args.Skip(1).ToArray());

            return command switch
            {
                "generate" => Generate(options),
                "verify" => Verify(options),
                "run" => RunAll(options),
                "hotbench" => HotBench(options),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int Generate(CliOptions options)
    {
        var outputRoot = Path.GetFullPath(options.Output ?? DefaultOutput);
        var vectors = ResolveVectors(options);

        if (vectors.Length == 0)
        {
            throw new InvalidOperationException($"Unknown vector '{options.Vector}'.");
        }

        Directory.CreateDirectory(outputRoot);

        foreach (var vector in vectors)
        {
            var folder = Path.Combine(outputRoot, vector.Id);
            if (Directory.Exists(folder) && !options.Force)
            {
                throw new InvalidOperationException(
                    $"Vector folder already exists: {folder}. Use --force to overwrite generated PoC artifacts.");
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.CreateDirectory(folder);
            var bundle = SyntheticVectorGenerator.Generate(vector);
            EvidenceWriter.Write(folder, bundle);
            Console.WriteLine($"Generated {vector.Id} -> {folder}");
        }

        return 0;
    }

    private static VectorDefinition[] ResolveVectors(CliOptions options)
    {
        if (options.BallotCount is not null || options.SlotCount is not null)
        {
            if (options.All)
            {
                throw new InvalidOperationException("Custom vectors cannot be combined with --all.");
            }

            if (options.BallotCount is null || options.SlotCount is null)
            {
                throw new InvalidOperationException("Custom vectors require both --ballots and --slots.");
            }

            var vectorId = string.IsNullOrWhiteSpace(options.Vector)
                ? $"sp07-bg-hush-valid-n{options.BallotCount.Value}-k{options.SlotCount.Value}-v1"
                : options.Vector;
            return [new VectorDefinition(vectorId, options.BallotCount.Value, options.SlotCount.Value)];
        }

        return options.All || string.IsNullOrWhiteSpace(options.Vector)
            ? VectorDefinition.Defaults
            : VectorDefinition.Defaults.Where(x => string.Equals(x.Id, options.Vector, StringComparison.Ordinal)).ToArray();
    }

    private static int Verify(CliOptions options)
    {
        var input = options.Input ?? options.Output ?? DefaultOutput;
        var inputPath = Path.GetFullPath(input);
        if (!Directory.Exists(inputPath))
        {
            throw new DirectoryNotFoundException(inputPath);
        }

        var vectorFolders = Directory.GetFiles(inputPath, "accepted-ballots.json").Length > 0
            ? new[] { inputPath }
            : Directory.GetDirectories(inputPath);

        var exitCode = 0;
        foreach (var vectorFolder in vectorFolders.OrderBy(x => x, StringComparer.Ordinal))
        {
            var result = EvidenceVerifier.Verify(vectorFolder);
            Console.WriteLine($"{Path.GetFileName(vectorFolder)}: {result.OverallStatus}");

            foreach (var check in result.Checks)
            {
                Console.WriteLine($"  {check.Code}: {check.Status} - {check.Message}");
            }

            if (!string.Equals(result.OverallStatus, "PASS", StringComparison.Ordinal))
            {
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private static int RunAll(CliOptions options)
    {
        var runOptions = options with { All = true, Force = true };
        var generateExit = Generate(runOptions);
        return generateExit != 0 ? generateExit : Verify(runOptions);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        ShowUsage();
        return 1;
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "help";

    private static void ShowUsage()
    {
        Console.WriteLine("HushVotingPublicationProofPoc");
        Console.WriteLine();
        Console.WriteLine("Local synthetic SP-07 publication-proof PoC harness.");
        Console.WriteLine("No server connection, no blockchain writes, no real voter data.");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  generate [--all] [--vector <id>] [--ballots <n>] [--slots <k>] [--output <path>] [--force]");
        Console.WriteLine("  verify   [--input <path>]");
        Console.WriteLine("  run      [--output <path>]");
        Console.WriteLine("  hotbench [--ballots <n>] [--slots <k>] [--rounds <r>] [--mode windowed|pippenger] [--window-bits <n>] [--output <path>]");
        Console.WriteLine();
        Console.WriteLine("Default output:");
        Console.WriteLine($"  {DefaultOutput}");
        Console.WriteLine();
        Console.WriteLine("Default vectors:");
        foreach (var vector in VectorDefinition.Defaults)
        {
            Console.WriteLine($"  {vector.Id} (N={vector.BallotCount}, K={vector.SlotCount})");
        }
    }

    internal static string ToCanonicalJson<T>(T value) =>
        JsonSerializer.Serialize(value, CanonicalJsonOptions);

    internal static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, WriteJsonOptions);
        File.WriteAllText(path, json + Environment.NewLine, Encoding.UTF8);
    }

    private static int HotBench(CliOptions options)
    {
        var ballots = options.BallotCount ?? 1000;
        var slots = options.SlotCount ?? 8;
        var rounds = options.Rounds ?? 3;
        var mode = string.IsNullOrWhiteSpace(options.Mode)
            ? "windowed"
            : options.Mode.Trim().ToLowerInvariant();
        var windowBits = options.WindowBits ?? 6;
        if (ballots <= 0 || slots <= 0 || rounds <= 0)
        {
            throw new InvalidOperationException("hotbench requires positive ballots, slots, and rounds.");
        }

        if (mode is not ("windowed" or "pippenger"))
        {
            throw new InvalidOperationException("hotbench --mode must be 'windowed' or 'pippenger'.");
        }

        if (windowBits is < 2 or > 12)
        {
            throw new InvalidOperationException("hotbench --window-bits must be between 2 and 12.");
        }

        var setup = Stopwatch.StartNew();
        var scalars = Enumerable.Range(0, ballots)
            .Select(index => DeriveHotBenchScalar("scalar", index, 0))
            .ToArray();
        var componentBases = BuildHotBenchComponentBases(ballots, slots * 2);
        setup.Stop();

        var roundMilliseconds = new List<double>();
        Point[] lastResult = [];
        for (var round = 0; round < rounds; round++)
        {
            var sw = Stopwatch.StartNew();
            lastResult = AggregateHotBenchComponents(componentBases, scalars, mode, windowBits);
            sw.Stop();
            roundMilliseconds.Add(sw.Elapsed.TotalMilliseconds);
        }

        var report = new
        {
            schema = "HushSp07CSharpHotBenchReportV1",
            engine = mode == "pippenger"
                ? "csharp_system_numerics_bigint_pippenger_v1"
                : "csharp_system_numerics_bigint_same_formula_v1",
            operation = "component_cipher_vector_exponentiation_shape",
            mode,
            windowBits = mode == "pippenger" ? windowBits : (int?)null,
            ballots,
            slots,
            components = slots * 2,
            pointScalarPairsPerRound = ballots * slots * 2,
            rounds,
            setupMilliseconds = setup.Elapsed.TotalMilliseconds,
            roundMilliseconds,
            bestMilliseconds = roundMilliseconds.Min(),
            averageMilliseconds = roundMilliseconds.Average(),
            processorCount = Environment.ProcessorCount,
            checksumSha512 = HotBenchChecksum(lastResult),
            notes = new[]
            {
                "Fair language baseline: uses System.Numerics.BigInteger arbitrary precision arithmetic, not fixed-width field arithmetic.",
                "Matches the Rust worker operation shape: K slots, two ciphertext components per slot, same exponent vector per component.",
                "This is an aggregation hot-path benchmark, not a full SP-07 proof generator."
            }
        };

        var json = JsonSerializer.Serialize(report, WriteJsonOptions);
        if (!string.IsNullOrWhiteSpace(options.Output))
        {
            var outputPath = Path.GetFullPath(options.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            File.WriteAllText(outputPath, json + Environment.NewLine, Encoding.UTF8);
            Console.WriteLine($"Wrote {outputPath}");
        }

        Console.WriteLine(json);
        return 0;
    }

    private static Point[][] BuildHotBenchComponentBases(int ballots, int components)
    {
        var result = new Point[components][];
        for (var component = 0; component < components; component++)
        {
            var bases = new Point[ballots];
            var current = BabyJubJub.ProjectiveIdentity;
            var stepScalar = DeriveHotBenchScalar("component-step", component, 0);
            var step = BabyJubJub.ScalarMulProjective(BabyJubJub.Generator, stepScalar);
            for (var index = 0; index < ballots; index++)
            {
                current = BabyJubJub.AddProjective(current, step);
                bases[index] = BabyJubJub.ToAffine(current);
            }

            result[component] = bases;
        }

        return result;
    }

    private static Point[] AggregateHotBenchComponents(
        Point[][] componentBases,
        BigInteger[] scalars,
        string mode,
        int windowBits)
    {
        var results = new Point[componentBases.Length];
        Parallel.For(
            0,
            componentBases.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(componentBases.Length, Environment.ProcessorCount) },
            component =>
            {
                var bases = componentBases[component];
                var aggregate = mode == "pippenger"
                    ? PippengerMsm(bases, scalars, windowBits)
                    : WindowedMsm(bases, scalars);

                results[component] = BabyJubJub.ToAffine(aggregate);
            });

        return results;
    }

    private static ProjectivePoint WindowedMsm(Point[] bases, BigInteger[] scalars)
    {
        var aggregate = BabyJubJub.ProjectiveIdentity;
        for (var index = 0; index < bases.Length; index++)
        {
            if (scalars[index] == BigInteger.Zero)
            {
                continue;
            }

            aggregate = BabyJubJub.AddProjective(
                aggregate,
                BabyJubJub.ScalarMulProjective(bases[index], scalars[index]));
        }

        return aggregate;
    }

    private static ProjectivePoint PippengerMsm(Point[] bases, BigInteger[] scalars, int windowBits)
    {
        if (bases.Length != scalars.Length)
        {
            throw new ArgumentException("Pippenger base/scalar dimensions differ.", nameof(bases));
        }

        var mask = (BigInteger.One << windowBits) - BigInteger.One;
        var windowCount = (int)((BabyJubJub.Order.GetBitLength() + windowBits - 1) / windowBits);
        var bucketCount = 1 << windowBits;
        var result = BabyJubJub.ProjectiveIdentity;
        for (var windowIndex = windowCount - 1; windowIndex >= 0; windowIndex--)
        {
            for (var shift = 0; shift < windowBits; shift++)
            {
                result = BabyJubJub.AddProjective(result, result);
            }

            var buckets = Enumerable.Range(0, bucketCount)
                .Select(_ => BabyJubJub.ProjectiveIdentity)
                .ToArray();
            var scalarShift = windowIndex * windowBits;
            for (var index = 0; index < bases.Length; index++)
            {
                var normalized = scalars[index] % BabyJubJub.Order;
                if (normalized < 0)
                {
                    normalized += BabyJubJub.Order;
                }

                var digit = (int)((normalized >> scalarShift) & mask);
                if (digit == 0)
                {
                    continue;
                }

                buckets[digit] = BabyJubJub.AddProjective(buckets[digit], BabyJubJub.ToProjective(bases[index]));
            }

            var running = BabyJubJub.ProjectiveIdentity;
            var windowSum = BabyJubJub.ProjectiveIdentity;
            for (var bucket = bucketCount - 1; bucket >= 1; bucket--)
            {
                running = BabyJubJub.AddProjective(running, buckets[bucket]);
                windowSum = BabyJubJub.AddProjective(windowSum, running);
            }

            result = BabyJubJub.AddProjective(result, windowSum);
        }

        return result;
    }

    private static BigInteger DeriveHotBenchScalar(string label, int first, int second)
    {
        var counter = 0;
        while (true)
        {
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(
                $"HUSH_SP07_RUST_WORKER_BIGINT_V1|{label}|{first}|{second}|{counter}"));
            var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true) % BabyJubJub.Order;
            if (value != BigInteger.Zero)
            {
                return value;
            }

            counter++;
        }
    }

    private static string HotBenchChecksum(IEnumerable<Point> points)
    {
        var builder = new StringBuilder();
        foreach (var point in points)
        {
            builder.Append(point.X.ToString(CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(point.Y.ToString(CultureInfo.InvariantCulture));
            builder.Append('\n');
        }

        return Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}

internal sealed record CliOptions(
    string? Output,
    string? Input,
    string? Vector,
    int? BallotCount,
    int? SlotCount,
    int? Rounds,
    string? Mode,
    int? WindowBits,
    bool All,
    bool Force)
{
    public static CliOptions Parse(string[] args)
    {
        string? output = null;
        string? input = null;
        string? vector = null;
        int? ballotCount = null;
        int? slotCount = null;
        int? rounds = null;
        string? mode = null;
        int? windowBits = null;
        var all = false;
        var force = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--output":
                case "-o":
                    output = ReadValue(args, ref index, arg);
                    break;
                case "--input":
                case "-i":
                    input = ReadValue(args, ref index, arg);
                    break;
                case "--vector":
                case "-v":
                    vector = ReadValue(args, ref index, arg);
                    break;
                case "--ballots":
                case "-n":
                    ballotCount = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--slots":
                case "-k":
                    slotCount = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--rounds":
                case "-r":
                    rounds = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--mode":
                    mode = ReadValue(args, ref index, arg);
                    break;
                case "--window-bits":
                    windowBits = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--all":
                    all = true;
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option '{arg}'.");
            }
        }

        return new CliOptions(output, input, vector, ballotCount, slotCount, rounds, mode, windowBits, all, force);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static int ReadPositiveInt(string[] args, ref int index, string option)
    {
        var value = ReadValue(args, ref index, option);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{option} requires a positive integer.");
        }

        return parsed;
    }
}

internal sealed record VectorDefinition(string Id, int BallotCount, int SlotCount)
{
    public static readonly VectorDefinition[] Defaults =
    [
        new("sp07-bg-hush-valid-n3-k2-v1", 3, 2),
        new("sp07-bg-hush-valid-n5-k3-v1", 5, 3),
        new("sp07-bg-hush-valid-n12-k4-v1", 12, 4)
    ];
}

internal static class SyntheticVectorGenerator
{
    public static SyntheticVectorBundle Generate(VectorDefinition vector)
    {
        if (vector.BallotCount < 2)
        {
            throw new InvalidOperationException("SP-07 vectors require at least two ballots.");
        }

        if (vector.SlotCount < 1)
        {
            throw new InvalidOperationException("SP-07 vectors require at least one encrypted slot.");
        }

        var seed = $"HUSH_SP07_POC_V1|{vector.Id}";
        var electionId = $"poc-election-{vector.Id}";
        var ballotDefinitionHash = HexSha256($"ballot-definition|{vector.Id}|K={vector.SlotCount}");
        var electionSecret = DeriveScalar(seed, "election-secret");
        var publicKey = BabyJubJub.ScalarMul(BabyJubJub.Generator, electionSecret);
        var publicKeyPayload = PointPayload.FromPoint(publicKey);
        var publicKeyDocument = new ElectionPublicKeyDocument(
            Schema: "HushSp07ElectionPublicKeyPocV1",
            VectorId: vector.Id,
            GroupProfile: ProtocolConstants.GroupProfile,
            ElectionPublicKey: publicKeyPayload,
            ElectionPublicKeyHash: HexSha256(Program.ToCanonicalJson(publicKeyPayload)));

        var acceptedBallots = BuildAcceptedBallots(vector, seed, publicKey);
        var permutation = BuildPermutation(vector.BallotCount, seed);
        var publishedBallots = new BallotDocument[permutation.Length];
        var rhoRows = new string[permutation.Length][];

        Parallel.For(0, permutation.Length, publishedIndex =>
        {
            var acceptedIndex = permutation[publishedIndex];
            var accepted = acceptedBallots[acceptedIndex];
            var publishedSlots = new List<CipherSlotPayload>();
            var rhoRow = new string[vector.SlotCount];

            for (var slotIndex = 0; slotIndex < vector.SlotCount; slotIndex++)
            {
                var rho = DeriveScalar(seed, "rho", publishedIndex, slotIndex);
                rhoRow[slotIndex] = ToDecimal(rho);
                var source = accepted.Slots[slotIndex].ToCipherSlot();
                var rerandomized = new CipherSlot(
                    BabyJubJub.ToAffine(BabyJubJub.AddProjective(
                        BabyJubJub.ToProjective(source.C1),
                        BabyJubJub.ScalarMulProjective(BabyJubJub.Generator, rho))),
                    BabyJubJub.ToAffine(BabyJubJub.AddProjective(
                        BabyJubJub.ToProjective(source.C2),
                        BabyJubJub.ScalarMulProjective(publicKey, rho))));
                publishedSlots.Add(CipherSlotPayload.FromSlot(rerandomized));
            }

            rhoRows[publishedIndex] = rhoRow;
            publishedBallots[publishedIndex] = new BallotDocument(
                BallotId: $"published-{publishedIndex:D3}",
                Slots: publishedSlots.ToArray());
        });

        var acceptedSet = new BallotSetDocument(
            Schema: "HushSp07AcceptedBallotsPocV1",
            VectorId: vector.Id,
            ElectionId: electionId,
            BallotDefinitionHash: ballotDefinitionHash,
            GroupProfile: ProtocolConstants.GroupProfile,
            CiphertextSlotCount: vector.SlotCount,
            Ballots: acceptedBallots.ToArray());
        var publishedSet = new BallotSetDocument(
            Schema: "HushSp07PublishedBallotsPocV1",
            VectorId: vector.Id,
            ElectionId: electionId,
            BallotDefinitionHash: ballotDefinitionHash,
            GroupProfile: ProtocolConstants.GroupProfile,
            CiphertextSlotCount: vector.SlotCount,
            Ballots: publishedBallots);

        var acceptedHash = HexSha256(Program.ToCanonicalJson(acceptedSet));
        var publishedHash = HexSha256(Program.ToCanonicalJson(publishedSet));
        var matrix = MatrixDimensions.For(vector.BallotCount);
        var commitmentKey = CommitmentKeyGenerator.Generate(vector.Id, matrix.N);
        var commitmentKeyHash = HexSha256(Program.ToCanonicalJson(commitmentKey));
        var privateWitness = new PrivateWitnessDocument(
            Schema: "HushSp07PrivateWitnessPocV1",
            VectorId: vector.Id,
            Warning: "PoC/test-only witness. Production audit packages must not export this file.",
            Seed: seed,
            PermutationPublishedToAccepted: permutation,
            RhoByPublishedBallotAndSlot: rhoRows);
        var proofStatement = BuildProofStatement(
            electionId,
            ballotDefinitionHash,
            acceptedSet,
            publishedSet,
            publicKeyDocument,
            commitmentKey,
            matrix);
        var proofWitness = new Sp07PublicationProofWitness(
            permutation,
            rhoRows.Select(row => row.ToArray()).ToArray());
        var profiledProofResult = new Sp07PublicationProofService().GenerateWithProfile(proofStatement, proofWitness);
        var proofResult = profiledProofResult.Result;
        var transcript = new PublicationProofTranscript(
            Schema: "PublicationProofTranscript-v1",
            PublicationProofMode: ProtocolConstants.PublicationProofMode,
            ProofConstruction: ProtocolConstants.ProofConstruction,
            ProofAdapter: ProtocolConstants.ProofAdapter,
            GroupProfile: ProtocolConstants.GroupProfile,
            ElectionId: electionId,
            BallotDefinitionHash: ballotDefinitionHash,
            ElectionPublicKeyHash: publicKeyDocument.ElectionPublicKeyHash,
            AcceptedBallotSetHash: acceptedHash,
            PublishedBallotStreamHash: publishedHash,
            AcceptedBallotCount: vector.BallotCount,
            PublishedBallotCount: vector.BallotCount,
            CiphertextSlotCount: vector.SlotCount,
            MatrixM: matrix.M,
            MatrixN: matrix.N,
            CommitmentKeyProfile: ProtocolConstants.CommitmentKeyProfile,
            CommitmentKeyHash: commitmentKeyHash,
            FiatShamirProfile: ProtocolConstants.FiatShamirProfile,
            ProofBytes: proofResult.ProofBytes,
            ProofHash: proofResult.ProofHash,
            ImplementationStatus: "poc_public_proof_generated_m_1",
            ProofStatus: "public_proof_generated_and_self_verified");

        var tallyReplay = TallyReplayBuilder.Build(vector.Id, electionId, publishedSet);

        var verification = EvidenceVerifier.VerifyBundle(
            acceptedSet,
            publishedSet,
            publicKeyDocument,
            commitmentKey,
            privateWitness,
            transcript,
            tallyReplay);

        return new SyntheticVectorBundle(
            Definition: vector,
            AcceptedBallots: acceptedSet,
            PublishedBallots: publishedSet,
            ElectionPublicKey: publicKeyDocument,
            CommitmentKey: commitmentKey,
            PrivateWitness: privateWitness,
            Transcript: transcript,
            TallyReplay: tallyReplay,
            VerifierOutput: verification,
            ProofProfile: profiledProofResult.Profile,
            Notes: NotesBuilder.Build(vector, verification));
    }

    private static Sp07PublicationProofStatement BuildProofStatement(
        string electionId,
        string ballotDefinitionHash,
        BallotSetDocument acceptedSet,
        BallotSetDocument publishedSet,
        ElectionPublicKeyDocument publicKeyDocument,
        CommitmentKeyDocument commitmentKey,
        (int M, int N) matrix) =>
        new(
            ElectionId: electionId,
            BallotDefinitionHash: ballotDefinitionHash,
            GroupProfile: ProtocolConstants.GroupProfile,
            AcceptedBallots: acceptedSet.Ballots.Select(ToProofBallot).ToArray(),
            PublishedBallots: publishedSet.Ballots.Select(ToProofBallot).ToArray(),
            ElectionPublicKey: ToProofPoint(publicKeyDocument.ElectionPublicKey),
            CommitmentKey: new Sp07CommitmentKeyPayload(
                commitmentKey.Profile,
                commitmentKey.MatrixN,
                ToProofPoint(commitmentKey.H),
                commitmentKey.G.Select(ToProofPoint).ToArray()),
            MatrixM: matrix.M,
            MatrixN: matrix.N);

    internal static Sp07PublicationProofStatement BuildProofStatement(
        BallotSetDocument acceptedSet,
        BallotSetDocument publishedSet,
        ElectionPublicKeyDocument publicKeyDocument,
        CommitmentKeyDocument commitmentKey,
        PublicationProofTranscript transcript) =>
        new(
            ElectionId: transcript.ElectionId,
            BallotDefinitionHash: transcript.BallotDefinitionHash,
            GroupProfile: ProtocolConstants.GroupProfile,
            AcceptedBallots: acceptedSet.Ballots.Select(ToProofBallot).ToArray(),
            PublishedBallots: publishedSet.Ballots.Select(ToProofBallot).ToArray(),
            ElectionPublicKey: ToProofPoint(publicKeyDocument.ElectionPublicKey),
            CommitmentKey: new Sp07CommitmentKeyPayload(
                commitmentKey.Profile,
                commitmentKey.MatrixN,
                ToProofPoint(commitmentKey.H),
                commitmentKey.G.Select(ToProofPoint).ToArray()),
            MatrixM: transcript.MatrixM,
            MatrixN: transcript.MatrixN);

    private static Sp07CipherBallotPayload ToProofBallot(BallotDocument ballot) =>
        new(ballot.Slots
            .Select(slot => new Sp07CipherSlotPayload(ToProofPoint(slot.C1), ToProofPoint(slot.C2)))
            .ToArray());

    private static Sp07PointPayload ToProofPoint(PointPayload point) =>
        new(point.X, point.Y);

    private static BallotDocument[] BuildAcceptedBallots(VectorDefinition vector, string seed, Point publicKey)
    {
        var ballots = new BallotDocument[vector.BallotCount];
        Parallel.For(0, vector.BallotCount, ballotIndex =>
        {
            var slots = new List<CipherSlotPayload>();
            for (var slotIndex = 0; slotIndex < vector.SlotCount; slotIndex++)
            {
                var messageScalar = ((ballotIndex + slotIndex) % 2) + 1;
                var message = BabyJubJub.ScalarMul(BabyJubJub.Generator, messageScalar);
                var nonce = DeriveScalar(seed, "accepted-nonce", ballotIndex, slotIndex);
                var encrypted = EncryptPoint(message, publicKey, nonce);
                slots.Add(CipherSlotPayload.FromSlot(encrypted));
            }

            ballots[ballotIndex] = new BallotDocument($"accepted-{ballotIndex:D3}", slots.ToArray());
        });

        return ballots;
    }

    private static int[] BuildPermutation(int count, string seed) =>
        Enumerable.Range(0, count)
            .Select(index => new
            {
                Index = index,
                SortKey = HexSha256($"{seed}|permutation|{index}")
            })
            .OrderBy(x => x.SortKey, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .Select(x => x.Index)
            .ToArray();

    private static CipherSlot EncryptPoint(Point message, Point publicKey, BigInteger nonce) =>
        new(
            BabyJubJub.ScalarMul(BabyJubJub.Generator, nonce),
            BabyJubJub.ToAffine(BabyJubJub.AddProjective(
                BabyJubJub.ToProjective(message),
                BabyJubJub.ScalarMulProjective(publicKey, nonce))));

    private static CipherSlot EncryptZero(Point publicKey, BigInteger nonce) =>
        new(
            BabyJubJub.ScalarMul(BabyJubJub.Generator, nonce),
            BabyJubJub.ScalarMul(publicKey, nonce));

    internal static BigInteger DeriveScalar(string seed, string label, params int[] indexes)
    {
        var indexText = indexes.Length == 0
            ? string.Empty
            : "|" + string.Join("|", indexes.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        var counter = 0;
        while (true)
        {
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes($"{seed}|{label}{indexText}|{counter}"));
            var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true) % BabyJubJub.Order;
            if (value != BigInteger.Zero)
            {
                return value;
            }

            counter++;
        }
    }

    internal static string HexSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string ToDecimal(BigInteger value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

internal static class EvidenceWriter
{
    public static void Write(string folder, SyntheticVectorBundle bundle)
    {
        Program.WriteJson(Path.Combine(folder, "accepted-ballots.json"), bundle.AcceptedBallots);
        Program.WriteJson(Path.Combine(folder, "published-ballots.json"), bundle.PublishedBallots);
        Program.WriteJson(Path.Combine(folder, "election-public-key.json"), bundle.ElectionPublicKey);
        Program.WriteJson(Path.Combine(folder, "commitment-key.json"), bundle.CommitmentKey);
        Program.WriteJson(Path.Combine(folder, "private-witness.json"), bundle.PrivateWitness);
        Program.WriteJson(Path.Combine(folder, "publication-proof-transcript.json"), bundle.Transcript);
        Program.WriteJson(Path.Combine(folder, "publication-proof-profile.json"), bundle.ProofProfile);
        Program.WriteJson(Path.Combine(folder, "publication-proof-verifier-output.json"), bundle.VerifierOutput);
        Program.WriteJson(Path.Combine(folder, "expected-verifier-output.json"), bundle.VerifierOutput);
        Program.WriteJson(Path.Combine(folder, "tally-replay.json"), bundle.TallyReplay);
        File.WriteAllText(Path.Combine(folder, "notes.md"), bundle.Notes, Encoding.UTF8);
    }
}

internal static class EvidenceVerifier
{
    public static VerificationOutput Verify(string vectorFolder)
    {
        var accepted = Read<BallotSetDocument>(vectorFolder, "accepted-ballots.json");
        var published = Read<BallotSetDocument>(vectorFolder, "published-ballots.json");
        var key = Read<ElectionPublicKeyDocument>(vectorFolder, "election-public-key.json");
        var commitmentKey = Read<CommitmentKeyDocument>(vectorFolder, "commitment-key.json");
        var witnessPath = Path.Combine(vectorFolder, "private-witness.json");
        var witness = File.Exists(witnessPath)
            ? Read<PrivateWitnessDocument>(vectorFolder, "private-witness.json")
            : null;
        var transcript = Read<PublicationProofTranscript>(vectorFolder, "publication-proof-transcript.json");
        var tally = Read<TallyReplayDocument>(vectorFolder, "tally-replay.json");

        return VerifyBundle(accepted, published, key, commitmentKey, witness, transcript, tally);
    }

    public static VerificationOutput VerifyBundle(
        BallotSetDocument accepted,
        BallotSetDocument published,
        ElectionPublicKeyDocument publicKeyDocument,
        CommitmentKeyDocument commitmentKey,
        PrivateWitnessDocument? witness,
        PublicationProofTranscript transcript,
        TallyReplayDocument tallyReplay)
    {
        var checks = new List<VerificationCheck>();
        var publicKey = publicKeyDocument.ElectionPublicKey.ToPoint();
        AddPointCheck(checks, "PVA-001", "election public key", publicKey);

        checks.Add(Check(
            "PVA-002",
            accepted.Ballots.Length == transcript.AcceptedBallotCount &&
            published.Ballots.Length == transcript.PublishedBallotCount &&
            accepted.Ballots.Length == published.Ballots.Length,
            "accepted and published counts match transcript"));

        checks.Add(Check(
            "PVA-003",
            accepted.CiphertextSlotCount == transcript.CiphertextSlotCount &&
            published.CiphertextSlotCount == transcript.CiphertextSlotCount,
            "ciphertext slot count matches transcript"));

        var acceptedHash = SyntheticVectorGenerator.HexSha256(Program.ToCanonicalJson(accepted));
        var publishedHash = SyntheticVectorGenerator.HexSha256(Program.ToCanonicalJson(published));
        checks.Add(Check(
            "PVA-004",
            string.Equals(acceptedHash, transcript.AcceptedBallotSetHash, StringComparison.Ordinal),
            "accepted ballot set hash matches transcript"));
        checks.Add(Check(
            "PVA-005",
            string.Equals(publishedHash, transcript.PublishedBallotStreamHash, StringComparison.Ordinal),
            "published ballot stream hash matches transcript"));

        if (witness is null)
        {
            checks.Add(new VerificationCheck(
                "PVA-006",
                "NOT_APPLICABLE",
                "private PoC witness was not loaded; public PUB-005 verification does not require it"));
        }
        else
        {
            checks.Add(Check(
                "PVA-006",
                WitnessRelationHolds(accepted, published, publicKey, witness),
                "private PoC witness maps accepted ballots to published ballots by permutation and per-slot rerandomization"));
        }

        checks.Add(Check(
            "PVA-007",
            PublicPrivacyScanPasses(accepted, published, transcript, tallyReplay),
            "public PoC artifacts do not expose witness or named voter fields"));

        var publicProofStatement = SyntheticVectorGenerator.BuildProofStatement(
            accepted,
            published,
            publicKeyDocument,
            commitmentKey,
            transcript);
        var proofVerification = new Sp07PublicationProofService().Verify(
            publicProofStatement,
            transcript.ProofBytes,
            transcript.ProofHash);
        checks.Add(new VerificationCheck(
            "PUB-005",
            proofVerification.IsValid ? "PASS" : "FAIL",
            proofVerification.Message));

        checks.Add(Check(
            "VFY-050",
            string.Equals(tallyReplay.PublishedBallotStreamHash, transcript.PublishedBallotStreamHash, StringComparison.Ordinal),
            "tally replay binds to published stream hash"));

        var blockingFailures = checks.Any(x => string.Equals(x.Status, "FAIL", StringComparison.Ordinal));
        var pendingProof = checks.Any(x => string.Equals(x.Status, "PENDING", StringComparison.Ordinal));
        var status = blockingFailures
            ? "FAIL"
            : pendingProof
                ? "PASS_WITH_PUBLIC_PROOF_PENDING"
                : "PASS";

        return new VerificationOutput(
            Schema: "HushSp07PublicationProofPocVerifierOutputV1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            OverallStatus: status,
            Checks: checks.ToArray());
    }

    private static T Read<T>(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, Program.ReadJsonOptions)
            ?? throw new InvalidOperationException($"Could not read {path}.");
    }

    private static bool WitnessRelationHolds(
        BallotSetDocument accepted,
        BallotSetDocument published,
        Point publicKey,
        PrivateWitnessDocument witness)
    {
        if (witness.PermutationPublishedToAccepted.Length != published.Ballots.Length ||
            witness.RhoByPublishedBallotAndSlot.Length != published.Ballots.Length)
        {
            return false;
        }

        var seen = new HashSet<int>();
        for (var publishedIndex = 0; publishedIndex < published.Ballots.Length; publishedIndex++)
        {
            var acceptedIndex = witness.PermutationPublishedToAccepted[publishedIndex];
            if (acceptedIndex < 0 || acceptedIndex >= accepted.Ballots.Length || !seen.Add(acceptedIndex))
            {
                return false;
            }

            var rhoRow = witness.RhoByPublishedBallotAndSlot[publishedIndex];
            if (rhoRow.Length != accepted.CiphertextSlotCount)
            {
                return false;
            }
        }

        var failed = 0;
        Parallel.For(0, published.Ballots.Length, (publishedIndex, loopState) =>
        {
            if (Volatile.Read(ref failed) != 0)
            {
                loopState.Stop();
                return;
            }

            var acceptedIndex = witness.PermutationPublishedToAccepted[publishedIndex];
            var rhoRow = witness.RhoByPublishedBallotAndSlot[publishedIndex];
            for (var slotIndex = 0; slotIndex < accepted.CiphertextSlotCount; slotIndex++)
            {
                if (!BigInteger.TryParse(rhoRow[slotIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var rho))
                {
                    Interlocked.Exchange(ref failed, 1);
                    loopState.Stop();
                    return;
                }

                var source = accepted.Ballots[acceptedIndex].Slots[slotIndex].ToCipherSlot();
                var expected = new CipherSlot(
                    BabyJubJub.ToAffine(BabyJubJub.AddProjective(
                        BabyJubJub.ToProjective(source.C1),
                        BabyJubJub.ScalarMulProjective(BabyJubJub.Generator, rho))),
                    BabyJubJub.ToAffine(BabyJubJub.AddProjective(
                        BabyJubJub.ToProjective(source.C2),
                        BabyJubJub.ScalarMulProjective(publicKey, rho))));
                var actual = published.Ballots[publishedIndex].Slots[slotIndex].ToCipherSlot();
                if (!expected.Equals(actual))
                {
                    Interlocked.Exchange(ref failed, 1);
                    loopState.Stop();
                    return;
                }
            }
        });

        return failed == 0;
    }

    private static bool PublicPrivacyScanPasses(params object[] publicArtifacts)
    {
        var forbidden = new[]
        {
            "rho",
            "permutation",
            "privatewitness",
            "votesecret",
            "checkoff",
            "linkedactor",
            "organizationvoter",
            "plaintextchoice",
            "acceptedtopublished",
            "publishedtoaccepted",
            "rerandomizationbypublishedballotandslot"
        };

        foreach (var artifact in publicArtifacts)
        {
            var json = Program.ToCanonicalJson(artifact).ToLowerInvariant();
            if (forbidden.Any(json.Contains))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddPointCheck(List<VerificationCheck> checks, string code, string label, Point point)
    {
        checks.Add(Check(
            code,
            BabyJubJub.IsValidPublicPoint(point),
            $"{label} is on curve, in subgroup, and not identity"));
    }

    private static VerificationCheck Check(
        string code,
        bool condition,
        string message,
        string passStatus = "PASS",
        string failStatus = "FAIL") =>
        new(code, condition ? passStatus : failStatus, message);
}

internal static class TallyReplayBuilder
{
    public static TallyReplayDocument Build(string vectorId, string electionId, BallotSetDocument published)
    {
        var tallyC1 = Enumerable.Range(0, published.CiphertextSlotCount)
            .Select(_ => BabyJubJub.ProjectiveIdentity)
            .ToArray();
        var tallyC2 = Enumerable.Range(0, published.CiphertextSlotCount)
            .Select(_ => BabyJubJub.ProjectiveIdentity)
            .ToArray();

        foreach (var ballot in published.Ballots)
        {
            for (var slotIndex = 0; slotIndex < published.CiphertextSlotCount; slotIndex++)
            {
                var slot = ballot.Slots[slotIndex].ToCipherSlot();
                tallyC1[slotIndex] = BabyJubJub.AddProjective(tallyC1[slotIndex], BabyJubJub.ToProjective(slot.C1));
                tallyC2[slotIndex] = BabyJubJub.AddProjective(tallyC2[slotIndex], BabyJubJub.ToProjective(slot.C2));
            }
        }

        var tally = Enumerable.Range(0, published.CiphertextSlotCount)
            .Select(slotIndex => new CipherSlot(
                BabyJubJub.ToAffine(tallyC1[slotIndex]),
                BabyJubJub.ToAffine(tallyC2[slotIndex])))
            .ToArray();
        var tallyPayload = tally.Select(CipherSlotPayload.FromSlot).ToArray();
        var publishedHash = SyntheticVectorGenerator.HexSha256(Program.ToCanonicalJson(published));
        var tallyHash = SyntheticVectorGenerator.HexSha256(Program.ToCanonicalJson(tallyPayload));
        return new TallyReplayDocument(
            Schema: "HushSp07TallyReplayPocV1",
            VectorId: vectorId,
            ElectionId: electionId,
            PublishedBallotStreamHash: publishedHash,
            FinalEncryptedTallyHash: tallyHash,
            SlotTallies: tallyPayload);
    }
}

internal static class CommitmentKeyGenerator
{
    public static CommitmentKeyDocument Generate(string vectorId, int matrixN)
    {
        var h = HashToBabyJubJubPoint($"{ProtocolConstants.CommitmentKeyProfile}|{vectorId}|h|{matrixN}");
        var points = new PointPayload[matrixN];
        Parallel.For(1, matrixN + 1, index =>
        {
            points[index - 1] = PointPayload.FromPoint(
                HashToBabyJubJubPoint($"{ProtocolConstants.CommitmentKeyProfile}|{vectorId}|g|{matrixN}|{index}"));
        });

        return new CommitmentKeyDocument(
            Schema: "HushSp07CommitmentKeyPocV1",
            VectorId: vectorId,
            Profile: ProtocolConstants.CommitmentKeyProfile,
            MatrixN: matrixN,
            H: PointPayload.FromPoint(h),
            G: points);
    }

    private static Point HashToBabyJubJubPoint(string seed)
    {
        for (var counter = 0; counter < 10_000; counter++)
        {
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes($"{seed}|{counter}"));
            var x = new BigInteger(bytes[..32], isUnsigned: true, isBigEndian: true) % BabyJubJub.FieldPrime;
            var sign = (bytes[32] & 1) == 1;
            var x2 = BabyJubJub.Mod(x * x);
            var numerator = BabyJubJub.Mod(1 - BabyJubJub.A * x2);
            var denominator = BabyJubJub.Mod(1 - BabyJubJub.D * x2);
            if (denominator == BigInteger.Zero)
            {
                continue;
            }

            var y2 = BabyJubJub.Mod(numerator * BabyJubJub.ModInverse(denominator));
            if (!BabyJubJub.TryModSqrt(y2, out var y))
            {
                continue;
            }

            if (((y & BigInteger.One) == BigInteger.One) != sign)
            {
                y = BabyJubJub.Mod(-y);
            }

            var point = new Point(x, y);
            if (!BabyJubJub.IsOnCurve(point))
            {
                continue;
            }

            var subgroupPoint = BabyJubJub.ScalarMul(point, 8);
            if (BabyJubJub.IsValidPublicPoint(subgroupPoint) && !subgroupPoint.Equals(BabyJubJub.Generator))
            {
                return subgroupPoint;
            }
        }

        throw new InvalidOperationException($"Could not hash seed to BabyJubJub subgroup point: {seed}");
    }
}

internal static class NotesBuilder
{
    public static string Build(VectorDefinition vector, VerificationOutput verification)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {vector.Id}");
        builder.AppendLine();
        builder.AppendLine("Synthetic local-only SP-07 PoC vector.");
        builder.AppendLine();
        builder.AppendLine($"- Ballots: {vector.BallotCount}");
        builder.AppendLine($"- Slots: {vector.SlotCount}");
        builder.AppendLine($"- Verifier status: `{verification.OverallStatus}`");
        builder.AppendLine("- Server connection: none");
        builder.AppendLine("- Blockchain writes: none");
        builder.AppendLine("- Real voter data: none");
        builder.AppendLine();
        builder.AppendLine("The private witness file is present only for PoC generation diagnostics. The PUB-005");
        builder.AppendLine("verifier check uses the public proof bytes in publication-proof-transcript.json and");
        builder.AppendLine("does not load the private witness material.");
        return builder.ToString();
    }
}

internal static class MatrixDimensions
{
    public static (int M, int N) For(int ballotCount)
    {
        if (ballotCount < 2)
        {
            throw new InvalidOperationException("Matrix dimensions require N >= 2.");
        }

        return (1, ballotCount);
    }
}

internal static class BabyJubJub
{
    public static readonly BigInteger A = BigInteger.Parse("168700", CultureInfo.InvariantCulture);
    public static readonly BigInteger D = BigInteger.Parse("168696", CultureInfo.InvariantCulture);
    public static readonly BigInteger FieldPrime = BigInteger.Parse(
        "21888242871839275222246405745257275088548364400416034343698204186575808495617",
        CultureInfo.InvariantCulture);
    public static readonly BigInteger Order = BigInteger.Parse(
        "2736030358979909402780800718157159386076813972158567259200215660948447373041",
        CultureInfo.InvariantCulture);
    private const int ScalarWindowBits = 4;
    private const int SmallScalarBitLength = 32;
    public static readonly Point Identity = new(BigInteger.Zero, BigInteger.One);
    public static readonly ProjectivePoint ProjectiveIdentity = new(BigInteger.Zero, BigInteger.One, BigInteger.One);
    public static readonly Point Generator = new(
        BigInteger.Parse("5299619240641551281634865583518297030282874472190772894086521144482721001553", CultureInfo.InvariantCulture),
        BigInteger.Parse("16950150798460657717958625567821834550301663161624707787222815936182638968203", CultureInfo.InvariantCulture));

    public static Point Add(Point left, Point right)
    {
        if (left.Equals(Identity)) return right;
        if (right.Equals(Identity)) return left;

        return ToAffine(AddProjective(ToProjective(left), ToProjective(right)));
    }

    public static Point ScalarMul(Point point, BigInteger scalar)
    {
        if (scalar == BigInteger.Zero)
        {
            return Identity;
        }

        if (scalar < 0)
        {
            scalar = -scalar;
            point = new Point(Mod(-point.X), point.Y);
        }

        var result = ScalarMulProjective(point, scalar);

        return ToAffine(result);
    }

    public static ProjectivePoint ScalarMulProjective(Point point, BigInteger scalar)
    {
        if (scalar < 0)
        {
            scalar = -scalar;
            point = new Point(Mod(-point.X), point.Y);
        }

        scalar %= Order;
        return scalar == BigInteger.Zero
            ? ProjectiveIdentity
            : scalar.GetBitLength() <= SmallScalarBitLength
                ? ScalarMulProjectiveDoubleAndAdd(ToProjective(point), scalar)
                : ScalarMulProjectiveWindowed(ToProjective(point), scalar);
    }

    private static ProjectivePoint ScalarMulProjectiveDoubleAndAdd(ProjectivePoint point, BigInteger scalar)
    {
        var result = ProjectiveIdentity;
        var temp = point;
        while (scalar > 0)
        {
            if (!scalar.IsEven)
            {
                result = AddProjective(result, temp);
            }

            temp = AddProjective(temp, temp);
            scalar >>= 1;
        }

        return result;
    }

    private static ProjectivePoint ScalarMulProjectiveWindowed(ProjectivePoint point, BigInteger scalar)
    {
        var table = BuildWindowTable(point);
        var digits = ToWindowDigits(scalar);
        var result = ProjectiveIdentity;
        for (var digitIndex = digits.Length - 1; digitIndex >= 0; digitIndex--)
        {
            for (var shift = 0; shift < ScalarWindowBits; shift++)
            {
                result = AddProjective(result, result);
            }

            var digit = digits[digitIndex];
            if (digit != 0)
            {
                result = AddProjective(result, table[digit]);
            }
        }

        return result;
    }

    private static ProjectivePoint[] BuildWindowTable(ProjectivePoint point)
    {
        var table = new ProjectivePoint[1 << ScalarWindowBits];
        table[0] = ProjectiveIdentity;
        table[1] = point;
        for (var index = 2; index < table.Length; index++)
        {
            table[index] = AddProjective(table[index - 1], point);
        }

        return table;
    }

    private static int[] ToWindowDigits(BigInteger scalar)
    {
        var mask = (BigInteger.One << ScalarWindowBits) - BigInteger.One;
        var digits = new List<int>();
        while (scalar > 0)
        {
            digits.Add((int)(scalar & mask));
            scalar >>= ScalarWindowBits;
        }

        return digits.ToArray();
    }

    public static bool IsValidPublicPoint(Point point) =>
        !point.Equals(Identity) && IsOnCurve(point) && ScalarMul(point, Order).Equals(Identity);

    public static bool IsOnCurve(Point point)
    {
        if (point.X < 0 || point.X >= FieldPrime || point.Y < 0 || point.Y >= FieldPrime)
        {
            return false;
        }

        var x2 = Mod(point.X * point.X);
        var y2 = Mod(point.Y * point.Y);
        var left = Mod(Mod(A * x2) + y2);
        var right = Mod(1 + Mod(Mod(D * x2) * y2));
        return left == right;
    }

    public static BigInteger Mod(BigInteger value)
    {
        var result = value % FieldPrime;
        return result < 0 ? result + FieldPrime : result;
    }

    public static BigInteger ModInverse(BigInteger value)
    {
        var current = Mod(value);
        if (current == BigInteger.Zero)
        {
            throw new DivideByZeroException("Cannot invert zero in the BabyJubJub field.");
        }

        var previousRemainder = FieldPrime;
        var remainder = current;
        var previousCoefficient = BigInteger.Zero;
        var coefficient = BigInteger.One;

        while (remainder != BigInteger.Zero)
        {
            var quotient = previousRemainder / remainder;
            (previousRemainder, remainder) = (remainder, previousRemainder - quotient * remainder);
            (previousCoefficient, coefficient) = (coefficient, previousCoefficient - quotient * coefficient);
        }

        if (previousRemainder != BigInteger.One)
        {
            throw new InvalidOperationException("BabyJubJub field element is not invertible.");
        }

        return Mod(previousCoefficient);
    }

    public static ProjectivePoint ToProjective(Point point) =>
        new(point.X, point.Y, BigInteger.One);

    public static Point ToAffine(ProjectivePoint point)
    {
        if (point.Z == BigInteger.Zero)
        {
            throw new InvalidOperationException("Projective BabyJubJub point has zero Z coordinate.");
        }

        var inverseZ = ModInverse(point.Z);
        return new Point(Mod(point.X * inverseZ), Mod(point.Y * inverseZ));
    }

    public static ProjectivePoint AddProjective(ProjectivePoint left, ProjectivePoint right)
    {
        // Projective twisted-Edwards addition avoids the field inversions paid by affine formulas.
        var z1z2 = Mod(left.Z * right.Z);
        var z1z2Squared = Mod(z1z2 * z1z2);
        var x1x2 = Mod(left.X * right.X);
        var y1y2 = Mod(left.Y * right.Y);
        var dTerm = Mod(D * Mod(x1x2 * y1y2));
        var oneMinusDTerm = Mod(z1z2Squared - dTerm);
        var onePlusDTerm = Mod(z1z2Squared + dTerm);
        var mixed = Mod(Mod(left.X + left.Y) * Mod(right.X + right.Y) - x1x2 - y1y2);
        var yNumerator = Mod(y1y2 - Mod(A * x1x2));

        return new ProjectivePoint(
            Mod(Mod(z1z2 * oneMinusDTerm) * mixed),
            Mod(Mod(z1z2 * onePlusDTerm) * yNumerator),
            Mod(oneMinusDTerm * onePlusDTerm));
    }

    public static bool TryModSqrt(BigInteger value, out BigInteger sqrt)
    {
        value = Mod(value);
        if (value == BigInteger.Zero)
        {
            sqrt = BigInteger.Zero;
            return true;
        }

        if (BigInteger.ModPow(value, (FieldPrime - 1) / 2, FieldPrime) != BigInteger.One)
        {
            sqrt = BigInteger.Zero;
            return false;
        }

        if (FieldPrime % 4 == 3)
        {
            sqrt = BigInteger.ModPow(value, (FieldPrime + 1) / 4, FieldPrime);
            return true;
        }

        var q = FieldPrime - 1;
        var s = 0;
        while (q.IsEven)
        {
            s++;
            q /= 2;
        }

        var z = new BigInteger(2);
        while (BigInteger.ModPow(z, (FieldPrime - 1) / 2, FieldPrime) != FieldPrime - 1)
        {
            z++;
        }

        var m = s;
        var c = BigInteger.ModPow(z, q, FieldPrime);
        var t = BigInteger.ModPow(value, q, FieldPrime);
        var r = BigInteger.ModPow(value, (q + 1) / 2, FieldPrime);

        while (t != BigInteger.One)
        {
            var i = 1;
            var t2i = BigInteger.ModPow(t, 2, FieldPrime);
            while (t2i != BigInteger.One)
            {
                t2i = BigInteger.ModPow(t2i, 2, FieldPrime);
                i++;
                if (i == m)
                {
                    sqrt = BigInteger.Zero;
                    return false;
                }
            }

            var b = BigInteger.ModPow(c, BigInteger.One << (m - i - 1), FieldPrime);
            m = i;
            c = BigInteger.ModPow(b, 2, FieldPrime);
            t = Mod(t * c);
            r = Mod(r * b);
        }

        sqrt = r;
        return true;
    }
}

internal static class ProtocolConstants
{
    public const string PublicationProofMode = "zk_rerandomization_shuffle_v1";
    public const string ProofConstruction = "bayer_groth_reencryption_shuffle_argument_v1";
    public const string ProofAdapter = "hush_babyjubjub_vector_ballot_bg_adapter_v1";
    public const string GroupProfile = "hush_babyjubjub_bn254_subgroup_v1";
    public const string CommitmentKeyProfile = "hush_sp07_hash_to_babyjubjub_commitment_key_v1";
    public const string FiatShamirProfile = "hush_sp07_bg_fs_sha512_to_zq_v1";
}

internal sealed record SyntheticVectorBundle(
    VectorDefinition Definition,
    BallotSetDocument AcceptedBallots,
    BallotSetDocument PublishedBallots,
    ElectionPublicKeyDocument ElectionPublicKey,
    CommitmentKeyDocument CommitmentKey,
    PrivateWitnessDocument PrivateWitness,
    PublicationProofTranscript Transcript,
    TallyReplayDocument TallyReplay,
    VerificationOutput VerifierOutput,
    Sp07ProofGenerationProfile ProofProfile,
    string Notes);

internal sealed record BallotSetDocument(
    string Schema,
    string VectorId,
    string ElectionId,
    string BallotDefinitionHash,
    string GroupProfile,
    int CiphertextSlotCount,
    BallotDocument[] Ballots);

internal sealed record BallotDocument(string BallotId, CipherSlotPayload[] Slots);

internal sealed record CipherSlotPayload(PointPayload C1, PointPayload C2)
{
    public static CipherSlotPayload FromSlot(CipherSlot slot) =>
        new(PointPayload.FromPoint(slot.C1), PointPayload.FromPoint(slot.C2));

    public CipherSlot ToCipherSlot() => new(C1.ToPoint(), C2.ToPoint());
}

internal sealed record PointPayload(string X, string Y)
{
    public static PointPayload FromPoint(Point point) =>
        new(
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture));

    public Point ToPoint() =>
        new(
            BigInteger.Parse(X, CultureInfo.InvariantCulture),
            BigInteger.Parse(Y, CultureInfo.InvariantCulture));
}

internal sealed record Point(BigInteger X, BigInteger Y);

internal sealed record ProjectivePoint(BigInteger X, BigInteger Y, BigInteger Z);

internal sealed record CipherSlot(Point C1, Point C2);

internal sealed record ElectionPublicKeyDocument(
    string Schema,
    string VectorId,
    string GroupProfile,
    PointPayload ElectionPublicKey,
    string ElectionPublicKeyHash);

internal sealed record CommitmentKeyDocument(
    string Schema,
    string VectorId,
    string Profile,
    int MatrixN,
    PointPayload H,
    PointPayload[] G);

internal sealed record PrivateWitnessDocument(
    string Schema,
    string VectorId,
    string Warning,
    string Seed,
    int[] PermutationPublishedToAccepted,
    string[][] RhoByPublishedBallotAndSlot);

internal sealed record PublicationProofTranscript(
    string Schema,
    string PublicationProofMode,
    string ProofConstruction,
    string ProofAdapter,
    string GroupProfile,
    string ElectionId,
    string BallotDefinitionHash,
    string ElectionPublicKeyHash,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int CiphertextSlotCount,
    int MatrixM,
    int MatrixN,
    string CommitmentKeyProfile,
    string CommitmentKeyHash,
    string FiatShamirProfile,
    string ProofBytes,
    string ProofHash,
    string ImplementationStatus,
    string ProofStatus);

internal sealed record TallyReplayDocument(
    string Schema,
    string VectorId,
    string ElectionId,
    string PublishedBallotStreamHash,
    string FinalEncryptedTallyHash,
    CipherSlotPayload[] SlotTallies);

internal sealed record VerificationOutput(
    string Schema,
    DateTimeOffset GeneratedAtUtc,
    string OverallStatus,
    VerificationCheck[] Checks);

internal sealed record VerificationCheck(string Code, string Status, string Message);
