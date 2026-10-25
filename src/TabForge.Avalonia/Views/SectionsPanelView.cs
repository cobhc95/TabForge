using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the Sections dock pane, the twin of the WPF SectionsPanelContent: the sections in bar order, each row filled with its section colour
//       (SectionColourPresenter) in its rest, hover and selected look (SectionListPresenter), a lock glyph on locked sections, row selection,
//       the press, release jump and confirm flash of each row (SectionButtonFeedback), and the Add at cursor, Edit and Remove buttons, which report that they need the full app.
// Does not own: the sections (the markers are read only here), the section editor, the timeline and the dock placement (DockPaneSetup).
//       Not ported yet: the hover glow, keyboard selection (the WPF list is a ListBox) and the Add and Edit dialogs.
// Tests: SectionColourPresenterTests (rules), ParityShotsTests (panel-sections shots).
public sealed class SectionsPanelView : Border
{
    private const string Heading = "Sections";
    private const string AddText = "Add at cursor…";
    private const string EditText = "Edit";
    private const string RemoveText = "Remove";
    private const string AddTip = "Add a section starting at the cursor bar. The dialog shows the exact bar.";
    private const string RemoveTip = "Remove the selected section marker. The bars and notes stay.";
    private const string NeedsFullAppText = "Editing sections isn't available yet.";
    private const string LockGlyph = "🔒";
    private const string FlashKey = "InstrumentAccent";

    // The confirm flash's glow over a row is drawn at this opacity at its peak (the flash strength scales it down as it fades).
    private const double FlashGlowPeakOpacity = 0.55;

    private readonly List<SectionRow> _rows = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly SectionButtonFeedback _feedback;
    private SectionRow? _selected;

    /// <summary>The panel for the given sections; they are shown in bar order, coloured as the timeline colours them.</summary>
    public SectionsPanelView(IEnumerable<MarkerModel> markers)
    {
        Padding = new Thickness(8);
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBrush");
        Focusable = true;
        _feedback = new SectionButtonFeedback(ApplyFeedback, AvaloniaUiThread.ForApplication());
        Rebuild(markers);
        this.FollowTheme(ShowRows);

        var list = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(5, 0, 5, 5) };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 5, 5) };
        buttons.Children.Add(ActionButton(AddText, AddTip));
        buttons.Children.Add(ActionButton(EditText, null));
        buttons.Children.Add(ActionButton(RemoveText, RemoveTip));
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        body.Children.Add(buttons);
        body.Children.Add(_status);
        body.Children.Add(list);

        Child = ToolWindowParts.Group(Heading, body);
    }

    /// <summary>Raised when a row is clicked or Enter is pressed on the highlighted row: the section and whether Ctrl was held (select its whole range).</summary>
    public event Action<MarkerModel, bool>? JumpRequested;

    /// <summary>The sections shown, in bar order.</summary>
    public IReadOnlyList<MarkerModel> Sections => _rows.Select(row => row.Marker).ToList();

    /// <summary>The highlighted (current) section, or null.</summary>
    public MarkerModel? Highlighted => _selected?.Marker;

    /// <summary>Shows <paramref name="markers"/> in bar order, keeping the highlight on the same section when it is still there.</summary>
    public void Rebuild(IEnumerable<MarkerModel> markers)
    {
        var keep = _selected?.Marker;
        var sorted = SectionListPresenter.InBarOrder(markers);
        var colours = SectionDisplayColours.Resolve(sorted);
        _rows.Clear();
        _selected = null;
        for (var i = 0; i < sorted.Count; i++) _rows.Add(BuildRow(sorted[i], colours[i]));
        _list.Children.Clear();
        _list.Children.AddRange(_rows.Select(row => row.View));
        Highlight(keep);
    }

    /// <summary>Measures the row press and flash on <paramref name="clock"/> instead of the real clock (a test drives time by hand).</summary>
    internal void UseFeedbackClock(Func<TimeSpan> clock) => _feedback.UseClock(clock);

    /// <summary>Flashes the row of <paramref name="section"/> after a jump to it (the confirm flash of the section buttons).</summary>
    public void FlashSection(MarkerModel section) => _feedback.Flash(section);

    /// <summary>The confirm flash strength of the row of <paramref name="section"/>: 1 at the jump, fading to 0.</summary>
    public double FlashStrength(MarkerModel section) => _feedback.FlashAlpha(section);

    /// <summary>Highlights the row of <paramref name="marker"/> (null clears it) without jumping.</summary>
    public void Highlight(MarkerModel? marker)
    {
        var row = marker is null ? null : _rows.FirstOrDefault(r => ReferenceEquals(r.Marker, marker));
        if (ReferenceEquals(row, _selected)) return;
        var previous = _selected;
        _selected = row;
        if (previous is not null) Show(previous, SectionRowState.Rest);
        if (row is not null) Show(row, SectionRowState.Selected);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_rows.Count == 0) return;
        var index = _selected is null ? -1 : _rows.IndexOf(_selected);
        if (e.Key is Key.Up or Key.Down)
        {
            var next = _rows[Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, _rows.Count - 1)];
            Highlight(next.Marker);
            next.View.BringIntoView();
        }
        else if (e.Key == Key.Enter && _selected is not null) JumpRequested?.Invoke(_selected.Marker, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        else return;
        e.Handled = true;
    }

    /// <summary>The section a row shows, its display colour, its row element, the flash glow over it and the scale its press draws it at.</summary>
    private sealed record SectionRow(MarkerModel Marker, Rgba Colour, Border View, Border Glow, ScaleTransform Scale, TextBlock Title, TextBlock Lock);

    private SectionRow BuildRow(MarkerModel marker, Rgba colour)
    {
        var title = new TextBlock { Text = marker.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var lockGlyph = new TextBlock { Text = LockGlyph, IsVisible = marker.LockPosition, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(lockGlyph, Dock.Right);
        line.Children.Add(lockGlyph);
        line.Children.Add(title);
        var glow = new Border { Background = new SolidColorBrush(AvaloniaPalette.ColorOf(Palette.Get(FlashKey))), CornerRadius = new CornerRadius(2), Opacity = 0, IsHitTestVisible = false };
        var scale = new ScaleTransform(1, 1);
        var view = new Border
        {
            Padding = new Thickness(7, 4), Margin = new Thickness(0, 1), CornerRadius = new CornerRadius(2),
            Child = new Grid { Children = { line, glow } },
            RenderTransform = scale, RenderTransformOrigin = RelativePoint.Center,
        };
        var row = new SectionRow(marker, colour, view, glow, scale, title, lockGlyph);
        Show(row, SectionRowState.Rest);
        view.PointerEntered += (_, _) =>
        {
            _feedback.SetHovered(row.Marker);
            Show(row, ReferenceEquals(row, _selected) ? SectionRowState.Selected : SectionRowState.Hover);
        };
        view.PointerExited += (_, _) =>
        {
            _feedback.SetHovered(null);
            _feedback.Release();   // a press that leaves the row is cancelled: no jump
            Show(row, StateOf(row));
        };
        view.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(view).Properties.IsLeftButtonPressed) return;
            Focus();
            Select(row);
            _feedback.Press(row.Marker);
        };
        // The jump lands on the release over the row that was pressed, as a click does; Ctrl held then selects the section's whole range.
        view.PointerReleased += (_, e) =>
        {
            if (_feedback.Release() is { } held && ReferenceEquals(held, row.Marker))
                JumpRequested?.Invoke(row.Marker, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        };
        return row;
    }

    // Applies each row's press depth (a render transform) and flash strength (an opacity) on every feedback frame; no layout runs.
    private void ApplyFeedback()
    {
        foreach (var row in _rows)
        {
            row.Scale.ScaleX = row.Scale.ScaleY = 1 - _feedback.PressDepth(row.Marker) * (1 - SectionButtonFeedback.PressedScale);
            row.Glow.Opacity = _feedback.FlashAlpha(row.Marker) * FlashGlowPeakOpacity;
        }
    }

    private void Select(SectionRow row)
    {
        var previous = _selected;
        _selected = row;
        if (previous is not null) Show(previous, SectionRowState.Rest);
        Show(row, SectionRowState.Selected);
    }

    private SectionRowState StateOf(SectionRow row) => ReferenceEquals(row, _selected) ? SectionRowState.Selected : SectionRowState.Rest;

    private void Show(SectionRow row, SectionRowState state)
    {
        // The resting dimming is on the fill only, so the name keeps its full ink.
        row.View.Background = new SolidColorBrush(AvaloniaPalette.ColorOf(SectionListPresenter.RowFill(row.Colour, state))) { Opacity = SectionListPresenter.RowOpacity(state) };
        // The name takes the ink that reads on the row as it really shows: its fill (at the row's opacity) over the panel, in the theme in use.
        var fill = SectionListPresenter.RowFill(row.Colour, state);
        var panel = AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames["PanelBrush"], ActualThemeVariant == ThemeVariant.Light);
        var alpha = fill.A / 255.0 * SectionListPresenter.RowOpacity(state);
        // Pure black or white, whichever reads better (at least 4.58:1 on any fill): the theme inks are a step softer and fall to 4.5:1 on a mid red.
        var shown = ThemeColours.Blend(panel, fill.ToUi(), alpha);
        var pureWhite = new TabForge.Platform.UiColor(0xFF, 0xFF, 0xFF, 0xFF);
        var pureBlack = new TabForge.Platform.UiColor(0xFF, 0, 0, 0);
        var ink = new SolidColorBrush(AvaloniaPalette.ColorOf(pureWhite));   // always white, as the timeline chips
        row.Title.Foreground = ink;
        row.Lock.Foreground = ink;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowRows();
    }

    private void ShowRows()
    {
        foreach (var row in _rows) Show(row, StateOf(row));
    }

    private Button ActionButton(string text, string? tip)
    {
        var button = new Button { Content = text, Padding = Dialogs.DialogMetrics.SectionActionPadding, Margin = Dialogs.DialogMetrics.SectionActionGap };
        if (tip is not null) ToolTip.SetTip(button, tip);
        button.Click += (_, _) => ShowStatus(NeedsFullAppText);
        ToolWindowParts.NotAvailable(button);
        return button;
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.IsVisible = true;
    }
}
