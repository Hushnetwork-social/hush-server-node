using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using Olimpo.KeyDerivation;

namespace HushVoting.IntegrationTests.Infrastructure;

// FEAT-008 AC-008-017, Phase 3 Tasks 3.1/3.2 and Phase 7 Tasks 7.1/7.2.
// Known public-vector observations only. No heap dump, DOM snapshot or secret diagnostic.
internal sealed class HushVotingRecoveryContainment(HushVotingScenario scenario)
{
    internal static readonly string[] Words = [.. Enumerable.Repeat("abandon", 23), "art"];
    private string[] _patterns = [];
    private int _requests, _networkLeaks, _otherOrigins;
    private readonly List<string> _failures = [];
    private int _checkpoints;
    public DerivedKeys Keys { get; private set; } = null!;

    public async Task ArmAsync()
    {
        var phrase = string.Join(' ', Words);
        Keys = HushVotingTestIdentity.DeriveP01(phrase);
        var historical = DeterministicKeyGenerator.DeriveKeys(phrase);
        var seed = MnemonicGenerator.MnemonicToSeed(phrase);
        try
        {
            var values = new List<string> { phrase, HushVotingScenario.DevicePassword };
            foreach (var value in new[] { Keys.SigningPrivateKey, Keys.EncryptPrivateKey, historical.SigningPrivateKey,
                         historical.EncryptPrivateKey, Convert.ToHexString(seed) })
            {
                values.Add(value.ToLowerInvariant()); values.Add(value.ToUpperInvariant());
                var bytes = Convert.FromHexString(value);
                try
                {
                    var encoded = Convert.ToBase64String(bytes);
                    values.Add(encoded); values.Add(encoded.Replace('+', '-').Replace('/', '_').TrimEnd('='));
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            _patterns = values.Distinct().ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(seed); }
        await HushVotingArtifactClient.RequireAsync();
        await HushVotingArtifactClient.RegisterAsync(_patterns);
        scenario.Context.Request += ObserveRequest;
        await scenario.Page.AddInitScriptAsync("(() => { const patterns = " + JsonSerializer.Serialize(_patterns) + ";" + Core + PageMonitor + " })();");
    }

    private void ObserveRequest(object? sender, IRequest request)
    {
        try
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
            Interlocked.Increment(ref _requests);
            if (uri.GetLeftPart(UriPartial.Authority) != scenario.BaseUrl) Interlocked.Increment(ref _otherOrigins);
            var text = Uri.UnescapeDataString(request.Url) + (request.PostData ?? "")
                + string.Join('\n', request.Headers.Select(header => header.Key + ":" + header.Value));
            if (_patterns.Any(text.Contains)) Interlocked.Increment(ref _networkLeaks);
        }
        catch { Interlocked.Increment(ref _networkLeaks); }
    }

    public string WorkerScript => "(() => { const patterns = " + JsonSerializer.Serialize(_patterns) + ";" + Core + """
        let writes = 0, leaks = 0;
        const originals = [];
        for (const method of ['put', 'add']) {
            const original = IDBObjectStore.prototype[method];
            originals.push(() => { IDBObjectStore.prototype[method] = original; });
            IDBObjectStore.prototype[method] = function(...args) {
                writes++; if (scan(args)) leaks++;
                return Reflect.apply(original, this, args);
            };
        }
        for (const method of ['log', 'info', 'warn', 'error', 'debug']) {
            const original = console[method];
            originals.push(() => { console[method] = original; });
            console[method] = function(...args) { if (scan(args)) leaks++; return Reflect.apply(original, this, args); };
        }
        globalThis.hvRecoveryStorageClean = () => controls && errors === 0 && writes > 0 && leaks === 0;
        globalThis.hvRecoveryStorageRestore = () => { originals.forEach(restore => restore()); return true; };
        return controls;
        })()
        """;

    public async Task CheckAsync(string checkpoint)
    {
        int[] facts;
        try { facts = await scenario.Page.EvaluateAsync<int[]>("() => hvRecoveryContainment()"); }
        catch { throw new InvalidOperationException("Recovery containment inspection failed; sensitive diagnostics suppressed."); }
        if (facts[0] == 0 || facts[1] == 0 || facts[2] == 0) _failures.Add(checkpoint + ":observer-not-active");
        if (facts[3] != 0) _failures.Add(checkpoint + ":inspection-incomplete");
        foreach (var (offset, name) in new[] { (4, "react-state"), (5, "worker-reply"), (6, "ordinary-message"),
                     (7, "history"), (8, "storage"), (9, "console") })
            if (facts[offset] != 0) _failures.Add(checkpoint + ":" + name);
        _checkpoints++;
    }

    public async Task FinishAsync()
    {
        await CheckAsync("authenticated");
        _requests.Should().BeGreaterThan(0);
        _networkLeaks.Should().Be(0, "actual browser/worker HTTP requests must contain no recovery credentials");
        _otherOrigins.Should().Be(0, "no analytics or third-party endpoint may receive this journey");
        foreach (var transaction in scenario.Faults.SubmittedTransactions)
            _patterns.Any(transaction.Contains).Should().BeFalse("signed node transactions carry public profile material only");
        scenario.Faults.IdentityQueryCount.Should().BeGreaterThan(0);
        scenario.Faults.SubmittedTransactions.Count.Should().Be(2);
        _checkpoints.Should().BeGreaterThanOrEqualTo(4);
        var channels = await scenario.Page.EvaluateAsync<int[]>("() => hvRecoveryChannels()");
        channels[0].Should().BeGreaterThan(0, "actual worker replies must be inspected");
        channels[1].Should().BeGreaterThan(0, "actual ordinary messages must be inspected");
        channels[2].Should().Be(1, "one normalized phrase crosses the dedicated secret-transfer channel");
        _failures.Should().BeEmpty("every observed secret boundary must remain clear");
        (await HushVotingVaultInspection.AllRetainedSlotsContainOnlyExpectedKeysAsync(
            scenario.Page, Keys, "Recovered voting identity", true)).Should().BeTrue();
        await HushVotingArtifactClient.CheckAsync();
    }

    private const string Core = """
        let errors = 0;
        const contains = value => patterns.some(pattern => value.includes(pattern));
        const scan = input => {
            const seen = new WeakSet(); let visited = 0;
            const visit = value => {
                if (typeof value === 'string') return contains(value);
                if (!value || typeof value !== 'object' || value === globalThis
                    || (typeof Node !== 'undefined' && value instanceof Node)) return false;
                if (seen.has(value)) return false;
                seen.add(value);
                if (++visited > 50000) throw new Error('Inspection bound');
                if (value instanceof ArrayBuffer || ArrayBuffer.isView(value)) {
                    const bytes = value instanceof ArrayBuffer ? new Uint8Array(value) : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
                    if (bytes.length > 1048576) throw new Error('Inspection bound');
                    return contains(new TextDecoder().decode(bytes)) || (bytes.length <= 64 && contains([...bytes].map(v => v.toString(16).padStart(2, '0')).join('')));
                }
                if (value instanceof Map) return [...value].some(([key, item]) => visit(key) || visit(item));
                if (value instanceof Set) return [...value].some(visit);
                if (Array.isArray(value) && [32, 64].includes(value.length) && value.every(item => Number.isInteger(item) && item >= 0 && item < 256)) return visit(Uint8Array.from(value));
                if (Array.isArray(value) && value.length > 0 && value.every(item => typeof item === 'string') && contains(value.join(' '))) return true;
                return Object.entries(Object.getOwnPropertyDescriptors(value)).some(([key, property]) =>
                    key !== '_owner' && (contains(key) || ('value' in property && visit(property.value))));
            };
            try { return visit(input); } catch { errors++; return true; }
        };
        const cyclic = { safe: 'public metadata' }; cyclic.self = cyclic;
        const privateBytes = Uint8Array.from(patterns[2].match(/../g), part => parseInt(part, 16));
        const controls = scan(patterns[0]) && scan({ value: patterns[0].split(' ') })
            && scan(new TextEncoder().encode(patterns[0])) && scan(privateBytes) && scan([...privateBytes]) && !scan(cyclic);
        privateBytes.fill(0);
        """;

    private const string PageMonitor = """
        const counts = { renderer: 0, commits: 0, react: 0, reply: 0, message: 0, history: 0, storage: 0, console: 0 };
        let replies = 0, messages = 0, transfers = 0;
        const roots = new Set();
        const inspectRoot = root => {
            let visited = 0; const stack = [root.current], seen = new Set();
            while (stack.length) {
                const fiber = stack.pop();
                if (!fiber || seen.has(fiber)) continue;
                seen.add(fiber); if (++visited > 10000) { errors++; return; }
                if (scan(fiber.memoizedProps) || scan(fiber.memoizedState) || scan(fiber.dependencies)) counts.react++;
                stack.push(fiber.child, fiber.sibling);
            }
        };
        globalThis.__REACT_DEVTOOLS_GLOBAL_HOOK__ = {
            supportsFiber: true,
            inject: () => ++counts.renderer,
            onCommitFiberRoot: (_id, root) => { counts.commits++; roots.add(root); inspectRoot(root); },
            onCommitFiberUnmount: () => {}
        };
        const worker = globalThis.SharedWorker;
        globalThis.SharedWorker = new Proxy(worker, { construct(target, args, newTarget) {
            const result = Reflect.construct(target, args, newTarget);
            result.port.addEventListener('message', event => { replies++; if (scan(event.data)) counts.reply++; });
            return result;
        } });
        const send = MessagePort.prototype.postMessage;
        MessagePort.prototype.postMessage = function(message, ...rest) {
            const allowed = message?.kind === 'secret-transfer' && ['mnemonic', 'devicePassword'].includes(message.purpose);
            if (allowed) {
                const { value, ...metadata } = message;
                if (scan(metadata)) counts.message++;
                if (message.purpose === 'mnemonic') { transfers++; if (value !== patterns[0]) counts.message++; }
            } else { messages++; if (scan(message)) counts.message++; }
            return Reflect.apply(send, this, [message, ...rest]);
        };
        for (const method of ['pushState', 'replaceState']) {
            const original = history[method];
            history[method] = function(...args) { if (scan(args)) counts.history++; return Reflect.apply(original, this, args); };
        }
        const setItem = Storage.prototype.setItem;
        Storage.prototype.setItem = function(...args) { if (scan(args)) counts.storage++; return Reflect.apply(setItem, this, args); };
        for (const method of ['log', 'info', 'warn', 'error', 'debug']) {
            const original = console[method];
            console[method] = function(...args) { if (scan(args)) counts.console++; return Reflect.apply(original, this, args); };
        }
        globalThis.addEventListener('error', event => { if (scan(event.message)) counts.console++; });
        globalThis.addEventListener('unhandledrejection', event => { if (scan(event.reason)) counts.console++; });
        globalThis.hvRecoveryChannels = () => [replies, messages, transfers];
        globalThis.hvRecoveryContainment = async () => {
            roots.forEach(inspectRoot);
            if (scan(history.state) || scan(location.href)) counts.history++;
            if (scan({ ...localStorage }) || scan({ ...sessionStorage }) || scan(document.cookie)) counts.storage++;
            if ((await caches.keys()).length || (await navigator.serviceWorker.getRegistrations()).length) counts.storage++;
            for (const metadata of await indexedDB.databases()) {
                if (metadata.name !== 'hushvoting-vault') { counts.storage++; continue; }
                const db = await new Promise((resolve, reject) => { const r = indexedDB.open(metadata.name); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Inspection failed')); });
                try {
                    for (const store of db.objectStoreNames) {
                        const rows = await new Promise((resolve, reject) => { const r = db.transaction(store, 'readonly').objectStore(store).getAll(); r.onsuccess = () => resolve(r.result); r.onerror = () => reject(new Error('Inspection failed')); });
                        if (scan(rows)) counts.storage++;
                    }
                } finally { db.close(); }
            }
            return [Number(controls), counts.renderer, counts.commits, errors, counts.react, counts.reply,
                counts.message, counts.history, counts.storage, counts.console];
        };
        """;
}
