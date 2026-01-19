using System.Runtime.CompilerServices;

// Allow integration tests to access internal classes
[assembly: InternalsVisibleTo("HushNode.IntegrationTests")]

// Allow unit tests to access internal classes
[assembly: InternalsVisibleTo("HushServerNode.Tests")]
