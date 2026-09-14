using System.Net.Sockets;
using System.Text.Json;

namespace HushVoting.IntegrationTests.Infrastructure;

// The runner holds scan values in memory until the final TRX exists. No
// plaintext scan file, digest ledger, command argument or diagnostic carries them.
internal static class HushVotingArtifactClient
{
    public static Task RegisterAsync(params string[] values) => SendAsync(new { operation = "register", values }, required: false);
    public static Task RequireAsync() => SendAsync(new { operation = "require" }, required: true);
    public static Task CheckAsync() => SendAsync(new { operation = "check" }, required: true);

    private static async Task SendAsync(object message, bool required)
    {
        var endpoint = Environment.GetEnvironmentVariable("HUSHVOTING_ARTIFACT_SOCKET");
        if (string.IsNullOrEmpty(endpoint))
        {
            if (required) throw new InvalidOperationException("Artifact acceptance requires the owned HushVoting E2E runner.");
            return;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), deadline.Token);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            try
            {
                await stream.WriteAsync(bytes, deadline.Token);
                await stream.WriteAsync(new byte[] { 10 }, deadline.Token);
                await stream.FlushAsync(deadline.Token);
            }
            finally { Array.Clear(bytes); }
            using var reader = new StreamReader(stream);
            var reply = await reader.ReadLineAsync(deadline.Token);
            using var result = JsonDocument.Parse(reply ?? "{}");
            if (!result.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException();
        }
        catch { throw new InvalidOperationException("HushVoting artifact registration/scan failed; sensitive diagnostics withheld."); }
    }
}
