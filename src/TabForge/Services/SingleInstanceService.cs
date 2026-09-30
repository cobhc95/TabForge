using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace TabForge.Services;

/// <summary>
/// "Open files from Explorer in a new tab": a second TabForge launch with a song file hands the path to the
/// TabForge already running and exits. The hand-off is a named pipe that only the same Windows account can
/// open (PipeOptions.CurrentUserOnly, name derived from the account); the receiver accepts one bounded message,
/// which must be an existing file with a supported extension, and opens it through the normal import path.
/// </summary>
public static class SingleInstanceService
{
    private const int MaxMessageBytes = 64 * 1024;

    private static string PipeName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
            return $"TabForge.Open.{hash}";
        }
    }

    /// <summary>Sends a file path to a running TabForge. False when none is running or it did not answer.</summary>
    public static bool TrySendToRunningInstance(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(800);
            var bytes = Encoding.UTF8.GetBytes(path);
            if (bytes.Length > MaxMessageBytes) return false;
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Listens for paths from later launches; <paramref name="open"/> is called on a background thread.</summary>
    public static void StartServer(Action<string> open, CancellationToken cancellation)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                NamedPipeServerStream server;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                }
                catch (IOException) { return; } // another TabForge already receives: this one stays a plain window
                using (server)
                {
                    try
                    {
                        await server.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                        var buffer = new MemoryStream();
                        var chunk = new byte[8192];
                        int read;
                        while ((read = await server.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
                        {
                            if (buffer.Length + read > MaxMessageBytes) { buffer.SetLength(0); break; }
                            buffer.Write(chunk, 0, read);
                        }
                        var path = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).Trim();
                        if (IsOpenableSong(path)) open(path);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { /* the sender went away: wait for the next one */ }
                }
            }
        }, cancellation);
    }

    /// <summary>Only a full path to an existing song file TabForge can open is accepted.</summary>
    public static bool IsOpenableSong(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32_767 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return false;
        return FileTypes.IsOpenable(Path.GetExtension(path));
    }
}
