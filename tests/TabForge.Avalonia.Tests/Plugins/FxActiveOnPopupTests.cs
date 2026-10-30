using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the redrawn "Active on" popup (its model and controls) and the headphones button in the plug-in toolbar and the container header.
/// Does not own: the rule matcher or the rows (FxActiveOnTests).
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]
public class FxActiveOnPopupTests
{
    private static readonly FxActiveOnDevice[] Known = { new("Focusrite USB", true), new("Speakers", true) };

    private static FxActiveOnPopupModel Model(ActiveOnRule? rule, List<ActiveOnRule?> applied, ActiveOnOutput output = default) =>
        new(rule, "IR Loader", _ => Known, () => output, applied.Add);

    [Fact]
    public void ModelListsAnyThenConnectedOutputsAndDefaultsToTheRule()
    {
        var model = Model(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio, DeviceName = "Focusrite USB" }, new());
        Assert.Equal(ActiveOnPresenter.DriverAny, model.DriverChoices[0].Label);
        Assert.Equal("Any (ASIO or WASAPI)", model.DriverChoices[0].Label);
        Assert.Equal("Any output device", model.DeviceChoices[0].Label);
        Assert.Equal(new[] { "Any output device", "Focusrite USB", "Speakers" }, model.DeviceChoices.Select(c => c.Label));
        Assert.Equal(ActiveOnDriverKind.Asio, model.DriverChoices[model.DriverIndex].Kind);
        Assert.Equal("Focusrite USB", model.DeviceChoices[model.DeviceIndex].Name);
        Assert.Equal("IR Loader plays only when both match. Leave a choice on Any to ignore it.", model.Subtitle);
    }

    [Fact]
    public void ModelMarksADeviceOfTheRuleThatIsNotConnected()
    {
        var model = Model(new ActiveOnRule { DeviceName = "Old Interface" }, new());
        var row = model.DeviceChoices.Single(c => c.Name == "Old Interface");
        Assert.Equal("Old Interface (not connected)", row.Label);
        Assert.False(row.Connected);
        Assert.Equal("Old Interface", model.DeviceChoices[model.DeviceIndex].Name);
    }

    [Fact]
    public void ModelStatusFollowsTheCurrentOutput()
    {
        var model = Model(null, new(), new ActiveOnOutput("WASAPI (shared)", "Speakers"));
        Assert.Equal("Right now: active (matches)", model.Status);
        model.DriverKind = ActiveOnDriverKind.Asio;
        Assert.Equal("Right now: inactive on this device", model.Status);
        model.DriverKind = ActiveOnDriverKind.Wasapi;
        model.DeviceName = "Speakers";
        Assert.Equal("Right now: active (matches)", model.Status);
        model.DeviceName = "Focusrite USB";
        Assert.Equal("Right now: inactive on this device", model.Status);
    }

    [Fact]
    public void ModelDoneAppliesOnceClearAppliesNothingAndCancelChangesNothing()
    {
        var applied = new List<ActiveOnRule?>();
        var done = Model(null, applied);
        done.DriverKind = ActiveOnDriverKind.Wasapi;
        done.Done();
        done.Done();
        Assert.Single(applied);
        Assert.Equal(ActiveOnDriverKind.Wasapi, applied[0]!.DriverKind);

        var cleared = Model(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio }, applied);
        cleared.Clear();
        Assert.Equal(2, applied.Count);
        Assert.Null(applied[1]);

        var cancelled = Model(null, applied);
        cancelled.DeviceName = "Speakers";
        cancelled.Cancel();
        cancelled.Done();
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void ButtonTooltipsNameTheRule()
    {
        Assert.Equal("Active on: any driver, any device. Click to set a rule.", ActiveOnPresenter.ButtonToolTip(null));
        Assert.Equal("Active on: ASIO only, Focusrite USB. Click to change.",
            ActiveOnPresenter.ButtonToolTip(new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio, DeviceName = "Focusrite USB" }));
        Assert.Equal("Select a plug-in or container", ActiveOnPresenter.ButtonToolTip(null, hasTarget: false));
    }

    [AvaloniaFact]
    public void PopupHasTwoDropDownsWithTheExpectedItemsAndThemeBrushes()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        var popup = new FxActiveOnPopup(FxTestKit.List(window), null, "IR Loader", _ => Known, () => default, null, _ => { });
        popup.Open();
        Assert.Equal(new[] { "Any (ASIO or WASAPI)", "ASIO only", "WASAPI only" }, popup.DriverBox.Items.Cast<string>());
        Assert.Equal(new[] { "Any output device", "Focusrite USB", "Speakers" }, popup.DeviceBox.Items.Cast<string>());
        Assert.Equal(2, popup.Panel.GetLogicalDescendants().OfType<ComboBox>().Count());
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(popup.Panel.Background);
        Assert.NotEqual(Colors.White, background.Color);
        Assert.True(window.TryFindResource("Panel2Brush", window.ActualThemeVariant, out var theme));
        Assert.Equal(((ISolidColorBrush)theme!).Color, background.Color);
        popup.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void ButtonSitsRightAfterWiringAndShowsTheRuleOfTheSelectedPlugin()
    {
        var (_, host, track) = FxTestKit.Song();
        var window = new FxChainWindow(host, track);
        window.Show();
        var list = FxTestKit.List(window);
        var button = window.ActiveOnButton;
        var wiring = DialogTestKit.Named<Button>(window, "WiringButton");
        var midi = DialogTestKit.Named<Button>(window, "MidiButton");

        var bar = Assert.IsAssignableFrom<Panel>(button.Parent);
        Assert.Same(bar, wiring.Parent);
        Assert.Equal(bar.Children.IndexOf(wiring) + 1, bar.Children.IndexOf(button));
        Assert.Equal(bar.Children.IndexOf(button) + 1, bar.Children.IndexOf(midi));
        Assert.Equal("Active on", global::Avalonia.Automation.AutomationProperties.GetName(button));

        list.Click(0);
        Assert.True(button.IsEnabled);
        Assert.Equal(ActiveOnPresenter.ButtonTipNone, ToolTip.GetTip(button));
        Assert.False(DialogTestKit.Named<global::Avalonia.Controls.Shapes.Path>(window, "ActiveOnButtonDot").IsVisible);

        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var popup = Assert.IsType<FxActiveOnPopup>(window.ActiveOnPopup);
        popup.ChooseDriver(ActiveOnDriverKind.Asio);
        FxActiveOnTests.Done(popup);
        Assert.Equal(ActiveOnDriverKind.Asio, track.Rig.Plugins[0].ActiveOn?.DriverKind);
        Assert.True(DialogTestKit.Named<global::Avalonia.Controls.Shapes.Path>(window, "ActiveOnButtonDot").IsVisible);
        Assert.Equal("Active on: ASIO only. Click to change.", ToolTip.GetTip(button));

        track.Rig.Plugins[0].ActiveOn = null;   // what an undo restores; the window reads the track again
        window.RefreshFromTrack();
        Assert.False(DialogTestKit.Named<global::Avalonia.Controls.Shapes.Path>(window, "ActiveOnButtonDot").IsVisible);
        Assert.Equal(ActiveOnPresenter.ButtonTipNone, ToolTip.GetTip(button));
        window.Close();
    }

    [AvaloniaFact]
    public void ButtonIsDimmedWithNothingSelected()
    {
        var track = FxTestKit.Track(new List<PluginSlot>());
        var window = FxTestKit.Window(track);
        window.Show();
        Assert.False(window.ActiveOnButton.IsEnabled);
        Assert.Equal(ActiveOnPresenter.ButtonTipDisabled, ToolTip.GetTip(window.ActiveOnButton));
        window.Close();
    }

    [AvaloniaFact]
    public void ContainerHeaderButtonFollowsTheContainerRuleAfterTheCountText()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(0);
        list.Click(1, shift: true);
        Assert.True(window.Run(FxChainIds.GroupSelected));
        list.Click(0);
        var button = DialogTestKit.Named<Button>(window, "ContainerActiveOnButton");
        var count = DialogTestKit.Named<TextBlock>(window, "ContainerCountText");
        var line = Assert.IsAssignableFrom<Panel>(button.Parent);
        Assert.Same(line, count.Parent);
        Assert.Equal(line.Children.IndexOf(count) + 1, line.Children.IndexOf(button));
        Assert.True(button.IsEnabled);
        Assert.Equal(ActiveOnPresenter.ButtonTipNone, ToolTip.GetTip(button));

        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var popup = Assert.IsType<FxActiveOnPopup>(window.ActiveOnPopup);
        Assert.Contains("plays only when both match", popup.Model.Subtitle);
        popup.Model.DeviceName = "Speakers";
        popup.DoneButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Speakers", track.Rig.Containers![0].ActiveOn?.DeviceName);
        Assert.Equal("Active on: Speakers. Click to change.", ToolTip.GetTip(button));
        window.Close();
    }
}
