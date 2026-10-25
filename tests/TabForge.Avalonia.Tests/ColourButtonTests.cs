using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks of the shared colour button (swatch and name, the popup under it, picking, Default, the keyboard), of the colour naming rule,
/// and that no colour setting site still builds the old swatch plus preset dropdown.
/// Does not own: the button, the popup or the naming rule.
/// Tests: this class.
/// </summary>
public class ColourButtonTests
{
    private const string Blue = "#4C9AFF", Teal = "#14B8A6", Odd = "#123456";

    private static (Window Owner, ColourButton Button) Open(string defaultHex = Blue)
    {
        var button = new ColourButton(defaultHex, new List<string>());
        var owner = new Window { Content = button };
        owner.Show();
        return (owner, button);
    }

    private static Popup PopupOf(ColourButton button) => button.ActivePicker!.Popup!;

    private static Button InPopup(ColourButton button, string text) =>
        button.ActivePicker!.Panel.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text);

    [Fact]
    public void NamesFollowTheDefaultThePresetsAndCustom()
    {
        Assert.Equal("Default glide blue", ColourNames.NameOf(Blue, Blue));
        Assert.Equal("Default", ColourNames.NameOf(Odd, Odd));
        Assert.Equal("Teal", ColourNames.NameOf(Teal.ToLowerInvariant(), Blue));
        Assert.Equal("Custom", ColourNames.NameOf(Odd, Blue));
    }

    [Fact]
    public void SameColourInAnotherHexFormKeepsItsName()
    {
        Assert.Equal(ColourNames.NameOf("#4C9AFF", Blue), ColourNames.NameOf("#FF4C9AFF", Blue));
        Assert.Equal("Default", ColourNames.NameOf("#FFF", "#FFFFFF"));
        Assert.Equal("Custom", ColourNames.NameOf("#80FFFFFF", "#FFFFFF"));
    }

    [AvaloniaFact]
    public void TheButtonShowsTheSwatchAndTheName()
    {
        var (owner, button) = Open();
        button.Show(Teal);
        Assert.Equal("Teal", button.DisplayName);
        Assert.Contains(button.GetLogicalDescendants().OfType<Border>(), b => b.Background is global::Avalonia.Media.SolidColorBrush s && s.Color == global::Avalonia.Media.Color.Parse(Teal));
        button.Show(Odd);
        Assert.Equal("Custom", button.DisplayName);
        owner.Close();
    }

    [AvaloniaFact]
    public void ClickingOpensThePopupUnderTheButtonAndPickingUpdatesIt()
    {
        var (owner, button) = Open();
        string? picked = null;
        button.Picked += hex => picked = hex;
        Click(button);
        var popup = PopupOf(button);
        Assert.True(popup.IsOpen);
        Assert.Same(button, popup.PlacementTarget);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        var grid = button.ActivePicker!.Panel.GetLogicalDescendants().OfType<UniformGrid>().Single(g => g.Name == TabForge.Avalonia.Views.ColourPickerPopup.PresetsName);
        Click((Button)grid.Children[2]);
        Assert.Null(picked);   // a preview only until the popup answers
        Click(button.ActivePicker!.Panel.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == TabForge.Avalonia.Views.ColourPickerPopup.CloseName));
        RunQueuedUiWork();
        var expected = new TabForge.Presenters.ColourPickerPresenter(Blue, Blue, new List<string>()).Presets[2].Hex;
        Assert.Equal(expected, picked, ignoreCase: true);
        Assert.Equal(TabForge.Platform.Theme.ColourNames.NameOf(expected, Blue), button.DisplayName);
        owner.Close();
    }

    private static void Hover(TopLevel owner, global::Avalonia.Controls.Control target) =>
        owner.MouseMove(target.TranslatePoint(new global::Avalonia.Point(target.Bounds.Width / 2, target.Bounds.Height / 2), owner)!.Value, RawInputModifiers.None);

    private static global::Avalonia.Media.IBrush? PaintOf(Control control) =>
        control.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Bd").Background;

    [AvaloniaFact]
    public void HoveringTheButtonPaintsTheHoverShade()
    {
        var (owner, button) = Open();
        Hover(owner, button);
        var hover = TabForge.Avalonia.Platform.HoverShade.Brush(button);
        Assert.Equal(((global::Avalonia.Media.ISolidColorBrush)hover).Color, ((global::Avalonia.Media.ISolidColorBrush)PaintOf(button)!).Color);
        owner.Close();
    }

    [AvaloniaFact]
    public void HoveringASwatchKeepsItsColourAndDrawsARing()
    {
        var (owner, button) = Open();
        Click(button);
        var cell = (Button)button.ActivePicker!.Panel.GetLogicalDescendants().OfType<UniformGrid>().Single(g => g.Name == TabForge.Avalonia.Views.ColourPickerPopup.PresetsName).Children[2];
        var fill = ((global::Avalonia.Media.ISolidColorBrush)cell.Background!).Color;
        var window = TopLevel.GetTopLevel(cell)!;
        RunQueuedUiWork();
        var rest = cell.BorderBrush;
        Hover(window, cell);
        Assert.True(cell.IsPointerOver);
        Assert.Equal(fill, ((global::Avalonia.Media.ISolidColorBrush)PaintOf(cell)!).Color);
        Assert.NotEqual(((global::Avalonia.Media.ISolidColorBrush)rest!).Color, ((global::Avalonia.Media.ISolidColorBrush)cell.BorderBrush!).Color);
        owner.Close();
    }

    [AvaloniaFact]
    public void ManyPreviewsThenXRaiseOnePickedAndCancelRaisesNone()
    {
        var (owner, button) = Open();
        var picks = new List<string>();
        button.Picked += picks.Add;
        Click(button);
        var grid = button.ActivePicker!.Panel.GetLogicalDescendants().OfType<UniformGrid>().Single(g => g.Name == TabForge.Avalonia.Views.ColourPickerPopup.PresetsName);
        button.ActivePicker.ShowMore();
        button.ActivePicker.Panel.GetLogicalDescendants().OfType<TabForge.Avalonia.Views.HueStrip>().Single().Pick(0.5, 0.3);
        for (var i = 0; i < 3; i++) Click((Button)grid.Children[i]);
        Click(button.ActivePicker.Panel.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == TabForge.Avalonia.Views.ColourPickerPopup.CloseName));
        RunQueuedUiWork();
        Assert.Single(picks);

        picks.Clear();
        var kept = button.Value;
        Click(button);
        Click((Button)button.ActivePicker!.Panel.GetLogicalDescendants().OfType<UniformGrid>().Single(g => g.Name == TabForge.Avalonia.Views.ColourPickerPopup.PresetsName).Children[5]);
        Click(InPopup(button, "Cancel"));
        RunQueuedUiWork();
        Assert.Empty(picks);
        Assert.Equal(kept, button.Value);
        owner.Close();
    }

    [AvaloniaFact]
    public void ClickingOutsideAfterAnInvalidCodeAddsNothingToRecent()
    {
        var recent = new List<string>();
        var button = new ColourButton(Blue, recent);
        var owner = new Window { Content = button };
        owner.Show();
        Click(button);
        button.ActivePicker!.ShowMore();
        button.ActivePicker.Panel.GetLogicalDescendants().OfType<TextBox>().Single().Text = "zz";
        PopupOf(button).IsOpen = false;
        RunQueuedUiWork();
        Assert.Empty(recent);
        owner.Close();
    }

    [AvaloniaFact]
    public void ClickingOutsideKeepsTheColourThatWasPicked()
    {
        var (owner, button) = Open();
        string? picked = null;
        button.Picked += hex => picked = hex;
        Click(button);
        var grid = button.ActivePicker!.Panel.GetLogicalDescendants().OfType<UniformGrid>().Single(g => g.Name == TabForge.Avalonia.Views.ColourPickerPopup.PresetsName);
        Click((Button)grid.Children[4]);
        PopupOf(button).IsOpen = false;
        RunQueuedUiWork();
        var expected = new TabForge.Presenters.ColourPickerPresenter(Blue, Blue, new List<string>()).Presets[4].Hex;
        Assert.Equal(expected, picked, ignoreCase: true);
        Assert.Equal(expected, button.Value, ignoreCase: true);
        owner.Close();
    }

    [AvaloniaFact]
    public void DefaultRestoresTheDefaultColour()
    {
        var (owner, button) = Open();
        button.Show(Teal);
        string? picked = null;
        button.Picked += hex => picked = hex;
        Click(button);
        Click(InPopup(button, "Default"));
        RunQueuedUiWork();
        Assert.Equal(Blue, picked, ignoreCase: true);
        Assert.Equal("Default glide blue", button.DisplayName);
        owner.Close();
    }

    [AvaloniaFact]
    public void EnterAndSpaceOpenThePopup()
    {
        foreach (var key in new[] { PhysicalKey.Enter, PhysicalKey.Space })
        {
            var (owner, button) = Open();
            Assert.True(button.Focusable);
            button.Focus();
            owner.KeyPressQwerty(key, RawInputModifiers.None);
            owner.KeyReleaseQwerty(key, RawInputModifiers.None);
            Assert.True(PopupOf(button).IsOpen, key.ToString());
            owner.Close();
        }
    }

    [Fact]
    public void NoColourSiteStillBuildsTheOldSwatchAndPresetDropdown()
    {
        var views = Path.Combine(TestRepo.CheckoutRoot(), "Source", "src", "TabForge.Avalonia", "Views");
        var offenders = Directory.EnumerateFiles(views, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("NamedColourSwatches.Presets"))
            .Select(Path.GetFileName).ToList();
        Assert.Empty(offenders);
        Assert.Contains("new ColourButton(", File.ReadAllText(Path.Combine(views, "PreferencesWindow.cs")));
        Assert.Contains("new ColourButton(", File.ReadAllText(Path.Combine(views, "ScoreTextStyleWindow.cs")));
    }
}
