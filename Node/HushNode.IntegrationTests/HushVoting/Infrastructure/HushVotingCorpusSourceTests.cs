using System.Runtime.InteropServices;
using HushVoting.IntegrationTests.Infrastructure;
using Xunit;

namespace HushVoting.IntegrationTests.ToolingTests;

// FEAT-009 Phase 6 Task 6.10. Public synthetic filesystem fixtures, not controlled-corpus qualification.
[Trait("Category", "HushVoting")]
[Trait("Category", "HV-CORPUS-TOOLING")]
public sealed class HushVotingCorpusSourceTests
{
    [Theory]
    [InlineData(53)]
    [InlineData(HushVotingCorpusSource.MaximumBytes)]
    public async Task RegularSource_IsReadOnlyComparedAndWiped(int size)
    {
        using var fixture = new PublicSource();
        var expected = Enumerable.Repeat((byte)0x42, size).ToArray();
        await File.WriteAllBytesAsync(fixture.FilePath, expected);
        var opened = await HushVotingCorpusSource.OpenAsync(fixture.FilePath);
        Assert.Equal(CorpusSourceFailure.None, opened.Failure);
        using var source = Assert.IsType<HushVotingCorpusSource>(opened.Source);
        var retained = source.Snapshot;
        Assert.Equal(expected, retained.ToArray());
        Assert.Equal(CorpusSourceFailure.None, await source.CheckUnchangedAsync());
        source.Dispose();
        Assert.All(retained.ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(expected, await File.ReadAllBytesAsync(fixture.FilePath));
        Assert.Equal(CorpusSourceFailure.ReadFailed, await source.CheckUnchangedAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(HushVotingCorpusSource.MaximumBytes + 1)]
    public async Task InvalidLength_IsRejectedWithoutSnapshot(int size)
    {
        using var fixture = new PublicSource();
        await File.WriteAllBytesAsync(fixture.FilePath, new byte[size]);
        var opened = await HushVotingCorpusSource.OpenAsync(fixture.FilePath);
        Assert.Null(opened.Source);
        Assert.Equal(CorpusSourceFailure.InvalidLength, opened.Failure);
    }

    [Theory]
    [InlineData("leaf link")]
    [InlineData("directory link")]
    [InlineData("directory")]
    [InlineData("fifo")]
    [InlineData("missing")]
    public async Task UnsafeSource_IsRejectedWithoutReading(string kind)
    {
        using var fixture = new PublicSource();
        string path = fixture.FilePath;
        if (kind is "leaf link" or "directory link")
        {
            await File.WriteAllBytesAsync(fixture.FilePath, new byte[53]);
            if (kind == "leaf link")
            {
                path = Path.Combine(fixture.DirectoryPath, "link.dat");
                File.CreateSymbolicLink(path, fixture.FilePath);
            }
            else
            {
                var link = Path.Combine(fixture.DirectoryPath, "linked-directory");
                Directory.CreateSymbolicLink(link, fixture.DirectoryPath);
                path = Path.Combine(link, "source.dat");
            }
        }
        else if (kind == "directory") Directory.CreateDirectory(path);
        else if (kind == "fifo") Assert.Equal(0, MakeFifo(path, 0x180));
        var opened = await HushVotingCorpusSource.OpenAsync(path).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(opened.Source);
        Assert.Equal(kind is "fifo" or "directory" ? CorpusSourceFailure.NotRegularFile : CorpusSourceFailure.UnsafeOrMissingSource, opened.Failure);
    }

    [Theory]
    [InlineData("change")]
    [InlineData("truncate")]
    [InlineData("grow")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("symlink")]
    public async Task ChangedSource_IsDetectedWithoutPerFileEvidence(string mutation)
    {
        using var fixture = new PublicSource();
        var initial = Enumerable.Repeat((byte)0x42, 53).ToArray();
        await File.WriteAllBytesAsync(fixture.FilePath, initial);
        var opened = await HushVotingCorpusSource.OpenAsync(fixture.FilePath);
        using var source = Assert.IsType<HushVotingCorpusSource>(opened.Source);
        switch (mutation)
        {
            case "change": await File.WriteAllBytesAsync(fixture.FilePath, new byte[53]); break;
            case "truncate": await File.WriteAllBytesAsync(fixture.FilePath, new byte[52]); break;
            case "grow": await File.WriteAllBytesAsync(fixture.FilePath, new byte[54]); break;
            case "replace": File.Delete(fixture.FilePath); await File.WriteAllBytesAsync(fixture.FilePath, initial); break;
            case "remove": File.Delete(fixture.FilePath); break;
            case "symlink":
                var replacement = Path.Combine(fixture.DirectoryPath, "replacement.dat");
                await File.WriteAllBytesAsync(replacement, initial);
                File.Delete(fixture.FilePath);
                File.CreateSymbolicLink(fixture.FilePath, replacement);
                break;
        }
        Assert.Equal(CorpusSourceFailure.Changed, await source.CheckUnchangedAsync());
        Assert.Equal(initial, source.Snapshot.ToArray());
    }

    [Fact]
    public async Task CancelledOpen_IsTypedAndRepeatedDisposalDoesNotLeakDescriptors()
    {
        using var fixture = new PublicSource();
        await File.WriteAllBytesAsync(fixture.FilePath, new byte[53]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var failed = await HushVotingCorpusSource.OpenAsync(fixture.FilePath, cancelled.Token);
        Assert.Equal(CorpusSourceFailure.Cancelled, failed.Failure);
        Assert.Null(failed.Source);
        // Count only descriptors owned by this fixture, not unrelated testhost activity.
        int OwnedDescriptors() => Directory.GetFiles("/proc/self/fd").Count(path =>
        {
            try { return new FileInfo(path).LinkTarget?.StartsWith(fixture.DirectoryPath + "/", StringComparison.Ordinal) == true; }
            catch (IOException) { return false; }
        });
        Assert.Equal(0, OwnedDescriptors());
        for (var index = 0; index < 20; index++)
        {
            var opened = await HushVotingCorpusSource.OpenAsync(fixture.FilePath);
            using var source = Assert.IsType<HushVotingCorpusSource>(opened.Source);
            Assert.Equal(CorpusSourceFailure.Cancelled, await source.CheckUnchangedAsync(cancelled.Token));
            source.Dispose();
        }
        Assert.Equal(0, OwnedDescriptors());
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);
    private sealed class PublicSource : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "hv-public-source-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(DirectoryPath, "source.dat");
        public PublicSource() => Directory.CreateDirectory(DirectoryPath);
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
