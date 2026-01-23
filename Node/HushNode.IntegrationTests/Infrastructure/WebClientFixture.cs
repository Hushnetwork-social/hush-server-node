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
    /// Starts HushWebClient container using pre-built image.
    /// The image must be built first with: docker compose -f docker-compose.e2e.yml build
    /// Uses fixed gRPC-Web port 14666 (HushServerNodeCore.E2EGrpcWebPort).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        // Start container using pre-built image (no --build flag for speed)
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f \"{_composeFilePath}\" up -d --wait",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker compose process");

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

            // Check if image doesn't exist - provide helpful message
            if (stderr.Contains("no such image") || stderr.Contains("pull access denied"))
            {
                throw new InvalidOperationException(
                    $"E2E test image not found. Build it first with:\n" +
                    $"  cd Node/HushNode.IntegrationTests && docker compose -f docker-compose.e2e.yml build\n\n" +
                    $"Original error: {stderr}");
            }

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
