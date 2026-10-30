using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the lane picker at the left end of the lane strip: a toolbar-size button labelled "Lane: <name>" with a chevron, accent-tinted while a controller
//   lane is chosen, its tooltip and name, the menu it opens (themed through the button, the lanes in order, a tick on the current one, a dot only on lanes
//   with data, "+ Any CC..."), choosing a lane or any CC, the last choice kept, a tab track's live picker (controller rows disabled), and the strip's place.
// Does not own: the look states of every toolbar button (PianoRollHoverTests) or the lane drawing (PianoRollControllerTests).
// Tests: PianoRollView.BuildLaneButton, LaneRows, PianoRollControllers.ButtonName.
public sealed class PianoRollLaneButtonTests
{
    private static (PianoRollView View, Window Window, Button Button) Rig(EditingSettings? settings = null, SongDocument? document = null)
    {
        document ??= PianoRollSongs.Midi();
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Editing = () => settings ?? new EditingSettings() });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PianoRollLaneButton"));
    }

    private static string Label(Button b) => b.GetVisualDescendants().OfType<TextBlock>().First().Text!;

    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void TheLabelFollowsTheEditedLane()
    {
        var (view, window, button) = Rig();
        Assert.Equal("LANE: VELOCITY", Label(button));
        var expected = new[] { (PianoRollControllers.PitchBend, "Lane: Pitch bend"), (PianoRollControllers.Program, "Lane: Program change"),
            (PianoRollControllers.ChannelAftertouch, "Lane: Channel aftertouch"), (7, "Lane: Volume"), (10, "Lane: Pan"), (11, "Lane: Expression"),
            (64, "Lane: Sustain"), (1, "Lane: Mod wheel"), (20, "Lane: CC 20") };
        foreach (var (lane, text) in expected)
        {
            view.ShowLane(lane);
            window.UpdateLayout();
            Assert.Equal(text.ToUpperInvariant(), Label(button));
        }
        window.Close();
    }

    [AvaloniaFact]
    public void TheButtonUsesTheCompactModeCaptionLookAndADrawnChevronAfterTheLabel()
    {
        var (_, window, button) = Rig();
        var content = Assert.IsType<StackPanel>(button.Content);
        var caption = Assert.IsType<TextBlock>(content.Children[0]);
        var chevron = Assert.IsType<global::Avalonia.Controls.Shapes.Path>(content.Children[1]);   // a drawn glyph, after the label
        Assert.Equal(global::TabForge.Avalonia.Platform.Controls.DropdownCaption.ChevronName, chevron.Name);
        Assert.Equal(global::TabForge.Avalonia.Platform.Controls.DropdownCaption.FontSize, caption.FontSize);
        Assert.Equal(FontWeight.SemiBold, caption.FontWeight);
        Assert.True(chevron.Bounds.X >= caption.Bounds.Right);
        window.Close();
    }

    [AvaloniaFact]
    public void TheTabTrackMenuHasNoHeadingAndEveryRowIsEnabled()
    {
        var (view, window, button) = Rig(document: PianoRollSongs.Tab());
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = view.LaneMenu!;
        Assert.All(menu.Items.OfType<MenuItem>(), i => Assert.True(i.IsEnabled));
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), i => i.Header is string h && h.StartsWith("EDIT", StringComparison.Ordinal));
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void ItIsAToolbarSizeButtonWithTheTooltipAndTheLanePickerName()
    {
        var (view, window, button) = Rig();
        Assert.Equal(PianoRollToolbarLayout.ControlHeight, button.Height);
        Assert.Equal(PianoRollSurface.LaneHeaderSize, button.Height);   // the strip is as tall as the button
        Assert.Equal(PianoRollView.LaneTip, ToolTip.GetTip(button));
        Assert.Equal("Lane picker", global::Avalonia.Automation.AutomationProperties.GetName(button));
        Assert.True(button.IsEnabled);
        Assert.True(button.Focusable);   // reachable from the keyboard
        window.Close();
    }

    [AvaloniaFact]
    public void TheListHasTheLanesInOrderATickOnTheCurrentOneAndDotsOnlyOnDataLanes()
    {
        var (view, window, _) = Rig();
        view.ShowLane(10);
        var rows = view.LaneRows();
        Assert.DoesNotContain(rows, r => r.Header == "EDIT LANE");
        var lanes = rows.Where(r => r.Id == "PianoRoll.Lane" && r.Arg != "any").ToList();
        Assert.Equal(PianoRollControllers.Lanes.Count, lanes.Count);
        Assert.Equal(new[] { "Velocity", "Pitch bend", "Mod wheel (CC1)", "Volume (CC7)", "Pan (CC10)", "Expression (CC11)", "Sustain (CC64)", "Program change", "Channel aftertouch" },
            lanes.Select(r => r.Header).ToArray());
        Assert.Equal(new[] { "10" }, lanes.Where(r => r.Checked).Select(r => r.Arg).ToArray());
        Assert.Contains(rows, r => r.Header == "+ Any CC...");
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingARowSwitchesTheLaneAndTheButtonSitsInTheKeyboardColumn()
    {
        var (view, window, button) = Rig();
        view.Run("PianoRoll.Lane", "64");
        window.UpdateLayout();
        Assert.Equal("LANE: SUSTAIN", Label(button));
        Assert.True(button.Bounds.Width >= PianoRollSurface.KeyStripSize);
        Assert.Equal(0, button.TranslatePoint(new Point(), view.Surface)!.Value.X);
        Assert.Equal(view.Surface.GridBottom, button.TranslatePoint(new Point(), view.Surface)!.Value.Y, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingAControllerLaneTintsTheButtonAndVelocityLeavesItPlain()
    {
        var (view, window, button) = Rig();
        var rest = Colour(button.Background);   // velocity: the field look (a filled button with its border)
        Assert.NotEqual(0, rest.A);
        Assert.NotEqual(0, Colour(button.BorderBrush).A);
        view.Run("PianoRoll.Lane", "7");
        window.UpdateLayout();
        Assert.NotEqual(rest, Colour(button.Background));   // the selected tint
        Assert.NotEqual(0, Colour(button.BorderBrush).A);   // and its accent edge
        view.Run("PianoRoll.Lane", "-1");
        window.UpdateLayout();
        Assert.Equal(rest, Colour(button.Background));
        window.Close();
    }

    [AvaloniaFact]
    public void ThePopupIsOpenedFromTheButtonAndTakesItsTheme()
    {
        var (view, window, button) = Rig();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = view.LaneMenu;
        Assert.NotNull(menu);
        Assert.True(menu!.IsOpen);
        Assert.True(((global::Avalonia.LogicalTree.ILogical)menu).IsAttachedToLogicalTree);
        Assert.Same(button, ((global::Avalonia.LogicalTree.ILogical)((global::Avalonia.LogicalTree.ILogical)menu).LogicalParent!).LogicalParent);   // the menu's popup hangs off the button: it takes the button's theme
        Assert.Equal(PianoRollControllers.Lanes.Count + 2, menu.Items.Count);   // the lanes, a separator and the "+ Any CC..." item
        Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, menu.Placement);   // under the button, left-aligned
        Assert.True(window.TryFindResource("Panel2Brush", window.ActualThemeVariant, out var theme));
        var panel = menu.GetVisualDescendants().OfType<Border>().First(b => b.Background is ISolidColorBrush);
        Assert.Equal(((ISolidColorBrush)theme!).Color, Colour(panel.Background));
        Assert.NotEqual(Colors.White, Colour(panel.Background));
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void ChoosingAnyCcAsksForTheNumberAndShowsThatLane()
    {
        var document = PianoRollSongs.Midi();
        var asked = "";
        var view = new PianoRollView(new PianoRollHost
        {
            Document = () => document,
            AskText = (title, label, initial) => { asked = title + "|" + label + "|" + initial; return Task.FromResult<string?>("20"); },
        });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        view.Run("PianoRoll.Lane", "any");
        window.UpdateLayout();
        Assert.StartsWith("Any CC|CC number", asked, StringComparison.Ordinal);
        Assert.Equal("LANE: CC 20", Label(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PianoRollLaneButton")));
        window.Close();
    }

    [AvaloniaFact]
    public void TheLastChoiceIsKeptAcrossViewsAndSongs()
    {
        var settings = new EditingSettings();
        var (view, window, button) = Rig(settings);
        view.Run("PianoRoll.Lane", "64");
        Assert.Equal(64, settings.PianoRollLane);
        window.Close();
        var (_, again, button2) = Rig(settings);
        Assert.Equal("LANE: SUSTAIN", Label(button2));
        again.Close();
    }

    [AvaloniaFact]
    public void ATabTrackShowsTheChosenLaneAndAllRowsAreSelectable()
    {
        var settings = new EditingSettings { PianoRollLane = 7 };
        var (view, window, button) = Rig(settings, PianoRollSongs.Tab());
        Assert.Equal("LANE: VOLUME", Label(button));
        Assert.True(button.IsEnabled);
        Assert.All(view.LaneRows().Where(r => r.Id == "PianoRoll.Lane"), r => Assert.True(r.Enabled));
        window.Close();
    }

    [AvaloniaFact]
    public async Task DrawingInATabTrackLaneCreatesAControllerTakeOnceAndKeepsItsPoints()
    {
        var document = PianoRollSongs.Tab();
        var (view, window, _) = Rig(document: document);
        var track = document.Project.Tracks[0];
        var clips = track.AudioClips.Count;
        Assert.True(await view.Presenter.CommitPointsAsync(7, new[] { new PianoRollPoint(1, 90) }, "Draw controller"));
        Assert.Equal(clips + 1, track.AudioClips.Count);
        Assert.Contains(track.AudioClips.Single(c => c.IsMidi).Controllers!, p => p.Controller == 7 && p.Value == 90);
        Assert.Single(view.Presenter.Points(7));
        Assert.True(await view.Presenter.CommitPointsAsync(7, new[] { new PianoRollPoint(1, 90), new PianoRollPoint(2, 100) }, "Draw controller"));
        Assert.Equal(clips + 1, track.AudioClips.Count);   // the take is made once
        window.Close();
    }

    [AvaloniaFact]
    public void TheButtonStaysWhenTheLaneAreaIsCollapsed()
    {
        var (view, window, button) = Rig();
        view.Surface.LaneShown = false;
        view.Surface.LaneChanged?.Invoke();
        window.UpdateLayout();
        Assert.True(button.IsVisible);
        Assert.Equal(view.Surface.GridBottom, button.TranslatePoint(new Point(), view.Surface)!.Value.Y, 1);
        window.Close();
    }
}
