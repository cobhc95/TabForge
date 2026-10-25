using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Views;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the FX chain window's own keys: the FX chain scope of the key map (Ctrl+S, Ctrl+O, Ctrl+G, Ctrl+Shift+G, F2), rebinding, the gesture text beside the
/// menu items, text-box safety, and Escape (cancel a drag, close the window unless a text box has the focus).
/// Does not own: the key map (HotkeyCatalog) or the window.
/// Tests: this class.
/// </summary>
public class FxChainKeysTests
{
    private static void Key(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    [AvaloniaFact]
    public void CtrlGGroupsTheSelectedRowsAndCtrlShiftGDissolves()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(1);
        list.Click(2, shift: true);
        Key(window, global::Avalonia.Input.Key.G, RawInputModifiers.Control);
        Assert.Single(track.Rig.Containers!);
        Assert.Equal(2, track.Rig.Containers![0].Count);
        Assert.IsType<ChainContainer>(list.Focused);
        Key(window, global::Avalonia.Input.Key.G, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Null(track.Rig.Containers);
        window.Close();
    }

    [AvaloniaFact]
    public void CtrlSOpensTheSavePickerAndCtrlOTheLoadPicker()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-key-{System.Guid.NewGuid():N}.tfchain");
        try
        {
            var window = FxTestKit.Window(files: new FxTestKit.PickFile(file));
            window.Show();
            Key(window, global::Avalonia.Input.Key.S, RawInputModifiers.Control);
            DialogTestKit.RunQueuedUiWork();
            Assert.True(File.Exists(file));
            FxTestKit.List(window).Click(0);
            Key(window, global::Avalonia.Input.Key.O, RawInputModifiers.Control);
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(3, FxTestKit.List(window).Count);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void F2FocusesTheNameBoxOfTheFocusedContainer()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        Assert.True(window.Run(FxChainIds.NewContainer));
        Assert.IsType<ChainContainer>(FxTestKit.List(window).Focused);
        Key(window, global::Avalonia.Input.Key.F2);
        DialogTestKit.RunQueuedUiWork();
        Assert.True(DialogTestKit.Named<TextBox>(window, "ContainerNameBox").IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void EscapeClosesTheWindowUnlessATextBoxHasTheFocus()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        window.Run(FxChainIds.NewContainer);
        var box = DialogTestKit.Named<TextBox>(window, "ContainerNameBox");
        box.Focus();
        DialogTestKit.RunQueuedUiWork();
        Key(window, global::Avalonia.Input.Key.Escape);
        Assert.True(window.IsVisible);
        FxTestKit.List(window).Focus();
        Key(window, global::Avalonia.Input.Key.Escape);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void EscapeCancelsADragWithoutMovingAnything()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        var list = FxTestKit.List(window);
        list.SimulateDragStart(0);
        list.SimulateDragMove(list.RowCentre(2) + 20);
        Assert.True(list.IsDragging);
        Key(window, global::Avalonia.Input.Key.Escape);
        Assert.False(list.IsDragging);
        Assert.True(window.IsVisible);
        Assert.Equal("Sample piano", track.Rig.Plugins[0].Name);
        window.Close();
    }

    [AvaloniaFact]
    public void MenuItemsShowTheGestureOfTheLiveBindings()
    {
        var window = FxTestKit.Window();
        window.Show();
        FxTestKit.Item(window, "Save FX chain…").Focus();
        var text = TabForge.Avalonia.Platform.AvaloniaMenuTemplates.GestureTextProperty;
        Assert.Equal("Ctrl+S", FxTestKit.Items(window, "Save FX chain…").Select(i => i.GetValue(text)).First(t => t.Length > 0));
        Assert.Equal("Ctrl+O", FxTestKit.Item(window, "Load FX chain…").GetValue(text));
        window.Close();
    }

    [AvaloniaFact]
    public void ARebindingIsUsedAtOnce()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var settings = new PluginSettings();
        var hotkeys = new HotkeySettings();
        hotkeys.Set(FxChainIds.Clear, 0, "Ctrl+K");
        var host = new TabForge.Avalonia.Views.Fx.StandaloneFxChainHost(settings, hotkeys, new TabForge.Plugins.PluginTrust(), null, new[] { track });
        var window = FxTestKit.Window(track, host);
        window.Show();
        Key(window, global::Avalonia.Input.Key.K, RawInputModifiers.Control);
        Assert.Empty(track.Rig.Plugins);
        window.Close();
    }
}
