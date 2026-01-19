using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// Collection definition for integration tests.
/// All integration tests share this collection to ensure sequential execution.
/// </summary>
[CollectionDefinition("Integration Tests", DisableParallelization = true)]
public class IntegrationTestCollection
{
}
