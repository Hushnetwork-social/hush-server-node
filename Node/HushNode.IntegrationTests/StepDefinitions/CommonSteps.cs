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
    ///
    /// For E2E tests: If a PendingTransactionWaiter was stored in ScenarioContext by a
    /// previous step (e.g., "the user sends message"), use it to avoid race conditions
    /// where the event fires before we start listening.
    /// </summary>
    [When(@"the transaction is processed")]
    [Given(@"the transaction is processed")]
    public async Task WhenTheTransactionIsProcessed()
    {
        var blockControl = GetBlockControl();

        Console.WriteLine("[E2E] WhenTheTransactionIsProcessed: checking for PendingTransactionWaiter in ScenarioContext");

        // Check for a pre-created waiter from an E2E step (avoids race condition)
        if (_scenarioContext.TryGetValue("PendingTransactionWaiter", out var waiterObj)
            && waiterObj is HushServerNodeCore.TransactionWaiter waiter)
        {
            Console.WriteLine("[E2E] Found PendingTransactionWaiter in ScenarioContext - using event-based waiting");
            _scenarioContext.Remove("PendingTransactionWaiter");
            try
            {
                Console.WriteLine("[E2E] Awaiting waiter...");
                await waiter.WaitAsync();
                Console.WriteLine("[E2E] Waiter completed successfully");
            }
            finally
            {
                waiter.Dispose();
            }
        }
        else
        {
            // Fallback: wait for transaction to reach mempool
            // This works for non-E2E tests where there's no race condition
            Console.WriteLine("[E2E] WARNING: No PendingTransactionWaiter found - using fallback WaitForPendingTransactionsAsync (may race!)");
            var node = GetNode();
            await node.WaitForPendingTransactionsAsync(minTransactions: 1, timeout: TimeSpan.FromSeconds(10));
        }

        // Produce block and wait for indexing to complete (BlockIndexCompletedEvent)
        Console.WriteLine("[E2E] Producing block and waiting for indexing...");
        await blockControl.ProduceBlockAsync();
        Console.WriteLine("[E2E] Block produced and indexed, continuing test");
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
