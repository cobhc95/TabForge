using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: the per-launch pipe token: made by TabForge, handed to the child it starts as a command-line argument, and sent by the child as
// the first bytes on the pipe; the pipe's server refuses a peer that does not send it. Does not own: the frames after it (Frames).
// Tests: no dedicated test class.

// The token travels on the child's command line: a same-user process can read it (Task Manager, GetCommandLine). That is outside the threat model
// (a same-user process can also open the child directly); the token only keeps a same-user process that merely found the pipe name out,
// together with FirstPipeInstance (the name cannot be pre-created) and the per-peer window of <see cref="AcceptAsync"/> (a silent peer cannot hold the pipe).

/// <summary>A random token that proves a pipe peer is the process TabForge itself started (a same-user process that merely found the pipe name is refused).</summary>
public static class PipeAuth
{
    /// <summary>Length of the token text and of the hello on the pipe.</summary>
    public const int TokenChars = 32;

    /// <summary>A new random token (128 bits, hex).</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenChars / 2)).ToLowerInvariant();

    /// <summary>True when <paramref name="token"/> has the shape <see cref="NewToken"/> makes.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenChars } && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>The peer's first bytes: the token.</summary>
    public static void WriteHello(Stream pipe, string token)
    {
        pipe.Write(Encoding.ASCII.GetBytes(token));
        pipe.Flush();
    }

    /// <summary>
    /// Reads the peer's hello and compares it with <paramref name="token"/> (constant time). False for a wrong, short or late hello
    /// (nothing within <paramref name="timeoutMs"/>) or a closed pipe; the caller then drops the peer. Awaited, never blocks a thread.
    /// </summary>
    public static async Task<bool> ReadAndVerifyAsync(Stream pipe, string token, int timeoutMs)
    {
        var got = new byte[TokenChars];
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var read = 0;
            while (read < got.Length)
            {
                var n = await pipe.ReadAsync(got.AsMemory(read), cts.Token).ConfigureAwait(false);
                if (n == 0) return false;
                read += n;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { return false; }
        var expected = Encoding.ASCII.GetBytes(token);
        var same = CryptographicOperations.FixedTimeEquals(got, expected);
        CryptographicOperations.ZeroMemory(got); CryptographicOperations.ZeroMemory(expected);
        return same;
    }

    /// <summary>Pause after a dropped peer before listening again.</summary>
    private const int DropBackoffMs = 25;

    /// <summary>Window a connected peer has to send the token before it is dropped and the pipe listens again.</summary>
    public const int PerPeerMs = 500;

    /// <summary>
    /// Accepts connections on <paramref name="pipe"/> until a peer presents <paramref name="token"/> or <paramref name="overallMs"/> passes.
    /// A peer that connects and sends no valid token within <paramref name="perPeerMs"/> is disconnected and the pipe listens again, so a
    /// squatter cannot keep the real child out. False when no authenticated peer arrived in time (or the pipe was closed).
    /// </summary>
    public static async Task<bool> AcceptAsync(NamedPipeServerStream pipe, string token, int overallMs, int perPeerMs = PerPeerMs)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                var left = overallMs - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
                if (left <= 0) return false;
                using var cts = new CancellationTokenSource(left);
                try
                {
                    if (!pipe.IsConnected) await pipe.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return false; }
                catch (IOException) { Drop(pipe); await Task.Delay(DropBackoffMs).ConfigureAwait(false); continue; }   // a peer connected and vanished before we listened
                var ok = await ReadAndVerifyAsync(pipe, token, Math.Min(perPeerMs, left)).ConfigureAwait(false);
                if (ok) return true;
                Drop(pipe);
                await Task.Delay(DropBackoffMs).ConfigureAwait(false);   // a squatter that reconnects at once does not spin this loop
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { return false; }
    }

    private static void Drop(NamedPipeServerStream pipe)
    {
        try { if (pipe.IsConnected) pipe.Disconnect(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { /* already gone */ }
    }
}
