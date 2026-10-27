using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace TabForge.Services.Video;

// Owns: sink-writer setup, media types and pumping.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed partial class VideoEncoder
{
    private void Note(string text)
    {
        lock (_notes) _notes.Add(text);
        Trace.Error(Trace.Engine, "VideoEncoder: " + text);   // always on (errors.log): the notes explain a bad recording on a PC we cannot see
    }

    // With codec properties asked for, a writer that rejects them is rebuilt without: the export never fails over a tuning property.
    private IMFSinkWriter BeginTuned(bool hardware, out int videoStream, out int audioStream)
    {
        if (hardware && _options.SimulateHardwareFailure) throw new COMException("simulated hardware failure", unchecked((int)0x8000FFFF));
        if (_options.Properties(_live).Count > 0)
        {
            try { return Begin(hardware, true, out videoStream, out audioStream); }
            catch (COMException e) { Note($"codec properties rejected (0x{e.HResult:X8}), encoding without them"); }
        }
        return Begin(hardware, false, out videoStream, out audioStream);
    }

    // Reads back which transform the sink writer really built; any failure leaves the request as the answer.
    private void Inspect(IMFSinkWriter writer, int video)
    {
        try
        {
            if (writer is not IMFSinkWriterEx ex) return;
            // The chain holds a colour converter first (RGB32 in) and then the encoder: pick the one in the encoder category.
            for (var i = 0; i < 6 && ex.GetTransformForStream(video, i, out var category, out var transform) >= 0; i++)
            {
                try
                {
                    if (category != Mf.VideoEncoderCategory || transform is null) continue;
                    // Hardware encoders are asynchronous MFTs, which are event generators; the software encoder is a plain synchronous transform.
                    UsesHardware = transform is IMFMediaEventGenerator;
                    Note("encoder transform: " + DescribeTransform(transform) + $", {(UsesHardware ? "asynchronous (hardware)" : "synchronous (software)")}");
                }
                finally { if (transform is not null && Marshal.IsComObject(transform)) Marshal.ReleaseComObject(transform); }
                var names = UsesHardware ? Mf.H264EncoderNames(4).Distinct().ToList() : new List<string> { "H264 Encoder MFT" };
                EncoderName = (UsesHardware ? "hardware: " : "software: ") + (names.Count > 0 ? string.Join(" or ", names) : "name unknown");
                Note("encoder " + EncoderName);
                return;
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { Note("encoder not identified: " + e.Message); }
    }

    private IMFSinkWriter Begin(bool hardware, bool tune, out int videoStream, out int audioStream)
    {
        Check(Mf.MFCreateAttributes(out var attrs, 3));
        attrs.SetUINT32(Mf.EnableHardware, hardware ? 1 : 0);
        attrs.SetUINT32(Mf.DisableThrottling, 1);
        if (tune && _live && _options.LowLatency) attrs.SetUINT32(VideoEncoderOptions.LowLatencyMode, 1);
        var temps = new List<object> { attrs };   // released with the call: the writer keeps its own references
        try
        {
            Check(Mf.MFCreateSinkWriterFromURL(_path, IntPtr.Zero, attrs, out var writer));
            try
            {
                var t = Own(temps, VideoType(Mf.H264, VideoBitrate(_width, _height, _fps)));
                writer.AddStream(t, out videoStream);
                writer.SetInputMediaType(videoStream, Own(temps, VideoType(Mf.Rgb32, 0)), tune ? Own(temps, Tuning()) : null);
                audioStream = -1;
                if (_audio is { } a)
                {
                    writer.AddStream(Own(temps, AudioType(Mf.Aac, a, AacBytesPerSecond)), out audioStream);
                    writer.SetInputMediaType(audioStream, Own(temps, AudioType(Mf.Pcm, a, a.SampleRate * a.Channels * 2)), null);
                }
                writer.BeginWriting();
                return writer;
            }
            catch { Marshal.ReleaseComObject(writer); throw; }
        }
        finally { foreach (var o in temps) Marshal.ReleaseComObject(o); }
    }

    private static T Own<T>(List<object> temps, T com) where T : class { temps.Add(com); return com; }

    private IMFAttributes Tuning()
    {
        var props = _options.Properties(_live);
        Check(Mf.MFCreateAttributes(out var a, props.Count));
        foreach (var (name, key, value) in props) { a.SetUINT32(key, value); Trace.Write(Trace.Engine, $"VideoEncoder: asking for {name}={value}"); }
        return a;
    }

    private IMFMediaType VideoType(Guid subtype, int bitrate)
    {
        Check(Mf.MFCreateMediaType(out var t));
        t.SetGUID(Mf.MajorType, Mf.Video);
        t.SetGUID(Mf.Subtype, subtype);
        t.SetUINT64(Mf.FrameSize, Mf.Pack(_width, _height));
        t.SetUINT64(Mf.FrameRate, Mf.Pack(_fps, 1));
        t.SetUINT64(Mf.PixelAspect, Mf.Pack(1, 1));
        t.SetUINT32(Mf.InterlaceMode, 2);
        if (bitrate > 0) { t.SetUINT32(Mf.AvgBitrate, bitrate); t.SetUINT32(Mf.Mpeg2Profile, 100); }
        else t.SetUINT32(Mf.DefaultStride, _width * 4);   // positive: top-down rows
        return t;
    }

    private static IMFMediaType AudioType(Guid subtype, VideoAudioFormat a, int bytesPerSecond)
    {
        Check(Mf.MFCreateMediaType(out var t));
        t.SetGUID(Mf.MajorType, Mf.Audio);
        t.SetGUID(Mf.Subtype, subtype);
        t.SetUINT32(Mf.AudioBits, 16);
        t.SetUINT32(Mf.AudioRate, a.SampleRate);
        t.SetUINT32(Mf.AudioChannels, a.Channels);
        t.SetUINT32(Mf.AudioBytesPerSec, bytesPerSecond);
        if (subtype == Mf.Pcm) t.SetUINT32(Mf.AudioBlockAlign, a.Channels * 2);
        else { t.SetUINT32(Mf.AacPayload, 0); t.SetUINT32(Mf.AacProfileLevel, 0x29); }
        return t;
    }

    private void Pump(IMFSinkWriter writer, int video, int audio)
    {
        var frameTicks = 10_000_000L / _fps;
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (item.Slot is { } slot)
            {
                try
                {
                    WriteVideo(writer, video, slot, item.Count, item.Time, frameTicks);
                    _videoSamples++;
                    Volatile.Write(ref _lastVideoTicks, item.Time);
                    if (!_live && UsesHardware) Pace(writer, video);
                }
                finally
                {
                    try { Recycle(slot); }
                    finally { _room.Release(); }
                }
            }
            else if (item.Audio is { } au)
            {
                try
                {
                    var pcm = new byte[item.Count * 2];
                    for (var i = 0; i < item.Count; i++) BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), (short)Math.Round(Math.Clamp(au[i], -1f, 1f) * 32767f));
                    var frames = item.Count / _audio!.Value.Channels;
                    var rate = _audio.Value.SampleRate;
                    Write(writer, audio, pcm, pcm.Length, _audioFrames * 10_000_000L / rate, frames * 10_000_000L / rate);
                    _audioFrames += frames;
                    _audioSamples++;
                    Volatile.Write(ref _audioEndTicks, _audioFrames * 10_000_000L / rate);
                }
                finally { ArrayPool<float>.Shared.Return(au); }
            }
            Interlocked.Increment(ref _beat);
        }
    }

    private static void WriteVideo(IMFSinkWriter writer, int stream, VideoSlot slot, int length, long time, long duration)
    {
        slot.Buffer.Unlock();
        slot.Buffer.SetCurrentLength(length);
        Check(Mf.MFCreateSample(out var sample));
        try
        {
            sample.AddBuffer(slot.Buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            writer.WriteSample(stream, sample);
        }
        finally { Marshal.ReleaseComObject(sample); }
    }

    private static void Write(IMFSinkWriter writer, int stream, byte[] data, int length, long time, long duration)
    {
        Check(Mf.MFCreateMemoryBuffer(length, out var buffer));
        buffer.Lock(out var ptr, out _, out _);
        Marshal.Copy(data, 0, ptr, length);
        buffer.Unlock();
        buffer.SetCurrentLength(length);
        Check(Mf.MFCreateSample(out var sample));
        sample.AddBuffer(buffer);
        sample.SetSampleTime(time);
        sample.SetSampleDuration(duration);
        try { writer.WriteSample(stream, sample); }
        finally { Marshal.ReleaseComObject(sample); Marshal.ReleaseComObject(buffer); }
    }

    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
}
