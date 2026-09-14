using System.Security.Cryptography;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace HushVoting.IntegrationTests.Infrastructure;

internal sealed record CorpusAggregate(int Available, int Attempted, int Imported, int Unchanged, int Failed, bool Cancelled);

// EPIC-001 -> FEAT-009 AC-009-074/075/076/077 -> Phase 6 Tasks 6.9/6.10,
// Phase 7 Tasks 7.1/7.2. Only aggregate facts leave the controlled-file loop.
internal sealed class HushVotingCorpusJourney(HushVotingScenario scenario)
{
    private string[] _networkSecrets = [];
    private int _denied;
    private int _secretRequests;
    private int _allowed;

    public async Task PrepareAsync()
    {
        await HushVotingArtifactClient.RequireAsync();
        await scenario.PrepareCorpusBrowserAsync();
        _networkSecrets = []; _denied = 0; _secretRequests = 0; _allowed = 0;
        var origin = new Uri(scenario.BaseUrl);
        await scenario.Context.RouteAsync("**/*", async route =>
        {
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var target)
                || target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port)
            {
                Interlocked.Increment(ref _denied);
                await route.AbortAsync();
                return;
            }
            var headers = await route.Request.AllHeadersAsync();
            var text = route.Request.Url + "\n" + route.Request.PostData + "\n" + string.Join('\n', headers.Values);
            if (_networkSecrets.Any(value => text.Contains(value, StringComparison.Ordinal)
                || text.Contains(Uri.EscapeDataString(value), StringComparison.Ordinal)))
            {
                Interlocked.Increment(ref _secretRequests);
                await route.AbortAsync();
                return;
            }
            Interlocked.Increment(ref _allowed);
            await route.ContinueAsync();
        });
        await scenario.Context.RouteWebSocketAsync("**/*", async route =>
        {
            Interlocked.Increment(ref _denied);
            await route.CloseAsync();
        });
        await scenario.Page.GotoAsync("/");
        // A blank-page navigation probes the route itself. The application CSP
        // can block an in-page fetch before Playwright routing even sees it.
        var probe = await scenario.Context.NewPageAsync();
        var beforeProbe = _denied; var blocked = false;
        try { await probe.GotoAsync("http://hushvoting-corpus-denied.invalid/probe"); }
        catch (PlaywrightException) { blocked = true; }
        finally { await probe.CloseAsync(); }
        if (!blocked || _denied - beforeProbe != 1 || _allowed == 0 || scenario.Page.Video is not null)
            throw new InvalidOperationException($"Corpus browser guard failed: blocked={blocked}; denied={_denied - beforeProbe}; allowed={_allowed}; capture={scenario.Page.Video is not null}.");
        _denied = 0;
    }

    public async Task<CorpusAggregate> RunAsync(IReadOnlyList<string> paths, string password,
        CancellationToken cancellation = default, Func<Task>? publicMutationProbe = null,
        Func<string, HushVotingCorpusEnvelope, bool>? validateShape = null)
    {
        if (paths.Count is < 1 or > 32) throw new InvalidOperationException("Corpus size is outside the bounded harness.");
        var attempted = 0; var imported = 0; var unchanged = 0; var failed = 0; var cancelled = false;
        foreach (var path in paths)
        {
            if (cancellation.IsCancellationRequested) { cancelled = true; break; }
            HushVotingCorpusSource? source = null;
            var fileFailed = false;
            try
            {
                // These actual fixture and browser checks precede each source open.
                await PrepareAsync();
                await HushVotingCorpusEnvelope.RegisterChunksAsync(path);
                await HushVotingCorpusEnvelope.RegisterChunksAsync(Path.GetFileName(path));
                await HushVotingCorpusEnvelope.RegisterChunksAsync(password);
                var opened = await HushVotingCorpusSource.OpenAsync(path, cancellation);
                attempted++;
                source = opened.Source;
                if (source is null)
                {
                    cancelled = opened.Failure == CorpusSourceFailure.Cancelled;
                    fileFailed = !cancelled;
                    continue;
                }
                await HushVotingCorpusEnvelope.RegisterChunksAsync(Convert.ToBase64String(source.Snapshot.Span));
                await HushVotingCorpusEnvelope.RegisterChunksAsync(Convert.ToHexString(source.Snapshot.Span));
                await HushVotingCorpusEnvelope.RegisterChunksAsync(Convert.ToHexString(source.Snapshot.Span).ToLowerInvariant());
                using var oracle = HushVotingCorpusEnvelope.Open(source.Snapshot.Span, password);
                if (oracle is null) { fileFailed = true; continue; }
                await oracle.RegisterArtifactValuesAsync();
                if (validateShape is not null && !validateShape(path, oracle)) { fileFailed = true; continue; }
                _networkSecrets = [password, oracle.Keys.SigningPrivateKey, oracle.Keys.EncryptPrivateKey,
                    Convert.ToBase64String(source.Snapshot.Span), Convert.ToHexString(source.Snapshot.Span)];
                await ImportAsync(source.Snapshot, password, oracle);
                if (_denied != 0 || _secretRequests != 0) throw new InvalidOperationException();
                imported++;
                if (publicMutationProbe is not null) await publicMutationProbe();
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch { fileFailed = true; }
            finally
            {
                if (source is not null)
                {
                    // Check even after a browser/oracle failure, then wipe and close the original descriptor.
                    try
                    {
                        if (await source.CheckUnchangedAsync() == CorpusSourceFailure.None) unchanged++;
                        else fileFailed = true;
                    }
                    finally { source.Dispose(); }
                }
                if (fileFailed) failed++;
                _networkSecrets = [];
                if (scenario.Context is not null) await scenario.Context.CloseAsync();
            }
            if (cancelled) break;
        }
        await HushVotingArtifactClient.CheckAsync();
        return new(paths.Count, attempted, imported, unchanged, failed, cancelled);
    }

    private async Task ImportAsync(ReadOnlyMemory<byte> envelope, string password, HushVotingCorpusEnvelope oracle)
    {
        const string alias = HushVotingIdentityJourney.Alias;
        var existing = await scenario.Identities.GetIdentityAsync(new() { PublicSigningAddress = oracle.Keys.SigningPublicKey },
            deadline: DateTime.UtcNow.AddSeconds(10));
        if (!existing.Successfull) await HushVotingServerIdentity.RegisterAsync(scenario, oracle.Keys, alias);
        else if (existing.PublicEncryptAddress != oracle.Keys.EncryptPublicKey || existing.ProfileName != alias || existing.IsPublic)
            throw new InvalidOperationException("Controlled fixture identity has incompatible prior state.");

        var page = scenario.Page;
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Restore Credential File") }).ClickAsync();
        var transfer = envelope.ToArray();
        try
        {
            // The real Web file input receives original bytes under a non-identifying filename.
            await page.GetByTestId("credential-file-input").SetInputFilesAsync(new FilePayload
                { Name = "controlled-source.dat", MimeType = "application/octet-stream", Buffer = transfer });
        }
        finally { CryptographicOperations.ZeroMemory(transfer); }
        await HushVotingIdentityJourney.FillSecretAsync(page.GetByTestId("backup-password-input"), password);
        var queries = scenario.Faults.IdentityQueryCount;
        await page.GetByTestId("submit-password").ClickAsync();
        await Expect(page.GetByTestId("restore-device-password")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(page.GetByTestId("backup-password-input")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("authenticated-shell")).ToHaveCountAsync(0);
        if (scenario.Faults.IdentityQueryCount <= queries) throw new InvalidOperationException();
        await HushVotingIdentityJourney.FillSecretAsync(page.GetByTestId("restore-device-password"), HushVotingScenario.DevicePassword);
        await HushVotingIdentityJourney.FillSecretAsync(page.GetByTestId("restore-device-password-confirmation"), HushVotingScenario.DevicePassword);
        var submissions = scenario.Faults.SubmittedTransactions.Count;
        var beforeActivation = scenario.Faults.IdentityQueryCount;
        using var licenceAdmission = scenario.Node.StartListeningForTransactions(timeout: TimeSpan.FromSeconds(30));
        await page.GetByTestId("submit-protection").ClickAsync();
        using var activation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (scenario.Faults.SubmittedTransactions.Count == submissions && !await page.GetByTestId("authenticated-shell").IsVisibleAsync())
            await Task.Delay(20, activation.Token);
        if (scenario.Faults.SubmittedTransactions.Count > submissions)
        {
            await licenceAdmission.WaitAsync();
            await scenario.Blocks.ProduceBlockAsync();
        }
        await Expect(page.GetByTestId("authenticated-shell")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (scenario.Faults.IdentityQueryCount <= beforeActivation
            || !await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(page, oracle.Keys, alias, false))
            throw new InvalidOperationException("Controlled import retained unexpected encrypted keys or metadata.");
    }
}
