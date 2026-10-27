using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace TabForge.Services;

// Owns: fetching the SoundFont over HTTPS with progress and cancel: size limit, SHA-256 check, atomic save.
// Does not own: the UI (the Settings button), where the file goes (SoundFontStore) or playing it.
// Tests: SoundEngineTests (fake HTTP handler; no test touches the network).
public sealed class SoundFontDownloader
{
    public const string Url = "https://github.com/mrbumpy409/GeneralUser-GS/raw/main/GeneralUser-GS.sf2";
    public const string Sha256 = "9575028c7a1f589f5770fccc8cff2734566af40cd26ed836944e9a5152688cfe";
    /// <summary>The file is 32.3 MB; anything above this is refused while it streams.</summary>
    public const long MaxBytes = 40L * 1024 * 1024;

    private readonly HttpMessageHandler _handler;
    private readonly string _url, _sha256;
    private readonly long _maxBytes;

    /// <param name="handler">The HTTP transport; tests pass a fake, the app passes null for the real one.</param>
    public SoundFontDownloader(HttpMessageHandler? handler = null, string url = Url, string sha256 = Sha256, long maxBytes = MaxBytes)
    {
        _handler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        _url = url; _sha256 = sha256; _maxBytes = maxBytes;
    }

    /// <summary>Downloads to <paramref name="destination"/> (only a verified file ever appears there). Returns null on success, else the reason in words. Progress is bytes so far and the total (0 = unknown).</summary>
    public async Task<string?> DownloadAsync(string destination, IProgress<(long Received, long Total)>? progress, CancellationToken cancel)
    {
        if (!_url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "The SoundFont address is not an HTTPS address.";
        var part = destination + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            using var client = new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return $"The server answered {(int)response.StatusCode}.";
            var total = response.Content.Headers.ContentLength ?? 0;
            if (total > _maxBytes) return "The SoundFont is larger than expected and was not downloaded.";
            using var hash = SHA256.Create();
            long received = 0;
            await using (var input = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await input.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    received += n;
                    if (received > _maxBytes) return "The SoundFont is larger than expected and was not downloaded.";
                    hash.TransformBlock(buffer, 0, n, null, 0);
                    await output.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
                    progress?.Report((received, total));
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            if (!string.Equals(Convert.ToHexString(hash.Hash!), _sha256, StringComparison.OrdinalIgnoreCase))
                return "The downloaded file does not match the expected SoundFont (checksum mismatch); it was discarded.";
            File.Move(part, destination, overwrite: true);   // same folder: the swap is atomic
            return null;
        }
        catch (OperationCanceledException) { return "Cancelled."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return $"The download failed: {ex.Message}";
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }   // swallow: a leftover partial file is harmless and is overwritten next time
        }
    }
}
