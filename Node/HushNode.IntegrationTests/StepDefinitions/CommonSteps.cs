using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
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
    /// Waits for BlockIndexCompletedEvent to ensure all indexing is complete
    /// before returning (guarantees data is persisted and queryable).
    /// </summary>
    [Given(@"a block is produced")]
    [When(@"a block is produced")]
    public async Task ABlockIsProduced()
    {
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
    }

    /// <summary>
    /// Waits for the transaction to reach the mempool, then produces a block.
    /// Use this after a UI action that submits a transaction to avoid race conditions.
    /// The step waits for BlockIndexCompletedEvent to ensure indexing is complete.
    /// </summary>
    [When(@"the transaction is processed")]
    [Given(@"the transaction is processed")]
    public async Task WhenTheTransactionIsProcessed()
    {
        var node = GetNode();

        // Wait for the transaction to reach the mempool
        await node.WaitForPendingTransactionsAsync(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));

        // Produce block and wait for indexing to complete (BlockIndexCompletedEvent)
        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
    }

    /// <summary>
    /// Waits for a specific number of transactions to reach the mempool, then produces a block.
    /// </summary>
    [When(@"(\d+) transactions? (?:is|are) processed")]
    [Given(@"(\d+) transactions? (?:is|are) processed")]
    public async Task WhenTransactionsAreProcessed(int transactionCount)
    {
        var node = GetNode();

        // Wait for the transactions to reach the mempool
        await node.WaitForPendingTransactionsAsync(minTransactions: transactionCount, timeout: TimeSpan.FromSeconds(15));

        // Produce block and wait for indexing to complete (BlockIndexCompletedEvent)
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

    private HushServerNodeCore GetNode()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.NodeKey, out var nodeObj)
            && nodeObj is HushServerNodeCore node)
        {
            return node;
        }
        throw new InvalidOperationException("HushServerNodeCore not found in ScenarioContext.");
    }
}
