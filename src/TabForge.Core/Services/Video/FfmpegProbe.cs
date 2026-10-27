using System.Diagnostics;
using System.IO;

namespace TabForge.Services.Video;

// Owns: asking an ffmpeg which encoders it has.
// Does not own: choosing among them (FfmpegArguments.Candidates) or encoding (FfmpegVideoEncoder).
// Tests: VideoEncoderPortTests (only when ffmpeg is found).
internal static class FfmpegProbe
{
    /// <summary>The output of "ffmpeg -encoders". Asked on every open (about 0.1 s): a cache would be static state, and the answer is only needed once per recording.</summary>
    public static string EncoderListing(string exe)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in FfmpegArguments.ListEncoders()) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info) ?? throw new VideoEncoderException("ffmpeg could not be started.");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(15000)) { process.Kill(true); throw new VideoEncoderException("ffmpeg did not answer."); }
            error.Wait(1000);
            return output;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { throw new VideoEncoderException("ffmpeg could not be started: " + e.Message, e); }
    }
}
