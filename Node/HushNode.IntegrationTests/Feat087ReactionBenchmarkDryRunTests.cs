using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Google.Protobuf;
using HushNetwork.proto;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.Reactions;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using ReactionEcPoint = HushShared.Reactions.Model.ECPoint;
using HushServerNode;
using HushServerNode.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-087 sequential reaction benchmark scaffold.
///
/// IMPORTANT:
/// - This benchmark intentionally avoids Playwright overhead.
/// - The current submitter uses the existing dev-mode reaction transaction path that
///   the integration host supports today.
/// - That makes this class useful for workload orchestration and timing mechanics,
///   but not yet valid as final FEAT-087 privacy/throughput evidence.
///
/// The non-dev submitter uses the real headless proof CLI when the proof path is ready.
/// </summary>
[Collection("Integration Tests")]
[Trait("Category", "PerformanceTest")]
[Trait("Category", "Benchmark")]
[Trait("Category", "FEAT-087")]
public sealed class Feat087ReactionBenchmarkDryRunTests : IAsyncLifetime
{
    private const int LargeVoterCount = 10000;
    private const int VoterCount = 1000;
    private const int MediumVoterCount = 100;
    private const int SmokeVoterCount = 10;
    private const int WorkloadSeed = 8701000;
    private static readonly TimeSpan VisibilitySla = TimeSpan.FromSeconds(3);
    private const string PublicPostContent = "FEAT-087 benchmark public post";

    private readonly ITestOutputHelper _output;

    private HushTestFixture? _fixture;
    private HushServerNodeCore? _node;
    private BlockProductionControl? _blockControl;
    private GrpcClientFactory? _grpcFactory;
    private IReactionSubmitter? _submitter;

    public Feat087ReactionBenchmarkDryRunTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
        await _fixture.ResetAllAsync();
    }

    public async Task DisposeAsync()
    {
        _grpcFactory?.Dispose();

        if (_node != null)
        {
            await _node.DisposeAsync();
        }

        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_1000Voters_OneVotePerBlock_DevModeDryRun()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(VoterCount),
            "feat087-reaction-benchmark-dry-run",
            Feat087ReactionProofMode.DevMode);
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_10Voters_OneVotePerBlock_DevModeDryRun_Smoke()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(SmokeVoterCount),
            "feat087-reaction-benchmark-smoke",
            Feat087ReactionProofMode.DevMode);
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_100Voters_OneVotePerBlock_DevModeDryRun()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(MediumVoterCount),
            "feat087-reaction-benchmark-medium",
            Feat087ReactionProofMode.DevMode);
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_10000Voters_OneVotePerBlock_DevModeDryRun()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(LargeVoterCount),
            "feat087-reaction-benchmark-large",
            Feat087ReactionProofMode.DevMode);
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_1Voter_OneVotePerBlock_NonDevMode_Smoke()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(1),
            "feat087-reaction-benchmark-non-dev-1-voter",
            Feat087ReactionProofMode.NonDev);
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_10Voters_OneVotePerBlock_NonDevMode()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(SmokeVoterCount),
            "feat087-reaction-benchmark-non-dev-smoke",
            Feat087ReactionProofMode.NonDev);
    }

    [Fact]
    public async Task Sequential_PublicPost_ReactionVisibility_100Voters_OneVotePerBlock_NonDevMode()
    {
        await RunSequentialBenchmarkAsync(
            CreateWorkload(MediumVoterCount),
            "feat087-reaction-benchmark-non-dev-medium",
            Feat087ReactionProofMode.NonDev);
    }

    private async Task RunSequentialBenchmarkAsync(
        Feat087ReactionBenchmarkWorkload workload,
        string reportKind,
        Feat087ReactionProofMode proofMode)
    {
        await StartRuntimeAsync(proofMode);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var proofPathReadiness = ReactionProofPathInspector.InspectFromCurrentRuntime(testHostDevModeEnabled: proofMode == Feat087ReactionProofMode.DevMode);
        var owner = TestIdentities.Alice;
        await RegisterUserWithPersonalFeedAsync(owner);

        var postId = await CreateOpenPostAsync(owner, PublicPostContent);
        var measurements = new List<Feat087ReactionBenchmarkMeasurement>(capacity: workload.VoterCount);

        _output.WriteLine($"[FEAT-087 DryRun] Seed={workload.Seed}, voters={workload.VoterCount}, SLA={VisibilitySla.TotalMilliseconds}ms");
        _output.WriteLine($"[FEAT-087 DryRun] PostId={postId:D}");
        _output.WriteLine($"[FEAT-087 DryRun] ProofMode={proofMode}, NonDevReady={proofPathReadiness.NonDevBenchmarkReady}");

        foreach (var voterPlan in workload.Voters)
        {
            var voter = TestIdentities.GenerateFromSeed(
                voterPlan.IdentitySeed,
                voterPlan.DisplayName);

            await RegisterUserWithPersonalFeedAsync(voter);

            var visiblePost = await ResolvePublicPostForUserAsync(voter.PublicSigningAddress, postId);
            visiblePost.PostId.Should().Be(postId.ToString("D"));

            var previousCount = await GetReactionTotalCountAsync(postId);

            var submitStartedAt = DateTimeOffset.UtcNow;
            await _submitter!.SubmitAsync(voter, postId, voterPlan.EmojiIndex);
            var submitCompletedAt = DateTimeOffset.UtcNow;

            await _blockControl!.ProduceBlockAsync();
            var blockProducedAt = DateTimeOffset.UtcNow;

            var tallyVisibleAt = await WaitForReactionCountAsync(postId, previousCount + 1, VisibilitySla);

            var measurement = new Feat087ReactionBenchmarkMeasurement(
                VoterNumber: voterPlan.VoterNumber,
                DisplayName: voterPlan.DisplayName,
                EmojiIndex: voterPlan.EmojiIndex,
                PreviousCount: previousCount,
                ExpectedCount: previousCount + 1,
                SubmitMs: (submitCompletedAt - submitStartedAt).TotalMilliseconds,
                PostBlockVisibilityMs: (tallyVisibleAt - blockProducedAt).TotalMilliseconds,
                EndToEndMs: (tallyVisibleAt - submitStartedAt).TotalMilliseconds);

            measurements.Add(measurement);

            if (voterPlan.VoterNumber == 1 || voterPlan.VoterNumber % 100 == 0 || voterPlan.VoterNumber == workload.VoterCount)
            {
                _output.WriteLine(
                    $"[FEAT-087 DryRun] voter={voterPlan.VoterNumber}/{workload.VoterCount} emoji={voterPlan.EmojiIndex} " +
                    $"submitMs={measurement.SubmitMs:F1} postBlockMs={measurement.PostBlockVisibilityMs:F1} totalMs={measurement.EndToEndMs:F1}");
            }
        }

        var finalCount = await GetReactionTotalCountAsync(postId);
        finalCount.Should().Be(workload.VoterCount, "each voter adds exactly one active reaction in the current sequential-per-block dry-run");

        measurements.Should().OnlyContain(
            x => x.PostBlockVisibilityMs <= VisibilitySla.TotalMilliseconds,
            "each tally update should become visible within the configured post-block SLA");

        var completedAtUtc = DateTimeOffset.UtcNow;
        var summary = BuildSummary(measurements);
        WriteSummary(summary);

        var reportPath = Feat087ReactionBenchmarkReportWriter.Write(new Feat087ReactionBenchmarkReport(
            ReportKind: reportKind,
            SubmitterKind: _submitter!.Kind,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            ProofPathReadiness: proofPathReadiness,
            Workload: workload,
            Summary: summary,
            Measurements: measurements));

        _output.WriteLine($"[FEAT-087 DryRun] Report={reportPath}");
    }

    private async Task StartRuntimeAsync(Feat087ReactionProofMode proofMode)
    {
        await DisposeRuntimeAsync();

        IReadOnlyDictionary<string, string?>? overrides = null;
        if (proofMode == Feat087ReactionProofMode.NonDev)
        {
            overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Reactions:DevMode"] = "false",
                ["Circuits:AllowPlaceholderVerificationKeys"] = "false",
                ["Circuits:AllowIncompleteVerification"] = "false",
            };
        }

        var (node, blockControl, grpcFactory) = await _fixture!.StartNodeAsync(configurationOverrides: overrides);
        _node = node;
        _blockControl = blockControl;
        _grpcFactory = grpcFactory;
        _submitter = proofMode switch
        {
            Feat087ReactionProofMode.DevMode => new DevModeReactionSubmitter(grpcFactory),
            Feat087ReactionProofMode.NonDev => new NonDevReactionSubmitter(
                grpcFactory,
                node.Services,
                ReactionProofPathInspector.InspectFromCurrentRuntime(testHostDevModeEnabled: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(proofMode), proofMode, null),
        };
    }

    private async Task DisposeRuntimeAsync()
    {
        _grpcFactory?.Dispose();
        _grpcFactory = null;

        if (_node != null)
        {
            await _node.DisposeAsync();
            _node = null;
        }

        _blockControl = null;
        _submitter = null;
    }

    private async Task RegisterUserWithPersonalFeedAsync(TestIdentity identity)
    {
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var blockchainClient = _grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var hasPersonalFeed = await feedClient.HasPersonalFeedAsync(new HasPersonalFeedRequest
        {
            PublicPublicKey = identity.PublicSigningAddress
        });

        if (hasPersonalFeed.FeedAvailable)
        {
            return;
        }

        var identityTxJson = TestTransactionFactory.CreateIdentityRegistration(identity);
        var identityResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = identityTxJson
        });
        identityResponse.Successfull.Should().BeTrue($"Identity registration should succeed: {identityResponse.Message}");
        await _blockControl!.ProduceBlockAsync();

        var (personalFeedTxJson, _) = TestTransactionFactory.CreatePersonalFeedWithKey(identity);
        var feedResponse = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = personalFeedTxJson
        });
        feedResponse.Successfull.Should().BeTrue($"Personal feed creation should succeed: {feedResponse.Message}");
        await _blockControl.ProduceBlockAsync();
    }

    private async Task<Guid> CreateOpenPostAsync(TestIdentity author, string content)
    {
        var blockchainClient = _grpcFactory!.CreateClient<HushBlockchain.HushBlockchainClient>();
        var poseidon = _node!.Services.GetRequiredService<IPoseidonHash>();
        var authorCommitment = ToBytes32(poseidon.Hash(DeriveUserSecret(author.PrivateSigningKey)));
        var (signedTransaction, postId) = TestTransactionFactory.CreateSocialPost(
            author,
            content,
            SocialPostVisibility.Open,
            authorCommitment: authorCommitment);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransaction
        });

        response.Successfull.Should().BeTrue($"Social post creation should succeed: {response.Message}");
        await _blockControl!.ProduceBlockAsync();

        var visiblePost = await ResolvePublicPostForUserAsync(author.PublicSigningAddress, postId);
        visiblePost.Content.Should().Be(content);
        return postId;
    }

    private async Task<SocialFeedWallPostProto> ResolvePublicPostForUserAsync(string requesterPublicAddress, Guid postId)
    {
        var feedClient = _grpcFactory!.CreateClient<HushFeed.HushFeedClient>();
        var response = await feedClient.GetSocialFeedWallAsync(new GetSocialFeedWallRequest
        {
            RequesterPublicAddress = requesterPublicAddress,
            IsAuthenticated = true,
            Limit = 200
        });

        response.Success.Should().BeTrue($"GetSocialFeedWall should succeed: {response.Message}");

        var post = response.Posts.FirstOrDefault(x => string.Equals(x.PostId, postId.ToString("D"), StringComparison.Ordinal));
        post.Should().NotBeNull($"user '{requesterPublicAddress}' should see public post '{postId:D}' in the social feed wall");
        return post!;
    }

    private async Task<int> GetReactionTotalCountAsync(Guid postId)
    {
        var client = _grpcFactory!.CreateClient<HushReactions.HushReactionsClient>();
        var response = await client.GetReactionTalliesAsync(new GetTalliesRequest
        {
            FeedId = ByteString.CopyFrom(postId.ToByteArray()),
            MessageIds = { ByteString.CopyFrom(postId.ToByteArray()) }
        });

        return response.Tallies.FirstOrDefault()?.TotalCount ?? 0;
    }

    private async Task<DateTimeOffset> WaitForReactionCountAsync(Guid postId, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow <= deadline)
        {
            var current = await GetReactionTotalCountAsync(postId);
            if (current == expectedCount)
            {
                return DateTimeOffset.UtcNow;
            }

            await Task.Delay(100);
        }

        var finalCount = await GetReactionTotalCountAsync(postId);
        finalCount.Should().Be(expectedCount, $"reaction tally should reach {expectedCount} within {timeout.TotalMilliseconds}ms after block production");
        return DateTimeOffset.UtcNow;
    }

    private static Feat087ReactionBenchmarkWorkload CreateWorkload(int voterCount)
    {
        var rng = new Random(WorkloadSeed);
        var voters = new List<Feat087ReactionBenchmarkWorkloadItem>(capacity: voterCount);

        for (var voterNumber = 1; voterNumber <= voterCount; voterNumber++)
        {
            voters.Add(new Feat087ReactionBenchmarkWorkloadItem(
                VoterNumber: voterNumber,
                IdentitySeed: $"FEAT087_BENCH_VOTER_{voterNumber}",
                DisplayName: $"BenchVoter{voterNumber:D4}",
                EmojiIndex: rng.Next(0, 6)));
        }

        return new Feat087ReactionBenchmarkWorkload(
            WorkloadKind: "sequential-public-post-one-vote-per-block",
            Seed: WorkloadSeed,
            VoterCount: voterCount,
            PublicPostContent: PublicPostContent,
            VisibilitySlaMs: VisibilitySla.TotalMilliseconds,
            Voters: voters);
    }

    private static Feat087ReactionBenchmarkSummary BuildSummary(IReadOnlyList<Feat087ReactionBenchmarkMeasurement> measurements)
    {
        var submitSamples = measurements.Select(x => x.SubmitMs).OrderBy(x => x).ToArray();
        var postBlockSamples = measurements.Select(x => x.PostBlockVisibilityMs).OrderBy(x => x).ToArray();
        var endToEndSamples = measurements.Select(x => x.EndToEndMs).OrderBy(x => x).ToArray();

        return new Feat087ReactionBenchmarkSummary(
            VoterCount: measurements.Count,
            SubmitP50Ms: Percentile(submitSamples, 0.50),
            SubmitP95Ms: Percentile(submitSamples, 0.95),
            SubmitMaxMs: submitSamples[^1],
            PostBlockP50Ms: Percentile(postBlockSamples, 0.50),
            PostBlockP95Ms: Percentile(postBlockSamples, 0.95),
            PostBlockMaxMs: postBlockSamples[^1],
            EndToEndP50Ms: Percentile(endToEndSamples, 0.50),
            EndToEndP95Ms: Percentile(endToEndSamples, 0.95),
            EndToEndMaxMs: endToEndSamples[^1]);
    }

    private void WriteSummary(Feat087ReactionBenchmarkSummary summary)
    {
        _output.WriteLine("[FEAT-087 DryRun] Summary");
        _output.WriteLine($"  submit p50={summary.SubmitP50Ms:F1}ms p95={summary.SubmitP95Ms:F1}ms max={summary.SubmitMaxMs:F1}ms");
        _output.WriteLine($"  post-block p50={summary.PostBlockP50Ms:F1}ms p95={summary.PostBlockP95Ms:F1}ms max={summary.PostBlockMaxMs:F1}ms");
        _output.WriteLine($"  end-to-end p50={summary.EndToEndP50Ms:F1}ms p95={summary.EndToEndP95Ms:F1}ms max={summary.EndToEndMaxMs:F1}ms");
    }

    private static double Percentile(IReadOnlyList<double> sortedSamples, double percentile)
    {
        if (sortedSamples.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedSamples.Count) - 1;
        index = Math.Clamp(index, 0, sortedSamples.Count - 1);
        return sortedSamples[index];
    }

    private static readonly BigInteger UserSecretOrder = BigInteger.Parse(
        "21888242871839275222246405745257275088614511777268538073601725287587578984328",
        CultureInfo.InvariantCulture);

    private static BigInteger DeriveUserSecret(string privateSigningKeyHex)
    {
        var privateKeyBytes = Convert.FromHexString(privateSigningKeyHex);
        var salt = Encoding.UTF8.GetBytes("hush-network-reactions");
        var info = Encoding.UTF8.GetBytes("user-secret-v1");
        var derivedBytes = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            privateKeyBytes,
            32,
            salt,
            info);

        var secret = new BigInteger(derivedBytes, isUnsigned: true, isBigEndian: true);
        var reduced = secret % UserSecretOrder;
        return reduced == BigInteger.Zero ? BigInteger.One : reduced;
    }

    private static BigInteger DeriveAddressMembershipSecret(string publicSigningAddress)
    {
        var addressBytes = Encoding.UTF8.GetBytes(publicSigningAddress);
        var salt = Encoding.UTF8.GetBytes("hush-network-address-commitment");
        var info = Encoding.UTF8.GetBytes("address-secret-v1");
        var derivedBytes = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            addressBytes,
            32,
            salt,
            info);

        var secret = new BigInteger(derivedBytes, isUnsigned: true, isBigEndian: true);
        var reduced = secret % UserSecretOrder;
        return reduced == BigInteger.Zero ? BigInteger.One : reduced;
    }

    private static byte[] ToBytes32(BigInteger value)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length == 32)
        {
            return bytes;
        }

        if (bytes.Length < 32)
        {
            var padded = new byte[32];
            Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
            return padded;
        }

        return bytes[^32..];
    }

    private interface IReactionSubmitter
    {
        string Kind { get; }
        Task SubmitAsync(TestIdentity reactor, Guid postId, int emojiIndex);
    }

    private sealed class DevModeReactionSubmitter : IReactionSubmitter
    {
        private readonly GrpcClientFactory _grpcFactory;

        public DevModeReactionSubmitter(GrpcClientFactory grpcFactory)
        {
            _grpcFactory = grpcFactory;
        }

        public string Kind => "dev-mode-v1";

        public async Task SubmitAsync(TestIdentity reactor, Guid postId, int emojiIndex)
        {
            var reactionScopeId = new FeedId(postId);
            var messageId = new FeedMessageId(postId);
            var nullifier = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"feat087-bench:{reactor.DisplayName}:{postId:D}"));
            var signedTransaction = TestTransactionFactory.CreateDevModeReaction(
                reactor,
                reactionScopeId,
                messageId,
                nullifier,
                emojiIndex);

            var blockchainClient = _grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();
            var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = signedTransaction
            });

            response.Successfull.Should().BeTrue($"Reaction submission should succeed for {reactor.DisplayName}: {response.Message}");
        }
    }

    private sealed class NonDevReactionSubmitter : IReactionSubmitter
    {
        private static readonly BigInteger NullifierDomain = new(1213481800);
        private static readonly BigInteger BackupDomain = ParseHexBigInteger("4241434B5550");
        private const string CircuitVersion = "omega-v1.0.0";

        private readonly GrpcClientFactory _grpcFactory;
        private readonly IServiceProvider _services;
        private readonly ReactionProofPathReadiness _readiness;

        public NonDevReactionSubmitter(
            GrpcClientFactory grpcFactory,
            IServiceProvider services,
            ReactionProofPathReadiness readiness)
        {
            _grpcFactory = grpcFactory;
            _services = services;
            _readiness = readiness;
        }

        public string Kind => "non-dev-headless-v1";

        public async Task SubmitAsync(TestIdentity reactor, Guid postId, int emojiIndex)
        {
            if (!_readiness.NonDevBenchmarkReady)
            {
                throw new InvalidOperationException(
                    "True non-dev benchmark proof path is not ready. " +
                    $"Current readiness: NonDevBenchmarkReady={_readiness.NonDevBenchmarkReady}. " +
                    $"Details: {_readiness.Notes}. " +
                    $"Expected client artifacts: '{_readiness.ClientWasmPath}' and '{_readiness.ClientZkeyPath}'. " +
                    $"Expected server verification key: '{_readiness.ServerVerificationKeyPath}'.");
            }

            var reactionScopeId = new FeedId(postId);
            var messageId = new FeedMessageId(postId);
            var feedInfoProvider = _services.GetRequiredService<IFeedInfoProvider>();
            var membershipService = _services.GetRequiredService<IMembershipService>();
            var poseidon = _services.GetRequiredService<IPoseidonHash>();
            var curve = _services.GetRequiredService<IBabyJubJub>();

            var feedPublicKey = await feedInfoProvider.GetFeedPublicKeyAsync(reactionScopeId)
                ?? throw new InvalidOperationException($"Feed public key unavailable for reaction scope '{reactionScopeId}'.");
            var authorCommitmentBytes = await feedInfoProvider.GetAuthorCommitmentAsync(messageId)
                ?? throw new InvalidOperationException($"Author commitment unavailable for message '{messageId}'.");
            var membershipScopeId = await feedInfoProvider.GetMembershipScopeIdAsync(reactionScopeId)
                ?? throw new InvalidOperationException($"Membership scope unavailable for reaction scope '{reactionScopeId}'.");

            var userSecret = membershipScopeId == PublicReactionScopes.GlobalHushMembers
                ? Feat087ReactionBenchmarkDryRunTests.DeriveAddressMembershipSecret(reactor.PublicSigningAddress)
                : Feat087ReactionBenchmarkDryRunTests.DeriveUserSecret(reactor.PrivateSigningKey);
            var userCommitment = Feat087ReactionBenchmarkDryRunTests.ToBytes32(poseidon.Hash(userSecret));
            var membershipProof = await membershipService.GetMembershipProofAsync(membershipScopeId, userCommitment);
            if (!membershipProof.IsMember ||
                membershipProof.MerkleRoot == null ||
                membershipProof.PathElements == null ||
                membershipProof.PathIndices == null)
            {
                throw new InvalidOperationException(
                    $"Non-dev benchmark membership proof unavailable for '{reactor.DisplayName}' in scope '{membershipScopeId}'. " +
                    $"This likely indicates the active membership tree does not contain the benchmark commitment.");
            }

            var scopeIdBigInt = ParseGuidBigInteger(postId);
            var nullifier = ToBytes32(poseidon.Hash4(userSecret, scopeIdBigInt, scopeIdBigInt, NullifierDomain));
            var backupKey = poseidon.Hash(userSecret, scopeIdBigInt, BackupDomain);
            var encryptedBackup = EncryptEmojiBackup(emojiIndex, backupKey);
            var (ciphertextC1, ciphertextC2, nonces) = EncryptVector(emojiIndex, feedPublicKey, curve);

            var circuitInputs = new CircuitInputsDto(
                nullifier: ToUnsignedDecimal(nullifier),
                ciphertext_c1: ciphertextC1.Select(ToCoordinatePair).ToArray(),
                ciphertext_c2: ciphertextC2.Select(ToCoordinatePair).ToArray(),
                message_id: scopeIdBigInt.ToString(CultureInfo.InvariantCulture),
                feed_id: scopeIdBigInt.ToString(CultureInfo.InvariantCulture),
                feed_pk: ToCoordinatePair(feedPublicKey),
                members_root: ToUnsignedDecimal(membershipProof.MerkleRoot),
                author_commitment: ToUnsignedDecimal(authorCommitmentBytes),
                user_secret: userSecret.ToString(CultureInfo.InvariantCulture),
                emoji_index: emojiIndex.ToString(CultureInfo.InvariantCulture),
                encryption_nonce: nonces.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(),
                merkle_path: membershipProof.PathElements.Select(ToUnsignedDecimal).ToArray(),
                merkle_indices: membershipProof.PathIndices.ToArray());

            var proofResult = await GenerateProofAsync(circuitInputs);
            var signedTransaction = TestTransactionFactory.CreateReaction(
                reactor,
                reactionScopeId,
                messageId,
                nullifier,
                ciphertextC1.Select(x => Feat087ReactionBenchmarkDryRunTests.ToBytes32(x.X)).ToArray(),
                ciphertextC1.Select(x => Feat087ReactionBenchmarkDryRunTests.ToBytes32(x.Y)).ToArray(),
                ciphertextC2.Select(x => Feat087ReactionBenchmarkDryRunTests.ToBytes32(x.X)).ToArray(),
                ciphertextC2.Select(x => Feat087ReactionBenchmarkDryRunTests.ToBytes32(x.Y)).ToArray(),
                proofResult.ProofBytes,
                proofResult.circuitVersion,
                encryptedBackup);

            var blockchainClient = _grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();
            var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
            {
                SignedTransaction = signedTransaction
            });

            response.Successfull.Should().BeTrue($"Reaction submission should succeed for {reactor.DisplayName}: {response.Message}");
        }

        private async Task<ProofCliOutput> GenerateProofAsync(CircuitInputsDto inputs)
        {
            var webClientRoot = Path.GetDirectoryName(_readiness.ClientPackageJsonPath)
                ?? throw new InvalidOperationException("Unable to derive hush-web-client root from the resolved package.json path.");
            var scriptPath = Path.Combine(webClientRoot, "scripts", "generate-reaction-proof.mjs");
            var inputPath = Path.Combine(Path.GetTempPath(), $"feat087-proof-input-{Guid.NewGuid():N}.json");
            var outputPath = Path.Combine(Path.GetTempPath(), $"feat087-proof-output-{Guid.NewGuid():N}.json");
            var proofTimeout = ResolveProofTimeout();
            var deleteTempFiles = false;

            try
            {
                await File.WriteAllTextAsync(
                    inputPath,
                    JsonSerializer.Serialize(inputs, new JsonSerializerOptions { WriteIndented = true }));

                var startInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    WorkingDirectory = webClientRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add(inputPath);
                startInfo.ArgumentList.Add(outputPath);
                startInfo.ArgumentList.Add(CircuitVersion);

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                using var cts = new CancellationTokenSource(proofTimeout);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Ignore cleanup failures for timed-out prover processes.
                    }

                    throw new TimeoutException(
                        $"Headless snarkjs proof generation exceeded the configured timeout of {proofTimeout.TotalMinutes:F1} minutes. " +
                        $"Input payload preserved at '{inputPath}'. Expected output path: '{outputPath}'.");
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Headless proof generation failed with exit code {process.ExitCode}. " +
                        $"stdout: {stdout} stderr: {stderr} " +
                        $"Input payload preserved at '{inputPath}'. Expected output path: '{outputPath}'.");
                }

                var outputJson = await File.ReadAllTextAsync(outputPath);
                var output = JsonSerializer.Deserialize<ProofCliOutput>(
                    outputJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Headless proof generation did not produce a readable output payload.");

                if (string.IsNullOrWhiteSpace(output.proof))
                {
                    throw new InvalidOperationException("Headless proof generation returned an empty proof payload.");
                }

                deleteTempFiles = true;
                return output with
                {
                    ProofBytes = Convert.FromBase64String(output.proof)
                };
            }
            finally
            {
                if (deleteTempFiles)
                {
                    TryDelete(inputPath);
                    TryDelete(outputPath);
                }
            }
        }

        private static (ReactionEcPoint[] C1, ReactionEcPoint[] C2, BigInteger[] Nonces) EncryptVector(
            int emojiIndex,
            ReactionEcPoint publicKey,
            IBabyJubJub curve)
        {
            var c1 = new ReactionEcPoint[6];
            var c2 = new ReactionEcPoint[6];
            var nonces = new BigInteger[6];

            for (var i = 0; i < 6; i++)
            {
                var message = i == emojiIndex ? BigInteger.One : BigInteger.Zero;
                var nonce = RandomScalar(curve.Order);
                var ephemeral = curve.ScalarMul(curve.Generator, nonce);
                var messagePoint = message == BigInteger.Zero
                    ? curve.Identity
                    : curve.ScalarMul(curve.Generator, message);
                var sharedSecret = curve.ScalarMul(publicKey, nonce);

                c1[i] = ephemeral;
                c2[i] = curve.Add(messagePoint, sharedSecret);
                nonces[i] = nonce;
            }

            return (c1, c2, nonces);
        }

        private static BigInteger RandomScalar(BigInteger order)
        {
            Span<byte> buffer = stackalloc byte[32];

            while (true)
            {
                RandomNumberGenerator.Fill(buffer);
                var candidate = new BigInteger(buffer, isUnsigned: true, isBigEndian: true) % order;
                if (candidate != BigInteger.Zero)
                {
                    return candidate;
                }
            }
        }

        private static byte[] EncryptEmojiBackup(int emojiIndex, BigInteger backupKey)
        {
            var normalizedEmoji = NormalizeEmojiIndex(emojiIndex);
            var encodedEmoji = normalizedEmoji + 128;
            var keyBytes = ToBytes32(backupKey);
            var encrypted = new byte[32];

            encrypted[0] = 1;
            encrypted[1] = (byte)(encodedEmoji ^ keyBytes[0]);

            for (var i = 2; i < encrypted.Length; i++)
            {
                var mask = keyBytes[(i - 1) % keyBytes.Length];
                encrypted[i] = (byte)(mask ^ encodedEmoji ^ i);
            }

            return encrypted;
        }

        private static int NormalizeEmojiIndex(int emojiIndex)
        {
            if (emojiIndex is < -1 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(emojiIndex), emojiIndex, "Reaction backup only supports indices in the inclusive range [-1, 6].");
            }

            return emojiIndex;
        }

        private static string[] ToCoordinatePair(ReactionEcPoint point) =>
        [
            point.X.ToString(CultureInfo.InvariantCulture),
            point.Y.ToString(CultureInfo.InvariantCulture),
        ];

        private static string ToUnsignedDecimal(byte[] bytes) =>
            new BigInteger(bytes, isUnsigned: true, isBigEndian: true).ToString(CultureInfo.InvariantCulture);

        private static BigInteger ParseGuidBigInteger(Guid value)
        {
            return new BigInteger(value.ToByteArray(), isUnsigned: true, isBigEndian: true);
        }

        private static BigInteger ParseHexBigInteger(string hex) =>
            BigInteger.Parse($"0{hex}", NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

        private static TimeSpan ResolveProofTimeout()
        {
            const int defaultTimeoutSeconds = 600;
            var configuredValue = Environment.GetEnvironmentVariable("FEAT087_HEADLESS_PROOF_TIMEOUT_SECONDS");
            if (int.TryParse(configuredValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            return TimeSpan.FromSeconds(defaultTimeoutSeconds);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore temp cleanup failures.
            }
        }
    }

    private sealed record CircuitInputsDto(
        string nullifier,
        string[][] ciphertext_c1,
        string[][] ciphertext_c2,
        string message_id,
        string feed_id,
        string[] feed_pk,
        string members_root,
        string author_commitment,
        string user_secret,
        string emoji_index,
        string[] encryption_nonce,
        string[] merkle_path,
        int[] merkle_indices);

    private sealed record ProofCliOutput(
        string circuitVersion,
        string[] publicSignals,
        string proof)
    {
        public byte[] ProofBytes { get; init; } = Array.Empty<byte>();
    }

    private enum Feat087ReactionProofMode
    {
        DevMode,
        NonDev,
    }
}
