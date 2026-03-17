using System.Diagnostics;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Manages HushWebClient Docker container lifecycle for E2E tests.
/// Starts container with dynamic gRPC port and waits for health check.
/// </summary>
internal sealed class WebClientFixture : IAsyncDisposable
{
    private const string WebClientServiceName = "hush-web-client";
    private readonly string _composeFilePath;
    private readonly string _webClientRootPath;
    private readonly string _buildStampPath;
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

        _webClientRootPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_composeFilePath)!, "..", "..", "..", "hush-web-client"));
        _buildStampPath = Path.Combine(Path.GetTempPath(), "hush-web-client-e2e-build.stamp");
    }

    /// <summary>
    /// Starts HushWebClient container.
    /// Always rebuilds and recreates the container so E2E runs execute the current web-client code.
    /// Uses fixed gRPC-Web port 14666 (HushServerNodeCore.E2EGrpcWebPort).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        if (this.ShouldRebuildImage())
        {
            Console.WriteLine("[E2E] Building hush-web-client E2E image...");
            await BuildImageAsync(cancellationToken);
            File.WriteAllText(_buildStampPath, DateTime.UtcNow.ToString("O"));
        }
        else
        {
            Console.WriteLine("[E2E] Reusing existing hush-web-client E2E image.");
        }

        // Recreate the container so the new image is guaranteed to be used.
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f \"{_composeFilePath}\" up -d --wait --force-recreate --remove-orphans",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker compose process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker compose failed with exit code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
        }

        // Wait for health check (docker --wait should handle this, but verify)
        await WaitForHealthyAsync(cancellationToken);

        _started = true;
    }

    public async Task<string> GetServiceLogsAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var sinceArgument = sinceUtc.HasValue
            ? $" --since \"{sinceUtc.Value.UtcDateTime:O}\""
            : string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f \"{_composeFilePath}\" logs --no-color{sinceArgument} {WebClientServiceName}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker compose logs process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker compose logs failed with exit code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
        }

        return string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : $"{stdout}{Environment.NewLine}[stderr]{Environment.NewLine}{stderr}";
    }

    /// <summary>
    /// Builds the E2E test image using docker compose.
    /// </summary>
    private async Task BuildImageAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f \"{_composeFilePath}\" build",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker build process");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker build failed with exit code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
        }

        Console.WriteLine("[E2E] Image build completed.");
    }

    private bool ShouldRebuildImage()
    {
        if (!File.Exists(_buildStampPath))
        {
            return true;
        }

        if (!Directory.Exists(_webClientRootPath))
        {
            return true;
        }

        var stampUtc = File.GetLastWriteTimeUtc(_buildStampPath);
        var excludedSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules",
            ".next",
            ".git",
            ".tmp"
        };

        foreach (var file in Directory.EnumerateFiles(_webClientRootPath, "*", SearchOption.AllDirectories))
        {
            if (this.ShouldIgnoreFile(file, excludedSegments))
            {
                continue;
            }

            if (File.GetLastWriteTimeUtc(file) > stampUtc)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldIgnoreFile(string filePath, HashSet<string> excludedSegments)
    {
        var relativePath = Path.GetRelativePath(_webClientRootPath, filePath);
        var segments = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => excludedSegments.Contains(segment));
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
