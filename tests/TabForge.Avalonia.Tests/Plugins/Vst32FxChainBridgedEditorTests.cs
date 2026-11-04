using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: the FX chain window over a real 32-bit helper editor: the Parameters button must close the docked bridged popup
//   (not just stop following it), the panel must take its place, and pressing again/redocking/row switches/closing the
//   window must leave no helper-owned popup behind.
// Does not own: the helper's editor window (native\tfvst32) or the editor state machine (FxEditorController).
[Collection("Vst32Native")]
public sealed class FxChainBridgedEditorTests
{
    [Vst32RealFact]
    public void TheEngineCloseEditorCommandClosesABridgedEditor()
    {
        var path = Fixture("Dexed32");
        using var shared = SharedBlock.Create($"tf-vst32-fx-close-{Guid.NewGuid():N}");
        using var owner = new TestOwner();

        var pids = new List<int>();
        try
        {
            Attach(shared);
            EH.LoadChain(0, "bridged", useMidiSynth: false, new List<PluginSpec> { Vst32TestAssets.Spec(path, instrument: true) });
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            var plugin = Assert.IsType<RemotePlugin>(Assert.Single(chain!.Plugins));
            pids.Add(plugin.HostProcessId);

            Assert.True(EH.Command(EngineCommand.OpenEditor,
                w => new OpenEditorMessage(0, 0, (long)owner.Handle, Dark: true, Docked: true, OnTop: false).Write(w)));
            EH.Collect();
            var popup = Popup(owner.Handle);
            Assert.NotEqual(IntPtr.Zero, popup);
            Assert.True(IsWindowVisible(popup), "the bridged editor must be visible after its open command");

            Assert.True(EH.Command(EngineCommand.CloseEditor, w => new CloseEditorMessage(0, 0).Write(w)));
            EH.Collect();

            Assert.Equal(IntPtr.Zero, Popup(owner.Handle));
        }
        finally
        {
            EH.Detach();
            AssertHelpersGone(pids);
        }
    }

    [AvaloniaFact]
    public void ParametersReplacesTheBridgedEditorAndBringsItBack()
    {
        // AvaloniaFactAttribute is sealed and cannot carry a conditional Skip: without the helper/fixtures there is nothing to drive.
        if (!OperatingSystem.IsWindows() || Vst32TestAssets.HelperPath is null || !System.IO.Directory.Exists(Vst32TestAssets.FixturesFolder)) return;
        var path = Fixture("Dexed32");
        using var shared = SharedBlock.Create($"tf-vst32-fx-params-{Guid.NewGuid():N}");
        using var owner = new TestOwner();

        var pids = new List<int>();
        FxChainWindow? window = null;
        try
        {
            Attach(shared);
            var specs = new List<PluginSpec>
            {
                Vst32TestAssets.Spec(path, instrument: true) with { Id = "dex-a", Name = "Dexed A" },
                Vst32TestAssets.Spec(path, instrument: true) with { Id = "dex-b", Name = "Dexed B" },
            };
            EH.LoadChain(0, "Bridged chain", useMidiSynth: false, specs);
            var chain = EH.ChainAt(0);
            Assert.NotNull(chain);
            Assert.Equal(2, chain!.Plugins.Count);
            pids.AddRange(chain.Plugins.OfType<RemotePlugin>().Select(p => p.HostProcessId));

            var slots = new[]
            {
                new PluginSlot { Id = "dex-a", Name = "Dexed A", Path = path, Format = PluginFormats.Bridged32, Type = PluginSlotType.Instrument, Vendor = "Digital Suburban" },
                new PluginSlot { Id = "dex-b", Name = "Dexed B", Path = path, Format = PluginFormats.Bridged32, Type = PluginSlotType.Instrument, Vendor = "Digital Suburban" },
            };
            var track = FxTestKit.Track(slots);
            track.SoundSource = SoundSources.Plugins;
            var settings = new PluginSettings();
            var engine = new RealBridgeEngine(track);
            var host = FxTestKit.Host(track, engine, settings);
            host.Trust.Approve(settings, path, host.PluginFiles);   // the fixture is outside Program Files
            window = FxTestKit.Window(track, host);
            window.Show();
            var area = DialogTestKit.Named<FxEditorArea>(window, "EditorArea");
            area.Surface.SetAreaForTest(owner.Handle);
            Pump();
            FxTestKit.List(window).Click(0);
            Pump();

            var popup = WaitForPopup(owner.Handle);
            Assert.NotEqual(IntPtr.Zero, popup);
            Assert.True(IsWindowVisible(popup), "the bridged editor must be docked before the Parameters button is pressed");
            Assert.False(window.ParameterPanel.IsVisible);
            var docked = WindowSize(popup);
            engine.RaiseEditorSized(0, docked.Width, docked.Height);
            Pump();
            Assert.True(area.EditorVisible);

            FxTestKit.PressButton(window, "ParametersButton");
            Pump();
            Assert.True(window.ParameterPanel.IsVisible, "the Parameters button must show the generic panel");
            Assert.False(area.EditorVisible, "the editor area must make way for the panel");
            Assert.True(window.ParameterPanel.RowCount > 0, "the panel must list the bridged plug-in's parameters");
            Assert.True(WaitPopupGone(owner.Handle, 3000), "the bridged editor popup must be closed, not just unfollowed, under the panel");

            // Another row while the panel is up must not leave the first editor behind.
            FxTestKit.List(window).Click(1);
            Pump();
            Assert.True(window.ParameterPanel.IsVisible);
            Assert.True(WaitPopupGone(owner.Handle, 3000), "switching rows under the panel must leave no editor popup behind");
            FxTestKit.List(window).Click(0);
            Pump();
            Assert.True(window.ParameterPanel.IsVisible);
            Assert.True(WaitPopupGone(owner.Handle, 3000));

            FxTestKit.PressButton(window, "ParametersButton");
            Pump();
            Assert.False(window.ParameterPanel.IsVisible);
            Assert.True(area.EditorVisible);
            popup = WaitForPopup(owner.Handle);
            Assert.NotEqual(IntPtr.Zero, popup);
            Assert.True(IsWindowVisible(popup), "pressing Parameters again must dock the bridged editor again");
            var redocked = WindowSize(popup);
            Assert.InRange(Math.Abs(redocked.Width - docked.Width), 0, 2);
            Assert.InRange(Math.Abs(redocked.Height - docked.Height), 0, 2);
            engine.RaiseEditorSized(0, redocked.Width, redocked.Height);
            Pump();
            Assert.True(area.FrameSize.Width > 0 && area.FrameSize.Height > 0, "the area must follow the editor size when it docks again");

            window.Close();
            window = null;
            Pump();
            Assert.True(WaitPopupGone(owner.Handle, 4000), "closing the FX window must leave no editor popup behind");
        }
        finally
        {
            try { window?.Close(); } catch (Exception) { /* the assertion already says why */ }
            EH.Detach();
            AssertHelpersGone(pids);
        }
    }

    internal static void Attach(SharedBlock shared)
    {
        EH.AttachReal(shared);
        EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
    }

    internal static string Fixture(string name)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(System.IO.File.Exists(path), $"missing fixture: {path}");
        return path;
    }

    internal static void Pump()
    {
        for (var pass = 0; pass < 20; pass++) DialogTestKit.RunQueuedUiWork();
    }

    private static IntPtr Popup(IntPtr owner) => PluginEditorWin32.FindEditorPopup(owner, IntPtr.Zero);

    private static IntPtr WaitForPopup(IntPtr owner)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var popup = Popup(owner);
            if (popup != IntPtr.Zero) return popup;
            Thread.Sleep(10);
        }
        return IntPtr.Zero;
    }

    private static bool WaitPopupGone(IntPtr owner, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            var popup = Popup(owner);
            if (popup == IntPtr.Zero || !IsWindowVisible(popup)) return true;
            DialogTestKit.RunQueuedUiWork();
            Thread.Sleep(10);
        }
        var last = Popup(owner);
        return last == IntPtr.Zero || !IsWindowVisible(last);
    }

    private static (int Width, int Height) WindowSize(IntPtr window)
    {
        Assert.True(GetWindowRect(window, out var rect), "the editor popup must have a window rect");
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    internal static void AssertHelpersGone(IReadOnlyCollection<int> pids)
    {
        // Only the helpers this test started: another job may run its own test helpers at the same time.
        foreach (var pid in pids) Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), $"helper {pid} left behind");
    }

    /// <summary>The window's engine, forwarding to the real in-process engine harness: the same command frames, session and helper route the app uses.</summary>
    internal sealed class RealBridgeEngine : IFxChainEngine
    {
        private readonly TrackModel _track;

        public RealBridgeEngine(TrackModel track) => _track = track;

        public bool IsRunning => EH.Attached;
        public double CpuLoad => 0;
        public MixerOptions Mixer { get; } = new();
        public AutoPitchMatcher? AutoPitch => null;
        public event Action? OutputChanged { add { } remove { } }
        public event Action? ChainLoaded { add { } remove { } }
        public event Action? EditorClosed { add { } remove { } }
        public event Action<int, int, int, int>? EditorSized;
        public event Action<int, int, int, IReadOnlyList<string>>? ProgramsReceived { add { } remove { } }
        public event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived { add { } remove { } }
        public event Action<int, int, IReadOnlyList<PluginParameterInfo>>? ParametersReceived;

        public int SlotOf(TrackModel track) => EH.Attached ? 0 : -1;
        public void SetPluginEnabled(TrackModel track, PluginSlot slot) { }
        public void SetPluginGain(TrackModel track, PluginSlot slot) { }

        public bool OpenEditor(TrackModel track, PluginSlot slot, IntPtr parent, bool dark, bool docked, bool onTop)
        {
            var index = IndexOf(slot);
            if (index < 0 || !EH.Command(EngineCommand.OpenEditor,
                    w => new OpenEditorMessage(0, index, (long)parent, dark, docked, onTop).Write(w))) return false;
            EH.Collect();
            // The client turns the engine's "no editor" answer (EditorSize 0 x 0) into this event; the harness has no client, so the same fact is raised here.
            if (docked && EH.ChainAt(0)?.Plugins.ElementAtOrDefault(index) is RemotePlugin { HasEditor: false }) EditorSized?.Invoke(0, index, 0, 0);
            return true;
        }

        public void CloseEditor(TrackModel track, PluginSlot slot)
        {
            var index = IndexOf(slot);
            if (index < 0) return;
            if (EH.Command(EngineCommand.CloseEditor, w => new CloseEditorMessage(0, index).Write(w))) EH.Collect();
        }

        public void RequestPrograms(TrackModel track, PluginSlot slot) { }
        public void SetProgram(TrackModel track, PluginSlot slot, int program) { }
        public void SetState(TrackModel track, PluginSlot slot, string state) { }

        public void RequestParameters(TrackModel track, PluginSlot slot)
        {
            var index = IndexOf(slot);
            if (index < 0 || EH.ChainAt(0)?.Plugins.ElementAtOrDefault(index) is not IPluginParameters parameters) return;
            IReadOnlyList<PluginParameterInfo> list;
            try { list = parameters.GetParameters(); } catch (Exception) { return; }
            ParametersReceived?.Invoke(0, index, list);
        }

        public void SetParameter(TrackModel track, PluginSlot slot, uint id, double value) { }
        public Task CollectStatesAsync(IEnumerable<TrackModel> tracks, int timeoutMs) => Task.CompletedTask;
        public Task<LoadedPluginInfo?[]> GetPluginInfoAsync(TrackModel track, IReadOnlyList<PluginSlot> plugins) => Task.FromResult(new LoadedPluginInfo?[plugins.Count]);
        public void SetMidiProcessors(TrackModel track, PluginSlot plugin) { }
        public void WatchMidiLog(TrackModel track, bool watch) { }

        public void RaiseEditorSized(int index, int width, int height) => EditorSized?.Invoke(0, index, width, height);

        private int IndexOf(PluginSlot slot) => _track.Rig.Plugins.IndexOf(slot);
    }

    /// <summary>A real Win32 owner window on its own pumping thread, as the app's UI thread is for the helper's editor popup.</summary>
    internal sealed class TestOwner : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private uint _threadId;
        private IntPtr _handle;

        public TestOwner()
        {
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                _handle = CreateWindowExW(0, "STATIC", "TabForge bridged editor test owner", 0x00CF0000, 0, 0, 480, 320,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _ready.Set();
                while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }
            }) { IsBackground = true, Name = "TabForge bridged editor test owner" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(5000);
        }

        public IntPtr Handle => _handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) PostMessageW(_handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            if (_threadId != 0) PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(3000);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
    private const int WmClose = 0x0010, WmQuit = 0x0012;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessageW(out Msg message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg message);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WinRect rect);
}
