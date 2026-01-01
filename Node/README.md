# HushServerNode

The HushServerNode is the blockchain node server implementation for HushNetwork.

## Solution Structure

**IMPORTANT**: The `Node/` folder is **self-contained** with its own solution file.

```
Node/
├── HushServerNode.sln          ← Use THIS solution for development
├── HushServerNode/             ← Main entry point
├── Core/                       ← Core modules (Blockchain, Bank, Feeds, etc.)
└── Infrastructure/             ← Database, caching, interfaces
```

Do NOT use the solution file in the parent folder (`HushNetwork.sln`) for Node development - that solution is for the Client projects.

## Build Commands

All commands should be run from the `Node/` directory:

```bash
cd Node

# Restore packages
dotnet restore

# Build
dotnet build --no-restore

# Run all tests
dotnet test --no-build --verbosity normal
```

## Test Projects

The solution includes the following test projects:

| Project | Tests | Description |
|---------|-------|-------------|
| `HushNode.Feeds.Tests` | 242 | Feed transaction handlers, content handlers |
| `HushNode.Reactions.Tests` | 126 | Protocol Omega reaction system |
| **Total** | **368** | |

## Running the Server

```bash
cd Node/HushServerNode
dotnet run
```

The server starts on:
- gRPC: `localhost:4665`
- Metrics/Health: `localhost:4666`

## Configuration

Configuration is in `Node/HushServerNode/ApplicationSettings.json`:

```json
{
  "ConnectionStrings": {
    "PostgresConnection": "Host=...;Database=...;Username=...;Password=..."
  },
  "UseFileBasedCredentials": true,
  "CredentialsFile": "stacker-credentials.dat"
}
```

See `MemoryBank/Tools/LOCAL_DOCKER_SETUP.md` for Docker development setup.

## Core Modules

| Module | Purpose |
|--------|---------|
| `HushNode.Blockchain` | Block production, chain management |
| `HushNode.Bank` | Token/currency transactions |
| `HushNode.Identity` | User identity and public key management |
| `HushNode.Feeds` | Social feed functionality |
| `HushNode.Reactions` | Protocol Omega anonymous reactions |
| `HushNode.MemPool` | Pending transaction pool |
| `HushNode.Indexing` | Blockchain indexing |
| `HushNode.Caching` | In-memory caching layer |
