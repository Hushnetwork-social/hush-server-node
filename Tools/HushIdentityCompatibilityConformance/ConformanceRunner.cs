using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushIdentityCompatibilityConformance.Adapters;
using HushIdentityCompatibilityConformance.Corpus;

namespace HushIdentityCompatibilityConformance;

/// <summary>
/// Executes every corpus vector (mnemonic, key, .dat, canonical, signature,
/// negative, lookup) against the .NET adapters and produces a secret-safe JSON
/// report per report.schema.json. Failure records carry contract/schema
/// version, producer ID, fixture ID, operation, field path, stable error code,
/// and expected/actual SHA-256 digests ONLY — never raw mnemonics, passwords,
/// private keys, decrypted content, or ciphertext.
/// </summary>
public static class ConformanceRunner
{
    public const string ContractVersion = "1.0.0";
    public const string SchemaVersion = "1.0.0";

    public sealed record ConformanceRecord(
        string ContractVersion,
        string SchemaVersion,
        string ProducerId,
        string FixtureId,
        string Operation,
        string FieldPath,
        string ErrorCode,
        string ExpectedDigest,
        string ActualDigest);

    public sealed record ConformanceReport(
        string SchemaVersion,
        string ContractVersion,
        string Runtime,
        string Result,
        Summary Summary,
        IReadOnlyList<ConformanceRecord> Records);

    /// <summary>Per-group timing (milliseconds) — never contains credential values.</summary>
    public sealed record Timing(string Operation, string ProducerId, double Milliseconds);

    /// <summary>Conformance run result: the secret-safe report plus side-channel timings.</summary>
    public sealed record RunResult(ConformanceReport Report, IReadOnlyList<Timing> Timings);

    public sealed record Summary(int Total, int Passed, int Failed);

    private sealed class FailureBuilder
    {
        public List<ConformanceRecord> Records { get; } = new();
        public int Total { get; set; }
    }

    /// <summary>Run the full corpus and return the secret-safe report.</summary>
    public static RunResult Run(string corpusRoot)
    {
        var f = new FailureBuilder();
        var timings = new List<Timing>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var mnemonicVectors = CorpusDocuments.ReadVectors<MnemonicVector>(corpusRoot, "vectors/mnemonic-vectors.json");
        var keyVectors = CorpusDocuments.ReadVectors<KeyVector>(corpusRoot, "vectors/key-vectors.json");
        var datVectors = CorpusDocuments.ReadVectors<DatVector>(corpusRoot, "vectors/dat-vectors.json");
        var canonicalVectors = CorpusDocuments.ReadVectors<CanonicalVector>(corpusRoot, "vectors/canonical-byte-vectors.json");
        var signatureVectors = CorpusDocuments.ReadVectors<SignatureVector>(corpusRoot, "vectors/signature-vectors.json");
        var negativeVectors = CorpusDocuments.ReadVectors<NegativeVector>(corpusRoot, "vectors/negative-vectors.json");
        var lookup = CorpusDocuments.ReadLookup(corpusRoot);

        // ---- mnemonic vectors --------------------------------------------------
        foreach (var v in mnemonicVectors)
        {
            var deriveSw = System.Diagnostics.Stopwatch.StartNew();
            var derived = DeriveCandidates(v.Mnemonic);
            deriveSw.Stop();
            timings.Add(new Timing("MNEMONIC_DERIVE", v.ProducerId, deriveSw.Elapsed.TotalMilliseconds));
            if (!derived.Ok || derived.Candidates is null)
            {
                Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "candidates", "ok", "failure");
                continue;
            }
            var candidate = derived.Candidates.FirstOrDefault(c => c.ProducerId == v.ProducerId);
            if (candidate is null)
            {
                Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "candidate", v.ProducerId, "missing");
                continue;
            }
            var seedHex = BytesToHex(DerivationAdapters.MnemonicToSeed(v.Mnemonic));
            Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "seedHex", v.SeedHex, seedHex);
            Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "signingAddress", v.SigningPublicKeyHex, candidate.SigningAddress);
            Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "encryptionAddress", v.EncryptionPublicKeyHex, candidate.EncryptionAddress);
            Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "publicKeyEncoding", v.PublicKeyEncoding, candidate.PublicKeyEncoding);
            var secrets = DeriveSelectedCredentials(v.Mnemonic, v.ProducerId);
            if (!secrets.Ok)
            {
                Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "private", "ok", "failure");
            }
            else
            {
                Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "signingPrivateKey", v.SigningPrivateKeyHex, secrets.Keys!.SigningPrivateKey);
                Check(f, v.Id, v.ProducerId, "MNEMONIC_DERIVE", "encryptionPrivateKey", v.EncryptionPrivateKeyHex, secrets.Keys.EncryptionPrivateKey);
            }
        }

        // ---- key vectors -------------------------------------------------------
        foreach (var v in keyVectors)
        {
            var pid = v.ProducerId ?? "P-00";
            if (v.Operation == "SCALAR_VALIDATE")
            {
                var ok = DerivationAdapters.IsUsableScalar(v.PrivateScalarHex ?? string.Empty);
                Check(f, v.Id, pid, v.Operation, "scalar", v.Expected, ok ? "OK" : "ERROR", ok ? null : v.ErrorCode);
                if (v.Expected == "OK" && ok && v.ExpectedPublicKeyHex is not null)
                {
                    Check(f, v.Id, pid, v.Operation, "publicKey", v.ExpectedPublicKeyHex, DerivationAdapters.DerivePublicKey(v.PrivateScalarHex!, compressed: true));
                }
            }
            else if (v.Operation is "PUBLIC_KEY_DERIVE" or "POINT_EQUIVALENCE")
            {
                try
                {
                    var pub = DerivationAdapters.DerivePublicKey(v.PrivateScalarHex!, v.Encoding == "COMPRESSED");
                    Check(f, v.Id, pid, v.Operation, "publicKey", v.ExpectedPublicKeyHex, pub, pub == v.ExpectedPublicKeyHex ? null : v.ErrorCode);
                }
                catch
                {
                    Check(f, v.Id, pid, v.Operation, "publicKey", v.Expected, "ERROR", v.ErrorCode);
                }
            }
            else if (v.Operation == "DECODE")
            {
                var point = DerivationAdapters.DecodePublicKeyPoint(v.InputHex ?? string.Empty);
                if (v.Expected == "ERROR")
                {
                    Check(f, v.Id, pid, v.Operation, "decode", "ERROR", point is null ? "ERROR" : "OK", v.ErrorCode);
                }
                else
                {
                    Check(f, v.Id, pid, v.Operation, "x", v.ExpectedPointXHex, point?.XHex, v.ErrorCode);
                    Check(f, v.Id, pid, v.Operation, "y", v.ExpectedPointYHex, point?.YHex, v.ErrorCode);
                }
            }
        }

        // ---- .dat vectors ------------------------------------------------------
        var datPositive = datVectors.FirstOrDefault(v => v.Id == "D-001");
        foreach (var v in datVectors)
        {
            var pid = v.ProducerId ?? "P-04";
            if (v.Operation == "OVERSIZED")
            {
                var baseEnvelope = Convert.FromHexString(datPositive?.EnvelopeHex ?? string.Empty);
                var oversized = baseEnvelope.Concat(new byte[DatAdapter.DatMaxEnvelopeBytes + 1]).ToArray();
                var result = DatAdapter.DecodeDatV1(oversized, datPositive?.Password ?? string.Empty);
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, result.Ok ? "OK" : result.Code, v.ErrorCode);
                continue;
            }
            if (v.Operation == "DECRYPT")
            {
                var decrypted = DatAdapter.DecodeDatV1(Convert.FromHexString(v.EnvelopeHex ?? string.Empty), v.Password ?? string.Empty);
                if (v.Expected == "OK")
                {
                    Check(f, v.Id, pid, v.Operation, "payload", v.ExpectedPayloadJson, decrypted.Ok ? SerializeRecord(decrypted.Result!.Record) : "failure");
                }
                else
                {
                    Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, decrypted.Ok ? "OK" : decrypted.Code, v.ErrorCode);
                }
                continue;
            }
            if (v.Operation == "KEY_CONSISTENCY")
            {
                var parsed = DatAdapter.ParsePortableCredentialsStrict(v.PayloadJson ?? string.Empty);
                if (!parsed.Ok)
                {
                    Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, parsed.Code, v.ErrorCode);
                    continue;
                }
                var consistency = DatAdapter.ValidateKeyConsistency(parsed.Record!);
                var code = !consistency.PrivatePublicConsistent ? "DAT_KEY_MISMATCH" :
                    !consistency.MnemonicKeyConsistent ? "DAT_MNEMONIC_KEY_MISMATCH" : "OK";
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, code, v.ErrorCode);
                continue;
            }
            var parsedParse = DatAdapter.ParsePortableCredentialsStrict(v.PayloadJson ?? string.Empty);
            if (v.Expected == "ERROR")
            {
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, parsedParse.Ok ? "OK" : parsedParse.Code, v.ErrorCode);
            }
        }

        // ---- canonical byte vectors -------------------------------------------
        var baseCanonical = canonicalVectors.FirstOrDefault(v => v.Id == "CB-001");
        if (baseCanonical is not null)
        {
            var serialized = CanonicalTransactionAdapter.SerializeUnsignedTransaction(CanonicalTransactionAdapter.Parse(baseCanonical.Json));
            Check(f, baseCanonical.Id, "P-01", "SERIALIZE", "json", baseCanonical.Json, serialized);
            Check(f, baseCanonical.Id, "P-01", "SERIALIZE", "utf8Hex", baseCanonical.Utf8Hex, BytesToHex(Encoding.UTF8.GetBytes(serialized)));
            Check(f, baseCanonical.Id, "P-01", "SERIALIZE", "utf8Length", baseCanonical.Utf8Length.ToString(), Encoding.UTF8.GetByteCount(serialized).ToString());
        }
        foreach (var v in canonicalVectors)
        {
            if (v.Id == "CB-001") continue;
            var bytes = Encoding.UTF8.GetBytes(v.Json);
            Check(f, v.Id, "P-01", "TAMPER", "utf8Hex", v.Utf8Hex, BytesToHex(bytes));
            Check(f, v.Id, "P-01", "TAMPER", "utf8Length", v.Utf8Length.ToString(), bytes.Length.ToString());
            Check(f, v.Id, "P-01", "TAMPER", "differsFromBase", "DIFFERENT", baseCanonical is not null && v.Json != baseCanonical.Json ? "DIFFERENT" : "SAME");
        }

        // ---- signature vectors ------------------------------------------------
        foreach (var v in signatureVectors)
        {
            var pid = v.ProducerId ?? "P-07";
            if (v.Operation == "DECODE")
            {
                var format = v.SignatureCompactHex is not null ? "compact" : "der";
                var decoded = SignatureAdapter.DecodeSignature(v.SignatureCompactHex ?? v.SignatureDerHex ?? string.Empty, format);
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, decoded.Ok ? "OK" : decoded.Code, v.ErrorCode);
            }
            else
            {
                bool result;
                if (v.SignatureDerHex is not null)
                {
                    result = SignatureAdapter.VerifyMessage(v.MessageUtf8, v.SignatureDerHex, v.PublicKeyHex, "der");
                }
                else if (v.SignatureCompactHex is not null)
                {
                    result = SignatureAdapter.VerifyMessage(v.MessageUtf8, v.SignatureCompactHex, v.PublicKeyHex, "compact");
                }
                else
                {
                    var sig = Convert.FromBase64String(v.SignatureCompactBase64 ?? string.Empty);
                    result = sig.Length == 64 ? SignatureAdapter.VerifyMessage(v.MessageUtf8, BytesToHex(sig), v.PublicKeyHex, "compact") : false;
                }
                var expectedOutcome = v.Expected == "VALID";
                Check(f, v.Id, pid, v.Operation, "outcome", expectedOutcome.ToString(), result.ToString(), expectedOutcome == result ? null : v.ErrorCode);
            }
        }

        // ---- negative vectors -------------------------------------------------
        foreach (var v in negativeVectors)
        {
            var pid = v.ProducerId ?? "P-00";
            if (v.Operation == "MNEMONIC_VALIDATE")
            {
                var validation = DerivationAdapters.ValidateMnemonicForProducer(v.Input, pid);
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, validation.Valid ? "VALID" : validation.Code, v.ErrorCode);
            }
            else if (v.Operation == "MNEMONIC_DERIVE")
            {
                var derived = DeriveCandidates(v.Input, v.Passphrase);
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, derived.Ok ? "OK" : derived.Code, v.ErrorCode);
            }
            else if (v.Operation == "PRODUCER_SELECT")
            {
                var derived = DeriveSelectedCredentials(M24Of(), v.Input);
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, derived.Ok ? "OK" : derived.Code, v.ErrorCode);
            }
            else if (v.Operation == "VERSION_SELECT")
            {
                Check(f, v.Id, pid, v.Operation, "errorCode", v.ErrorCode, v.Input == ContractVersion ? "OK" : "UNSUPPORTED_VERSION", v.ErrorCode);
            }
        }

        // ---- lookup scenarios --------------------------------------------------
        foreach (var s in lookup.Scenarios)
        {
            var deduped = DedupeForLookup(s.Candidates);
            var result = ResolveLookup(deduped, lookup.Registry);
            Check(f, s.Id, "P-00", "LOOKUP", "matchCount", s.Expected.MatchCount.ToString(), result.MatchCount.ToString());
            Check(f, s.Id, "P-00", "LOOKUP", "ambiguous", s.Expected.Ambiguous.ToString(), result.Ambiguous.ToString());
            if (s.Expected.Producers is not null)
            {
                var producers = string.Join(',', result.Matches.SelectMany(m => m.ProducerIds).Distinct().OrderBy(x => x, StringComparer.Ordinal));
                var expected = string.Join(',', s.Expected.Producers.OrderBy(x => x, StringComparer.Ordinal));
                Check(f, s.Id, "P-00", "LOOKUP", "producers", expected, producers);
            }
        }

        sw.Stop();
        timings.Add(new Timing("TOTAL", "P-00", sw.Elapsed.TotalMilliseconds));

        return new RunResult(
            new ConformanceReport(
                SchemaVersion,
                ContractVersion,
                "dotnet",
                f.Records.Count == 0 ? "PASS" : "FAIL",
                new Summary(f.Total, f.Total - f.Records.Count, f.Records.Count),
                f.Records),
            timings);
    }

    private static string M24Of() => "abandon amount liar amount expire adjust cage candy arch gather drum bullet absurd math era live bid rhythm alien crouch range attend journey unaware";

    // ---- candidate derivation (mirror of the TypeScript API) ------------------

    public sealed record CandidateDescriptor(string ProducerId, string ProducerName, int Precedence, List<string> ProducerIds, string SigningAddress, string EncryptionAddress, string PublicKeyEncoding)
    {
        public List<string> ProducerIds { get; set; } = ProducerIds;
    }

    public sealed record DerivedCandidatesResult(bool Ok, string? Code, List<CandidateDescriptor>? Candidates, List<(string ProducerId, string? Code)> RejectedProducers);

    public static DerivedCandidatesResult DeriveCandidates(string mnemonic, string? passphrase = null)
    {
        if (passphrase is not null && passphrase.Length > 0) return new DerivedCandidatesResult(false, "UNSUPPORTED_PASSPHRASE", null, new());
        if (mnemonic.Trim().Length == 0) return new DerivedCandidatesResult(false, "INVALID_MNEMONIC", null, new());

        var candidates = new List<CandidateDescriptor>();
        var rejected = new List<(string ProducerId, string? Code)>();
        foreach (var producer in DerivationAdapters.ApprovedProducers)
        {
            var validation = DerivationAdapters.ValidateMnemonicForProducer(mnemonic, producer.ProducerId);
            if (!validation.Valid)
            {
                rejected.Add((producer.ProducerId, validation.Code));
                continue;
            }
            var derived = DerivationAdapters.DeriveProducerKeys(producer.ProducerId, mnemonic);
            if (!derived.Ok)
            {
                rejected.Add((producer.ProducerId, derived.Code));
                continue;
            }
            candidates.Add(new CandidateDescriptor(
                producer.ProducerId,
                producer.Name,
                producer.Precedence,
                new List<string> { producer.ProducerId },
                derived.Keys!.SigningAddress,
                derived.Keys.EncryptionAddress,
                derived.Keys.PublicKeyEncoding));
        }

        if (candidates.Count == 0)
        {
            var firstRejection = rejected.FirstOrDefault();
            return new DerivedCandidatesResult(false, firstRejection.Code ?? "INVALID_MNEMONIC", null, rejected);
        }

        return new DerivedCandidatesResult(true, null, DedupeCandidates(candidates), rejected);
    }

    private static List<CandidateDescriptor> DedupeCandidates(List<CandidateDescriptor> candidates)
    {
        var byKey = new Dictionary<string, CandidateDescriptor>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var c in candidates)
        {
            var key = $"{c.SigningAddress}|{c.EncryptionAddress}";
            if (byKey.TryGetValue(key, out var existing))
            {
                var producerIds = existing.ProducerIds.Concat(c.ProducerIds).Distinct(StringComparer.Ordinal).ToList();
                if (c.Precedence < existing.Precedence)
                {
                    byKey[key] = new CandidateDescriptor(c.ProducerId, c.ProducerName, c.Precedence, producerIds, existing.SigningAddress, existing.EncryptionAddress, existing.PublicKeyEncoding);
                }
                else
                {
                    byKey[key] = new CandidateDescriptor(existing.ProducerId, existing.ProducerName, existing.Precedence, producerIds, existing.SigningAddress, existing.EncryptionAddress, existing.PublicKeyEncoding);
                }
                continue;
            }
            byKey[key] = c;
            order.Add(key);
        }
        return order.Select(k => byKey[k]).ToList();
    }

    public sealed record SelectedCredentialsResult(bool Ok, string? Code, DerivationAdapters.DerivedKeys? Keys);

    public static SelectedCredentialsResult DeriveSelectedCredentials(string mnemonic, string producerId, string? passphrase = null)
    {
        var producer = DerivationAdapters.ApprovedProducers.FirstOrDefault(p => p.ProducerId == producerId);
        if (producer.ProducerId is null) return new SelectedCredentialsResult(false, "UNSUPPORTED_PRODUCER", null);
        if (passphrase is not null && passphrase.Length > 0) return new SelectedCredentialsResult(false, "UNSUPPORTED_PASSPHRASE", null);
        if (mnemonic.Trim().Length == 0) return new SelectedCredentialsResult(false, "INVALID_MNEMONIC", null);
        var validation = DerivationAdapters.ValidateMnemonicForProducer(mnemonic, producerId);
        if (!validation.Valid) return new SelectedCredentialsResult(false, validation.Code, null);
        var derived = DerivationAdapters.DeriveProducerKeys(producerId, mnemonic);
        if (!derived.Ok) return new SelectedCredentialsResult(false, derived.Code, null);
        return new SelectedCredentialsResult(true, null, derived.Keys);
    }

    // ---- lookup resolution (mirror of the TypeScript API) ---------------------

    public sealed record LookupMatch(string RegistryId, string ProfileAlias, List<string> ProducerIds);

    public sealed record LookupResolution(int MatchCount, bool Ambiguous, List<LookupMatch> Matches);

    private static List<CandidateDescriptor> DedupeForLookup(IReadOnlyList<LookupCandidate> candidates)
    {
        var byKey = new Dictionary<string, CandidateDescriptor>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            var key = $"{c.SigningAddress}|{c.EncryptionAddress}";
            if (byKey.TryGetValue(key, out var existing))
            {
                existing.ProducerIds = existing.ProducerIds.Concat(new[] { c.ProducerId }).Distinct(StringComparer.Ordinal).ToList();
                continue;
            }
            byKey[key] = new CandidateDescriptor(
                c.ProducerId,
                c.ProducerId,
                0,
                new List<string> { c.ProducerId },
                c.SigningAddress,
                c.EncryptionAddress,
                c.SigningAddress.StartsWith("04", StringComparison.Ordinal) ? "UNCOMPRESSED" : "COMPRESSED");
        }
        return byKey.Values.ToList();
    }

    public static LookupResolution ResolveLookup(IReadOnlyList<CandidateDescriptor> candidates, IReadOnlyList<LookupRegistryEntry> registry)
    {
        var matches = new List<LookupMatch>();
        for (var i = 0; i < registry.Count; i++)
        {
            var entry = registry[i];
            var matching = candidates.Where(c =>
                string.Equals(c.SigningAddress, entry.SigningAddress, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.EncryptionAddress, entry.EncryptionAddress, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count > 0)
            {
                var producerIds = matching.SelectMany(m => m.ProducerIds).Distinct(StringComparer.Ordinal).ToList();
                matches.Add(new LookupMatch((i + 1).ToString("000"), entry.ProfileAlias, producerIds));
            }
        }
        return new LookupResolution(matches.Count, matches.Count > 1, matches);
    }

    // ---- reporting helpers ----------------------------------------------------

    private static void Check(FailureBuilder f, string fixtureId, string producerId, string operation, string fieldPath, string? expected, string? actual, string? expectedErrorCode = null)
    {
        f.Total += 1;
        var pass = expected is not null && actual is not null && expected == actual;
        if (pass) return;
        f.Records.Add(new ConformanceRecord(
            ContractVersion,
            SchemaVersion,
            producerId,
            fixtureId,
            operation,
            fieldPath,
            expectedErrorCode ?? "MISMATCH",
            DigestOf(expected),
            DigestOf(actual)));
    }

    private static string DigestOf(string? value) => BytesToHex(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static string SerializeRecord(DatAdapter.PortableCredentialsRecord record)
    {
        // Declaration order must match JSON.stringify of the historical producer.
        return JsonSerializer.Serialize(new
        {
            record.ProfileName,
            record.PublicSigningAddress,
            record.PrivateSigningKey,
            record.PublicEncryptAddress,
            record.PrivateEncryptKey,
            record.IsPublic,
            record.Mnemonic,
        });
    }

    private static string BytesToHex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
