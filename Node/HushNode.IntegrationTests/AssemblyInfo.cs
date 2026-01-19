using Xunit;

// Disable parallel test execution for integration tests
// This is required because scenarios share the same database and Redis containers
[assembly: CollectionBehavior(DisableTestParallelization = true)]
