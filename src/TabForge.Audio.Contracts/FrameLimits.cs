namespace TabForge.Audio.Contracts;

// Owns: the largest payload each engine command and each engine event may have, so the reader refuses an oversized frame before it
// allocates a buffer for it. The two directions have their own tables because the two pipes reuse the same numbers for different types.
// Does not own: the framing (Frames), the payload layouts (EngineProtocol) or the isolated plug-in host's pipe (AudioEngine Isolation).
// Tests: TestEngineMessagesRoundTrip, TestEngineMessagesGolden.
/// <summary>
/// Per-command payload limits of the engine pipe. A command that carries one plug-in state gets <see cref="StateBytes"/>, one that
/// carries lists gets <see cref="ListBytes"/>, the master tap chunk gets <see cref="MasterChunkBytes"/>, and every other command gets
/// <see cref="ControlBytes"/>. Writers are held to the same limits by <see cref="Frames.Write"/>'s overall bound.
/// </summary>
public static class FrameLimits
{
    /// <summary>A command or event of a few numbers and short texts (a MIDI log chunk, a program list, a device description).</summary>
    public const int ControlBytes = 1024 * 1024;

    /// <summary>A command or event carrying one plug-in's state: the base64 text (<see cref="PluginStateLimits.MaxBase64Chars"/>) plus its identifiers.</summary>
    public const int StateBytes = PluginStateLimits.MaxBase64Chars + ControlBytes;

    /// <summary>A command carrying a list of clips, routes, bar maps, MIDI processors or auto-pitch changes (bounded per item by its reader).</summary>
    public const int ListBytes = 32 * 1024 * 1024;

    /// <summary>One master tap chunk: <see cref="MasterTapLimits.MaxChunkFrames"/> stereo float32 frames, plus its header.</summary>
    public const int MasterChunkBytes = MasterTapLimits.MaxChunkFrames * 2 * sizeof(float) + ControlBytes;

    /// <summary>The largest payload a command from TabForge to the engine may have, by its type byte.</summary>
    public static int ForCommand(byte type) => type switch
    {
        (byte)EngineCommand.SetState or (byte)EngineCommand.ChainState => StateBytes,
        (byte)EngineCommand.SetTransport or (byte)EngineCommand.SetClips or (byte)EngineCommand.SetGraph or (byte)EngineCommand.SetMidiProcessors
            or (byte)EngineCommand.LoadChain or (byte)EngineCommand.RenderOffline or (byte)EngineCommand.SetAutoPitch => ListBytes,
        _ => ControlBytes,
    };

    /// <summary>The largest payload an event from the engine to TabForge may have, by its type byte.</summary>
    public static int ForEvent(byte type) => type switch
    {
        (byte)EngineEvent.PluginState => StateBytes,
        (byte)EngineEvent.MasterAudio => MasterChunkBytes,
        _ => ControlBytes,
    };
}
