using System.Globalization;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Elections.PublicationProof;

public sealed class Sp07PublicationProofService
{
    public const string ProofSystemVersion = "sp07-bayer-groth-hush-vector-shuffle-v1.0.0";
    public const string SupportedMatrixProfile = "matrix_m_1_publication_proof_v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public Sp07ProofGenerationResult Generate(
        Sp07PublicationProofStatement statement,
        Sp07PublicationProofWitness witness) =>
        GenerateCore(statement, witness, null);

    public Sp07ProfiledProofGenerationResult GenerateWithProfile(
        Sp07PublicationProofStatement statement,
        Sp07PublicationProofWitness witness)
    {
        var profiler = new ProofProfiler();
        var result = GenerateCore(statement, witness, profiler);
        return new Sp07ProfiledProofGenerationResult(result, profiler.ToProfile());
    }

    private Sp07ProofGenerationResult GenerateCore(
        Sp07PublicationProofStatement statement,
        Sp07PublicationProofWitness witness,
        ProofProfiler? profiler)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(witness);
        profiler ??= ProofProfiler.Disabled;

        var parsed = profiler.Measure("parse.statement", () => ParsedStatement.Create(statement));
        var parsedWitness = profiler.Measure(
            "parse.witness",
            () => ParsedWitness.Create(witness, parsed.BallotCount, parsed.SlotCount));
        profiler.Measure("witness.relation_check", () => EnsureWitnessRelation(parsed, parsedWitness));

        var source = profiler.Measure("deterministic.scalar_source", () => new DeterministicScalarSource(HashJson(new
        {
            label = "SP07_PROOF_SOURCE_V1",
            statement,
            witness
        })));

        var outer = profiler.Measure(
            "proof.outer_argument.total",
            () => GenerateOuterArgument(statement, parsed, parsedWitness, source, profiler));
        var payload = new Sp07PublicationProofPayload(
            Schema: "HushSp07PublicationProofPayloadV1",
            ProofSystemVersion: ProofSystemVersion,
            MatrixProfile: SupportedMatrixProfile,
            OuterCommitmentsA: outer.CommitmentsA.Select(ToPayload).ToArray(),
            OuterCommitmentsB: outer.CommitmentsB.Select(ToPayload).ToArray(),
            ProductArgument: outer.ProductArgument,
            MultiExponentiationArgument: outer.MultiExponentiationArgument);

        var encoded = profiler.Measure("proof.encode", () => EncodeProof(payload));
        var selfVerification = profiler.Measure("proof.self_verify", () => Verify(statement, encoded.ProofBytes, encoded.ProofHash));
        if (!selfVerification.IsValid)
        {
            throw new InvalidOperationException($"Generated SP-07 proof failed public self-verification: {selfVerification.Message}");
        }

        return new Sp07ProofGenerationResult(payload, encoded.ProofBytes, encoded.ProofHash, selfVerification);
    }

    public Sp07ProofVerificationResult Verify(
        Sp07PublicationProofStatement statement,
        string proofBytes,
        string? expectedProofHash = null)
    {
        ArgumentNullException.ThrowIfNull(statement);

        if (string.IsNullOrWhiteSpace(proofBytes))
        {
            return Fail("PUB-005", "SP-07 proof bytes are missing.");
        }

        Sp07PublicationProofPayload? proof;
        string proofHash;
        try
        {
            var proofJson = Encoding.UTF8.GetString(Convert.FromBase64String(proofBytes));
            proofHash = Sha256Lower(Encoding.UTF8.GetBytes(proofJson));
            if (!string.IsNullOrWhiteSpace(expectedProofHash) &&
                !string.Equals(proofHash, expectedProofHash, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("PUB-005", "SP-07 proof hash does not match the supplied proof bytes.", proofHash);
            }

            proof = JsonSerializer.Deserialize<Sp07PublicationProofPayload>(proofJson, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Fail("PUB-005", $"SP-07 proof bytes are not a valid public proof payload: {ex.Message}");
        }

        if (proof is null)
        {
            return Fail("PUB-005", "SP-07 proof payload could not be decoded.");
        }

        if (!string.Equals(proof.Schema, "HushSp07PublicationProofPayloadV1", StringComparison.Ordinal) ||
            !string.Equals(proof.ProofSystemVersion, ProofSystemVersion, StringComparison.Ordinal) ||
            !string.Equals(proof.MatrixProfile, SupportedMatrixProfile, StringComparison.Ordinal))
        {
            return Fail("PUB-005", "SP-07 proof payload version or matrix profile is unsupported.", proofHash);
        }

        ParsedStatement parsed;
        ParsedProof parsedProof;
        try
        {
            parsed = ParsedStatement.Create(statement);
            parsedProof = ParsedProof.Create(proof, parsed.BallotCount, parsed.SlotCount);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Fail("PUB-005", $"SP-07 proof payload is malformed: {ex.Message}", proofHash);
        }

        var outer = VerifyOuterArgument(statement, parsed, parsedProof);
        if (!outer.IsValid)
        {
            return outer with { ProofHash = proofHash };
        }

        return new Sp07ProofVerificationResult(
            true,
            "PUB-005",
            "SP-07 public rerandomization shuffle proof verified without private witness material.",
            proofHash);
    }

    private static OuterArgumentState GenerateOuterArgument(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        ParsedWitness witness,
        DeterministicScalarSource source,
        ProofProfiler profiler)
    {
        if (parsed.MatrixM != 1)
        {
            throw new NotSupportedException("This first SP-07 public proof implementation supports only matrix m=1.");
        }

        var aMatrix = witness.PublishedToAccepted.Select(index => NormalizeScalar(index)).ToArray();
        var outerR = source.Next("outer-r");
        var cA = new[] { Commit(aMatrix, outerR, parsed.CommitmentKey) };
        var outerX = OuterChallengeX(statement, cA);
        var outerXPowers = ScalarPowers(outerX, parsed.BallotCount);
        var bVector = witness.PublishedToAccepted
            .Select(index => outerXPowers[index])
            .ToArray();
        var outerS = source.Next("outer-s");
        var cB = new[] { Commit(bVector, outerS, parsed.CommitmentKey) };
        var outerY = OuterChallengeY(statement, cA, cB);
        var outerZ = OuterChallengeZ(statement, cA, cB);
        var cMinusZ = Commit(Enumerable.Repeat(NegateScalar(outerZ), parsed.BallotCount).ToArray(), BigInteger.Zero, parsed.CommitmentKey);
        var cD = Add(ScalarMul(cA[0], outerY), cB[0]);
        var productStatementCommitment = Add(cD, cMinusZ);
        var productWitnessValues = aMatrix
            .Select((value, index) => AddScalars(AddScalars(MulScalars(outerY, value), bVector[index]), NegateScalar(outerZ)))
            .ToArray();
        var productWitnessRandomness = AddScalars(MulScalars(outerY, outerR), outerS);
        var productTarget = productWitnessValues.Aggregate(BigInteger.One, MulScalars);

        var productArgument = profiler.Measure(
            "proof.product_argument.generate",
            () => GenerateSingleValueProductArgument(
                statement,
                parsed,
                productStatementCommitment,
                productTarget,
                productWitnessValues,
                productWitnessRandomness,
                source));

        var multiArgument = profiler.Measure(
            "proof.multi_exponentiation_argument.generate",
            () => GenerateMultiExponentiationArgument(
                statement,
                parsed,
                witness,
                cB,
                bVector,
                outerS,
                outerX,
                source,
                profiler));

        return new OuterArgumentState(
            cA,
            cB,
            new Sp07ProductArgumentPayload("single_value_product_m_1_v1", productArgument),
            multiArgument);
    }

    private static Sp07ProofVerificationResult VerifyOuterArgument(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        ParsedProof proof)
    {
        if (parsed.MatrixM != 1)
        {
            return Fail("PUB-005", "SP-07 verifier only supports matrix m=1 in this implementation.");
        }

        var cA = proof.OuterCommitmentsA;
        var cB = proof.OuterCommitmentsB;
        var outerX = OuterChallengeX(statement, cA);
        var xPowers = ScalarPowers(outerX, parsed.BallotCount);
        var outerY = OuterChallengeY(statement, cA, cB);
        var outerZ = OuterChallengeZ(statement, cA, cB);
        var cMinusZ = Commit(Enumerable.Repeat(NegateScalar(outerZ), parsed.BallotCount).ToArray(), BigInteger.Zero, parsed.CommitmentKey);
        var cD = Add(ScalarMul(cA[0], outerY), cB[0]);
        var productStatementCommitment = Add(cD, cMinusZ);
        var productTarget = Enumerable.Range(0, parsed.BallotCount)
            .Select(index => AddScalars(AddScalars(MulScalars(outerY, NormalizeScalar(index)), xPowers[index]), NegateScalar(outerZ)))
            .Aggregate(BigInteger.One, MulScalars);

        var productFailure = VerifySingleValueProductArgumentFailure(
                statement,
                parsed,
                productStatementCommitment,
                productTarget,
                proof.ProductArgument.SingleValueProduct);
        if (productFailure is not null)
        {
            return Fail("PUB-005", $"SP-07 product argument verification failed: {productFailure}");
        }

        var acceptedAggregate = CipherVectorExponentiation(parsed.AcceptedBallots, xPowers);
        if (!VerifyMultiExponentiationArgument(
                statement,
                parsed,
                proof.MultiExponentiationArgument,
                cB,
                acceptedAggregate))
        {
            return Fail("PUB-005", "SP-07 multi-exponentiation argument verification failed.");
        }

        return new Sp07ProofVerificationResult(true, "PUB-005", "SP-07 public proof verified.");
    }

    private static Sp07SingleValueProductArgumentPayload GenerateSingleValueProductArgument(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        Point commitmentA,
        BigInteger product,
        IReadOnlyList<BigInteger> values,
        BigInteger randomness,
        DeterministicScalarSource source)
    {
        if (values.Count < 2)
        {
            throw new InvalidOperationException("Single-value product argument requires at least two values.");
        }

        if (!commitmentA.Equals(Commit(values, randomness, parsed.CommitmentKey)))
        {
            throw new InvalidOperationException("Single-value product statement commitment does not match its witness opening.");
        }

        var n = values.Count;
        var prefixProducts = new BigInteger[n];
        var running = BigInteger.One;
        for (var index = 0; index < n; index++)
        {
            running = MulScalars(running, values[index]);
            prefixProducts[index] = running;
        }

        var d = Enumerable.Range(0, n).Select(index => source.Next($"svp-d-{index}")).ToArray();
        var randomD = source.Next("svp-rd");
        var delta = new BigInteger[n];
        delta[0] = d[0];
        for (var index = 1; index < n - 1; index++)
        {
            delta[index] = source.Next($"svp-delta-{index}");
        }

        delta[n - 1] = BigInteger.Zero;
        var s0 = source.Next("svp-s0");
        var sx = source.Next("svp-sx");
        var deltaPrime = Enumerable.Range(0, n - 1)
            .Select(index => NegateScalar(MulScalars(delta[index], d[index + 1])))
            .ToArray();
        var capitalDelta = Enumerable.Range(0, n - 1)
            .Select(index => AddScalars(
                AddScalars(delta[index + 1], NegateScalar(MulScalars(values[index + 1], delta[index]))),
                NegateScalar(MulScalars(prefixProducts[index], d[index + 1]))))
            .ToArray();
        var cD = Commit(d, randomD, parsed.CommitmentKey);
        var cDelta = Commit(deltaPrime, s0, parsed.CommitmentKey);
        var cCapitalDelta = Commit(capitalDelta, sx, parsed.CommitmentKey);
        var challenge = SingleValueChallenge(statement, parsed, cCapitalDelta, cDelta, cD, product, commitmentA);
        var aTilde = values.Select((value, index) => AddScalars(MulScalars(challenge, value), d[index])).ToArray();
        var bTilde = prefixProducts.Select((value, index) => AddScalars(MulScalars(challenge, value), delta[index])).ToArray();
        var rTilde = AddScalars(MulScalars(challenge, randomness), randomD);
        var sTilde = AddScalars(MulScalars(challenge, sx), s0);

        return new Sp07SingleValueProductArgumentPayload(
            ToPayload(cD),
            ToPayload(cDelta),
            ToPayload(cCapitalDelta),
            aTilde.Select(ToScalarString).ToArray(),
            bTilde.Select(ToScalarString).ToArray(),
            ToScalarString(rTilde),
            ToScalarString(sTilde));
    }

    private static string? VerifySingleValueProductArgumentFailure(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        Point commitmentA,
        BigInteger product,
        Sp07SingleValueProductArgumentPayload argument)
    {
        var cD = ParsePoint(argument.CommitmentD);
        var cDelta = ParsePoint(argument.CommitmentDelta);
        var cCapitalDelta = ParsePoint(argument.CommitmentCapitalDelta);
        var aTilde = ParseScalarVector(argument.ATilde, "aTilde");
        var bTilde = ParseScalarVector(argument.BTilde, "bTilde");
        var rTilde = ParseScalar(argument.RTilde, "rTilde");
        var sTilde = ParseScalar(argument.STilde, "sTilde");
        var n = aTilde.Length;

        if (n < 2 || bTilde.Length != n)
        {
            return "response vector dimensions are invalid";
        }

        var challenge = SingleValueChallenge(statement, parsed, cCapitalDelta, cDelta, cD, product, commitmentA);
        var prodCa = Add(ScalarMul(commitmentA, challenge), cD);
        var commA = Commit(aTilde, rTilde, parsed.CommitmentKey);
        if (!prodCa.Equals(commA))
        {
            return "commitment opening response A does not match";
        }

        var e = Enumerable.Range(0, n - 1)
            .Select(index => AddScalars(
                MulScalars(challenge, bTilde[index + 1]),
                NegateScalar(MulScalars(bTilde[index], aTilde[index + 1]))))
            .ToArray();
        var prodDelta = Add(ScalarMul(cCapitalDelta, challenge), cDelta);
        var commDelta = Commit(e, sTilde, parsed.CommitmentKey);
        if (!prodDelta.Equals(commDelta))
        {
            return "Delta relation response does not match";
        }

        if (bTilde[0] != aTilde[0])
        {
            return "prefix product first response does not match";
        }

        if (bTilde[n - 1] != MulScalars(challenge, product))
        {
            return "prefix product final response does not match";
        }

        return null;
    }

    private static Sp07MultiExponentiationArgumentPayload GenerateMultiExponentiationArgument(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        ParsedWitness witness,
        IReadOnlyList<Point> outerCommitmentsB,
        IReadOnlyList<BigInteger> bVector,
        BigInteger outerCommitmentRandomness,
        BigInteger outerX,
        DeterministicScalarSource source,
        ProofProfiler profiler)
    {
        var slotCount = parsed.SlotCount;
        var rhoBar = profiler.Measure("proof.mexp.rho_bar", () =>
        {
            var result = new BigInteger[slotCount];
            for (var publishedIndex = 0; publishedIndex < parsed.BallotCount; publishedIndex++)
            {
                for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
                {
                    result[slotIndex] = AddScalars(
                        result[slotIndex],
                        NegateScalar(MulScalars(witness.Rerandomization[publishedIndex][slotIndex], bVector[publishedIndex])));
                }
            }

            return result;
        });

        var acceptedAggregate = profiler.Measure(
            "proof.mexp.accepted_aggregate",
            () => CipherVectorExponentiation(parsed.AcceptedBallots, ScalarPowers(outerX, parsed.BallotCount)));
        var a0 = profiler.Measure(
            "proof.mexp.random_vectors",
            () => Enumerable.Range(0, parsed.BallotCount).Select(index => source.Next($"mexp-a0-{index}")).ToArray());
        var r0 = source.Next("mexp-r0");
        var b0 = source.Next("mexp-b0");
        var s0 = source.Next("mexp-s0");
        var tau0 = Enumerable.Range(0, slotCount).Select(index => source.Next($"mexp-tau0-{index}")).ToArray();
        var cA0 = profiler.Measure("proof.mexp.commit_a0", () => Commit(a0, r0, parsed.CommitmentKey));
        var d0 = profiler.Measure("proof.mexp.d0_published_vector_exp", () => CipherVectorExponentiation(parsed.PublishedBallots, a0));
        var d1 = profiler.Measure("proof.mexp.d1_published_vector_exp", () => CipherVectorExponentiation(parsed.PublishedBallots, bVector));
        var cB0 = profiler.Measure("proof.mexp.commit_b0", () => Commit([b0], s0, parsed.CommitmentKey));
        var cB1 = Commit([BigInteger.Zero], BigInteger.Zero, parsed.CommitmentKey);
        var e0 = profiler.Measure(
            "proof.mexp.e0_encrypt_and_add",
            () => CipherAdd(EncryptConstantMessage(parsed.ElectionPublicKeyTable, b0, tau0, slotCount), d0));
        var e1 = profiler.Measure(
            "proof.mexp.e1_encrypt_and_add",
            () => CipherAdd(EncryptConstantMessage(parsed.ElectionPublicKeyTable, BigInteger.Zero, rhoBar, slotCount), d1));

        if (!CipherEquals(e1, acceptedAggregate))
        {
            throw new InvalidOperationException("Internal SP-07 multi-exponentiation relation did not bind to the accepted aggregate.");
        }

        var challenge = profiler.Measure(
            "proof.mexp.challenge",
            () => MultiExponentiationChallenge(
                statement,
                parsed,
                acceptedAggregate,
                outerCommitmentsB,
                cA0,
                [cB0, cB1],
                [e0, e1]));
        var a = profiler.Measure(
            "proof.mexp.responses",
            () => a0.Select((value, index) => AddScalars(value, MulScalars(challenge, bVector[index]))).ToArray());
        var r = AddScalars(r0, MulScalars(challenge, outerCommitmentRandomness));
        var tau = tau0.Select((value, index) => AddScalars(value, MulScalars(challenge, rhoBar[index]))).ToArray();

        return new Sp07MultiExponentiationArgumentPayload(
            ToPayload(cA0),
            [ToPayload(cB0), ToPayload(cB1)],
            [ToPayload(e0), ToPayload(e1)],
            a.Select(ToScalarString).ToArray(),
            ToScalarString(r),
            ToScalarString(b0),
            ToScalarString(s0),
            tau.Select(ToScalarString).ToArray());
    }

    private static bool VerifyMultiExponentiationArgument(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        ParsedMultiExponentiationArgument argument,
        IReadOnlyList<Point> outerCommitmentsB,
        CipherBallot acceptedAggregate)
    {
        if (argument.CommitmentB.Length != 2 ||
            argument.DiagonalCiphertexts.Length != 2 ||
            argument.A.Length != parsed.BallotCount ||
            argument.TauBySlot.Length != parsed.SlotCount)
        {
            return false;
        }

        if (!argument.CommitmentB[1].Equals(Identity))
        {
            return false;
        }

        if (!CipherEquals(argument.DiagonalCiphertexts[1], acceptedAggregate))
        {
            return false;
        }

        var challenge = MultiExponentiationChallenge(
            statement,
            parsed,
            acceptedAggregate,
            outerCommitmentsB,
            argument.CommitmentA0,
            argument.CommitmentB,
            argument.DiagonalCiphertexts);
        var prodCa = Add(argument.CommitmentA0, ScalarMul(outerCommitmentsB[0], challenge));
        var commA = Commit(argument.A, argument.R, parsed.CommitmentKey);
        if (!prodCa.Equals(commA))
        {
            return false;
        }

        var prodCb = Add(argument.CommitmentB[0], ScalarMul(argument.CommitmentB[1], challenge));
        var commB = Commit([argument.B], argument.S, parsed.CommitmentKey);
        if (!prodCb.Equals(commB))
        {
            return false;
        }

        var prodE = CipherAdd(argument.DiagonalCiphertexts[0], CipherScalarMul(argument.DiagonalCiphertexts[1], challenge));
        var encryptedGb = EncryptConstantMessage(parsed.ElectionPublicKeyTable, argument.B, argument.TauBySlot, parsed.SlotCount);
        var prodC = CipherVectorExponentiation(parsed.PublishedBallots, argument.A);
        return CipherEquals(prodE, CipherAdd(encryptedGb, prodC));
    }

    private static BigInteger OuterChallengeX(Sp07PublicationProofStatement statement, IReadOnlyList<Point> cA) =>
        Challenge("SP07_OUTER_X_V1", new
        {
            p = FieldPrime.ToString(CultureInfo.InvariantCulture),
            q = Order.ToString(CultureInfo.InvariantCulture),
            statement.ElectionId,
            statement.BallotDefinitionHash,
            statement.GroupProfile,
            statement.ElectionPublicKey,
            statement.CommitmentKey,
            statement.AcceptedBallots,
            statement.PublishedBallots,
            cA = cA.Select(ToPayload).ToArray()
        });

    private static BigInteger OuterChallengeY(
        Sp07PublicationProofStatement statement,
        IReadOnlyList<Point> cA,
        IReadOnlyList<Point> cB) =>
        Challenge("SP07_OUTER_Y_V1", new
        {
            cB = cB.Select(ToPayload).ToArray(),
            p = FieldPrime.ToString(CultureInfo.InvariantCulture),
            q = Order.ToString(CultureInfo.InvariantCulture),
            statement.ElectionId,
            statement.BallotDefinitionHash,
            statement.GroupProfile,
            statement.ElectionPublicKey,
            statement.CommitmentKey,
            statement.AcceptedBallots,
            statement.PublishedBallots,
            cA = cA.Select(ToPayload).ToArray()
        });

    private static BigInteger OuterChallengeZ(
        Sp07PublicationProofStatement statement,
        IReadOnlyList<Point> cA,
        IReadOnlyList<Point> cB) =>
        Challenge("SP07_OUTER_Z_V1", new
        {
            cB = cB.Select(ToPayload).ToArray(),
            p = FieldPrime.ToString(CultureInfo.InvariantCulture),
            q = Order.ToString(CultureInfo.InvariantCulture),
            statement.ElectionId,
            statement.BallotDefinitionHash,
            statement.GroupProfile,
            statement.ElectionPublicKey,
            statement.CommitmentKey,
            statement.AcceptedBallots,
            statement.PublishedBallots,
            cA = cA.Select(ToPayload).ToArray()
        });

    private static BigInteger SingleValueChallenge(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        Point cCapitalDelta,
        Point cDelta,
        Point cD,
        BigInteger product,
        Point commitmentA) =>
        Challenge("SP07_SINGLE_VALUE_PRODUCT_X_V1", new
        {
            p = FieldPrime.ToString(CultureInfo.InvariantCulture),
            q = Order.ToString(CultureInfo.InvariantCulture),
            statement.ElectionId,
            statement.ElectionPublicKey,
            statement.CommitmentKey,
            cCapitalDelta = ToPayload(cCapitalDelta),
            cDelta = ToPayload(cDelta),
            cD = ToPayload(cD),
            product = ToScalarString(product),
            commitmentA = ToPayload(commitmentA),
            matrixN = parsed.BallotCount
        });

    private static BigInteger MultiExponentiationChallenge(
        Sp07PublicationProofStatement statement,
        ParsedStatement parsed,
        CipherBallot acceptedAggregate,
        IReadOnlyList<Point> outerCommitmentsB,
        Point commitmentA0,
        IReadOnlyList<Point> commitmentB,
        IReadOnlyList<CipherBallot> diagonalCiphertexts) =>
        Challenge("SP07_MULTI_EXPONENTIATION_X_V1", new
        {
            p = FieldPrime.ToString(CultureInfo.InvariantCulture),
            q = Order.ToString(CultureInfo.InvariantCulture),
            statement.ElectionId,
            statement.ElectionPublicKey,
            statement.CommitmentKey,
            publishedMatrix = statement.PublishedBallots,
            acceptedAggregate = ToPayload(acceptedAggregate),
            outerCommitmentsB = outerCommitmentsB.Select(ToPayload).ToArray(),
            commitmentA0 = ToPayload(commitmentA0),
            commitmentB = commitmentB.Select(ToPayload).ToArray(),
            diagonalCiphertexts = diagonalCiphertexts.Select(ToPayload).ToArray(),
            matrixN = parsed.BallotCount,
            slotCount = parsed.SlotCount
        });

    private static void EnsureWitnessRelation(ParsedStatement statement, ParsedWitness witness)
    {
        var seen = new HashSet<int>();
        for (var publishedIndex = 0; publishedIndex < statement.BallotCount; publishedIndex++)
        {
            var acceptedIndex = witness.PublishedToAccepted[publishedIndex];
            if (acceptedIndex < 0 || acceptedIndex >= statement.BallotCount || !seen.Add(acceptedIndex))
            {
                throw new InvalidOperationException("SP-07 witness mapping is not a valid permutation.");
            }
        }

        var failed = 0;
        Parallel.For(0, statement.BallotCount, (publishedIndex, loopState) =>
        {
            if (Volatile.Read(ref failed) != 0)
            {
                loopState.Stop();
                return;
            }

            var acceptedIndex = witness.PublishedToAccepted[publishedIndex];
            for (var slotIndex = 0; slotIndex < statement.SlotCount; slotIndex++)
            {
                var source = statement.AcceptedBallots[acceptedIndex].Slots[slotIndex];
                var rho = witness.Rerandomization[publishedIndex][slotIndex];
                var expected = new CipherSlot(
                    ToAffine(AddProjective(ToProjective(source.C1), ScalarMulProjective(GeneratorMulTable, rho))),
                    ToAffine(AddProjective(ToProjective(source.C2), ScalarMulProjective(statement.ElectionPublicKeyTable, rho))));
                var actual = statement.PublishedBallots[publishedIndex].Slots[slotIndex];
                if (!expected.Equals(actual))
                {
                    Interlocked.Exchange(ref failed, 1);
                    loopState.Stop();
                    return;
                }
            }
        });

        if (failed != 0)
        {
            throw new InvalidOperationException("SP-07 witness does not rerandomize the accepted set into the published stream.");
        }
    }

    private static Point Commit(IReadOnlyList<BigInteger> values, BigInteger randomness, CommitmentKey commitmentKey)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("Commitment requires at least one value.", nameof(values));
        }

        if (commitmentKey.G.Length < values.Count)
        {
            throw new ArgumentException("Commitment key does not contain enough generators.", nameof(commitmentKey));
        }

        var result = ScalarMulProjective(commitmentKey.HTable, randomness);
        if (values.Count >= ParallelCommitValueThreshold)
        {
            var workerCount = Math.Min(Environment.ProcessorCount, values.Count);
            var partials = new ProjectivePoint[workerCount];
            Parallel.For(
                0,
                workerCount,
                new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                workerIndex =>
                {
                    var start = workerIndex * values.Count / workerCount;
                    var end = (workerIndex + 1) * values.Count / workerCount;
                    var partial = ProjectiveIdentity;
                    for (var index = start; index < end; index++)
                    {
                        var value = NormalizeScalar(values[index]);
                        if (value == BigInteger.Zero)
                        {
                            continue;
                        }

                        partial = AddProjective(partial, ScalarMulProjective(commitmentKey.GTables[index], value));
                    }

                    partials[workerIndex] = partial;
                });

            foreach (var partial in partials)
            {
                result = AddProjective(result, partial);
            }

            return ToAffine(result);
        }

        for (var index = 0; index < values.Count; index++)
        {
            var value = NormalizeScalar(values[index]);
            if (value == BigInteger.Zero)
            {
                continue;
            }

            result = AddProjective(result, ScalarMulProjective(commitmentKey.GTables[index], value));
        }

        return ToAffine(result);
    }

    private static CipherBallot CipherVectorExponentiation(
        IReadOnlyList<CipherBallot> ballots,
        IReadOnlyList<BigInteger> exponents)
    {
        if (ballots.Count == 0 || ballots.Count != exponents.Count)
        {
            throw new ArgumentException("Ciphertext vector and exponent vector dimensions differ.", nameof(ballots));
        }

        var slotCount = ballots[0].Slots.Length;
        var slots = new CipherSlot[slotCount];
        var normalizedExponents = exponents.Select(NormalizeScalar).ToArray();
        var useParallelSlots = slotCount >= ParallelSlotThreshold && ballots.Count >= ParallelBallotThreshold;

        void BuildSlot(int slotIndex)
        {
            var c1 = ProjectiveIdentity;
            var c2 = ProjectiveIdentity;
            for (var index = 0; index < ballots.Count; index++)
            {
                var scalar = normalizedExponents[index];
                if (scalar == BigInteger.Zero)
                {
                    continue;
                }

                c1 = AddProjective(
                    c1,
                    ScalarMulProjective(ballots[index].Slots[slotIndex].C1, scalar));
                c2 = AddProjective(
                    c2,
                    ScalarMulProjective(ballots[index].Slots[slotIndex].C2, scalar));
            }

            slots[slotIndex] = new CipherSlot(ToAffine(c1), ToAffine(c2));
        }

        if (useParallelSlots)
        {
            Parallel.For(
                0,
                slotCount,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(slotCount, Environment.ProcessorCount) },
                BuildSlot);
        }
        else
        {
            for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                BuildSlot(slotIndex);
            }
        }

        return new CipherBallot(slots);
    }

    private static CipherBallot EncryptConstantMessage(
        ScalarMulTable publicKeyTable,
        BigInteger messageScalar,
        IReadOnlyList<BigInteger> slotRandomness,
        int slotCount)
    {
        if (slotRandomness.Count != slotCount)
        {
            throw new ArgumentException("Slot randomness dimension differs from ciphertext slot count.", nameof(slotRandomness));
        }

        var message = ScalarMulProjective(GeneratorMulTable, messageScalar);
        var slots = new CipherSlot[slotCount];
        for (var slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            slots[slotIndex] = new CipherSlot(
                ToAffine(ScalarMulProjective(GeneratorMulTable, slotRandomness[slotIndex])),
                ToAffine(AddProjective(message, ScalarMulProjective(publicKeyTable, slotRandomness[slotIndex]))));
        }

        return new CipherBallot(slots);
    }

    private static CipherSlot EncryptZero(Point publicKey, BigInteger randomness) =>
        new(ScalarMul(Generator, randomness), ScalarMul(publicKey, randomness));

    private static CipherBallot CipherNeutral(int slotCount) =>
        new(Enumerable.Range(0, slotCount).Select(_ => new CipherSlot(Identity, Identity)).ToArray());

    private static CipherBallot CipherAdd(CipherBallot left, CipherBallot right)
    {
        if (left.Slots.Length != right.Slots.Length)
        {
            throw new ArgumentException("Ciphertext slot dimensions differ.", nameof(right));
        }

        return new CipherBallot(left.Slots
            .Select((slot, index) => CipherSlotAdd(slot, right.Slots[index]))
            .ToArray());
    }

    private static CipherSlot CipherSlotAdd(CipherSlot left, CipherSlot right) =>
        new(Add(left.C1, right.C1), Add(left.C2, right.C2));

    private static CipherBallot CipherScalarMul(CipherBallot ballot, BigInteger scalar)
    {
        scalar = NormalizeScalar(scalar);
        if (scalar == BigInteger.Zero)
        {
            return CipherNeutral(ballot.Slots.Length);
        }

        var slots = new CipherSlot[ballot.Slots.Length];
        var useParallelSlots = ballot.Slots.Length >= ParallelSlotThreshold;
        void BuildSlot(int slotIndex)
        {
            var slot = ballot.Slots[slotIndex];
            slots[slotIndex] = new CipherSlot(
                ToAffine(ScalarMulProjective(slot.C1, scalar)),
                ToAffine(ScalarMulProjective(slot.C2, scalar)));
        }

        if (useParallelSlots)
        {
            Parallel.For(
                0,
                ballot.Slots.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(ballot.Slots.Length, Environment.ProcessorCount) },
                BuildSlot);
        }
        else
        {
            for (var slotIndex = 0; slotIndex < ballot.Slots.Length; slotIndex++)
            {
                BuildSlot(slotIndex);
            }
        }

        return new CipherBallot(slots);
    }

    private static bool CipherEquals(CipherBallot left, CipherBallot right) =>
        left.Slots.Length == right.Slots.Length &&
        left.Slots.Zip(right.Slots).All(pair => pair.First.Equals(pair.Second));

    private static (string ProofBytes, string ProofHash) EncodeProof(Sp07PublicationProofPayload proof)
    {
        var json = JsonSerializer.Serialize(proof, JsonOptions);
        return (
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
            Sha256Lower(Encoding.UTF8.GetBytes(json)));
    }

    private static BigInteger Challenge(string label, object payload) =>
        NormalizeScalar(new BigInteger(
            SHA512.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { label, payload }, JsonOptions))),
            isUnsigned: true,
            isBigEndian: true));

    private static string HashJson(object value) =>
        Sha256Lower(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions)));

    private static string Sha256Lower(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static BigInteger[] ParseScalarVector(IReadOnlyList<string> values, string label) =>
        values.Select((value, index) => ParseScalar(value, $"{label}[{index}]")).ToArray();

    private static BigInteger ParseScalar(string value, string label)
    {
        if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var scalar))
        {
            throw new FormatException($"Scalar '{label}' is not a base-10 integer.");
        }

        return NormalizeScalar(scalar);
    }

    private static string ToScalarString(BigInteger scalar) =>
        NormalizeScalar(scalar).ToString(CultureInfo.InvariantCulture);

    private static BigInteger NormalizeScalar(BigInteger value)
    {
        var result = value % Order;
        return result < 0 ? result + Order : result;
    }

    private static BigInteger AddScalars(BigInteger left, BigInteger right) =>
        NormalizeScalar(left + right);

    private static BigInteger MulScalars(BigInteger left, BigInteger right) =>
        NormalizeScalar(left * right);

    private static BigInteger NegateScalar(BigInteger value) =>
        NormalizeScalar(-value);

    private static BigInteger[] ScalarPowers(BigInteger value, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Power count cannot be negative.");
        }

        var normalized = NormalizeScalar(value);
        var powers = new BigInteger[count];
        var running = BigInteger.One;
        for (var index = 0; index < count; index++)
        {
            powers[index] = running;
            running = MulScalars(running, normalized);
        }

        return powers;
    }

    private static Sp07PointPayload ToPayload(Point point) =>
        new(point.X.ToString(CultureInfo.InvariantCulture), point.Y.ToString(CultureInfo.InvariantCulture));

    private static Sp07CipherBallotPayload ToPayload(CipherBallot ballot) =>
        new(ballot.Slots.Select(slot => new Sp07CipherSlotPayload(ToPayload(slot.C1), ToPayload(slot.C2))).ToArray());

    private static Point ParsePoint(Sp07PointPayload point)
    {
        var parsed = new Point(
            BigInteger.Parse(point.X, CultureInfo.InvariantCulture),
            BigInteger.Parse(point.Y, CultureInfo.InvariantCulture));
        if (!IsOnCurve(parsed))
        {
            throw new ArgumentException("Point is not on BabyJubJub.");
        }

        return parsed;
    }

    private static Point Add(Point left, Point right)
    {
        if (left.Equals(Identity)) return right;
        if (right.Equals(Identity)) return left;

        return ToAffine(AddProjective(ToProjective(left), ToProjective(right)));
    }

    private static Point ScalarMul(Point point, BigInteger scalar)
    {
        scalar = NormalizeScalar(scalar);
        if (scalar == BigInteger.Zero)
        {
            return Identity;
        }

        var result = ScalarMulProjective(point, scalar);

        return ToAffine(result);
    }

    private static ProjectivePoint ScalarMulProjective(Point point, BigInteger scalar)
    {
        scalar = NormalizeScalar(scalar);
        return scalar == BigInteger.Zero
            ? ProjectiveIdentity
            : scalar.GetBitLength() <= SmallScalarBitLength
                ? ScalarMulProjectiveDoubleAndAdd(ToProjective(point), scalar)
                : ScalarMulProjectiveWindowed(ToProjective(point), scalar);
    }

    private static ProjectivePoint ScalarMulProjective(ScalarMulTable table, BigInteger scalar)
    {
        scalar = NormalizeScalar(scalar);
        return scalar == BigInteger.Zero
            ? ProjectiveIdentity
            : scalar.GetBitLength() <= SmallScalarBitLength
                ? ScalarMulProjectiveDoubleAndAdd(table.Point, scalar)
                : ScalarMulProjectiveWindowed(table.WindowTable, scalar);
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
        return ScalarMulProjectiveWindowed(table, scalar);
    }

    private static ProjectivePoint ScalarMulProjectiveWindowed(IReadOnlyList<ProjectivePoint> table, BigInteger scalar)
    {
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

    private static ScalarMulTable BuildScalarMulTable(Point point) =>
        new(ToProjective(point), BuildWindowTable(ToProjective(point)));

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

    private static ProjectivePoint ToProjective(Point point) =>
        new(point.X, point.Y, BigInteger.One);

    private static Point ToAffine(ProjectivePoint point)
    {
        if (point.Z == BigInteger.Zero)
        {
            throw new InvalidOperationException("Projective BabyJubJub point has zero Z coordinate.");
        }

        var inverseZ = ModInverse(point.Z);
        return new Point(Mod(point.X * inverseZ), Mod(point.Y * inverseZ));
    }

    private static ProjectivePoint AddProjective(ProjectivePoint left, ProjectivePoint right)
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

    private static bool IsOnCurve(Point point)
    {
        if (point.X < 0 || point.X >= FieldPrime || point.Y < 0 || point.Y >= FieldPrime)
        {
            return false;
        }

        var x2 = Mod(point.X * point.X);
        var y2 = Mod(point.Y * point.Y);
        return Mod(Mod(A * x2) + y2) == Mod(1 + Mod(Mod(D * x2) * y2));
    }

    private static BigInteger Mod(BigInteger value)
    {
        var result = value % FieldPrime;
        return result < 0 ? result + FieldPrime : result;
    }

    private static BigInteger ModInverse(BigInteger value)
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

    private static Sp07ProofVerificationResult Fail(string code, string message, string? proofHash = null) =>
        new(false, code, message, proofHash);

    private sealed class ProofProfiler
    {
        public static readonly ProofProfiler Disabled = new(enabled: false);

        private readonly List<Sp07ProofTimingRecord> _timings = [];
        private readonly bool _enabled;

        public ProofProfiler(bool enabled = true)
        {
            _enabled = enabled;
        }

        public void Add(string name, TimeSpan elapsed) =>
            _timings.Add(new Sp07ProofTimingRecord(name, elapsed.TotalMilliseconds));

        public Sp07ProofGenerationProfile ToProfile() =>
            new(_timings.ToArray());

        public T Measure<T>(string name, Func<T> operation)
        {
            if (!_enabled)
            {
                return operation();
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                return operation();
            }
            finally
            {
                stopwatch.Stop();
                Add(name, stopwatch.Elapsed);
            }
        }

        public void Measure(string name, Action operation)
        {
            if (!_enabled)
            {
                operation();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                operation();
            }
            finally
            {
                stopwatch.Stop();
                Add(name, stopwatch.Elapsed);
            }
        }
    }

    private static readonly BigInteger A = BigInteger.Parse("168700", CultureInfo.InvariantCulture);
    private static readonly BigInteger D = BigInteger.Parse("168696", CultureInfo.InvariantCulture);
    private static readonly BigInteger FieldPrime = BigInteger.Parse(
        "21888242871839275222246405745257275088548364400416034343698204186575808495617",
        CultureInfo.InvariantCulture);
    private static readonly BigInteger Order = BigInteger.Parse(
        "2736030358979909402780800718157159386076813972158567259200215660948447373041",
        CultureInfo.InvariantCulture);
    private const int ScalarWindowBits = 4;
    private const int SmallScalarBitLength = 32;
    private const int ParallelSlotThreshold = 2;
    private const int ParallelBallotThreshold = 8;
    private const int ParallelCommitValueThreshold = 24;
    private static readonly Point Identity = new(BigInteger.Zero, BigInteger.One);
    private static readonly ProjectivePoint ProjectiveIdentity = new(BigInteger.Zero, BigInteger.One, BigInteger.One);
    private static readonly Point Generator = new(
        BigInteger.Parse("5299619240641551281634865583518297030282874472190772894086521144482721001553", CultureInfo.InvariantCulture),
        BigInteger.Parse("16950150798460657717958625567821834550301663161624707787222815936182638968203", CultureInfo.InvariantCulture));
    private static readonly ScalarMulTable GeneratorMulTable = BuildScalarMulTable(Generator);

    private sealed record Point(BigInteger X, BigInteger Y);

    private sealed record ProjectivePoint(BigInteger X, BigInteger Y, BigInteger Z);

    private sealed record ScalarMulTable(ProjectivePoint Point, IReadOnlyList<ProjectivePoint> WindowTable);

    private sealed record CipherSlot(Point C1, Point C2);

    private sealed record CipherBallot(CipherSlot[] Slots);

    private sealed record CommitmentKey(
        Point H,
        Point[] G,
        ScalarMulTable HTable,
        ScalarMulTable[] GTables);

    private sealed record OuterArgumentState(
        IReadOnlyList<Point> CommitmentsA,
        IReadOnlyList<Point> CommitmentsB,
        Sp07ProductArgumentPayload ProductArgument,
        Sp07MultiExponentiationArgumentPayload MultiExponentiationArgument);

    private sealed class DeterministicScalarSource(string seed)
    {
        private int _counter;

        public BigInteger Next(string label)
        {
            while (true)
            {
                var bytes = SHA512.HashData(Encoding.UTF8.GetBytes($"{seed}|{label}|{_counter++}"));
                var value = NormalizeScalar(new BigInteger(bytes, isUnsigned: true, isBigEndian: true));
                if (value != BigInteger.Zero)
                {
                    return value;
                }
            }
        }
    }

    private sealed record ParsedStatement(
        string ElectionId,
        Point ElectionPublicKey,
        ScalarMulTable ElectionPublicKeyTable,
        CommitmentKey CommitmentKey,
        CipherBallot[] AcceptedBallots,
        CipherBallot[] PublishedBallots,
        int MatrixM,
        int MatrixN,
        int BallotCount,
        int SlotCount)
    {
        public static ParsedStatement Create(Sp07PublicationProofStatement statement)
        {
            if (statement.MatrixM != 1)
            {
                throw new NotSupportedException("Only matrix m=1 is supported by this SP-07 proof library slice.");
            }

            if (statement.AcceptedBallots.Count < 2 ||
                statement.AcceptedBallots.Count != statement.PublishedBallots.Count ||
                statement.MatrixN != statement.AcceptedBallots.Count)
            {
                throw new ArgumentException("SP-07 statement ballot dimensions are invalid.", nameof(statement));
            }

            var accepted = statement.AcceptedBallots.Select(ParseBallot).ToArray();
            var published = statement.PublishedBallots.Select(ParseBallot).ToArray();
            var slotCount = accepted[0].Slots.Length;
            if (slotCount == 0 ||
                accepted.Any(ballot => ballot.Slots.Length != slotCount) ||
                published.Any(ballot => ballot.Slots.Length != slotCount))
            {
                throw new ArgumentException("SP-07 statement slot dimensions are invalid.", nameof(statement));
            }

            var commitmentH = ParsePoint(statement.CommitmentKey.H);
            var commitmentG = statement.CommitmentKey.G.Select(ParsePoint).ToArray();
            var key = new CommitmentKey(
                commitmentH,
                commitmentG,
                BuildScalarMulTable(commitmentH),
                commitmentG.Select(BuildScalarMulTable).ToArray());
            if (key.G.Length < statement.MatrixN)
            {
                throw new ArgumentException("SP-07 commitment key is shorter than matrix n.", nameof(statement));
            }

            var electionPublicKey = ParsePoint(statement.ElectionPublicKey);

            return new ParsedStatement(
                statement.ElectionId,
                electionPublicKey,
                BuildScalarMulTable(electionPublicKey),
                key,
                accepted,
                published,
                statement.MatrixM,
                statement.MatrixN,
                accepted.Length,
                slotCount);
        }

        private static CipherBallot ParseBallot(Sp07CipherBallotPayload payload) =>
            new(payload.Slots
                .Select(slot => new CipherSlot(ParsePoint(slot.C1), ParsePoint(slot.C2)))
                .ToArray());
    }

    private sealed record ParsedWitness(int[] PublishedToAccepted, BigInteger[][] Rerandomization)
    {
        public static ParsedWitness Create(Sp07PublicationProofWitness witness, int ballotCount, int slotCount)
        {
            if (witness.PublishedToAccepted.Count != ballotCount ||
                witness.RerandomizationByPublishedBallotAndSlot.Count != ballotCount)
            {
                throw new ArgumentException("SP-07 witness dimensions do not match ballot count.", nameof(witness));
            }

            return new ParsedWitness(
                witness.PublishedToAccepted.ToArray(),
                witness.RerandomizationByPublishedBallotAndSlot
                    .Select((row, rowIndex) =>
                    {
                        if (row.Count != slotCount)
                        {
                            throw new ArgumentException($"SP-07 witness row {rowIndex} does not match slot count.", nameof(witness));
                        }

                        return row.Select((value, slotIndex) => ParseScalar(value, $"witness[{rowIndex}][{slotIndex}]")).ToArray();
                    })
                    .ToArray());
        }
    }

    private sealed record ParsedProof(
        Point[] OuterCommitmentsA,
        Point[] OuterCommitmentsB,
        Sp07ProductArgumentPayload ProductArgument,
        ParsedMultiExponentiationArgument MultiExponentiationArgument)
    {
        public static ParsedProof Create(Sp07PublicationProofPayload proof, int ballotCount, int slotCount)
        {
            var cA = proof.OuterCommitmentsA.Select(ParsePoint).ToArray();
            var cB = proof.OuterCommitmentsB.Select(ParsePoint).ToArray();
            if (cA.Length != 1 || cB.Length != 1)
            {
                throw new ArgumentException("SP-07 m=1 proof must contain exactly one outer cA and cB commitment.", nameof(proof));
            }

            return new ParsedProof(
                cA,
                cB,
                proof.ProductArgument,
                ParsedMultiExponentiationArgument.Create(proof.MultiExponentiationArgument, ballotCount, slotCount));
        }
    }

    private sealed record ParsedMultiExponentiationArgument(
        Point CommitmentA0,
        Point[] CommitmentB,
        CipherBallot[] DiagonalCiphertexts,
        BigInteger[] A,
        BigInteger R,
        BigInteger B,
        BigInteger S,
        BigInteger[] TauBySlot)
    {
        public static ParsedMultiExponentiationArgument Create(
            Sp07MultiExponentiationArgumentPayload argument,
            int ballotCount,
            int slotCount)
        {
            var a = ParseScalarVector(argument.A, "multiExponentiation.a");
            var tau = ParseScalarVector(argument.TauBySlot, "multiExponentiation.tauBySlot");
            if (a.Length != ballotCount || tau.Length != slotCount)
            {
                throw new ArgumentException("SP-07 multi-exponentiation response dimensions are invalid.", nameof(argument));
            }

            return new ParsedMultiExponentiationArgument(
                ParsePoint(argument.CommitmentA0),
                argument.CommitmentB.Select(ParsePoint).ToArray(),
                argument.DiagonalCiphertexts.Select(payload => new CipherBallot(payload.Slots
                    .Select(slot => new CipherSlot(ParsePoint(slot.C1), ParsePoint(slot.C2)))
                    .ToArray())).ToArray(),
                a,
                ParseScalar(argument.R, "multiExponentiation.r"),
                ParseScalar(argument.B, "multiExponentiation.b"),
                ParseScalar(argument.S, "multiExponentiation.s"),
                tau);
        }
    }
}
