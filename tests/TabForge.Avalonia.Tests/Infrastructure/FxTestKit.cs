using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: what the FX chain window tests share: an engine that records its calls, a host with no song, a host on a real song document, windows whose
/// questions and pickers are answered by the test, and a scratch folder for chain files and presets.
/// Does not own: the window or the host (the production classes under test).
/// Tests: the Fx* test classes that call it.
/// </summary>
internal static class FxTestKit
{
    /// <summary>An engine that runs (or not), has a chain for every track and records every call as text.</summary>
    internal sealed class FakeEngine : IFxChainEngine
    {
        public List<string> Calls { get; } = new();
        public bool Running { get; set; } = true;
        public bool HasChain { get; set; } = true;
        public bool Opens { get; set; } = true;
        public double Load { get; set; } = 0.25;
        public bool IsRunning => Running;
        public ActiveOnOutput ActiveOutput { get; set; }
        public event Action? OutputChanged;
        public void RaiseOutputChanged() => OutputChanged?.Invoke();
        public double CpuLoad => Load;
        public AutoPitchMatcher? AutoPitch => null;
        public MixerOptions Mixer { get; } = new();
        public float OutputPeak { get; set; }
        public float PeakOf(TrackModel track) => OutputPeak;
        public int SlotOf(TrackModel track) => Running && HasChain ? 0 : -1;
        public void SetPluginEnabled(TrackModel track, PluginSlot slot) => Calls.Add($"enabled:{slot.Name}:{slot.Enabled}");
        public void SetPluginGain(TrackModel track, PluginSlot slot) => Calls.Add($"gain:{slot.Name}:{slot.OutputDb:0.0}");
        public bool OpenEditor(TrackModel track, PluginSlot slot, IntPtr parent, bool dark, bool docked, bool onTop) { Calls.Add($"open:{slot.Name}:{(docked ? "docked" : "float")}:{onTop}"); return Opens; }
        public void CloseEditor(TrackModel track, PluginSlot slot) => Calls.Add($"close:{slot.Name}");
        public void RequestPrograms(TrackModel track, PluginSlot slot) => Calls.Add($"programs:{slot.Name}");
        public void SetProgram(TrackModel track, PluginSlot slot, int program) => Calls.Add($"program:{slot.Name}:{program}");
        public void SetState(TrackModel track, PluginSlot slot, string state) => Calls.Add($"state:{slot.Name}:{state}");
        public Task CollectStatesAsync(IEnumerable<TrackModel> tracks, int timeoutMs)
        {
            Calls.Add("collect");
            foreach (var slot in tracks.SelectMany(t => t.Rig.Plugins)) slot.State ??= "collected";
            return Task.CompletedTask;
        }
        public Task<LoadedPluginInfo?[]> GetPluginInfoAsync(TrackModel track, IReadOnlyList<PluginSlot> plugins) => Task.FromResult(new LoadedPluginInfo?[plugins.Count]);
        public void SetMidiProcessors(TrackModel track, PluginSlot plugin) => Calls.Add($"midi:{plugin.Name}:{plugin.MidiProcessors.Count}");
        public void WatchMidiLog(TrackModel track, bool watch) => Calls.Add($"watch:{watch}");
        public void RequestParameters(TrackModel track, PluginSlot slot) => Calls.Add($"params:{slot.Name}");
        public void SetParameter(TrackModel track, PluginSlot slot, uint id, double value) => Calls.Add($"param:{slot.Name}:{id}:{value:0.##}");
        public event Action<int, int, IReadOnlyList<TabForge.Audio.Contracts.PluginParameterInfo>>? ParametersReceived;
        public void RaiseParameters(int slot, int index, IReadOnlyList<TabForge.Audio.Contracts.PluginParameterInfo> list) => ParametersReceived?.Invoke(slot, index, list);
        public event Action<int, IReadOnlyList<MidiLogLine>>? MidiLogReceived;
        public void RaiseMidiLog(int slot, params MidiLogLine[] lines) => MidiLogReceived?.Invoke(slot, lines);
        public event Action? ChainLoaded;
        public event Action? EditorClosed;
        public event Action<int, int, int, int>? EditorSized;
        public event Action<int, int, int, IReadOnlyList<string>>? ProgramsReceived;
        public void RaiseChainLoaded() => ChainLoaded?.Invoke();
        public void RaiseEditorClosed() => EditorClosed?.Invoke();
        public void RaiseEditorSized(int slot, int index, int width, int height) => EditorSized?.Invoke(slot, index, width, height);
        public void RaisePrograms(int slot, int index, int current, params string[] names) => ProgramsReceived?.Invoke(slot, index, current, names);
    }

    /// <summary>A track with the given plug-ins on its chain.</summary>
    public static TrackModel Track(IEnumerable<PluginSlot>? plugins = null, string name = "Sample track")
    {
        var track = new TrackModel { Name = name };
        if (plugins is not null) track.Rig.Plugins = plugins.ToList();
        return track;
    }

    /// <summary>A host with no song around one track: edits apply at once and are counted.</summary>
    public static StandaloneFxChainHost Host(TrackModel track, IFxChainEngine? engine = null, PluginSettings? plugins = null, Action<TrackModel>? changed = null, Action? saved = null) =>
        new(plugins ?? new PluginSettings(), new HotkeySettings(), new TabForge.Plugins.PluginTrust(), engine, new[] { track }, changed, saved);

    /// <summary>A window on a track whose questions are answered by <paramref name="ask"/> (default: Yes), files by <paramref name="files"/>.</summary>
    public static FxChainWindow Window(TrackModel? track = null, IFxChainHost? host = null, IFileDialogs? files = null, Func<string, MessageBoxAnswer>? ask = null)
    {
        track ??= Track(FxChainWindow.SampleChain());
        var window = new FxChainWindow(host ?? Host(track), track);
        window.Dialogs.Ask = (_, text, _, _, _) => Task.FromResult((ask ?? (_ => MessageBoxAnswer.Yes))(text));
        window.Dialogs.SavePresetName = (_, request) => Task.FromResult<string?>(request.InitialName);
        window.Dialogs.ChoosePlugin = (_, _) => Task.FromResult<VstPluginInfo?>(null);
        window.Dialogs.AskLoadPreset = (_, _, _, _, preferred) => Task.FromResult<FxPresetLoadAnswer?>(new FxPresetLoadAnswer(preferred, Remember: false));
        if (files is not null) window.Dialogs.Files = files;
        return window;
    }

    /// <summary>A song document with a blank project and one track holding the sample chain, plus a host that edits it as undoable steps.</summary>
    public static (SongDocument Document, SongFxChainHost Host, TrackModel Track) Song(IFxChainEngine? engine = null)
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var track = document.Project.Tracks[0];
        track.Rig.Plugins = FxChainWindow.SampleChain();
        document.MarkClean();
        return (document, new SongFxChainHost(document, null, null, () => null, null, new TabForge.Plugins.PluginTrust()), track);
    }

    /// <summary>The chain list of the window.</summary>
    public static FxChainList List(Window window) => DialogTestKit.Named<FxChainList>(window, "ChainList");

    /// <summary>A scratch folder under the temp folder that the plug-in library uses as its root while the scope lives.</summary>
    public static IDisposable ScratchLibrary()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-fx-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        PluginLibrary.RootOverride = folder;
        return new Scratch(folder);
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _folder;
        public Scratch(string folder) => _folder = folder;
        public void Dispose()
        {
            PluginLibrary.RootOverride = null;
            try { System.IO.Directory.Delete(_folder, true); }
            catch (System.IO.IOException) { /* Not logged: a test scratch folder another process still holds is left in the temp folder. */ }
        }
    }

    /// <summary>A file dialog that answers every open and save with one path, as if the user picked it.</summary>
    internal sealed class PickFile : IFileDialogs
    {
        public PickFile(string chosen) => Chosen = chosen;
        public string Chosen { get; set; }
        public string? OpenFile(FileDialogOptions options, object? owner) => Chosen;
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => new[] { Chosen };
        public string? SaveFile(FileDialogOptions options, object? owner) => Chosen;
        public string? PickFolder(FileDialogOptions options, object? owner) => null;
        public string[]? PickFolders(FileDialogOptions options, object? owner) => null;
    }

    private const double CheckBoxPressX = 8;

    /// <summary>Presses and releases the left mouse button on the middle of a control, through the window's hit test (as a user click).</summary>
    public static void Press(Window window, Control control, global::Avalonia.Input.RawInputModifiers modifiers = global::Avalonia.Input.RawInputModifiers.None)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // A box inside the Options popup is not in the window while the popup is closed: its click is the tick it makes.
        if (control is CheckBox inPopup && control.GetVisualRoot() is null)
        {
            inPopup.IsChecked = !inPopup.IsChecked;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return;
        }
        // A check box is pressed on its box (the middle of a short one can fall between the box and its text).
        var x = control is CheckBox ? CheckBoxPressX : control.Bounds.Width / 2;
        var middle = control.TranslatePoint(new global::Avalonia.Point(x, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not in the window.");
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, middle, global::Avalonia.Input.MouseButton.Left, modifiers);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, middle, global::Avalonia.Input.MouseButton.Left, modifiers);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Presses the named button of the window with a real click.</summary>
    public static void PressButton(Window window, string name) => Press(window, DialogTestKit.Named<Button>(window, name));

    /// <summary>The menu item with the header, found anywhere in the window (the first match).</summary>
    public static MenuItem Item(Window window, string header) =>
        global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<MenuItem>().First(m => m.Header as string == header);

    /// <summary>A double-click on the row of the plug-in (the chain list reports it).</summary>
    public static void RaiseActivated(Window window, int row)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var chrome = List(window).RowAt(row);
        var middle = chrome.TranslatePoint(new Point(chrome.Bounds.Width / 2, chrome.Bounds.Height / 2), window)!.Value;
        for (var click = 0; click < 2; click++)
        {
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, middle, global::Avalonia.Input.MouseButton.Left);
            global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, middle, global::Avalonia.Input.MouseButton.Left);
        }
    }

    /// <summary>Every menu item with the header (the Chains menu repeats Save).</summary>
    public static IEnumerable<MenuItem> Items(Window window, string header) =>
        global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<MenuItem>().Where(m => m.Header as string == header);

    /// <summary>Runs a menu item as a click does.</summary>
    public static void Choose(Window window, string header) => Item(window, header).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
}
