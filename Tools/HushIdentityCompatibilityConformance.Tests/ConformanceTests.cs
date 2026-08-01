using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HushIdentityCompatibilityConformance.Adapters;
using HushIdentityCompatibilityConformance.Corpus;
using Xunit;

namespace HushIdentityCompatibilityConformance.Tests;

/// <summary>Task 4.1/4.2 — corpus input, schema, and integrity validation.</summary>
public class CorpusValidationTests
{
    private static readonly string? Corpus = CorpusLocator.Find();
    private static readonly string Digest = Corpus is null ? string.Empty : CorpusLocator.ManifestDigest(Corpus);

    private static string TempCorpus()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "hush-conformance-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        foreach (var dir in new[] { "schemas", "producers", "vectors", "lookup" })
        {
            Directory.CreateDirectory(Path.Combine(tmp, dir));
        }
        return tmp;
    }

    [Fact]
    public void Valid_corpus_passes_when_digest_matches()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var result = CorpusValidator.Validate(Corpus, Digest);
        result.Valid.Should().BeTrue("errors: " + string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Wrong_manifest_digest_is_rejected()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var result = CorpusValidator.Validate(Corpus, new string('0', 64));
        result.Valid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("manifest digest mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_manifest_file_is_rejected()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var tmp = TempCorpus();
        try
        {
            var result = CorpusValidator.Validate(tmp, Digest);
            result.Valid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("manifest.json missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Tampered_vector_file_is_rejected_by_integrity()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var tmp = CopyCorpus(Corpus);
        try
        {
            var target = Path.Combine(tmp, "vectors", "mnemonic-vectors.json");
            File.AppendAllText(target, " "); // changes bytes + digest
            var result = CorpusValidator.Validate(tmp, CorpusLocator.ManifestDigest(tmp));
            result.Valid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("digest mismatch", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Schema_violation_is_rejected()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var tmp = CopyCorpus(Corpus);
        try
        {
            // Corrupt a document structurally, then regenerate integrity via a
            // fresh manifest so only the schema check can catch it.
            var target = Path.Combine(tmp, "vectors", "negative-vectors.json");
            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(target))!;
            doc["vectors"]!.AsArray().Clear(); // schema requires minItems 1
            File.WriteAllText(target, System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
            var newDigest = CorpusLocator.ManifestDigest(tmp);
            var result = CorpusValidator.Validate(tmp, newDigest);
            result.Valid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("failed schema", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Unexpected_corpus_file_is_rejected()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var tmp = CopyCorpus(Corpus);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "vectors", "extra.json"), "{}\n");
            var result = CorpusValidator.Validate(tmp, CorpusLocator.ManifestDigest(tmp));
            result.Valid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("not listed in manifest", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Manifest_schema_violation_is_rejected()
    {
        if (Corpus is null) throw new InvalidOperationException("corpus not found; set HUSH_CONFORMANCE_CORPUS");
        var tmp = CopyCorpus(Corpus);
        try
        {
            // Parseable manifest whose files entry violates manifest.schema.json
            // (bad path pattern, negative bytes) — integrity digest matches, so
            // only the schema check can reject it.
            File.WriteAllText(Path.Combine(tmp, "manifest.json"), "{\"contractVersion\":\"1.0.0\",\"files\":[{\"path\":\"bad path !!!\",\"bytes\":-1,\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}]}\n");
            var newDigest = CorpusLocator.ManifestDigest(tmp);
            var result = CorpusValidator.Validate(tmp, newDigest);
            result.Valid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("manifest.schema.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static string CopyCorpus(string source)
    {
        var tmp = TempCorpus();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(tmp, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
        return tmp;
    }
}

/// <summary>Task 4.3/4.4 — producer conformance adapters.</summary>
public class DerivationAdapterTests
{
    [Fact]
    public void P01_12_and_24_word_mnemonics_derive_corpus_compressed_addresses()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<MnemonicVector>(corpus, "vectors/mnemonic-vectors.json");
        foreach (var v in vectors.Where(v => v.ProducerId == "P-01"))
        {
            var validation = DerivationAdapters.ValidateMnemonicForProducer(v.Mnemonic, "P-01");
            validation.Valid.Should().BeTrue(v.Id);
            var derived = DerivationAdapters.DeriveP01Keys(v.Mnemonic);
            derived.Ok.Should().BeTrue(v.Id);
            derived.Keys!.SigningAddress.Should().Be(v.SigningPublicKeyHex, v.Id);
            derived.Keys.EncryptionAddress.Should().Be(v.EncryptionPublicKeyHex, v.Id);
            derived.Keys.PublicKeyEncoding.Should().Be("COMPRESSED", v.Id);
            derived.Keys.SigningPrivateKey.Should().Be(v.SigningPrivateKeyHex, v.Id);
        }
    }

    [Fact]
    public void P02_mnemonics_derive_corpus_uncompressed_addresses()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<MnemonicVector>(corpus, "vectors/mnemonic-vectors.json");
        foreach (var v in vectors.Where(v => v.ProducerId == "P-02"))
        {
            var derived = DerivationAdapters.DeriveP02Keys(v.Mnemonic);
            derived.SigningAddress.Should().Be(v.SigningPublicKeyHex, v.Id);
            derived.EncryptionAddress.Should().Be(v.EncryptionPublicKeyHex, v.Id);
            derived.PublicKeyEncoding.Should().Be("UNCOMPRESSED", v.Id);
            derived.SigningPrivateKey.Should().Be(v.SigningPrivateKeyHex, v.Id);
        }
    }

    [Fact]
    public void P02_rejects_12_word_input()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<MnemonicVector>(corpus, "vectors/mnemonic-vectors.json");
        var twelve = vectors.First(v => v.WordCount == 12);
        var validation = DerivationAdapters.ValidateMnemonicForProducer(twelve.Mnemonic, "P-02");
        validation.Valid.Should().BeFalse();
        validation.Code.Should().Be("INVALID_WORD_COUNT");
    }

    [Fact]
    public void Seed_derivation_matches_corpus()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<MnemonicVector>(corpus, "vectors/mnemonic-vectors.json");
        foreach (var v in vectors)
        {
            DerivationAdapters.BytesToHex(DerivationAdapters.MnemonicToSeed(v.Mnemonic)).Should().Be(v.SeedHex, v.Id);
        }
    }
}

/// <summary>Task 4.3/4.4 — .dat v1 adapter boundaries.</summary>
public class DatAdapterTests
{
    [Fact]
    public async Task Positive_fixture_decodes_with_full_consistency()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<DatVector>(corpus, "vectors/dat-vectors.json");
        var d001 = vectors.First(v => v.Id == "D-001");
        var result = DatAdapter.DecodeDatV1(Convert.FromHexString(d001.EnvelopeHex!), d001.Password!);
        result.Ok.Should().BeTrue();
        result.Result!.PrivatePublicConsistent.Should().BeTrue();
        result.Result.MnemonicKeyConsistent.Should().BeTrue();
    }

    [Theory]
    [InlineData("D-002", "DAT_WRONG_PASSWORD")]
    [InlineData("D-003", "DAT_INVALID_MAGIC")]
    [InlineData("D-004", "DAT_UNSUPPORTED_VERSION")]
    [InlineData("D-005", "DAT_WRONG_PASSWORD")]
    [InlineData("D-007", "DAT_MALFORMED")]
    [InlineData("D-008", "DAT_WRONG_PASSWORD")]
    public void Negative_envelope_vectors_reject_deterministically(string id, string expectedCode)
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<DatVector>(corpus, "vectors/dat-vectors.json");
        var v = vectors.First(x => x.Id == id);
        var result = DatAdapter.DecodeDatV1(Convert.FromHexString(v.EnvelopeHex!), v.Password!);
        result.Ok.Should().BeFalse(id);
        result.Code.Should().Be(expectedCode, id);
    }

    [Fact]
    public void Oversized_envelope_is_rejected_before_decryption()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<DatVector>(corpus, "vectors/dat-vectors.json");
        var d001 = vectors.First(v => v.Id == "D-001");
        var oversized = Convert.FromHexString(d001.EnvelopeHex!).Concat(new byte[DatAdapter.DatMaxEnvelopeBytes + 1]).ToArray();
        var result = DatAdapter.DecodeDatV1(oversized, d001.Password!);
        result.Ok.Should().BeFalse();
        result.Code.Should().Be("DAT_MALFORMED");
    }

    [Theory]
    [InlineData("D-009", "DAT_MISSING_FIELD")]
    [InlineData("D-010", "DAT_UNKNOWN_FIELD")]
    [InlineData("D-011", "DAT_DUPLICATE_FIELD")]
    [InlineData("D-012", "DAT_INVALID_FIELD")]
    [InlineData("D-013", "DAT_INVALID_FIELD")]
    public void Strict_parse_rejects_malformed_records(string id, string expectedCode)
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<DatVector>(corpus, "vectors/dat-vectors.json");
        var v = vectors.First(x => x.Id == id);
        var result = DatAdapter.ParsePortableCredentialsStrict(v.PayloadJson!);
        result.Ok.Should().BeFalse(id);
        result.Code.Should().Be(expectedCode, id);
    }

    [Fact]
    public void Duplicate_detection_does_not_false_positive_on_escaped_quote_values()
    {
        // A value containing an escaped quote followed by a colon must not be
        // mistaken for a duplicate property (regex heuristic robustness).
        var payload = "{\"ProfileName\":\"say \\\"hi\\\": now\",\"PublicSigningAddress\":\"0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5\",\"PrivateSigningKey\":\"6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885\",\"PublicEncryptAddress\":\"032ebaf076203f15ac8119cfdbc9394d1c7b9929b0647e4f607e27da95701f8556\",\"PrivateEncryptKey\":\"1a68f2d543282dd612502a1b3688e85eeca280057129d512011645a51cf6d552\",\"IsPublic\":true,\"Mnemonic\":null}";
        var result = DatAdapter.ParsePortableCredentialsStrict(payload);
        result.Ok.Should().BeTrue("escaped-quote colon inside a value is not a duplicate key");
        result.Record!.ProfileName.Should().Be("say \"hi\": now");
    }

    [Theory]
    [InlineData("D-014", "DAT_MNEMONIC_KEY_MISMATCH")]
    [InlineData("D-015", "DAT_KEY_MISMATCH")]
    public void Key_consistency_vectors_detect_mismatches(string id, string expectedCode)
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<DatVector>(corpus, "vectors/dat-vectors.json");
        var v = vectors.First(x => x.Id == id);
        var parsed = DatAdapter.ParsePortableCredentialsStrict(v.PayloadJson!);
        parsed.Ok.Should().BeTrue(id);
        var consistency = DatAdapter.ValidateKeyConsistency(parsed.Record!);
        if (expectedCode == "DAT_KEY_MISMATCH")
        {
            consistency.PrivatePublicConsistent.Should().BeFalse(id);
        }
        else
        {
            consistency.PrivatePublicConsistent.Should().BeTrue(id);
            consistency.MnemonicKeyConsistent.Should().BeFalse(id);
        }
    }
}

/// <summary>Task 4.3/4.4 — canonical transaction bytes.</summary>
public class CanonicalTransactionTests
{
    [Fact]
    public void Canonical_serialization_matches_exact_corpus_bytes()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<CanonicalVector>(corpus, "vectors/canonical-byte-vectors.json");
        var baseV = vectors.First(v => v.Id == "CB-001");
        var serialized = CanonicalTransactionAdapter.SerializeUnsignedTransaction(CanonicalTransactionAdapter.Parse(baseV.Json));
        serialized.Should().Be(baseV.Json);
        Encoding.UTF8.GetBytes(serialized).Length.Should().Be(baseV.Utf8Length);
        DerivationAdapters.BytesToHex(Encoding.UTF8.GetBytes(serialized)).Should().Be(baseV.Utf8Hex);
    }

    [Fact]
    public void Non_ascii_alias_keeps_raw_utf8_bytes()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<CanonicalVector>(corpus, "vectors/canonical-byte-vectors.json");
        var v = vectors.First(x => x.Id == "CB-008");
        var bytes = Encoding.UTF8.GetBytes(v.Json);
        DerivationAdapters.BytesToHex(bytes).Should().Be(v.Utf8Hex);
        // The raw UTF-8 representation must not contain any \u escape sequence.
        Encoding.UTF8.GetString(bytes).Should().NotContain(@"\u");
        bytes.Should().Contain(new byte[] { 0xc3, 0xa1 }); // á as raw UTF-8
    }

    [Fact]
    public void Tamper_variants_differ_from_base()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<CanonicalVector>(corpus, "vectors/canonical-byte-vectors.json");
        var baseJson = vectors.First(v => v.Id == "CB-001").Json;
        foreach (var v in vectors.Where(x => x.Id != "CB-001"))
        {
            v.Json.Should().NotBe(baseJson, v.Id);
            Encoding.UTF8.GetByteCount(v.Json).Should().Be(v.Utf8Length, v.Id);
        }
    }
}

/// <summary>Task 4.3/4.4 — signature interoperability (P-07 contract).</summary>
public class SignatureAdapterTests
{
    [Fact]
    public void Fixed_corpus_fixtures_verify_in_compact_and_der()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<SignatureVector>(corpus, "vectors/signature-vectors.json");
        var s001 = vectors.First(v => v.Id == "S-001");
        SignatureAdapter.VerifyMessage(s001.MessageUtf8, s001.SignatureCompactHex!, s001.PublicKeyHex, "compact").Should().BeTrue();
        SignatureAdapter.VerifyMessage(s001.MessageUtf8, s001.SignatureDerHex!, s001.PublicKeyHex, "der").Should().BeTrue();
    }

    [Fact]
    public void Base64_only_fixture_verifies()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<SignatureVector>(corpus, "vectors/signature-vectors.json");
        var s008 = vectors.First(v => v.Id == "S-008");
        var sig = Convert.FromBase64String(s008.SignatureCompactBase64!);
        SignatureAdapter.VerifyMessage(s008.MessageUtf8, SignatureAdapter.BytesToHex(sig), s008.PublicKeyHex, "compact").Should().BeTrue();
    }

    [Fact]
    public void Wrong_message_and_wrong_key_are_invalid()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<SignatureVector>(corpus, "vectors/signature-vectors.json");
        var s001 = vectors.First(v => v.Id == "S-001");
        var tampered = s001.MessageUtf8.Replace("12:34:56.789Z", "12:34:56.790Z");
        SignatureAdapter.VerifyMessage(tampered, s001.SignatureCompactHex!, s001.PublicKeyHex, "compact").Should().BeFalse();
        var s005 = vectors.First(v => v.Id == "S-005");
        SignatureAdapter.VerifyMessage(s005.MessageUtf8, s005.SignatureCompactBase64 is null ? string.Empty : SignatureAdapter.BytesToHex(Convert.FromBase64String(s005.SignatureCompactBase64)), s005.PublicKeyHex, "compact").Should().BeFalse();
    }

    [Fact]
    public void Malformed_signatures_return_typed_failure()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var vectors = CorpusDocuments.ReadVectors<SignatureVector>(corpus, "vectors/signature-vectors.json");
        var s006 = vectors.First(v => v.Id == "S-006");
        var decoded = SignatureAdapter.DecodeSignature(s006.SignatureCompactHex!, "compact");
        decoded.Ok.Should().BeFalse();
        decoded.Code.Should().Be("SIGNATURE_MALFORMED");
        var s007 = vectors.First(v => v.Id == "S-007");
        var der = SignatureAdapter.DecodeSignature(s007.SignatureDerHex!, "der");
        der.Ok.Should().BeFalse();
        der.Code.Should().Be("SIGNATURE_MALFORMED");
    }
}

/// <summary>Task 4.6 — report shape, redaction, and exit codes.</summary>
public class ReportAndExitCodeTests
{
    [Fact]
    public void Full_corpus_run_passes_and_report_is_secret_safe()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var result = ConformanceRunner.Run(corpus);
        var report = result.Report;
        report.Result.Should().Be("PASS");
        report.Summary.Total.Should().BeGreaterThan(100);
        report.Summary.Failed.Should().Be(0);
        report.Records.Should().BeEmpty();
    }

    [Fact]
    public void Full_corpus_completes_within_sixty_second_budget()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = ConformanceRunner.Run(corpus);
        sw.Stop();
        result.Report.Result.Should().Be("PASS");
        sw.Elapsed.TotalSeconds.Should().BeLessThan(60, "complete .NET corpus suite must finish within 60s in CI");
    }

    [Fact]
    public void Candidate_derivation_completes_within_one_second_per_mnemonic()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var result = ConformanceRunner.Run(corpus);
        var deriveTimings = result.Timings.Where(t => t.Operation == "MNEMONIC_DERIVE").ToList();
        deriveTimings.Should().NotBeEmpty();
        foreach (var t in deriveTimings)
        {
            t.Milliseconds.Should().BeLessThan(1000, $"candidate derivation for {t.ProducerId} within 1s");
        }
        result.Timings.Should().Contain(t => t.Operation == "TOTAL" && t.Milliseconds > 0);
    }

    [Fact]
    public void Per_producer_timings_contain_no_credential_values()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var result = ConformanceRunner.Run(corpus);
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Timings);
        serialized.Should().NotContain("abandon amount");
        serialized.Should().NotContain("hush-public-test-password");
        serialized.Should().NotContain("6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885");
    }

    [Fact]
    public void Tampered_corpus_produces_mismatch_records()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var tmp = Path.Combine(Path.GetTempPath(), "hush-tamper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            foreach (var f in Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(corpus, f);
                var dest = Path.Combine(tmp, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest);
            }
            // Flip one expected address byte; integrity is then regenerated so the
            // mismatch surfaces as a conformance failure, not an input failure.
            var target = Path.Combine(tmp, "vectors", "mnemonic-vectors.json");
            var text = File.ReadAllText(target);
            text = text.Replace("0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5", "0337fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5");
            File.WriteAllText(target, text);
            var report = ConformanceRunner.Run(tmp).Report;
            report.Result.Should().Be("FAIL");
            report.Records.Should().NotBeEmpty();
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Exit_code_is_zero_on_full_pass()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var reportPath = Path.Combine(Path.GetTempPath(), "hush-report-" + Guid.NewGuid().ToString("N") + ".json");
        var code = Program.Main(new[] { "--corpus", corpus, "--manifest-digest", CorpusLocator.ManifestDigest(corpus), "--report", reportPath });
        code.Should().Be(0);
        File.Exists(reportPath).Should().BeTrue();
        File.Delete(reportPath);
    }

    [Fact]
    public void Exit_code_is_two_on_invalid_corpus_input()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var reportPath = Path.Combine(Path.GetTempPath(), "hush-report-" + Guid.NewGuid().ToString("N") + ".json");
        var code = Program.Main(new[] { "--corpus", corpus, "--manifest-digest", new string('0', 64), "--report", reportPath });
        code.Should().Be(2);
    }

    [Fact]
    public void Reports_never_contain_credential_values()
    {
        var corpus = CorpusLocator.Find() ?? throw new InvalidOperationException("corpus not found");
        var reportPath = Path.Combine(Path.GetTempPath(), "hush-report-" + Guid.NewGuid().ToString("N") + ".json");
        Program.Main(new[] { "--corpus", corpus, "--manifest-digest", CorpusLocator.ManifestDigest(corpus), "--report", reportPath });
        var text = File.ReadAllText(reportPath);
        text.Should().NotContain("abandon amount"); // mnemonic words
        text.Should().NotContain("hush-public-test-password"); // .dat password
        text.Should().NotContain("6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885"); // private key
        File.Delete(reportPath);
    }
}

/// <summary>Task 4.7 — runtime isolation from HushServerNode.</summary>
public class RuntimeIsolationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "global.json")) && Directory.Exists(Path.Combine(d.FullName, "Node")))
            {
                return d.FullName;
            }
        }
        throw new InvalidOperationException("hush-server-node root not found");
    }

    [Fact]
    public void Runner_is_a_non_production_exe_tool()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot, "Tools", "HushIdentityCompatibilityConformance", "HushIdentityCompatibilityConformance.csproj"));
        csproj.Should().Contain("<OutputType>Exe</OutputType>");
    }

    [Fact]
    public void Main_solution_and_server_projects_do_not_reference_the_runner()
    {
        var sln = File.ReadAllText(Path.Combine(RepoRoot, "Node", "HushServerNode.sln"));
        sln.Should().NotContain("HushIdentityCompatibilityConformance");
        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(RepoRoot, "Node"), "*.csproj", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csproj);
            content.Should().NotContain("HushIdentityCompatibilityConformance", Path.GetFileName(csproj));
        }
    }

    [Fact]
    public void Server_projects_never_derive_user_keys_from_the_corpus()
    {
        // The runner is the only project that references the corpus path.
        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(RepoRoot, "Node"), "*.csproj", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csproj);
            content.Should().NotContain("conformance/identity", Path.GetFileName(csproj));
        }
    }
}
