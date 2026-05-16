using FluentAssertions;
using HushNode.Elections;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ElectionsHostBuildSp07WorkerPathTests
{
    [Fact]
    public void TryResolveRepoLocalSp07RustWorkerPath_ShouldFindReleaseWorkerFromNestedOutputDirectory()
    {
        var temp = CreateTempDirectory();
        try
        {
            var startDirectory = Path.Combine(temp, "Node", "HushServerNode", "bin", "Debug", "net9.0");
            Directory.CreateDirectory(startDirectory);
            var releaseWorkerPath = CreateWorker(temp, "release");
            CreateWorker(temp, "debug");

            var resolved = ElectionsHostBuild.TryResolveRepoLocalSp07RustWorkerPath(startDirectory);

            resolved.Should().Be(releaseWorkerPath);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public void TryResolveRepoLocalSp07RustWorkerPath_WhenReleaseWorkerIsMissing_ShouldFindDebugWorker()
    {
        var temp = CreateTempDirectory();
        try
        {
            var startDirectory = Path.Combine(temp, "Node", "HushServerNode", "bin", "Debug", "net9.0");
            Directory.CreateDirectory(startDirectory);
            var debugWorkerPath = CreateWorker(temp, "debug");

            var resolved = ElectionsHostBuild.TryResolveRepoLocalSp07RustWorkerPath(startDirectory);

            resolved.Should().Be(debugWorkerPath);
        }
        finally
        {
            DeleteTempDirectory(temp);
        }
    }

    [Fact]
    public void CreateSp07RustWorkerProcessOptions_WithConfiguredPath_ShouldUseConfigurationBeforeEnvironment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elections:Sp07PublicationProof:RustWorkerPath"] = " C:\\tools\\hush-sp07-rust-worker.exe ",
                ["Elections:Sp07PublicationProof:WorkerTimeoutSeconds"] = "45",
                ["Elections:Sp07PublicationProof:WorkerThreads"] = "2"
            })
            .Build();

        var options = ElectionsHostBuild.CreateSp07RustWorkerProcessOptions(configuration);

        options.ExecutablePath.Should().Be("C:\\tools\\hush-sp07-rust-worker.exe");
        options.Timeout.Should().Be(TimeSpan.FromSeconds(45));
        options.Threads.Should().Be(2);
        options.WorkingDirectory.Should().Be(AppContext.BaseDirectory);
    }

    private static string CreateWorker(string root, string profile)
    {
        var workerRoot = Path.Combine(root, "Tools", "HushSp07RustWorker");
        Directory.CreateDirectory(workerRoot);
        File.WriteAllText(Path.Combine(workerRoot, "Cargo.toml"), "[package]");

        var workerPath = Path.Combine(
            workerRoot,
            "target",
            profile,
            OperatingSystem.IsWindows() ? "hush-sp07-rust-worker.exe" : "hush-sp07-rust-worker");
        Directory.CreateDirectory(Path.GetDirectoryName(workerPath)!);
        File.WriteAllText(workerPath, string.Empty);
        return Path.GetFullPath(workerPath);
    }

    private static string CreateTempDirectory()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"hush-sp07-worker-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        return temp;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for Windows test handles.
        }
    }
}
