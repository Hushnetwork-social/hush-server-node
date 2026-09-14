using System.Runtime.CompilerServices;

// Allow integration tests to access internal classes
[assembly: InternalsVisibleTo("HushNode.IntegrationTests")]

// Owned process-restart test host; no production entry point is exposed.
[assembly: InternalsVisibleTo("HushVoting.NodeProcessHost")]

// Allow unit tests to access internal classes
[assembly: InternalsVisibleTo("HushServerNode.Tests")]
