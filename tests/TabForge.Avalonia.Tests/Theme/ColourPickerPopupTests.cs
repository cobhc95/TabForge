using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks of the colour picker popup (anchoring, the ready-made and recent colours, More colours, the square, hue, opacity and code,
/// every way of closing, the recent list) and of its presenter, and that no caller still opens the old dialog window.
/// Does not own: the popup or the presenter; expected texts come from the presenter.
/// Tests: this class.
/// </summary>
public class ColourPickerPopupTests
{
    private const string Blue = "#4C9AFF", White = "#FFFFFF";

    private static (Window Owner, Button Anchor) Open()
    {
        var anchor = new Button { Content = "swatch" };
        var owner = new Window { Content = anchor };
        owner.Show();
        return (owner, anchor);
    }

    private static T Part<T>(ColourPickerPopup picker, string name) where T : Control =>
        picker.Panel.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static Button Named(ColourPickerPopup picker, string text) =>
        picker.Panel.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);

    private static void Press(ColourPickerPopup picker, Key key) =>
        picker.Panel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = picker.Panel });

    [AvaloniaFact]
    public async Task OpensAnchoredUnderTheSwatchAndCancelClosesIt()
    {
        var (owner, anchor) = Open();
        var picker = new ColourPickerPopup(Blue, White, new List<string>());
        var pending = picker.AskAsync(anchor);

        Assert.True(picker.Popup!.IsOpen);
        Assert.Same(anchor, picker.Popup.PlacementTarget);
        Assert.Equal(PlacementMode.Bottom, picker.Popup.Placement);
        Assert.Equal(DialogMetrics.ColourPopupWidth, picker.Panel.Width);
        Click(Named(picker, "Cancel"));
        Assert.Null(await pending);
        Assert.False(picker.Popup.IsOpen);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task AnAnchorRemovedWithThePopupOpenAnswersNullAndTheNextPickerStillOpens()
    {
        var (owner, anchor) = Open();
        var picker = new ColourPickerPopup(Blue, White, new List<string>());
        var pending = picker.AskAsync(anchor);
        owner.Content = null;   // the anchor leaves the tree with the popup open: it never raises Closed
        Assert.Null(await pending.WaitAsync(System.TimeSpan.FromSeconds(5)));
        owner.Content = anchor;
        var next = new ColourPickerPopup(Blue, White, new List<string>());
        _ = next.AskAsync(anchor);
        Assert.True(next.Popup!.IsOpen);
        Click(Named(next, "Cancel"));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task SixteenReadyMadeColoursInRowsOfEightAndAClickSelectsOne()
    {
        var (owner, anchor) = Open();
        var picker = new ColourPickerPopup("#808080", White, new List<string>());
        var pending = picker.AskAsync(anchor);
        var grid = Part<UniformGrid>(picker, ColourPickerPopup.PresetsName);

        Assert.Equal(16, grid.Children.Count);
        Assert.Equal(8, grid.Columns);
        var third = (Button)grid.Children[2];
        Click(third);
        Assert.Equal(DialogMetrics.SelectedOutline, third.BorderThickness);
        Assert.Equal(DialogMetrics.SwatchOutline, ((Button)grid.Children[0]).BorderThickness);
Click(Part<Button>(picker, ColourPickerPopup.CloseName));
        Assert.Equal(new ColourPickerPresenter("#808080", White, new List<string>()).Presets[2].Hex, await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public void MoreColoursExpandsTheSamePopup()
    {
        var (owner, anchor) = Open();
        var picker = new ColourPickerPopup(Blue, White, new List<string>());
        _ = picker.AskAsync(anchor);
        var more = Part<StackPanel>(picker, ColourPickerPopup.MoreName);
        var link = Part<Button>(picker, ColourPickerPopup.MoreLinkName);

        Assert.False(more.IsVisible);
        Click(link);
        Assert.True(more.IsVisible);
        Assert.False(link.IsVisible);
        Assert.Same(anchor, picker.Popup!.PlacementTarget);
        picker.Popup.IsOpen = false;
        owner.Close();
    }

    [AvaloniaFact]
    public async Task SquareHueOpacityAndCodeMakeTheirColours()
    {
        var (owner, anchor) = Open();
        var picker = new ColourPickerPopup("#FF4C9AFF", "#FFFFFFFF", new List<string>());
        var pending = picker.AskAsync(anchor);
        picker.ShowMore();
        Part<HueStrip>(picker, ColourPickerPopup.HueName).Pick(0.5, 0);
        Part<ColourSquare>(picker, ColourPickerPopup.SquareName).Pick(1, 0);
        Assert.Equal("#FFFF0000", Part<TextBox>(picker, ColourPickerPopup.CodeName).Text);
        Part<Slider>(picker, ColourPickerPopup.OpacityName).Value = 50;
        Assert.Equal("#80FF0000", Part<TextBox>(picker, ColourPickerPopup.CodeName).Text);
        Part<TextBox>(picker, ColourPickerPopup.CodeName).Text = "#00FF00";
Click(Part<Button>(picker, ColourPickerPopup.CloseName));

        Assert.Equal("#00FF00", await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task AnInvalidCodeShowsAPlainMessageAndKeepsThePopupOpen()
    {
        var (owner, anchor) = Open();
        var picker = new ColourPickerPopup(Blue, White, new List<string>());
        var pending = picker.AskAsync(anchor);
        picker.ShowMore();
        var error = Part<TextBlock>(picker, ColourPickerPopup.ErrorName);
        Assert.False(error.IsVisible);
        Part<TextBox>(picker, ColourPickerPopup.CodeName).Text = "zz";
        Assert.True(error.IsVisible);
        Assert.Equal("Type a code like #RRGGBB.", error.Text);

        Assert.False(pending.IsCompleted);
        Part<TextBox>(picker, ColourPickerPopup.CodeName).Text = "#14B8A6";
        Assert.False(error.IsVisible);
        Press(picker, Key.Enter);
        Assert.Equal("#14B8A6", await pending);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(Blue, White, false)]
    [InlineData("#804C9AFF", "#FFFFFFFF", true)]
    [InlineData(Blue, "#FFFFFFFF", true)]
    public void OpacityIsOnlyOfferedWhereTheColourCanBeSeeThrough(string initial, string defaultValue, bool offered)
    {
        var picker = new ColourPickerPopup(initial, defaultValue, new List<string>());
        var row = Part<Slider>(picker, ColourPickerPopup.OpacityName).Parent as Control;
        Assert.Equal(offered, row!.IsVisible);
        Assert.Equal(offered, new ColourPickerPresenter(initial, defaultValue, new List<string>()).SupportsOpacity);
        var words = picker.Panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");
        Assert.DoesNotContain(words, text => text.Contains("alpha", System.StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task DefaultCancelEscEnterXAndAClickOutsideEachAnswerTheirResult()
    {
        var (owner, anchor) = Open();
        async Task<string?> Run(System.Action<ColourPickerPopup> close)
        {
            var picker = new ColourPickerPopup(Blue, White, new List<string>());
            var pending = picker.AskAsync(anchor);
            close(picker);
            return await pending;
        }

        Assert.Equal(White, await Run(p => Click(Named(p, "Default"))));
        Assert.Null(await Run(p => Click(Named(p, "Cancel"))));
        Assert.Equal(Blue, await Run(p => Click(Part<Button>(p, ColourPickerPopup.CloseName))));
        Assert.Null(await Run(p => Press(p, Key.Escape)));
        Assert.Equal(Blue, await Run(p => Press(p, Key.Enter)));
        Assert.Equal(Blue, await Run(p => p.Popup!.IsOpen = false));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task AClickAppliesAtOnceCancelRestoresTheOpeningColourAndDoubleClickClosesKeepingIt()
    {
        var (owner, anchor) = Open();
        var seen = new List<string>();
        var picker = new ColourPickerPresenter(Blue, White, new List<string>());
        var cells = new ColourPickerPopup(Blue, White, new List<string>());
        cells.Applied += seen.Add;
        var pending = cells.AskAsync(anchor);
        var grid = Part<UniformGrid>(cells, ColourPickerPopup.PresetsName);
        Click((Button)grid.Children[2]);
        Assert.Equal(new[] { picker.Presets[2].Hex }, seen, StringComparer.OrdinalIgnoreCase);
        Assert.False(pending.IsCompleted);
        Click(Named(cells, "Cancel"));
        Assert.Equal(Blue, seen[^1]);
        Assert.Null(await pending);

        var second = new ColourPickerPopup(Blue, White, new List<string>());
        var answer = second.AskAsync(anchor);
        var cell = (Button)Part<UniformGrid>(second, ColourPickerPopup.PresetsName).Children[3];
        Click(cell);
        cell.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, new PointerEventArgs(InputElement.PointerPressedEvent, cell, new Pointer(0, PointerType.Mouse, true), null, default, 0, new PointerPointProperties(), KeyModifiers.None)));
        Assert.Equal(picker.Presets[3].Hex, await answer, StringComparer.OrdinalIgnoreCase);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task AKeptDifferentColourJoinsTheFrontOfTheRecentListOnce()
    {
        var (owner, anchor) = Open();
        var recent = Enumerable.Range(1, 8).Select(i => $"#0000{i:X2}").ToList();
        var picker = new ColourPickerPopup("#000003", White, recent);
        var pending = picker.AskAsync(anchor);
        Assert.Equal(8, Part<WrapPanel>(picker, ColourPickerPopup.RecentName).Children.Count);
Click((Button)Part<UniformGrid>(picker, ColourPickerPopup.PresetsName).Children[0]);
        Click(Part<Button>(picker, ColourPickerPopup.CloseName));
        var kept = await pending;

        Assert.Equal(9, recent.Count);
        Assert.Equal(kept, recent[0]);
        Assert.Single(recent, text => text == kept);
        var cancelled = new ColourPickerPopup(Blue, White, recent);
        var second = cancelled.AskAsync(anchor);
        Click(Named(cancelled, "Cancel"));
        Assert.Null(await second);
        Assert.Equal(kept, recent[0]);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ASecondPickerOnTheSameSwatchWhileOneIsOpenOpensNoSecondPopup()
    {
        var (owner, anchor) = Open();
        var first = new ColourPickerPopup(Blue, White, new List<string>());
        var pending = first.AskAsync(anchor);
        var second = new ColourPickerPopup(Blue, White, new List<string>());
        var joined = second.AskAsync(anchor);

        Assert.Null(second.Popup);
        Assert.Same(pending, joined);
        Click(Named(first, "Cancel"));
        Assert.Null(await pending);
        Assert.Null(await joined);
        var again = new ColourPickerPopup(Blue, White, new List<string>());
        var reopened = again.AskAsync(anchor);
        Assert.True(again.Popup!.IsOpen);
        Click(Named(again, "Cancel"));
        Assert.Null(await reopened);
        owner.Close();
    }

    [Fact]
    public void ThirtyThreeUsedColoursKeepTheNewestThirtyTwo()
    {
        var recent = new List<string>();
        var presenter = new ColourPickerPresenter(Blue, White, recent);
        for (var i = 0; i < 33; i++) presenter.Remember($"#00{i:X4}");

        Assert.Equal(ColourPickerPresenter.RecentLimit, recent.Count);
        Assert.Equal("#000020", recent[0]);
        Assert.Equal("#000001", recent[^1]);
        Assert.DoesNotContain("#000000", recent);
    }

    [Fact]
    public void ThePresenterTurnsTheSquareHueAndCodeIntoColours()
    {
        var presenter = new ColourPickerPresenter("#FF0000", White, new List<string>());
        Assert.Equal(0, presenter.Hue);
        Assert.False(presenter.SupportsOpacity);
        presenter.Apply(120, 100, 100, 40);
        Assert.Equal("#00FF00", presenter.UseColourText);
        Assert.False(presenter.TypeCode("12"));
        Assert.NotNull(presenter.CodeError);
        Assert.True(presenter.TypeCode("0000FF"));
        Assert.Equal("#0000FF", presenter.CodeText);
        Assert.Null(presenter.CodeError);
        Assert.Equal(16, presenter.Presets.Count);
    }

    [Fact]
    public void NoCallerOpensTheOldDialogAndEveryFormerCallerUsesThePopup()
    {
        var source = Path.Combine(ArchitectureScan.RepoRoot(), "Source", "src");
        var all = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).ToList();
        Assert.DoesNotContain(all, file => File.ReadAllText(file).Contains("ColourPickerWindow"));
        foreach (var caller in new[] { "ColourButton.cs", "PreviewToolsMenu.cs" })
            Assert.Contains("new ColourPickerPopup(", File.ReadAllText(all.Single(file => Path.GetFileName(file) == caller)));
    }
}
