using Grpc.Core;
using Grpc.Net.Client;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Factory for creating typed gRPC clients connected to a dynamic test endpoint.
/// Manages a single channel per factory for connection reuse.
/// </summary>
public sealed class GrpcClientFactory : IDisposable
{
    private readonly GrpcChannel _channel;
    private bool _disposed;

    /// <summary>
    /// Creates a new GrpcClientFactory for the specified endpoint.
    /// </summary>
    /// <param name="host">The gRPC server host (e.g., "localhost")</param>
    /// <param name="port">The gRPC server port</param>
    public GrpcClientFactory(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        var address = $"http://{host}:{port}";
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            // HTTP/2 for native gRPC
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true
            }
        });
    }

    /// <summary>
    /// Creates a typed gRPC client connected to this factory's endpoint.
    /// </summary>
    /// <typeparam name="TClient">The gRPC client type (e.g., HushFeed.HushFeedClient)</typeparam>
    /// <returns>A new instance of the typed client</returns>
    public TClient CreateClient<TClient>() where TClient : ClientBase<TClient>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Use Activator to create the client with the channel
        return (TClient)Activator.CreateInstance(typeof(TClient), _channel)!;
    }

    /// <summary>
    /// Gets the underlying channel for advanced scenarios.
    /// </summary>
    public GrpcChannel Channel => _channel;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Dispose();
    }
}
