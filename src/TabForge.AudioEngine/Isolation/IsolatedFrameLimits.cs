using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Isolation;

// Owns: the largest payload each message may have on the isolated plug-in pipe, in both directions (the engine reads replies, the plug-in
//   host reads commands). Only a state carries more than a control message.
// Does not own: the framing (Frames), the engine pipe's table (FrameLimits) or what the messages mean (RemotePlugin, PluginHostMain).
// Tests: TestEngineMessagesRoundTrip, TestEngineMessagesGolden.
/// <summary>The per-type payload limits of the isolated plug-in pipe (see <see cref="Frames.Read"/>).</summary>
public static class IsolatedFrameLimits
{
    /// <summary>The host reads these: a state to restore is the only large command.</summary>
    public static int ForCommand(byte type) => type == RemotePlugin.CmdSetState ? FrameLimits.StateBytes : FrameLimits.ControlBytes;

    /// <summary>The engine reads these: a plug-in's state is the only large reply.</summary>
    public static int ForReply(byte type) => type == RemotePlugin.MsgState ? FrameLimits.StateBytes : FrameLimits.ControlBytes;
}
