namespace TabForge.Playback;

/// <summary>
/// Owns: a temporary per-channel program the running playback uses instead of the score's (instrument picker preview).
/// Does not own: the document (nothing is edited, so no dirty mark or undo entry). Tests: SelfTestLiveInstrumentPreview.
/// </summary>
public sealed partial class PlaybackEngine
{
    private readonly int[] _programPreview = Enumerable.Repeat(-1, 16).ToArray();

    /// <summary>The channel's preview program, or -1 when none is active.</summary>
    public int ProgramPreviewOf(int channel) => Volatile.Read(ref _programPreview[channel & 0x0F]);

    /// <summary>Switches the channel's sound now; loop restarts and seeks keep it until <see cref="EndProgramPreview"/>.</summary>
    public void BeginProgramPreview(int deviceId, int channel, int program)
    {
        var ch = channel & 0x0F;
        program = Math.Clamp(program, 0, 127);
        Volatile.Write(ref _programPreview[ch], program);
        lock (_outputGate) _output.Send(deviceId, 0xC0 | ch, program, 0);
    }

    /// <summary>Drops the preview and sends the score's own program again.</summary>
    public void EndProgramPreview(int deviceId, int channel, int originalProgram)
    {
        var ch = channel & 0x0F;
        Volatile.Write(ref _programPreview[ch], -1);
        lock (_outputGate) _output.Send(deviceId, 0xC0 | ch, Math.Clamp(originalProgram, 0, 127), 0);
    }

    private int ProgramFor(int status, int data1)
    {
        if ((status & 0xF0) != 0xC0) return data1;
        var p = Volatile.Read(ref _programPreview[status & 0x0F]);
        return p >= 0 ? p : data1;
    }
}
