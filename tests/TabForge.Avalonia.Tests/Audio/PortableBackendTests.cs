using TabForge.Platform.Contracts;
using System;
using System.IO;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Synth;
using TabForge.Platform;
using TabForge.Platform.Portable;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: cross-platform Stage 3, the choices: which audio, MIDI and MP3 adapters each OS gets (Windows keeps NAudio WASAPI/ASIO, winmm and Media Foundation),
//   the portable adapters listing without their libraries, and the non-Windows sound bank rule (the OS flag is injected).
// Does not own: the devices themselves (PortableDeviceTests) or the native libraries (native/tfportable).
// Tests: itself.
public sealed class PortableBackendTests
{
    private static EngineConfig Config(string driver, string device = "") => new(driver, device, 48000, 256, false);

    [Fact]
    public void WindowsHostKeepsTheNAudioDriversAndTheHeadlessOutput()
    {
        foreach (var driver in new[] { AudioDriverNames.WasapiShared, AudioDriverNames.WasapiExclusive, AudioDriverNames.Asio, AudioDriverNames.DirectSound, AudioOutputFactory.Null })
            Assert.IsType<WindowsAudioBackend>(AudioBackends.For(Config(driver), windowsHost: true));
        Assert.IsType<MiniaudioBackend>(AudioBackends.For(Config(AudioDriverNames.Miniaudio), windowsHost: true));   // only when chosen (the dev switch offers it)
    }

    [Fact]
    public void OtherHostsUseMiniaudioExceptForTheHeadlessOutput()
    {
        foreach (var driver in new[] { AudioDriverNames.WasapiShared, AudioDriverNames.Asio, AudioDriverNames.Miniaudio })
            Assert.IsType<MiniaudioBackend>(AudioBackends.For(Config(driver), windowsHost: false));
        Assert.IsType<WindowsAudioBackend>(AudioBackends.For(Config(AudioOutputFactory.Null), windowsHost: false));
        var settings = Config(AudioDriverNames.WasapiShared, "Speakers");
        Assert.Equal(new EngineConfig(AudioDriverNames.Miniaudio, "", 48000, 256, false), AudioBackends.Normalize(settings, windowsHost: false));
        Assert.Same(settings, AudioBackends.Normalize(settings, windowsHost: true));   // Windows settings are never rewritten
    }

#if WINDOWS
    [Fact]
    public void ThisWindowsMachineDefaultsToTheWindowsAdapters()
    {
        if (!OperatingSystem.IsWindows()) return;
        var devSwitch = Environment.GetEnvironmentVariable(AudioBackends.DevVariable) is not null || Environment.GetEnvironmentVariable(MidiBackends.DevVariable) is not null
            || Environment.GetEnvironmentVariable(LameMp3Encoder.DevVariable) is not null;
        if (devSwitch) return;   // a developer opted in on this computer
        Assert.IsType<TabForge.Platform.Windows.WinMmMidiInputBackend>(MidiBackends.Input);
        Assert.IsType<TabForge.Platform.Windows.WindowsMidiDevices>(MidiBackends.Output);
        Assert.IsType<TabForge.Platform.Windows.WindowsMp3Encoder>(SystemAudio.Mp3);
        Assert.Equal(new[] { AudioDriverNames.WasapiShared, AudioDriverNames.WasapiExclusive, AudioDriverNames.Asio, AudioDriverNames.DirectSound }, AudioDriverNames.All);
        Assert.Equal(AudioDriverNames.WasapiShared, AudioDriverNames.Default);
        Assert.IsType<TabForge.Platform.Windows.WindowsAudioDevices>(TabForge.Avalonia.Platform.PlatformAdapters.AudioDevices());
    }
#endif

    [Fact]
    public void MidiAndMp3ChoiceFollowsTheHostAndTheDevSwitch()
    {
#if WINDOWS
        Assert.IsType<TabForge.Platform.Windows.WinMmMidiInputBackend>(MidiBackends.CreateInput(windowsHost: true, portableRequested: false));
        Assert.IsType<TabForge.Platform.Windows.WindowsMidiDevices>(MidiBackends.CreateOutput(windowsHost: true, portableRequested: false));
        Assert.IsType<TabForge.Platform.Windows.WindowsMp3Encoder>(SystemAudio.CreateMp3(windowsHost: true, lameRequested: false));
#endif
        Assert.IsType<RtMidiInputBackend>(MidiBackends.CreateInput(windowsHost: false, portableRequested: false));
        Assert.IsType<RtMidiOutputDevices>(MidiBackends.CreateOutput(windowsHost: false, portableRequested: false));
        Assert.IsType<LameMp3Encoder>(SystemAudio.CreateMp3(windowsHost: false, lameRequested: false));
        Assert.IsType<RtMidiInputBackend>(MidiBackends.CreateInput(windowsHost: true, portableRequested: true));
        Assert.IsType<LameMp3Encoder>(SystemAudio.CreateMp3(windowsHost: true, lameRequested: true));
    }

    [Fact]
    public void NonWindowsNeverReadsTheWindowsBank()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tabforge-no-such-bank", "GeneralUser-GS.sf2");
        var bank = new GmBank(windowsHost: false, bundledPath: missing);
        Assert.Null(bank.Current.Font);
        var failure = Assert.Throws<FileNotFoundException>(() => bank.Current.Bank);
        Assert.Equal(GmBank.NoBankMessage, failure.Message);
        Assert.True(bank.Select("", out _));   // Windows GM asked for: still no gm.dls, still the download message
        Assert.Throws<FileNotFoundException>(() => bank.Current.Bank);
        Assert.False(bank.Select(Path.Combine(Path.GetTempPath(), "tabforge-no-such-bank", "missing.sf2"), out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void NonWindowsPlaysTheBundledSoundFontWhenItIsThere()
    {
        var dls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "gm.dls");
        if (!OperatingSystem.IsWindows() || !File.Exists(dls)) return;   // the SoundFont image for this test is made from the gm.dls of this computer
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-bundled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var bundled = Path.Combine(folder, "GeneralUser-GS.sf2");
        try
        {
            File.WriteAllBytes(bundled, DlsToSoundFont.Convert(File.ReadAllBytes(dls)));
            var bank = new GmBank(windowsHost: false, bundledPath: bundled);
            Assert.NotNull(bank.Current.Font);
            Assert.Equal(bundled, bank.Current.Path);
            Assert.Equal(GmBank.SoundFontMasterVolume, bank.Current.MasterVolume);
            Assert.NotNull(bank.Current.Bank);
            var version = bank.Current.Version;
            Assert.True(bank.Select("", out _));
            Assert.NotNull(bank.Current.Font);   // an empty selection falls back to the bundled font, never to gm.dls
            Assert.True(bank.Current.Version > version);
            Assert.Null(new GmBank(windowsHost: true, bundledPath: bundled).Current.Font);   // Windows ignores the bundled font until one is selected
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }   // swallow: best-effort test cleanup
    }

    [Fact]
    public void WindowsKeepsWindowsGmAsTheDefaultBank()
    {
        var bank = new GmBank(windowsHost: true, bundledPath: Path.Combine(Path.GetTempPath(), "tabforge-no-such-bank", "GeneralUser-GS.sf2"));
        Assert.Null(bank.Current.Font);
        Assert.True(bank.Current.WindowsHost);
        Assert.Equal(GmSynthTuning.EffectiveMasterVolume, bank.Current.MasterVolume);
    }

    [Fact]
    public void SoundEngineChoiceOffWindowsIsTheSoundFontOnly()
    {
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve(SoundEngines.WindowsGm, windowsHost: false));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("", windowsHost: false));
        Assert.Equal(SoundEngines.WindowsGm, SoundEngines.Resolve(SoundEngines.WindowsGm, windowsHost: true));
        Assert.Equal(SoundEngines.WindowsGm, SoundEngines.Resolve(null, windowsHost: true));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve(SoundEngines.SoundFont, windowsHost: true));
        var settings = new PluginSettings { SoundEngine = SoundEngines.SoundFont };
        if (SoundFontStore.IsInstalled || File.Exists(SoundFontStore.BundledPath)) return;
        Assert.True(SoundFontStore.ChosenButMissing(settings, windowsHost: false));   // the Download SoundFont prompt shows instead of silence
        Assert.True(SoundFontStore.ChosenButMissing(settings, windowsHost: true));
        Assert.Equal("", SoundFontStore.ResolvedPath(windowsHost: false));
    }

    [Fact]
    public void BundledSoundFontIsUsedOffWindowsOnlyWhenNoDownloadedOne()
    {
        if (SoundFontStore.IsInstalled || File.Exists(SoundFontStore.BundledPath)) return;   // a real file is there: do not touch it
        Directory.CreateDirectory(Path.GetDirectoryName(SoundFontStore.BundledPath)!);
        File.WriteAllBytes(SoundFontStore.BundledPath, new byte[] { 1 });
        try
        {
            var settings = new PluginSettings { SoundEngine = SoundEngines.SoundFont };
            Assert.Equal(SoundFontStore.BundledPath, SoundFontStore.ResolvedPath(windowsHost: false));
            Assert.False(SoundFontStore.ChosenButMissing(settings, windowsHost: false));
            Assert.Equal("", SoundFontStore.ResolvedPath(windowsHost: true));
        }
        finally { File.Delete(SoundFontStore.BundledPath); }
    }

    [Fact]
    public void PortableListsNeverThrow()
    {
        Assert.NotNull(new RtMidiInputBackend().InputNames());
        _ = new RtMidiOutputDevices().OutputDeviceCount();
        Assert.NotNull(new PortableAudioDevices(nullDevice: true).CaptureEndpointNames());
        Assert.NotNull(new MiniaudioBackend(nullDevice: true).Devices(capture: false));
        if (TfPortable.Current is not null) return;
        Assert.Empty(new RtMidiInputBackend().InputNames());
        Assert.False(new RtMidiInputBackend().IsAvailable);
        Assert.False(new MiniaudioBackend(nullDevice: true).IsAvailable);
        Assert.Throws<MidiPortException>(() => new RtMidiInputBackend().Open(0));
    }
}
