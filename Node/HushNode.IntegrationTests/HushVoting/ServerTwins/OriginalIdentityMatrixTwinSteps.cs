// Original AC-007-071 / AC-008-076 / AC-009-081 require executable backend Twins.
// Direct real RPC, indexing and reset assertions; never a wrapper around receipt counts.
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.ServerTwins;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class OriginalIdentityMatrixTwinSteps(IdentityNodeResetTwinSteps identity)
{
    [Given("the owned server has verified P01 rejection admission exact lookup and same-key reset")]
    public async Task P01Async()
    {
        await identity.IndexedAsync("P01 private");
        await identity.ResetAsync();
        await identity.RecreateAsync();
    }

    [When("the owned server verifies P02 admission and resets that indexed chain")]
    public async Task P02Async()
    {
        await identity.IndexedAsync("P02 public");
        await identity.ResetAsync();
    }
}
