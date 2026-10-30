using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the checks of the optional SoundFont sound engine and the optional bundled fonts: the settings, the live bank switch on a running
//   synth (with the local SoundFont copy, never the network), the font provider and the downloader (fake HTTP, hash mismatch, size limit, cancel).
// Does not own: the code it checks.
// Tests: SoundEngines, SoundFontStore, SoundFontDownloader, GmBank, GmSynth, FontProvider, AvaloniaFonts.
[Collection("SoundEngineGlobals")]
public sealed class SoundEngineTests
{
    private static string LocalSoundFont
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "Docs", "ThirdPartyDownloads", "generaluser", "GeneralUser-GS.sf2"))) dir = Path.GetDirectoryName(dir);
            return dir is null ? "" : Path.Combine(dir, "Docs", "ThirdPartyDownloads", "generaluser", "GeneralUser-GS.sf2");
        }
    }

    [Fact]
    public void DefaultsFollowTheOperatingSystemAndStoredChoicesWin()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? SoundEngines.WindowsGm : SoundEngines.SoundFont, SoundEngines.Resolve(""));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve(SoundEngines.SoundFont));
        Assert.Equal(SoundEngines.WindowsGm, SoundEngines.Resolve(SoundEngines.WindowsGm));
        Assert.Equal(OperatingSystem.IsWindows() ? FontProvider.WindowsSource : FontProvider.BundledSource, FontProvider.DefaultSource);
        var rows = SettingsCatalog.Build(new AppSettings());
        Assert.Contains(rows, r => r.Key == "vst.soundengine");
        Assert.Contains(rows, r => r.Key == "appearance.fontsource");
        Assert.Contains(rows, r => r.Key == "vst.soundengine");
    }

    [Fact]
    public void MissingSoundFontFallsBackToWindowsGm()
    {
        if (SoundFontStore.IsInstalled) return;   // a machine that downloaded it: nothing is missing (the test never changes the profile folder)
        var plugins = new PluginSettings { SoundEngine = SoundEngines.SoundFont };
        Assert.True(SoundFontStore.ChosenButMissing(plugins));
        Assert.Equal("", SoundFontStore.ActivePath(plugins));
        Assert.Equal("", SoundFontStore.ActivePath(new PluginSettings { SoundEngine = SoundEngines.WindowsGm }));
    }

    [Fact]
    public void NoneIsOfferedAndDefaultsAreUnchanged()
    {
        Assert.Contains(SoundEngines.WindowsGm, SoundEngines.All);
        Assert.Contains(SoundEngines.SoundFont, SoundEngines.All);
        Assert.Contains(SoundEngines.None, SoundEngines.All);
        Assert.Equal(OperatingSystem.IsWindows() ? SoundEngines.WindowsGm : SoundEngines.SoundFont, SoundEngines.Default);
        Assert.Equal(SoundEngines.None, SoundEngines.Resolve(SoundEngines.None, windowsHost: true));
        Assert.Equal(SoundEngines.None, SoundEngines.Resolve(SoundEngines.None, windowsHost: false));
        Assert.Equal(SoundEngines.WindowsGm, SoundEngines.Resolve("", windowsHost: true));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("", windowsHost: false));
        Assert.Equal(SoundEngines.WindowsGm, SoundEngines.Resolve("bogus", windowsHost: true));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("bogus", windowsHost: false));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve(SoundEngines.WindowsGm, windowsHost: false));
    }

    [Fact]
    public void NoneRoundTripsAndWarnsAboutNothing()
    {
        var plugins = new PluginSettings { SoundEngine = SoundEngines.None };
        Assert.Equal(SoundEngines.None, SoundEngines.Resolve(plugins.SoundEngine));
        Assert.Equal(SoundEngines.None, SoundFontStore.ActivePath(plugins));
        Assert.False(SoundFontStore.ChosenButMissing(plugins));
        Assert.False(SoundFontStore.ChosenButMissing(plugins, windowsHost: false));
        Assert.Contains(SettingsCatalog.Build(new AppSettings()), r => r.Key == "vst.playall" && r.DependsOn == "vst.soundengine");
    }

    [Fact]
    public void NoneKeepsOnlyInstrumentTracksInTheEngine()
    {
        var mixer = new MixerOptions();
        AvaloniaEngineRouting.ApplyMixerOptions(new PluginSettings { SoundEngine = SoundEngines.None, PlayAllThroughEngine = true }, mixer);
        Assert.True(mixer.SoundNone);
        var plain = new TrackModel { Name = "Piano" };
        Assert.False(MixerGroups.MidiInEngine(plain, mixer));
        Assert.False(MixerGroups.GmSounds(plain, mixer));
        var vst = new TrackModel { Name = "VST", SoundSource = SoundSources.Plugins };
        vst.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Type = PluginSlotType.Instrument });
        Assert.True(MixerGroups.MidiInEngine(vst, mixer));
        var effects = new TrackModel { Name = "FX", SoundSource = SoundSources.Plugins };
        effects.Rig.Plugins.Add(new PluginSlot { Name = "Echo" });
        Assert.True(MixerGroups.MidiInEngine(effects, mixer));   // stays loaded, silent
        Assert.False(MixerGroups.GmSounds(effects, mixer));
        Assert.False(MixerGroups.GmSounds(vst, mixer));
        var normal = new MixerOptions();
        Assert.True(MixerGroups.GmSounds(plain, normal));
    }

    [Fact]
    public void NoneDropsWindowsMidiButKeepsTheClickAndExternalOutputs()
    {
        using var client = new AudioEngineClient(new PluginTrust());
        client.AttachFakeForTest();
        client.Mixer.SoundNone = true;
        var heard = new List<(int Device, int Status, int D1, int D2)>();
        var resets = 0;
        var routing = new RoutedMidiOutput(new CaptureOutput((d, s, a, b) => heard.Add((d, s, a, b)), () => resets++), client);
        routing.SetRoutes(Enumerable.Repeat(-1, 16).ToArray());
        routing.Send(-1, 0x90, 60, 100);          // a track's note for the Windows synth: silent
        routing.Send(3, 0x90, 60, 100);           // an external MIDI output: untouched
        routing.SendMetronome(-1, 0x99, 38, 100); // the click fallback: still sounds
        Assert.DoesNotContain(heard, m => m.Device == -1 && m.Status == 0x90);
        Assert.Contains(heard, m => m.Device == 3 && m.Status == 0x90);
        Assert.Contains(heard, m => m.Device == -1 && m.Status == 0x99);
        routing.ResetAll();                        // all-notes-off still reaches both outputs
        Assert.True(resets > 0);
    }

    [Fact]
    public void NoneWithPlayAllStillSendsEngineTracksToTheirSlots()
    {
        using var client = new AudioEngineClient(new PluginTrust());
        client.AttachFakeForTest();
        client.Mixer.SoundNone = true;
        var engineHeard = new List<TabForge.Audio.Contracts.TimedMidi>();
        client.WrittenForTest = m => engineHeard.Add(m);
        var routing = new RoutedMidiOutput(new CaptureOutput((_, _, _, _) => { }, () => { }), client);
        var routes = Enumerable.Repeat(-1, 16).ToArray();
        routes[0] = 5;
        routing.SetRoutes(routes);
        routing.Send(-1, 0x90, 60, 100);   // channel 0 rides an engine slot: a VST instrument plays normally
        Assert.Contains(engineHeard, m => m.Slot == 5);
    }

    [Fact]
    public void NoneHoldsNoSoundBank()
    {
        var bank = new GmBank(windowsHost: true, bundledPath: Path.Combine(Path.GetTempPath(), "tabforge-no-such-bank", "GeneralUser-GS.sf2"));
        Assert.True(bank.Select(SoundEngines.None, out var error), error);
        Assert.True(bank.Current.Silent);
        Assert.Equal(SoundEngines.None, SoundFontStore.ActivePath(new PluginSettings { SoundEngine = SoundEngines.None }));
        Assert.True(bank.Select("", out _));   // switching back restores Windows GM
        Assert.False(bank.Current.Silent);
    }

    [Fact]
    public void PlayAllStaysInMoreOptionsAndIsDisabledWithANoteInNone()
    {
        var saved = new AppSettings();
        saved.Plugins.SoundEngine = SoundEngines.None;
        var rows = SettingsCatalog.Build(saved).ToDictionary(r => r.Key);
        var playall = rows["vst.playall"];
        Assert.False(TabForge.Presenters.PreferencesPagePresenter.IsEnabled(playall, k => rows.GetValueOrDefault(k)));
        Assert.Equal("Not used while Sound engine is None.", playall.DisabledNote);
        Assert.True(saved.Plugins.PlayAllThroughEngine);   // choosing None never overwrites the stored choice
        Assert.True(SettingsCatalog.PlaceOf("vst.playall") is { More: true });
        Assert.Equal(3, SettingsCatalog.Build(saved).Count(r => r.Group == "Playback engine" && r.More));
        saved.Plugins.SoundEngine = SoundEngines.WindowsGm;
        rows = SettingsCatalog.Build(saved).ToDictionary(r => r.Key);
        Assert.True(TabForge.Presenters.PreferencesPagePresenter.IsEnabled(rows["vst.playall"], k => rows.GetValueOrDefault(k)));
    }

    /// <summary>Every message the output gets and every reset of it, for the routing tests.</summary>
    private sealed class CaptureOutput(Action<int, int, int, int> heard, Action reset) : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) => heard(deviceId, status, data1, data2);
        public void ResetAll() => reset();
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void ARunningSynthSwitchesBankLive()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(DlsToSoundFont.WindowsBankPath) || LocalSoundFont.Length == 0) return;   // needs gm.dls and the local copy
        {
            var bank = new GmBank();
            using var synth = new GmSynth(48000, 512, bank);
            var windows = RenderNote(synth);
            Assert.True(bank.Select(LocalSoundFont, out var error), error);
            var soundFont = RenderNote(synth);   // the same synth object, next block: rebuilt on the new bank
            Assert.True(windows > 0.001 && soundFont > 0.001);
            Assert.NotEqual(windows, soundFont, 3);
            Assert.False(bank.Select(LocalSoundFont + ".missing.sf2", out _));   // an unreadable file leaves the bank as it was
            Assert.Equal(LocalSoundFont, bank.Current.Path);
        }
    }

    private static double RenderNote(GmSynth synth)
    {
        var output = new[] { new float[4800], new float[4800] };
        var input = new[] { new float[4800], new float[4800] };
        var midi = new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } };
        synth.Process(input, output, 4800, midi, new TransportInfo());
        synth.Process(input, output, 4800, ReadOnlySpan<BlockMidi>.Empty, new TransportInfo());
        return Math.Sqrt(output[0].Sum(v => (double)v * v) / output[0].Length);
    }

    [Fact]
    public void FontProviderMapsLogicalFamiliesAndRestores()
    {
        var fonts = new FontProvider();
        {
            fonts.Configure(FontProvider.WindowsSource);
            Assert.Equal("Segoe UI", fonts.Map("Segoe UI"));
            Assert.Equal("Consolas", UiFonts.MonoFamily(fonts));
            Assert.True(fonts.Configure(FontProvider.BundledSource));
            Assert.Equal("Inter", fonts.Map("Segoe UI"));
            Assert.Equal("JetBrains Mono", fonts.Map("consolas"));
            Assert.Equal("Segoe UI Symbol", fonts.Map("Segoe UI Symbol"));
            Assert.Equal("Arial", fonts.Map("Arial"));
            Assert.Equal("JetBrains Mono", UiFonts.MonoFamily(fonts));
            Assert.Equal(FontProvider.SourceChoices[1], FontProvider.ChoiceOf(FontProvider.BundledSource));
            Assert.Equal(FontProvider.WindowsSource, FontProvider.SourceOf(FontProvider.SourceChoices[0]));
        }
    }

    [AvaloniaFact]
    public void BundledFontsLoadFromTheResourcesAndBoldDiffers()
    {
        var fonts = AvaloniaFonts.Provider;
        Application.Current!.Resources[AvaloniaFonts.ResourceKey] = fonts;
        try
        {
            fonts.Configure(FontProvider.BundledSource);
            var measure = new AvaloniaPathsAndText();
            var regular = measure.Measure("Hamburgefonstiv 0123", "Segoe UI", 20, 400, false, 1).Width;
            var bold = measure.Measure("Hamburgefonstiv 0123", "Segoe UI", 20, 700, false, 1).Width;
            var mono = measure.Measure("iiiiiiii", "Consolas", 20, 400, false, 1).Width;
            var monoWide = measure.Measure("WWWWWWWW", "Consolas", 20, 400, false, 1).Width;
            Assert.True(bold > regular, $"Inter Bold should be wider than Regular ({bold} vs {regular})");
            Assert.Equal(mono, monoWide, 3);   // JetBrains Mono: every glyph the same width
            Assert.Contains(AvaloniaFonts.BundledFolder, AvaloniaFonts.Family("Segoe UI").Key!.Source!.ToString() + "#");
        }
        finally { fonts.Configure(""); }
    }

    [AvaloniaFact]
    public void BundledFamiliesResolveToTheRealGlyphTypefaces()
    {
        var fonts = AvaloniaFonts.Provider;
        Application.Current!.Resources[AvaloniaFonts.ResourceKey] = fonts;
        try
        {
            fonts.Configure(FontProvider.BundledSource);
            foreach (var (logical, expected) in new[] { ("Segoe UI", "Inter"), ("Consolas", "JetBrains Mono") })
            {
                var typeface = new global::Avalonia.Media.Typeface(AvaloniaFonts.Family(logical, fonts));
                Assert.True(global::Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(typeface, out var glyph), logical + " did not resolve");
                Assert.Equal(expected, glyph.FamilyName);
            }
        }
        finally { fonts.Configure(""); }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => answer(request, cancel);
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static string Temp() => Path.Combine(Path.GetTempPath(), "tf-sf-" + Guid.NewGuid().ToString("N"), "bank.sf2");

    [Fact]
    public async Task DownloadSavesAVerifiedFileAndReportsProgress()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var seen = 0L;
        var progress = new Progress<(long, long)>(p => seen = Math.Max(seen, p.Item1));
        var target = Temp();
        var downloader = new SoundFontDownloader(new FakeHandler((_, _) => Task.FromResult(Ok(bytes))), "https://fake.test/a.sf2", hash);
        Assert.Null(await downloader.DownloadAsync(target, progress, CancellationToken.None));
        Assert.Equal(bytes, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + ".part"));
        await Task.Delay(50);
        Assert.True(seen > 0);
    }

    [Fact]
    public async Task AHashMismatchLeavesNothingBehind()
    {
        var target = Temp();
        var downloader = new SoundFontDownloader(new FakeHandler((_, _) => Task.FromResult(Ok(new byte[1000]))), "https://fake.test/a.sf2", SoundFontDownloader.Sha256);
        var error = await downloader.DownloadAsync(target, null, CancellationToken.None);
        Assert.Contains("checksum", error);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task ATooLargeFileAndAPlainHttpAddressAreRefused()
    {
        var target = Temp();
        var big = new SoundFontDownloader(new FakeHandler((_, _) => Task.FromResult(Ok(new byte[5000]))), "https://fake.test/a.sf2", "00", maxBytes: 1000);
        Assert.Contains("larger", await big.DownloadAsync(target, null, CancellationToken.None));
        Assert.False(File.Exists(target));
        var plain = new SoundFontDownloader(new FakeHandler((_, _) => throw new InvalidOperationException("must not be called")), "http://fake.test/a.sf2");
        Assert.Contains("HTTPS", await plain.DownloadAsync(target, null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelStopsTheDownloadAndKeepsNoFile()
    {
        var target = Temp();
        using var cancel = new CancellationTokenSource();
        var downloader = new SoundFontDownloader(new FakeHandler(async (_, token) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); return Ok(Array.Empty<byte>()); }), "https://fake.test/a.sf2");
        Assert.Equal("Cancelled.", await downloader.DownloadAsync(target, null, cancel.Token));
        Assert.False(File.Exists(target));
    }
}
