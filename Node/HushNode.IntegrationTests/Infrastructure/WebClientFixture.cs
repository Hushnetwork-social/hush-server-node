using System.Diagnostics;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Manages HushWebClient Docker container lifecycle for E2E tests.
/// Starts container with dynamic gRPC port and waits for health check.
/// </summary>
internal sealed class WebClientFixture : IAsyncDisposable
{
    private readonly string _composeFilePath;
    private bool _started;

    /// <summary>
    /// Gets the base URL for the web client.
    /// </summary>
    public string BaseUrl { get; } = "http://localhost:3000";

    /// <summary>
    /// Gets whether the container has been started.
    /// </summary>
    public bool IsStarted => _started;

    public WebClientFixture()
    {
        // Find docker-compose.e2e.yml relative to the test project
        // AppContext.BaseDirectory is bin/Debug/ (no net9.0 subfolder in this project)
        // Go up to HushNode.IntegrationTests, then look for docker-compose.e2e.yml
        var baseDir = AppContext.BaseDirectory;

        // Try different relative paths depending on whether we're in bin/Debug or bin/Debug/net9.0
        var possiblePaths = new[]
        {
            Path.Combine(baseDir, "..", "..", "docker-compose.e2e.yml"),       // bin/Debug -> project root
            Path.Combine(baseDir, "..", "..", "..", "docker-compose.e2e.yml"), // bin/Debug/net9.0 -> project root
        };

        _composeFilePath = possiblePaths
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"docker-compose.e2e.yml not found. Searched: {string.Join(", ", possiblePaths.Select(Path.GetFullPath))}");
    }

    /// <summary>
    /// Starts HushWebClient container with the specified gRPC port.
    /// </summary>
    /// <param name="grpcPort">The port HushServerNodeCore is listening on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(int grpcPort, CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        // Start container with dynamic port
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f \"{_composeFilePath}\" up -d --build --wait",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Set GRPC_PORT environment variable
        startInfo.Environment["GRPC_PORT"] = grpcPort.ToString();

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker compose process");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Docker compose failed with exit code {process.ExitCode}.\n" +
                $"Stdout: {stdout}\n" +
                $"Stderr: {stderr}");
        }

        // Wait for health check (docker --wait should handle this, but verify)
        await WaitForHealthyAsync(cancellationToken);

        _started = true;
    }

    /// <summary>
    /// Waits for the web client health endpoint to respond.
    /// </summary>
    private async Task WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var maxAttempts = 60;
        var healthUrl = $"{BaseUrl}/api/health";

        for (int i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Container not ready yet
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Request timeout, container not ready
            }

            await Task.Delay(1000, cancellationToken);
        }

        throw new TimeoutException(
            $"WebClient health check at {healthUrl} did not respond after {maxAttempts} seconds");
    }

    /// <summary>
    /// Stops and removes the container.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_started)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f \"{_composeFilePath}\" down --remove-orphans --timeout 10",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }

        _started = false;
    }
}
