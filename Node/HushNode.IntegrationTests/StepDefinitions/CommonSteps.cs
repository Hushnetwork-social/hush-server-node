using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// Common step definitions shared across multiple feature files.
/// These steps can be used with Given, When, or And keywords.
/// </summary>
[Binding]
public sealed class CommonSteps
{
    private readonly ScenarioContext _scenarioContext;

    public CommonSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    /// <summary>
    /// Triggers block production. Works with Given, When, or And keywords.
    /// </summary>
    [Given(@"a block is produced")]
    [When(@"a block is produced")]
    public async Task ABlockIsProduced()
    {
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
    }

    private BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var controlObj)
            && controlObj is BlockProductionControl blockControl)
        {
            return blockControl;
        }
        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }
}
