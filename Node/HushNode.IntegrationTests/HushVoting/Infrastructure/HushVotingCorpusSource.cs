using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace HushVoting.IntegrationTests.Infrastructure;

internal enum CorpusSourceFailure { None, UnsafeOrMissingSource, NotRegularFile, InvalidLength, ReadFailed, Changed, Cancelled }
internal sealed record CorpusSourceOpen(HushVotingCorpusSource? Source, CorpusSourceFailure Failure);

// FEAT-009 Phase 6 Tasks 6.9/6.10, AC-009-074/077: test-harness source custody.
// The connected controlled runner must establish its local/network/capture guard BEFORE calling this boundary.
// No environment variables are consumed here; caller-owned browser/network guards precede source opens.
internal sealed class HushVotingCorpusSource : IDisposable
{
    internal const int MaximumBytes = 1024 * 1024;
    private readonly string _path;
    private readonly FileStream _original;
    private readonly SourceStat _identity;
    private readonly byte[] _bytes;
    private bool _disposed;
    public ReadOnlyMemory<byte> Snapshot => _bytes;

    private HushVotingCorpusSource(string path, FileStream original, SourceStat identity, byte[] bytes)
        => (_path, _original, _identity, _bytes) = (path, original, identity, bytes);

    public static async Task<CorpusSourceOpen> OpenAsync(string path, CancellationToken cancellation = default)
    {
        if (cancellation.IsCancellationRequested) return new(null, CorpusSourceFailure.Cancelled);
        FileStream? stream = null;
        byte[] bytes = [];
        byte[] overflow = [];
        try
        {
            var handle = OpenRegular(path, out var identity, out var failure);
            if (handle is null) return new(null, failure);
            try { stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false); }
            catch { handle.Dispose(); throw; }
            bytes = new byte[checked((int)identity.Size)];
            await stream.ReadExactlyAsync(bytes, cancellation);
            overflow = new byte[1];
            if (await stream.ReadAsync(overflow, cancellation) != 0)
            {
                stream.Dispose();
                CryptographicOperations.ZeroMemory(bytes);
                return new(null, CorpusSourceFailure.Changed);
            }
            return new(new HushVotingCorpusSource(path, stream, identity, bytes), CorpusSourceFailure.None);
        }
        catch (OperationCanceledException)
        {
            stream?.Dispose();
            CryptographicOperations.ZeroMemory(bytes);
            return new(null, CorpusSourceFailure.Cancelled);
        }
        catch
        {
            stream?.Dispose();
            CryptographicOperations.ZeroMemory(bytes);
            return new(null, CorpusSourceFailure.ReadFailed);
        }
        finally { CryptographicOperations.ZeroMemory(overflow); }
    }

    public async Task<CorpusSourceFailure> CheckUnchangedAsync(CancellationToken cancellation = default)
    {
        if (_disposed) return CorpusSourceFailure.ReadFailed;
        var reopened = await OpenAsync(_path, cancellation);
        if (reopened.Source is null) return reopened.Failure == CorpusSourceFailure.Cancelled
            ? CorpusSourceFailure.Cancelled : CorpusSourceFailure.Changed;
        using var current = reopened.Source;
        return _identity.Inode == current._identity.Inode && _identity.DeviceMajor == current._identity.DeviceMajor
            && _identity.DeviceMinor == current._identity.DeviceMinor && _identity.Size == current._identity.Size
            && CryptographicOperations.FixedTimeEquals(_bytes, current._bytes)
                ? CorpusSourceFailure.None : CorpusSourceFailure.Changed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_bytes);
        _original.Dispose();
    }

    private static SafeFileHandle? OpenRegular(string path, out SourceStat identity, out CorpusSourceFailure failure)
    {
        identity = default;
        failure = CorpusSourceFailure.UnsafeOrMissingSource;
        if (!OperatingSystem.IsLinux()) return null;
        // Traverse every directory relative to already opened descriptors. No symlink component is followed.
        var components = Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0) return null;
        SafeFileHandle? directory = null;
        SafeFileHandle? file = null;
        try
        {
            directory = new SafeFileHandle((IntPtr)OpenAt(-100, "/", ReadFlags | DirectoryFlag), ownsHandle: true);
            if (directory.IsInvalid) return null;
            foreach (var component in components[..^1])
            {
                var next = new SafeFileHandle((IntPtr)OpenAt(directory.DangerousGetHandle().ToInt32(), component, ReadFlags | DirectoryFlag), ownsHandle: true);
                directory.Dispose();
                directory = next;
                if (directory.IsInvalid) return null;
            }
            file = new SafeFileHandle((IntPtr)OpenAt(directory.DangerousGetHandle().ToInt32(), components[^1], ReadFlags), ownsHandle: true);
            if (file.IsInvalid) return null;
            if (StatX(file.DangerousGetHandle().ToInt32(), "", 0x1000, 0x301, out identity) != 0
                || (identity.Mask & 0x301) != 0x301) return null;
            if ((identity.Mode & 0xf000) != 0x8000)
            {
                failure = CorpusSourceFailure.NotRegularFile;
                return null;
            }
            if (identity.Size is 0 or > MaximumBytes)
            {
                failure = CorpusSourceFailure.InvalidLength;
                return null;
            }
            failure = CorpusSourceFailure.None;
            var result = file;
            file = null;
            return result;
        }
        finally { file?.Dispose(); directory?.Dispose(); }
    }

    // Linux UAPI constants/layout verified against linux/stat.h and asm-generic/fcntl.h.
    // O_RDONLY | O_NONBLOCK (never wait on a FIFO) | O_NOFOLLOW | O_CLOEXEC.
    private const int ReadFlags = 0x800 | 0x20000 | 0x80000;
    private const int DirectoryFlag = 0x10000;
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int directory, string path, int flags);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int StatX(int descriptor, string path, int flags, uint mask, out SourceStat result);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct SourceStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
}
