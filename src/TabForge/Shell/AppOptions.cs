using TabForge.Playback;
using TabForge.Visualization;

namespace TabForge.Shell;

/// <summary>
/// The settings-driven option objects the application shares: what every playback engine reads and what the code-drawn views read.
/// The composition root (<c>App</c>) creates one and hands it to each main window (a torn-off window gets its parent's); the window's
/// <c>DocumentManager</c> gives <see cref="Playback"/> to the engine of every document it holds, so a settings change applies live to every open song, in any window.
/// The mixer routing options belong to the audio engine client (<c>AudioEngineClient.Mixer</c>).
/// Owns: the instances. Does not own: applying settings (the window's settings applier writes them).
/// </summary>
public sealed class AppOptions
{
    public PlaybackPreferences Playback { get; } = new();
    public VisualOptions Visual { get; } = new();
}
