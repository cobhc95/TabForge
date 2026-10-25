using TabForge.Audio;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the engine settings the FX chain needs once per process: the crash list (a plug-in that crashed is not loaded again until it is allowed) and the
//   automatic pitch matcher that measures a VST instrument when it loads.
// Does not own: the engine client (AudioEngineClient), the matcher (AutoPitchMatcher) or the window that shows its result (FxChainWindow).
// Tests: no dedicated test class.
public static class FxEngineSetup
{
    /// <summary>Gives the engine the live crash list and a pitch matcher; each is set once, so calling again changes nothing.</summary>
    public static void Attach(AudioEngineClient engine, PluginSettings plugins, IUiThread ui)
    {
        engine.Quarantine ??= () => plugins.Quarantined;
        engine.AutoPitch ??= new AutoPitchMatcher(engine, () => plugins, ui);
    }
}
