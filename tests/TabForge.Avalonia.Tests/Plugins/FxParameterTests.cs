using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the generic CLAP parameter panel: the search and paging rules, the value text with units, the undo and redo of changes, the wire form of the list, and the panel in the
/// FX chain window (it shows for a CLAP plug-in without an editor, and by the Parameters button; its sliders reach the engine; Undo puts the value back).
/// Does not own: the engine or the plug-in side of the parameters (ClapHostTests, ClapProtocolTests).
/// Tests: this class.
/// </summary>
public class FxParameterTests
{
    private static PluginParameterInfo P(uint id, string name, double value = 0.5, string module = "", string text = "", bool stepped = false, bool readOnly = false, double min = 0, double max = 1) =>
        new(id, name, module, min, max, 0, value, stepped, readOnly, text);

    [Fact]
    public void SearchMatchesEveryWordInNameOrModule()
    {
        var model = new FxParameterModel();
        model.Load(new[] { P(1, "Cutoff", module: "Filter"), P(2, "Resonance", module: "Filter"), P(3, "Cutoff", module: "Delay") });
        model.Search = "cut filter";
        Assert.Equal(new uint[] { 1 }, model.Matches().Select(p => p.Id).ToArray());
        model.Search = "";
        Assert.Equal(3, model.Matches().Count);
    }

    [Fact]
    public void RowsShowAPageAtATimeAndMoreOnRequest()
    {
        var model = new FxParameterModel();
        model.Load(Enumerable.Range(0, FxParameterModel.PageSize * 2 + 5).Select(i => P((uint)i, "P" + i)).ToList());
        Assert.Equal(FxParameterModel.PageSize, model.Visible().Count);
        Assert.Equal(FxParameterModel.PageSize + 5, model.Hidden);
        model.ShowMore();
        model.ShowMore();
        Assert.Equal(0, model.Hidden);
    }

    [Theory]
    [InlineData("-6.0 dB", "dB")]
    [InlineData("440 Hz", "Hz")]
    [InlineData("0.50", "")]
    [InlineData("", "")]
    [InlineData("On", "")]
    public void UnitsAreWhatFollowsTheNumber(string text, string units) => Assert.Equal(units, FxParameterModel.Units(text));

    [Fact]
    public void ValueTextKeepsTheUnitsWhileTheSliderMoves()
    {
        var p = P(1, "Gain", 0.5, text: "-6.0 dB", min: -12, max: 12);
        Assert.Equal("-6.0 dB", FxParameterModel.ValueText(p, 0.5));
        Assert.Equal("3.5 dB", FxParameterModel.ValueText(p, 3.5));
        Assert.Equal("2", FxParameterModel.ValueText(P(2, "Mode", 0, stepped: true, max: 4), 1.6));
    }

    [Fact]
    public void ChangesUndoAndRedoAndANewChangeDropsTheRedo()
    {
        var model = new FxParameterModel();
        model.Load(new[] { P(1, "A", 0.2) });
        Assert.True(model.Commit(1, 0.2, 0.7));
        Assert.False(model.Commit(1, 0.7, 0.7));
        Assert.Equal((1u, 0.2), model.Undo());
        Assert.True(model.CanRedo);
        Assert.Equal((1u, 0.7), model.Redo());
        model.Undo();
        model.Commit(1, 0.2, 0.9);
        Assert.False(model.CanRedo);
        Assert.Null(model.Redo());
    }

    [Fact]
    public void APollDoesNotMoveTheValueBeingDragged()
    {
        var model = new FxParameterModel();
        model.Load(new[] { P(1, "A", 0.2), P(2, "B", 0.2) });
        model.Hold(1, 0.9);
        Assert.False(model.Load(new[] { P(1, "A", 0.3), P(2, "B", 0.4) }, held: 1));
        Assert.Equal(0.9, model.ValueOf(model.All[0]));
        Assert.Equal(0.4, model.ValueOf(model.All[1]));
    }

    [Fact]
    public void AListWithOtherParametersStartsTheHistoryAgain()
    {
        var model = new FxParameterModel();
        model.Load(new[] { P(1, "A") });
        model.Commit(1, 0.1, 0.5);
        Assert.True(model.Load(new[] { P(7, "Z") }));
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void TheWireFormCarriesTheValueText()
    {
        var list = new[] { P(5, "Gain", 0.25, module: "Out", text: "-3.0 dB"), P(6, "Mode", 2, stepped: true, readOnly: true, max: 4) };
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) PluginParameterWire.Write(w, list);
        stream.Position = 0;
        using var r = new BinaryReader(stream);
        Assert.Equal(list, PluginParameterWire.Read(r));
    }

    [Fact]
    public void TheParameterMessagesRoundTripAndKeepTheirWireValues()
    {
        Assert.Equal(48, (int)EngineCommand.GetParams);   // golden: the wire values
        Assert.Equal(49, (int)EngineCommand.SetParam);
        Assert.Equal(29, (int)EngineEvent.Params);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            new GetParamsMessage(3, 1).Write(w);
            new SetParamMessage(3, 1, 4000000000u, -0.25).Write(w);
        }
        stream.Position = 0;
        using var r = new BinaryReader(stream);
        Assert.Equal(new GetParamsMessage(3, 1), GetParamsMessage.Read(r));
        Assert.Equal(new SetParamMessage(3, 1, 4000000000u, -0.25), SetParamMessage.Read(r));
    }

    private static PluginSlot Clap() => new() { Name = "Sample clap", Type = PluginSlotType.Effect, Format = PluginFormats.Clap, Vendor = "Sample" };

    private static (FxChainWindow Window, FxTestKit.FakeEngine Engine, PluginSlot Slot) Open(bool opens)
    {
        var slot = Clap();
        var track = FxTestKit.Track(new[] { slot });
        track.SoundSource = SoundSources.Plugins;
        var engine = new FxTestKit.FakeEngine { Opens = opens };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        DialogTestKit.Named<FxEditorArea>(window, "EditorArea").Surface.SetAreaForTest((IntPtr)1234);
        DialogTestKit.RunQueuedUiWork();
        FxTestKit.List(window).Click(0);
        DialogTestKit.RunQueuedUiWork();
        return (window, engine, slot);
    }

    [AvaloniaFact]
    public void AClapPluginWithoutAnEditorShowsTheParameterPanel()
    {
        var (window, engine, _) = Open(opens: false);
        Assert.True(window.ParameterPanel.IsVisible);
        Assert.Contains("params:Sample clap", engine.Calls);
        engine.RaiseParameters(0, 0, new[] { P(1, "Cutoff", 0.5, text: "1000 Hz"), P(2, "Mix") });
        Assert.Equal(2, window.ParameterPanel.RowCount);
        Assert.Equal("1000 Hz", window.ParameterPanel.ValueTextOf(1));
        window.Close();
    }

    [AvaloniaFact]
    public void ASliderReachesTheEngineAndUndoPutsTheValueBack()
    {
        var (window, engine, _) = Open(opens: false);
        engine.RaiseParameters(0, 0, new[] { P(1, "Cutoff", 0.5, text: "1000 Hz") });
        var panel = window.ParameterPanel;
        panel.SliderOf(1)!.Value = 0.8;
        Assert.Contains("param:Sample clap:1:0.8", engine.Calls);
        Assert.Equal("0.8 Hz", panel.ValueTextOf(1));
        Assert.True(panel.CanUndo);
        panel.UndoLast();
        Assert.Contains("param:Sample clap:1:0.5", engine.Calls);
        Assert.Equal(0.5, panel.SliderOf(1)!.Value);
        Assert.True(panel.CanRedo);
        window.Close();
    }

    [AvaloniaFact]
    public void ASearchNarrowsTheRows()
    {
        var (window, engine, _) = Open(opens: false);
        engine.RaiseParameters(0, 0, new[] { P(1, "Cutoff"), P(2, "Resonance"), P(3, "Mix") });
        window.ParameterPanel.SearchText = "res";
        DialogTestKit.RunQueuedUiWork();   // the text box says it changed on the next turn
        Assert.Equal(1, window.ParameterPanel.RowCount);
        Assert.Equal("1 of 3", window.ParameterPanel.CountText);
        window.Close();
    }

    [AvaloniaFact]
    public void TheParametersButtonReplacesTheEditorOfAClapPlugin()
    {
        var (window, engine, _) = Open(opens: true);
        Assert.False(window.ParameterPanel.IsVisible);
        engine.Calls.Clear();
        FxTestKit.PressButton(window, "ParametersButton");
        Assert.True(window.ParameterPanel.IsVisible);
        Assert.Contains("close:Sample clap", engine.Calls);
        Assert.Contains("params:Sample clap", engine.Calls);
        FxTestKit.PressButton(window, "ParametersButton");
        Assert.False(window.ParameterPanel.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePanelAndItsButtonAreForClapPluginsOnly()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine { Opens = false }));
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        FxTestKit.List(window).Click(1);
        DialogTestKit.RunQueuedUiWork();
        Assert.False(DialogTestKit.Named<global::Avalonia.Controls.Button>(window, "ParametersButton").IsVisible);
        Assert.False(window.ParameterPanel.IsVisible);
        window.Close();
    }

    private static void PressKey(FxParameterPanel panel, Key key, KeyModifiers modifiers) =>
        panel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    [AvaloniaFact]
    public void UndoAndRedoKeysFollowTheHotkeyCatalogueAndARebind()
    {
        var model = new FxParameterModel();
        model.Load(new[] { P(1, "A", 0.2) });
        model.Commit(1, 0.2, 0.7);
        var applied = new List<(uint Id, double Value)>();
        var panel = new FxParameterPanel(model, (id, value) => applied.Add((id, value)));
        var keys = new HotkeySettings();
        HotkeyTip.SetSource(panel, () => keys);
        keys.Set("Edit.Undo", 1, "Ctrl+U");
        PressKey(panel, Key.Z, KeyModifiers.Control);
        Assert.Empty(applied);                       // the old default no longer undoes
        PressKey(panel, Key.U, KeyModifiers.Control);
        Assert.Equal(new[] { (1u, 0.2) }, applied);
        keys.Set("Edit.Redo", 1, "Ctrl+Shift+R");
        PressKey(panel, Key.R, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal((1u, 0.7), applied[^1]);
    }
}
