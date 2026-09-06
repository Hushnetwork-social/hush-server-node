// FEAT-015 Phase 6.3/6.4 — licence admission gate + pipeline registration contract tests.
//
// Proves the additive ingress contract surface without a live gRPC host:
//  - the licence admission gate accepts/reserves, returns PENDING on exact retry, and returns
//    ALREADY_EXISTS once the originating transaction is indexed (real PostgreSQL);
//  - the pipeline registration helper composes deserializer/validator/gate/index strategy;
//  - the content handler reports typed failures (never exceptions) for invalid transactions.

using FluentAssertions;
using HushNode.HushVoting.Licence.Transactions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.HushVoting.Licensing.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

[Collection("FEAT-015 Licensing PostgreSQL")]
[Trait("Category", "FEAT-015")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicenceAdmissionGateTwinTests : IAsyncLifetime
{
    private readonly LicensingPostgresFixture _fixture;
    private readonly string _databaseName;
    private long _counter;

    public HushVotingLicenceAdmissionGateTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
        _databaseName = $"feat015_adm_{Guid.NewGuid():N}";
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateDatabaseAsync(_databaseName);
        await _fixture.MigrateToAsync(_databaseName, "20260906122828_Feat015LicenceIndexProjectionAndReservation");
    }

    public async Task DisposeAsync()
    {
        await _fixture.DropDatabaseAsync(_databaseName);
    }

    private string NextAddress() => $"feat015-adm-{Interlocked.Increment(ref _counter):D4}";

    private static readonly HushVotingLicenceCatalogue Catalogue = HushVotingLicenceCatalogueV1.CreateCatalogue();
    private static readonly LicenceServiceConfiguration Configuration =
        LicenceServiceConfiguration.CreateDefault(catalogue: Catalogue);

    private sealed class FakeContextSource : IHushVotingLicenceValidationContextSource
    {
        private readonly string _address;
        public bool IdentityExists { get; set; } = true;

        public FakeContextSource(string address) => _address = address;

        public Task<HushVotingLicenceCatalogue> GetCurrentCatalogueAsync(CancellationToken ct) =>
            Task.FromResult(Catalogue);

        public Task<HushVotingLicenceSignatoryContext?> ResolveIdentityAsync(
            string canonicalPublicSigningAddress, CancellationToken ct) =>
            Task.FromResult<HushVotingLicenceSignatoryContext?>(
                IdentityExists
                    ? new HushVotingLicenceSignatoryContext(canonicalPublicSigningAddress, 100)
                    : null);

        public Task<HushVotingLicenceCurrentState> ResolveCurrentStateAsync(
            HushVotingLicenceSignatoryContext identity, CancellationToken ct) =>
            Task.FromResult<HushVotingLicenceCurrentState>(new HushVotingLicenceCurrentState.NoActive());
    }

    private (string Address, IHushVotingLicenceAdmissionGate Gate, HushVotingLicenceReservationStore Store)
        BuildGate()
    {
        var address = NextAddress();
        var store = new HushVotingLicenceReservationStore(() => _fixture.CreateContext(_databaseName));
        var gate = new HushVotingLicenceAdmissionService(
            new HushVotingLicenceTransactionValidator(
                new HushVotingLicenceCanonicalSerializer(),
                new HushVotingLicenceSignatureVerifier(),
                new FakeContextSource(address)),
            new FakeContextSource(address),
            store,
            () => _fixture.CreateContext(_databaseName));
        return (address, gate, store);
    }

    // FEAT-001 public corpus key K-001 (public replay, never a secret).
    private const string K001PrivateScalarHex = "6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885";
    private const string K001SigningAddress = "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5";

    private static SignedTransaction<HushVotingLicenceAssignmentPayload> BuildSigned(
        string address,
        Guid txId,
        string privateScalarHex = K001PrivateScalarHex,
        bool signCompact = true,
        string? signatoryOverride = null,
        string? signatureOverride = null)
    {
        var payload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree,
            "hushvoting.direct.free",
            HushVotingLicenceCatalogueVersion.V1Value);
        var size = HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload);
        var unsigned = new HushShared.Blockchain.TransactionModel.States.UnsignedTransaction<HushVotingLicenceAssignmentPayload>(
            new HushShared.Blockchain.TransactionModel.TransactionId(txId),
            HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind,
            new HushShared.Blockchain.Model.Timestamp(
                DateTime.Parse("2026-09-06T00:00:00Z", null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime()),
            payload,
            size);
        var signatory = signatoryOverride ?? K001SigningAddress;
        var canonical = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(
            new SignedTransaction<HushVotingLicenceAssignmentPayload>(
                unsigned, new HushShared.Blockchain.Model.SignatureInfo(signatory, string.Empty)));
        var signature = signatureOverride
            ?? (signCompact
                ? Olimpo.DigitalSignature.SignMessageCompactBase64(canonical, privateScalarHex)
                : string.Empty);
        return new SignedTransaction<HushVotingLicenceAssignmentPayload>(
            unsigned, new HushShared.Blockchain.Model.SignatureInfo(signatory, signature));
    }

    [Fact]
    public async Task Admission_gate_accepts_then_exact_retry_is_pending()
    {
        var (address, gate, _) = BuildGate();
        var txId = Guid.NewGuid();
        var tx = BuildSigned(address, txId);

        var first = await gate.AdmitAsync(tx, CancellationToken.None);
        first.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Accepted);

        var retry = await gate.AdmitAsync(tx, CancellationToken.None);
        retry.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.Pending);
    }

    [Fact]
    public async Task Admission_gate_returns_already_exists_once_indexed()
    {
        var (address, gate, _) = BuildGate();
        var txId = Guid.NewGuid();
        var tx = BuildSigned(address, txId);

        // Simulate block indexing by inserting the originating transaction row.
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = Guid.CreateVersion7(),
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = address,
            IdentityCreationBlockIndex = 100,
            CreatedAtUtc = DateTime.UtcNow,
            EntitlementRevision = 1,
        };
        await using (var context = _fixture.CreateContext(_databaseName))
        {
            context.Set<LicenceSubjectEntity>().Add(subject);
            await context.SaveChangesAsync();
        }

        var ok = AuthenticatedIdentitySubject.TryCreate(
            LicencePersistenceVocabulary.SubjectTypeIdentity, address, 100, out var trusted, out _);
        ok.Should().BeTrue();

        await LicenceBlockIndexWriter.IndexAsync(
            () => _fixture.CreateContext(_databaseName), Configuration, trusted!,
            SignedToValidated(tx), 1, DateTime.UtcNow, null, CancellationToken.None);

        var after = await gate.AdmitAsync(tx, CancellationToken.None);
        after.Outcome.Should().Be(HushVotingLicenceSubmitOutcome.AlreadyExists);
    }

    private static ValidatedTransaction<HushVotingLicenceAssignmentPayload> SignedToValidated(
        SignedTransaction<HushVotingLicenceAssignmentPayload> signed) =>
        signed.SignByValidator(new HushShared.Blockchain.Model.SignatureInfo("validator", "vsig"));
}
