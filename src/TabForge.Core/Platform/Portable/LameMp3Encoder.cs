using TabForge.Platform.Contracts;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace TabForge.Platform.Portable;

// Owns: MP3 export through the LAME library (libmp3lame, LGPL, loaded at run time as a separate replaceable file): loading it, and encoding 16-bit PCM to a file.
// Does not own: which encoder an OS uses (SystemAudio in IMp3Encoder.cs) or the render that produces the PCM (RenderJob).
// Tests: PortableBackendTests (encode a sine, decode it with NLayer, check the duration).
/// <summary>
/// The portable <see cref="IMp3Encoder"/>: LAME's C API through function pointers. libmp3lame is found next to the program (the Linux and macOS
/// packages bundle it) or on the system (Homebrew, apt). With no library <see cref="IsAvailable"/> is false and MP3 export is switched off, as before.
/// </summary>
public sealed unsafe class LameMp3Encoder : IMp3Encoder
{
    /// <summary>Dev switch for Windows: set to "lame" to export MP3 through LAME instead of Media Foundation (needs libmp3lame.dll). Default off.</summary>
    public const string DevVariable = "TABFORGE_MP3_ENCODER";

    private static string[] Candidates() => new[]
    {
        "libmp3lame.64.dll", "libmp3lame.dll", "mp3lame.dll",
        "libmp3lame.so.0", "libmp3lame.so",
        "libmp3lame.0.dylib", "libmp3lame.dylib", "/opt/homebrew/lib/libmp3lame.0.dylib", "/opt/homebrew/lib/libmp3lame.dylib", "/usr/local/lib/libmp3lame.0.dylib",
    };

    private readonly Lazy<nint> _library = new(Load);

    public bool IsAvailable() => _library.Value != 0;

    private static nint Load()
    {
        foreach (var name in Candidates())
        {
            var path = Path.IsPathRooted(name) ? name : Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)) return handle;
            if (!Path.IsPathRooted(name) && NativeLibrary.TryLoad(name, out handle)) return handle;
        }
        return 0;
    }

    public void Encode(IWaveProvider pcm16, string path, int bitsPerSecond)
    {
        var format = pcm16.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16) throw new ArgumentException("The MP3 encoder takes 16-bit PCM.", nameof(pcm16));
        if (format.Channels is < 1 or > 2) throw new ArgumentException("The MP3 encoder takes mono or stereo.", nameof(pcm16));
        if (!IsAvailable()) throw new PlatformNotSupportedException("The LAME library (libmp3lame) is not installed.");
        var lame = _library.Value;
        var init = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(lame, "lame_init");
        var setInRate = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(lame, "lame_set_in_samplerate");
        var setChannels = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(lame, "lame_set_num_channels");
        var setBitrate = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(lame, "lame_set_brate");
        var setMode = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(lame, "lame_set_mode");
        var setQuality = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(lame, "lame_set_quality");
        var initParams = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(lame, "lame_init_params");
        var encodeInterleaved = (delegate* unmanaged[Cdecl]<nint, short*, int, byte*, int, int>)NativeLibrary.GetExport(lame, "lame_encode_buffer_interleaved");
        var encodeMono = (delegate* unmanaged[Cdecl]<nint, short*, short*, int, byte*, int, int>)NativeLibrary.GetExport(lame, "lame_encode_buffer");
        var flush = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)NativeLibrary.GetExport(lame, "lame_encode_flush");
        var close = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(lame, "lame_close");

        var gfp = init();
        if (gfp == 0) throw new InvalidOperationException("LAME could not be initialised.");
        try
        {
            var channels = format.Channels;
            _ = setInRate(gfp, format.SampleRate);
            _ = setChannels(gfp, channels);
            _ = setBitrate(gfp, Math.Clamp(bitsPerSecond / 1000, 32, 320));
            _ = setMode(gfp, channels == 1 ? 3 : 1);   // MONO, JOINT_STEREO
            _ = setQuality(gfp, 2);
            if (initParams(gfp) < 0) throw new InvalidOperationException("LAME rejected the encoder settings.");

            const int FramesPerRead = 4096;
            var bytes = new byte[FramesPerRead * channels * 2];
            var samples = new short[FramesPerRead * channels];
            var mp3 = new byte[(int)(1.25 * FramesPerRead) + 7200];
            using var file = File.Create(path);
            fixed (short* pcm = samples)
            fixed (byte* outBuffer = mp3)
            {
                int read;
                while ((read = ReadFully(pcm16, bytes)) > 0)
                {
                    var frames = read / (channels * 2);
                    if (frames == 0) break;
                    Buffer.BlockCopy(bytes, 0, samples, 0, frames * channels * 2);
                    var made = channels == 1 ? encodeMono(gfp, pcm, pcm, frames, outBuffer, mp3.Length) : encodeInterleaved(gfp, pcm, frames, outBuffer, mp3.Length);
                    if (made < 0) throw new InvalidOperationException($"LAME failed while encoding (code {made}).");
                    file.Write(mp3, 0, made);
                    if (read < bytes.Length) break;
                }
                var tail = flush(gfp, outBuffer, mp3.Length);
                if (tail > 0) file.Write(mp3, 0, tail);
            }
        }
        finally { _ = close(gfp); }
    }

    private static int ReadFully(IWaveProvider source, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = source.Read(buffer, total, buffer.Length - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
