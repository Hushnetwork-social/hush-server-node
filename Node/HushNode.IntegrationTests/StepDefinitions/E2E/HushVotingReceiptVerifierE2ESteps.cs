using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions.E2E;

/// <summary>
/// Focused browser coverage for FEAT-136 public receipt verification.
/// </summary>
[Binding]
internal sealed class HushVotingReceiptVerifierE2ESteps : BrowserStepsBase
{
    private const string ReceiptKey = "FEAT136ReceiptJson";
    private const string PackageKey = "FEAT136PackageZipBytes";
    private const string ElectionId = "13e6fa69-1d53-4968-8b1c-397333458253";
    private const string PackageId = "HushElectionPackage-13e6fa69-1d53-4968-8b1c-397333458253";
    private const string VerifierProfileId = "public_anonymous_v1";
    private const string ReceiptCommitment = "receipt-a";
    private const string ReceiptCommitmentScheme =
        "sha256(receipt_secret|prepared_ballot_hash|accepted_ballot_id)";
    private const string PreparedBallotHash = "prepared-final-a";
    private const string BallotDefinitionHash = "NRASoflgGqzNd3Y/lR7Haz1FDI2k5Pzhj5YChdYFfHc=";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true,
    };

    public HushVotingReceiptVerifierE2ESteps(ScenarioContext scenarioContext) : base(scenarioContext)
    {
    }

    [Given(@"the FEAT-136 package-bound receipt and finalized package ZIP are prepared for the browser")]
    public void GivenTheFeat136ReceiptAndFinalizedPackageZipArePreparedForTheBrowser()
    {
        var package = BuildFinalizedPackageZip();
        var receiptJson = BuildReceiptJson(package.PackageHash);

        ScenarioContext[ReceiptKey] = receiptJson;
        ScenarioContext[PackageKey] = package.ZipBytes;

        Console.WriteLine("[E2E HushVoting FEAT-136] Receipt and finalized package ZIP prepared");
    }

    [When(@"the public user opens the receipt verifier")]
    public async Task WhenThePublicUserOpensTheReceiptVerifier()
    {
        var page = await GetOrCreatePageAsync();

        await NavigateToAsync(page, "/verify-receipt");

        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/verify-receipt"),
            new PageAssertionsToHaveURLOptions { Timeout = 15000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions
        {
            Name = "Verify receipt",
        })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.GetByText("No login needed", new PageGetByTextOptions
        {
            Exact = false,
        })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        Console.WriteLine("[E2E HushVoting FEAT-136] Public verifier route opened");
    }

    [When(@"the public user imports the FEAT-136 receipt and package ZIP")]
    public async Task WhenThePublicUserImportsTheFeat136ReceiptAndPackageZip()
    {
        var page = await GetOrCreatePageAsync();
        var receiptJson = ScenarioContext.Get<string>(ReceiptKey);
        var packageZipBytes = ScenarioContext.Get<byte[]>(PackageKey);

        await page.Locator("input[data-testid='receipt-verifier-receipt-input']").SetInputFilesAsync(
            new FilePayload
            {
                Name = "accepted-ballot.hush-receipt.json",
                MimeType = "application/json",
                Buffer = Encoding.UTF8.GetBytes(receiptJson),
            });

        await page.Locator("input[data-testid='receipt-verifier-package-input']").SetInputFilesAsync(
            new FilePayload
            {
                Name = "finalized-public-package.zip",
                MimeType = "application/zip",
                Buffer = packageZipBytes,
            });

        await Assertions.Expect(page.GetByTestId("receipt-verifier-submit")).ToBeEnabledAsync(
            new LocatorAssertionsToBeEnabledOptions { Timeout = 15000 });

        Console.WriteLine("[E2E HushVoting FEAT-136] Receipt and package files imported");
    }

    [When(@"the public user runs receipt verification")]
    public async Task WhenThePublicUserRunsReceiptVerification()
    {
        var page = await GetOrCreatePageAsync();

        await page.GetByTestId("receipt-verifier-submit").ClickAsync();

        Console.WriteLine("[E2E HushVoting FEAT-136] Receipt verification submitted");
    }

    [Then(@"the FEAT-136 receipt verifier should show a verified included result")]
    public async Task ThenTheFeat136ReceiptVerifierShouldShowAVerifiedIncludedResult()
    {
        var page = await GetOrCreatePageAsync();
        var result = page.GetByTestId("receipt-verifier-result-verified_included");

        await Assertions.Expect(result).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(result).ToContainTextAsync(
            "Receipt included",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(result).ToContainTextAsync(
            "verified_included",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        Console.WriteLine("[E2E HushVoting FEAT-136] Verified included result is visible");
    }

    [Then(@"the FEAT-136 receipt verifier should not show forbidden private voting data")]
    public async Task ThenTheFeat136ReceiptVerifierShouldNotShowForbiddenPrivateVotingData()
    {
        var page = await GetOrCreatePageAsync();
        var bodyText = await page.Locator("body").InnerTextAsync();

        foreach (var forbiddenValue in new[]
        {
            "candidate-choice-feat-136",
            "organization-voter-feat-136",
            "receipt-secret-feat-136",
            "private-audit-feat-136",
            "kms-key-feat-136",
            "cast-randomness-feat-136",
        })
        {
            bodyText.Should().NotContain(forbiddenValue);
        }

        Console.WriteLine("[E2E HushVoting FEAT-136] Forbidden private voting data is not visible");
    }

    private static string BuildReceiptJson(string packageHash)
    {
        return JsonSerializer.Serialize(new
        {
            schema = "hushvoting.receipt.export",
            schemaVersion = 1,
            receiptProof = new
            {
                electionId = ElectionId,
                receiptCommitment = ReceiptCommitment,
                receiptCommitmentScheme = ReceiptCommitmentScheme,
                preparedBallotHash = PreparedBallotHash,
                ballotDefinitionVersion = 1,
                ballotDefinitionHash = BallotDefinitionHash,
                expectedPackageId = PackageId,
                expectedPackageHash = packageHash,
                expectedVerifierProfileId = VerifierProfileId,
            },
            exportEnvelope = new
            {
                receiptGeneratedAt = "2026-05-21T10:00:00Z",
                exportedBy = "HushVoting",
                exporterVersion = "feat-136-e2e-fixture",
            },
        }, JsonOptions);
    }

    private static (byte[] ZipBytes, string PackageHash) BuildFinalizedPackageZip()
    {
        var acceptedBallots = new[]
        {
            AcceptedBallot("nullifier-a", ReceiptCommitment, PreparedBallotHash),
            AcceptedBallot("nullifier-b", "receipt-b", "prepared-final-b"),
        };
        var receiptCommitments = new[]
        {
            ReceiptCommitmentRecord(ReceiptCommitment, PreparedBallotHash),
        };

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["ElectionRecord.json"] = JsonBytes(new
            {
                electionId = ElectionId,
                lifecycleState = "Finalized",
            }),
            ["VerifierProfile.json"] = JsonBytes(new
            {
                profileId = VerifierProfileId,
                displayName = VerifierProfileId,
            }),
            ["artifacts/election-record/accepted-ballot-set.json"] = JsonBytes(new
            {
                electionId = ElectionId,
                acceptedBallotCount = acceptedBallots.Length,
                acceptedBallotInventoryHash = AcceptedBallotInventoryHash(acceptedBallots),
                acceptedBallots,
            }),
            ["artifacts/election-record/sp04-receipt-commitments.json"] = JsonBytes(receiptCommitments),
            ["artifacts/election-record/sp04-evidence.json"] = JsonBytes(new
            {
                electionId = ElectionId,
                acceptedBoundReceiptCount = receiptCommitments.Length,
                receiptCommitmentSetHash = ReceiptCommitmentSetHash(receiptCommitments),
            }),
        };

        var manifestEntries = files.Select(file => new
        {
            path = file.Key,
            sha256Hash = Sha256Hex(file.Value),
            sizeBytes = file.Value.Length,
            mediaType = "application/json",
            visibility = "public",
            requirement = "required",
            requiredProfileIds = new[] { VerifierProfileId },
        }).ToArray();

        files["AuditPackageManifest.json"] = JsonBytes(new
        {
            manifestVersion = "1.0",
            packageId = PackageId,
            electionId = ElectionId,
            packageView = "publicAnonymous",
            verifierProfileId = VerifierProfileId,
            createdAt = "2026-05-21T10:00:00Z",
            entries = manifestEntries,
        });

        files["VerifierInputManifest.json"] = JsonBytes(new
        {
            manifestVersion = "1.0",
            packageId = PackageId,
            electionId = ElectionId,
            packageView = "publicAnonymous",
            verifierProfileId = VerifierProfileId,
            auditPackageManifestHash = Sha256Hex(files["AuditPackageManifest.json"]),
        });

        return (ZipFiles(files), PackageDirectoryHash(files));
    }

    private static AcceptedBallotFixture AcceptedBallot(
        string ballotNullifier,
        string receiptCommitment,
        string preparedBallotHash)
    {
        return new AcceptedBallotFixture
        {
            BallotNullifier = ballotNullifier,
            EncryptedBallotPackage = $"ballot-{ballotNullifier}",
            ProofBundle = $"proof-{ballotNullifier}",
            PreparedBallotHash = preparedBallotHash,
            ReceiptCommitment = receiptCommitment,
            ReceiptCommitmentScheme = ReceiptCommitmentScheme,
            BallotDefinitionVersion = 1,
            BallotDefinitionHash = BallotDefinitionHash,
        };
    }

    private static ReceiptCommitmentFixture ReceiptCommitmentRecord(
        string receiptCommitment,
        string preparedBallotHash)
    {
        return new ReceiptCommitmentFixture
        {
            AcceptedBallotId = "ab2e6a0b-62b9-4a2a-a07d-65da60d3e3ab",
            PreparedBallotId = "e1be878a-cc73-4abd-a428-898745de47bc",
            AcceptedAt = "2026-05-19T23:34:00Z",
            PreparedBallotHash = preparedBallotHash,
            ReceiptCommitment = receiptCommitment,
            ReceiptCommitmentScheme = ReceiptCommitmentScheme,
        };
    }

    private static string AcceptedBallotInventoryHash(IEnumerable<AcceptedBallotFixture> ballots)
    {
        var payload = string.Join(
            "\n",
            ballots
                .OrderBy(ballot => ballot.BallotNullifier, StringComparer.Ordinal)
                .Select(ballot => string.Join(
                    "|",
                    ballot.BallotNullifier,
                    Sha256UpperHex(ballot.EncryptedBallotPackage),
                    Sha256UpperHex(ballot.ProofBundle))));

        return Sha256Hex(Encoding.UTF8.GetBytes(payload));
    }

    private static string ReceiptCommitmentSetHash(IEnumerable<ReceiptCommitmentFixture> records)
    {
        var payload = string.Join(
            "\n",
            records
                .OrderBy(record => StripUuidDashes(record.AcceptedBallotId), StringComparer.Ordinal)
                .Select(record => string.Join(
                    "|",
                    StripUuidDashes(record.AcceptedBallotId),
                    StripUuidDashes(record.PreparedBallotId),
                    record.PreparedBallotHash,
                    record.ReceiptCommitment,
                    record.ReceiptCommitmentScheme,
                    "2026-05-19T23:34:00.0000000Z")));

        return Sha256UpperHex(payload);
    }

    private static string PackageDirectoryHash(IReadOnlyDictionary<string, byte[]> files)
    {
        var payload = string.Join(
            "\n",
            files
                .OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => $"{file.Key}|sha256:{Sha256Hex(file.Value)}"));

        return $"sha256:{Sha256Hex(Encoding.UTF8.GetBytes($"{payload}\n"))}";
    }

    private static byte[] ZipFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 5, 21, 10, 0, 0, TimeSpan.Zero);
                using var entryStream = entry.Open();
                entryStream.Write(file.Value, 0, file.Value.Length);
            }
        }

        return stream.ToArray();
    }

    private static byte[] JsonBytes(object value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Sha256UpperHex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string StripUuidDashes(string value)
    {
        return value.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private sealed class AcceptedBallotFixture
    {
        [JsonPropertyName("ballotNullifier")]
        public required string BallotNullifier { get; init; }

        [JsonPropertyName("encryptedBallotPackage")]
        public required string EncryptedBallotPackage { get; init; }

        [JsonPropertyName("proofBundle")]
        public required string ProofBundle { get; init; }

        [JsonPropertyName("preparedBallotHash")]
        public required string PreparedBallotHash { get; init; }

        [JsonPropertyName("receiptCommitment")]
        public required string ReceiptCommitment { get; init; }

        [JsonPropertyName("receiptCommitmentScheme")]
        public required string ReceiptCommitmentScheme { get; init; }

        [JsonPropertyName("ballotDefinitionVersion")]
        public required int BallotDefinitionVersion { get; init; }

        [JsonPropertyName("ballotDefinitionHash")]
        public required string BallotDefinitionHash { get; init; }
    }

    private sealed class ReceiptCommitmentFixture
    {
        [JsonPropertyName("acceptedBallotId")]
        public required string AcceptedBallotId { get; init; }

        [JsonPropertyName("preparedBallotId")]
        public required string PreparedBallotId { get; init; }

        [JsonPropertyName("acceptedAt")]
        public required string AcceptedAt { get; init; }

        [JsonPropertyName("preparedBallotHash")]
        public required string PreparedBallotHash { get; init; }

        [JsonPropertyName("receiptCommitment")]
        public required string ReceiptCommitment { get; init; }

        [JsonPropertyName("receiptCommitmentScheme")]
        public required string ReceiptCommitmentScheme { get; init; }
    }
}
