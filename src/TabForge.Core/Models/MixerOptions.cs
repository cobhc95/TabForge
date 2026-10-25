namespace TabForge.Models;

// Owns: per-engine how-a-track-sounds settings (route, auto GM).
// Does not own: the routing functions (MixerGroups) or the settings applier.
// Tests: AvaloniaEngineRoutingTests, AudioSmokeTests.

/// <summary>
/// Settings that decide how a track sounds (see <see cref="MixerGroups.RouteOf"/>). The routing functions take an
/// instance, which each audio engine client owns (<c>AudioEngineClient.Mixer</c>); the settings applier updates it.
/// </summary>
public sealed class MixerOptions
{
    /// <summary>
    /// Settings > Audio &amp; VST: every MIDI track is played by the audio engine's General MIDI synth (so the whole song goes
    /// out through the chosen audio driver, ASIO included) instead of Windows MIDI, which ignores the driver.
    /// </summary>
    public bool PlayAllThroughEngine { get; set; } = true;

    /// <summary>Settings: tick a track's GM sound automatically when no VST instrument plays it, and untick it when one does again.</summary>
    public bool AutoGmSound { get; set; } = true;
}
