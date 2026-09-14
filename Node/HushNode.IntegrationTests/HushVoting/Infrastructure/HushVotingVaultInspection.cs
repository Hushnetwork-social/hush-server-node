using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Playwright;
using Olimpo.KeyDerivation;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Read-only independent Web suite-v1 inspection. Only boolean facts leave the decryptor.</summary>
internal static class HushVotingVaultInspection
{
    internal sealed record Facts(bool MetadataMatches, bool KeysMatch, bool ConcreteKeysOnly, bool Active, bool NetworkMatches, bool DevicePasswordProtected, bool PendingRegistration,
        bool HasPendingTransaction, bool PendingTransactionMatches, bool PendingLifecycleMatches, bool PendingTransactionCleared);

    public static async Task<Facts> InspectAsync(IPage page, DerivedKeys expectedKeys, string alias, bool isPublic, bool rollback = false,
        string? expectedTransaction = null, string? expectedPendingLifecycle = null, string devicePassword = HushVotingScenario.DevicePassword)
    {
        var owned = new List<byte[]>();
        var stage = "read";
        byte[] Own(byte[] bytes) { owned.Add(bytes); return bytes; }
        byte[] Decode(string value) => Own(Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=')));
        try
        {
            var encrypted = await page.EvaluateAsync<string>("""
                async rollback => {
                    const db = await new Promise((resolve, reject) => {
                        const request = indexedDB.open('hushvoting-vault');
                        request.onsuccess = () => resolve(request.result);
                        request.onerror = () => reject(new Error('Vault inspection unavailable'));
                    });
                    try {
                        return await new Promise((resolve, reject) => {
                            const transaction = db.transaction(['vaultJournal','vaultSlots'], 'readonly');
                            transaction.onerror = () => reject(new Error('Vault inspection unavailable'));
                            const pointer = transaction.objectStore('vaultJournal').get('current');
                            pointer.onsuccess = () => {
                                const state = pointer.result;
                                if (!state || !['slot-a','slot-b'].includes(state.activeSlot)) { reject(new Error('No committed vault')); return; }
                                const key = rollback ? (state.activeSlot === 'slot-a' ? 'slot-b' : 'slot-a') : state.activeSlot;
                                const active = transaction.objectStore('vaultSlots').get(key);
                                active.onsuccess = () => {
                                    const record = active.result;
                                    if (!record || (rollback ? record.generation >= state.generation : record.generation !== state.generation) || !(record.bytes instanceof Uint8Array) || record.bytes.byteLength > 1048576) {
                                        reject(new Error('Invalid committed vault')); return;
                                    }
                                    resolve(new TextDecoder('utf-8', {fatal:true}).decode(record.bytes));
                                };
                            };
                        });
                    } finally { db.close(); }
                }
                """, rollback);
            using var envelopeDocument = JsonDocument.Parse(encrypted);
            stage = "envelope";
            var envelope = envelopeDocument.RootElement;
            var suite = envelope.GetProperty("suite");
            var kdf = suite.GetProperty("kdf");
            // Closed delivered Web suite; never let storage choose unbounded KDF work.
            if (!suite.GetProperty("id").ValueEquals("hush/vault/suite/v1") || !kdf.GetProperty("algorithm").ValueEquals("Argon2id")
                || kdf.GetProperty("minMemoryKiB").GetInt32() != 19456 || kdf.GetProperty("iterations").GetInt32() != 2
                || kdf.GetProperty("parallelism").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported inspection suite.");
            var salt = Decode(envelope.GetProperty("extensions").GetProperty("extensions").GetProperty("hush.vault.kdf-salt").GetProperty("salt").GetString()!);
            if (salt.Length != 16) throw new InvalidOperationException("Unsupported inspection salt.");
            var passwordBytes = Own(Encoding.UTF8.GetBytes(devicePassword));
            stage = "kdf";
            var passwordKey = Own(new byte[32]);
            var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id).WithVersion(Argon2Parameters.Version13)
                .WithMemoryAsKB(19456).WithIterations(2).WithParallelism(1).WithSalt(salt).Build();
            var argon = new Argon2BytesGenerator();
            argon.Init(parameters);
            argon.GenerateBytes(passwordBytes, passwordKey);
            var kek = Own(HKDF.DeriveKey(HashAlgorithmName.SHA256, passwordKey, 32, salt, Encoding.ASCII.GetBytes("hush/vault/v1/credential-kek")));
            var records = envelope.GetProperty("records");
            var ordinary = records.GetProperty("ordinary");
            var preview = envelope.GetProperty("preview");
            var aad = Own(Canonical(JsonSerializer.SerializeToElement(new
            {
                envelopeFormatVersion = envelope.GetProperty("envelopeFormatVersion"),
                parameterSuiteVersion = envelope.GetProperty("parameterSuiteVersion"),
                recordSchemaVersion = envelope.GetProperty("recordSchemaVersion"),
                platformWrapperVersion = envelope.GetProperty("platformWrapperVersion"),
                suiteId = suite.GetProperty("id"),
                kdf = new { algorithm = "Argon2id", memoryKiB = 19456, iterations = 2, parallelism = 1 },
                adapterBinding = "browser", preview,
                vaultGeneration = records.GetProperty("generation").GetProperty("active"),
                recordGeneration = ordinary.GetProperty("generation"), recordPurpose = "ordinary",
                producer = new { id = ordinary.GetProperty("producerId"), version = ordinary.GetProperty("producerVersion") },
                signingAddress = preview.GetProperty("signingAddressPrefix").GetString() + preview.GetProperty("signingAddressSuffix").GetString(),
                criticalExtensions = Array.Empty<string>()
            })));
            byte[] Decrypt(byte[] key, string nonce, string cipher)
            {
                var joined = Decode(cipher);
                if (joined.Length < 16) throw new InvalidOperationException("Invalid inspection ciphertext.");
                var plaintext = Own(new byte[joined.Length - 16]);
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(Decode(nonce), joined.AsSpan(0, joined.Length - 16), joined.AsSpan(joined.Length - 16), plaintext, aad);
                return plaintext;
            }
            var package = ordinary.GetProperty("keyPackage");
            stage = "unwrap";
            var dek = Decrypt(kek, package.GetProperty("wrappingNonce").GetString()!, package.GetProperty("wrappedDataKey").GetString()!);
            if (dek.Length != 32) throw new InvalidOperationException("Unsupported inspection data-key size.");
            stage = "record";
            var plaintext = Decrypt(dek, ordinary.GetProperty("encryptionNonce").GetString()!, ordinary.GetProperty("ciphertext").GetString()!);
            using var document = JsonDocument.Parse(plaintext);
            var record = document.RootElement;
            stage = "metadata";
            var keys = record.GetProperty("keyBinding");
            string[] concreteSchema = ["schemaVersion", "alias", "visibility", "producerId", "producerVersion", "lifecycleStatus", "networkBinding", "keyBinding", "signingPrivateKey", "encryptionPrivateKey", "protectionModeClass", "generation", "transactionDigest"];
            var version = record.GetProperty("schemaVersion").GetInt32();
            if (version == 2) concreteSchema = [.. concreteSchema, "pendingTransaction"];
            if (version is not (1 or 2) || envelope.GetProperty("recordSchemaVersion").GetInt32() != version
                || ordinary.GetProperty("schemaVersion").GetInt32() != version || preview.GetProperty("recordSchemaVersion").GetInt32() != version)
                throw new InvalidOperationException("Unsupported or inconsistent record version.");
            var hasPending = version == 2 && record.GetProperty("pendingTransaction").ValueKind != JsonValueKind.Null;
            var pendingMatches = false;
            var pendingLifecycleMatches = false;
            if (hasPending)
            {
                var pending = record.GetProperty("pendingTransaction");
                pendingLifecycleMatches = expectedPendingLifecycle is not null
                    && pending.GetProperty("lifecycle").ValueEquals(expectedPendingLifecycle);
                var transaction = pending.GetProperty("transaction");
                var exact = transaction.GetProperty("exactJson").GetString()!;
                var digest = Convert.ToHexString(SHA256.HashData(Own(Encoding.UTF8.GetBytes(exact)))).ToLowerInvariant();
                pendingMatches = expectedTransaction is not null && exact == expectedTransaction
                    && transaction.GetProperty("digest").ValueEquals(digest) && record.GetProperty("transactionDigest").ValueEquals(digest)
                    && pending.GetProperty("schemaVersion").GetInt32() == 2;
            }
            return new Facts(
                record.GetProperty("alias").ValueEquals(alias) && record.GetProperty("visibility").ValueEquals(isPublic ? "public" : "private") && preview.GetProperty("alias").ValueEquals(alias),
                keys.GetProperty("signingAddress").ValueEquals(expectedKeys.SigningPublicKey) && keys.GetProperty("encryptionAddress").ValueEquals(expectedKeys.EncryptPublicKey)
                    && record.GetProperty("signingPrivateKey").ValueEquals(expectedKeys.SigningPrivateKey) && record.GetProperty("encryptionPrivateKey").ValueEquals(expectedKeys.EncryptPrivateKey),
                records.GetProperty("mnemonic").ValueKind == JsonValueKind.Null && record.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(concreteSchema.Order()),
                record.GetProperty("lifecycleStatus").ValueEquals("Active") && preview.GetProperty("lifecycleStatus").ValueEquals("Active")
                    && record.GetProperty("generation").GetInt32() == ordinary.GetProperty("generation").GetInt32()
                    && ordinary.GetProperty("generation").GetInt32() == records.GetProperty("generation").GetProperty("active").GetInt32(),
                record.GetProperty("networkBinding").GetProperty("canonicalNetworkId").ValueEquals("hushnetwork-devnet"),
                record.GetProperty("protectionModeClass").ValueEquals("device-password"),
                record.GetProperty("lifecycleStatus").ValueEquals("PendingRegistration") && preview.GetProperty("lifecycleStatus").ValueEquals("PendingRegistration"),
                hasPending, pendingMatches, pendingLifecycleMatches,
                !hasPending && record.GetProperty("transactionDigest").ValueKind == JsonValueKind.Null);
        }
        catch (Exception) { throw new InvalidOperationException($"Independent encrypted-vault inspection failed at {stage}; credential diagnostics omitted."); }
        finally { foreach (var bytes in owned) CryptographicOperations.ZeroMemory(bytes); }
    }

    public static async Task<bool> AllRetainedSlotsContainOnlyExpectedKeysAsync(IPage page, DerivedKeys keys, string alias, bool isPublic)
    {
        var count = await page.EvaluateAsync<int>("""
            async () => {
                const db = await new Promise((resolve, reject) => { const r = indexedDB.open('hushvoting-vault'); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Vault count failed')); });
                try { return await new Promise((resolve, reject) => { const r = db.transaction('vaultSlots', 'readonly').objectStore('vaultSlots').count(); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Vault count failed')); }); }
                finally { db.close(); }
            }
            """);
        if (count is < 1 or > 2) throw new InvalidOperationException("Unexpected retained vault slot count.");
        var active = await InspectAsync(page, keys, alias, isPublic);
        if (!active.Active || !active.ConcreteKeysOnly || !active.KeysMatch || !active.NetworkMatches) return false;
        if (count == 1) return true;
        var retained = await InspectAsync(page, keys, alias, isPublic, rollback: true);
        return retained.ConcreteKeysOnly && retained.KeysMatch && retained.NetworkMatches;
    }

    private static byte[] Canonical(JsonElement element)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            void Write(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Write(property.Value); }
                    writer.WriteEndObject();
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item); writer.WriteEndArray();
                }
                else value.WriteTo(writer);
            }
            Write(element);
        }
        return output.ToArray();
    }
}
