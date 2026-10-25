using TabForge.Models;

namespace TabForge.Playback;

/// <summary>
/// Owns: a temporary per-channel program the running playback uses instead of the score's (instrument picker preview), kept through loop restarts and partial timeline refreshes, dropped when a start, a seek rebuild or a loop-wrap recompile compiles the song again.
/// Does not own: the document (nothing is edited, so no dirty mark or undo entry). Tests: SelfTestLiveInstrumentPreview.
/// </summary>
public sealed partial class PlaybackEngine
{
    private readonly int[] _programPreview = Enumerable.Repeat(-1, 16).ToArray();

    /// <summary>The channel's preview program, or -1 when none is active.</summary>
    public int ProgramPreviewOf(int channel) => Volatile.Read(ref _programPreview[channel & 0x0F]);

    /// <summary>Switches the channel's sound now (a stopped song has nothing to switch: the caller auditions a note); loop restarts and seeks keep it until <see cref="EndProgramPreview"/>.</summary>
    public void BeginProgramPreview(SongProject project, int deviceId, int channel, int program)
    {
        var ch = channel & 0x0F;
        Volatile.Write(ref _programPreview[ch], Math.Clamp(program, 0, MidiLimits.Max));
        PushChannelProgram(project, deviceId, ch);
    }

    /// <summary>Drops the preview and (unless resend is false) sends the song's own program again, on every channel when stopped (an audition note moved some channel's sound).</summary>
    public void EndProgramPreview(SongProject project, int deviceId, int channel, bool resend = true)
    {
        var ch = channel & 0x0F;
        Volatile.Write(ref _programPreview[ch], -1);
        if (!resend) return;
        if (_running) PushChannelProgram(project, deviceId, ch);
        else SendSetupFromProject(project);
    }

    private void PushChannelProgram(SongProject project, int deviceId, int channel) =>
        PushChannelState(project, e => e.DeviceId == deviceId && (e.Status & 0x0F) == channel && (e.Status & 0xF0) == 0xC0, send: true);

    /// <summary>Drops every preview: a timeline compiled from the whole song already carries its programs.</summary>
    private void ClearProgramPreviews()
    {
        for (var channel = 0; channel < _programPreview.Length; channel++) Volatile.Write(ref _programPreview[channel], -1);
    }

    private int ProgramFor(int status, int data1)
    {
        if ((status & 0xF0) != 0xC0) return data1;
        var p = Volatile.Read(ref _programPreview[status & 0x0F]);
        return p >= 0 ? p : data1;
    }
}
