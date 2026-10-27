using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the plug-in list selection and commands: Ctrl/Shift selection and the arrow keys, Delete removing every selected plug-in as one undo step, Cut, Copy, Paste and
/// Duplicate keeping the settings of each plug-in (also into the window of another track), the right-click menu, and that the keys only act while the list has the focus.
/// Does not own: the list gestures in detail (FxChainListTests), the clipboard text (ChainClip) or the window (production classes).
/// Tests: this class.
/// </summary>
public class FxChainClipboardTests
{
    private static void Key(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) => window.KeyPress(key, modifiers, PhysicalKey.None, null);

    private static (SongDocument Document, FxChainWindow Window, TrackModel Track, FxChainList List) Open()
    {
        var (document, host, track) = FxTestKit.Song();
        track.Rig.Plugins[0].State = "piano-state";
        track.Rig.Plugins[1].State = "reverb-state";
        var window = new FxChainWindow(host, track);
        window.Show();
        var list = FxTestKit.List(window);
        list.Focus();
        return (document, window, track, list);
    }

    private static string[] Names(TrackModel track) => track.Rig.Plugins.Select(p => p.Name).ToArray();

    [AvaloniaFact]
    public void CtrlShiftAndTheArrowKeysSelectSeveralRows()
    {
        var (_, window, _, list) = Open();
        list.Click(0);
        list.Click(2, shift: true);
        Assert.Equal(new[] { 0, 1, 2 }, list.SelectedRows);
        list.Click(1, ctrl: true);
        Assert.Equal(new[] { 0, 2 }, list.SelectedRows);
        Assert.Equal(2, list.FocusedIndex);   // the focus stays on a selected row when the row clicked last leaves the selection
        list.Click(0);
        Key(window, global::Avalonia.Input.Key.Down, RawInputModifiers.Shift);
        Assert.Equal(new[] { 0, 1 }, list.SelectedRows);
        Key(window, global::Avalonia.Input.Key.Down);
        Assert.Equal(new[] { 2 }, list.SelectedRows);
        Key(window, global::Avalonia.Input.Key.A, RawInputModifiers.Control);
        Assert.Equal(3, list.SelectedRows.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteRemovesEverySelectedPluginAsOneUndoStep()
    {
        var (document, window, track, list) = Open();
        list.Click(0);
        list.Click(2, ctrl: true);
        Key(window, global::Avalonia.Input.Key.Delete);
        Assert.Equal(new[] { "Sample reverb" }, Names(track));
        DocumentEdits.Undo(document);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, Names(document.Project.Tracks[0]));
        Assert.False(document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void TheListKeysDoNothingWhileAnotherControlHasTheFocus()
    {
        var (_, window, track, list) = Open();
        list.Click(1);
        var add = DialogTestKit.Named<Button>(window, "AddButton");
        add.Focus();
        Key(window, global::Avalonia.Input.Key.Delete);
        Key(window, global::Avalonia.Input.Key.D, RawInputModifiers.Control);
        Assert.Equal(3, track.Rig.Plugins.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void CopyAndPasteKeepTheSettingsAndLandAfterTheSelection()
    {
        var (document, window, track, list) = Open();
        var original = track.Rig.Plugins[0];
        list.Click(0);
        Key(window, global::Avalonia.Input.Key.C, RawInputModifiers.Control);
        list.Click(2);
        Key(window, global::Avalonia.Input.Key.V, RawInputModifiers.Control);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay", "Sample piano" }, Names(track));
        var copy = track.Rig.Plugins[3];
        Assert.Equal("piano-state", copy.State);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.NotSame(original.MidiProcessors, copy.MidiProcessors);
        Assert.Equal("transpose", copy.MidiProcessors[0].Type);
        Assert.Same(copy, list.Focused);
        DocumentEdits.Undo(document);
        Assert.Equal(3, document.Project.Tracks[0].Rig.Plugins.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void CutRemovesThePluginsAndPasteWorksInTheWindowOfAnotherTrack()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var first = document.Project.Tracks[0];
        first.Rig.Plugins = FxChainWindow.SampleChain();
        first.Rig.Plugins[1].State = "reverb-state";
        var second = new TrackModel { Name = "Other" };
        second.Rig.Plugins = FxChainWindow.SampleChain().Take(1).ToList();
        document.Project.Tracks.Add(second);
        document.MarkClean();
        var owner = new Window();
        owner.Show();
        var windows = new FxChainWindows(owner, () => document, null, null, new PluginTrust());
        var a = windows.Open(first);
        var b = windows.Open(second);
        var listA = FxTestKit.List(a);
        listA.Focus();
        listA.Click(1);
        Key(a, global::Avalonia.Input.Key.X, RawInputModifiers.Control);
        Assert.Equal(new[] { "Sample piano", "Sample delay" }, Names(first));
        var listB = FxTestKit.List(b);
        listB.Focus();
        Key(b, global::Avalonia.Input.Key.V, RawInputModifiers.Control);
        Assert.Equal(new[] { "Sample piano", "Sample reverb" }, Names(second));
        Assert.Equal("reverb-state", second.Rig.Plugins[1].State);
        Assert.Equal(SoundSources.Plugins, second.SoundSource);
        a.Close();
        b.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void DuplicateReadsTheStatesFirstAndCopiesGoRightAfterTheSelection()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var list = FxTestKit.List(window);
        list.Focus();
        list.Click(0);
        list.Click(1, shift: true);
        Key(window, global::Avalonia.Input.Key.D, RawInputModifiers.Control);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample piano", "Sample reverb", "Sample delay" }, Names(track));
        Assert.Contains("collect", engine.Calls);
        Assert.Equal("collected", track.Rig.Plugins[2].State);
        Assert.Equal(5, track.Rig.Plugins.Select(p => p.Id).Distinct().Count());
        Assert.Equal(new[] { 2, 3 }, list.SelectedRows);
        window.Close();
    }

    [AvaloniaFact]
    public void PastingStopsAtThePluginLimit()
    {
        var (_, window, track, list) = Open();
        track.Rig.Plugins.AddRange(Enumerable.Range(0, InputLimits.MaxPluginsPerTrack - 3).Select(i => new PluginSlot { Name = "Filler " + i }));
        window.RefreshFromTrack();
        list.Click(0);
        Key(window, global::Avalonia.Input.Key.D, RawInputModifiers.Control);
        Assert.Equal(InputLimits.MaxPluginsPerTrack, track.Rig.Plugins.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickMenuHasTheClipboardCommandsWithTheLiveKeys()
    {
        var (_, window, _, list) = Open();
        list.Click(1);
        var menu = list.ContextMenu!;
        list.RaiseEvent(new ContextRequestedEventArgs());
        var items = menu.Items.OfType<MenuItem>().ToList();
        var headers = items.Select(i => i.Header as string).ToList();
        Assert.Equal(new[] { "Cut", "Copy", "Paste", "Duplicate", "Delete", "Select all" }, headers.Take(6));
        Assert.False(items.First(i => i.Header as string == "Paste").IsEnabled);   // nothing copied yet
        Assert.Equal("Ctrl+C", items.First(i => i.Header as string == "Copy").InputGesture?.ToString());
        Assert.Equal("Delete", items.First(i => i.Header as string == "Delete").InputGesture?.ToString());
        items.First(i => i.Header as string == "Copy").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        list.RaiseEvent(new ContextRequestedEventArgs());
        Assert.True(menu.Items.OfType<MenuItem>().First(i => i.Header as string == "Paste").IsEnabled);
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void DuplicatingAContainerCopiesItWithItsPluginsAndANewId()
    {
        var (_, window, track, list) = Open();
        list.Click(0);
        list.Click(1, shift: true);
        Key(window, global::Avalonia.Input.Key.G, RawInputModifiers.Control);
        Assert.IsType<ChainContainer>(list.Focused);
        var original = track.Rig.Containers![0];
        Key(window, global::Avalonia.Input.Key.D, RawInputModifiers.Control);
        Assert.Equal(2, track.Rig.Containers!.Count);
        Assert.NotEqual(original.Id, track.Rig.Containers[1].Id);
        Assert.All(track.Rig.Containers, c => Assert.Equal(2, c.Count));
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample piano", "Sample reverb", "Sample delay" }, Names(track));
        window.Close();
    }

    [AvaloniaFact]
    public void ARightClickOutsideTheSelectionSelectsThatRowAlone()
    {
        var (_, window, _, list) = Open();
        list.Click(0);
        var chrome = list.RowAt(2);
        var middle = chrome.TranslatePoint(new global::Avalonia.Point(chrome.Bounds.Width / 2, chrome.Bounds.Height / 2), window)!.Value;
        window.MouseDown(middle, MouseButton.Right);
        window.MouseUp(middle, MouseButton.Right);
        Assert.Equal(new[] { 2 }, list.SelectedRows);
        window.Close();
    }
}
