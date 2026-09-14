using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using HushVoting.IntegrationTests.Infrastructure;
using Microsoft.Playwright;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class LookupTransportSteps(HushVotingScenario scenario)
{
    private readonly ConcurrentQueue<IRequest> _requests = new();
    private int _before;

    [Given("the HushVoting browser and actual node public lookup boundaries are observed")]
    public void Observe()
    {
        _before = scenario.Faults.IdentityQueryCount;
        scenario.Page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath == "/api/identity") _requests.Enqueue(request);
        };
    }

    [Then(@"exactly (\d+) bounded same-origin unsigned lookups reach the node with non-cacheable replies")]
    public async Task VerifyAsync(int expected)
    {
        _requests.Count.Should().Be(expected);
        scenario.Faults.IdentityQueryCount.Should().Be(_before + expected);
        scenario.Faults.IdentityQueriesUnsigned.Skip(_before).All(unsigned => unsigned).Should().BeTrue();
        var observedAddresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in _requests)
        {
            var uri = new Uri(request.Url);
            (uri.GetLeftPart(UriPartial.Authority) == scenario.BaseUrl && uri.Query.Length == 0 && request.Method == "POST").Should().BeTrue();
            var bytes = request.PostDataBuffer;
            (bytes is { Length: > 0 and <= 65_536 }).Should().BeTrue();
            try
            {
                using var body = JsonDocument.Parse(bytes!);
                var fields = body.RootElement.EnumerateObject().ToArray();
                if (fields.Length != 1 || fields[0].Name != "publicSigningAddress" || fields[0].Value.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException();
                var address = fields[0].Value.GetString()!;
                if (address.Length is not (66 or 130)) throw new InvalidOperationException();
                observedAddresses.Add(address);
            }
            catch { throw new InvalidOperationException("Public identity request contained unexpected fields; values omitted."); }
            var response = await request.ResponseAsync() ?? throw new InvalidOperationException("Missing public identity response.");
            response.Status.Should().Be(200);
            var headers = await response.AllHeadersAsync();
            (headers.TryGetValue("cache-control", out var policy) && policy.Contains("no-store", StringComparison.Ordinal)).Should().BeTrue();
        }
        observedAddresses.SetEquals(scenario.Faults.IdentityLookups.Skip(_before).Select(lookup => lookup.SigningAddress)).Should().BeTrue();
    }
}
