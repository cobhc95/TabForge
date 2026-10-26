using TabForge.Audio;
using TabForge.Platform.Windows;
using TabForge.Playback;
using TabForge.Plugins;

namespace TabForge.Avalonia.Playback;

// Owns: the one place the Avalonia app builds its playback adapters: the audio engine client (with the Windows plug-in
// file-trust adapter inside the PluginTrustLoadGate the app uses, so both apps ask the same trust), the winmm MIDI output
// and the NAudio device list. The app composes the same adapters in the UI-thread adapter.
// Does not own: the engine's lifetime and I/O (AudioEngineClient), the trust rules (PluginTrust), the device calls (the Core
//   Windows adapters), or any view (nothing is wired to UI yet).
// Tests: AvaloniaPlaybackCompositionTests.
// Windows only: the audio engine, the MIDI and audio device calls and the plug-in file checks are Windows-only (the project builds net8.0-windows on Windows).
public static class AvaloniaPlaybackComposition
{
    /// <summary>A new engine client over the desktop plug-in trust. The caller keeps the one client for the process.</summary>
    public static AudioEngineClient CreateEngineClient(PluginTrust trust) => new(new PluginTrustLoadGate(new WindowsPluginFileTrust(), trust));

    /// <summary>A new MIDI output over winmm. WinMM allows one handle per device, so the caller keeps the one output for the process.</summary>
    public static IMidiOutput CreateMidiOutput() => new WinMmMidiOutput(new WindowsMidiDevices());
}
