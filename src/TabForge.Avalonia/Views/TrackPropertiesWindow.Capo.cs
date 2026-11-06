using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Presenters;
using PartialCapoModel = TabForge.Models.PartialCapo;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Capo card: Full/Partial placement buttons, the selected capo's fret and string range, all working capos on the shared board, and two removal buttons.
/// Clicking places or selects; dragging previews only and writes the working fret once on release.
/// The capos are working copies of the track's <see cref="TrackModel.Capo"/> and <see cref="TrackModel.PartialCapos"/>: nothing reaches the song until OK
/// (TrackSetup.SetCapoAndPartials, one undo step); Cancel restores the snapshot.
/// Owns: placement mode, working copies, removal and selection.
/// Does not own: the capo rule (TrackSetup), the range and texts (TrackPropertiesPresenter), the board (TrackPropertiesWindowCapoBoard) or the undo step (the window's OK).
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesRedesignTests, TrackPropertiesWindowTests, TrackPropertiesJobBTests, PartialCapoTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    private const int MaxPartialCapos = PartialCapoRules.MaxCount;
    private const int FullCapo = -1;
    private int _capoFret;
    private int _capoSelected = FullCapo;
    private bool _placePartialMode;
    private Border _capoCard = null!;
    private readonly List<PartialCapo> _partials = new();
    // The working-slot origin each partial's flags were last aligned to (TrackTuningDraft.Origin): a string added or
    // removed moves the flags with its strings, and a removed string's flag is dropped.
    private List<int> _partialSlotsOrigin = new();
    private WrapPanel _capoChips = null!;
    private TrackPropertiesWindowCapoBoard _capoBoard = null!;
    private Button _capoRemove = null!;
    private ToggleButton _placeFull = null!, _placePartial = null!;
    private NumericUpDown _capoFretBox = null!;
    private WrapPanel _capoStrings = null!;
    private ComboBox _capoFrom = null!, _capoTo = null!;
    private (int From, int To)? _nextPartialStrings;
    private bool _syncingCapo;

    private Border CapoCard()
    {
        _capoChips = new WrapPanel { Name = "CapoChips", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _capoBoard = new TrackPropertiesWindowCapoBoard { Name = "CapoBoard" };
        DialogMetrics.WithMargin(_capoBoard, DialogMetrics.TrackCapoStripGap);
        _capoBoard.CommitRequested += fret => { SetCapoFret(fret); return true; };
        _capoBoard.SelectionRequested += selected => { _placePartialMode = selected >= 0; SelectCapo(selected); };
        _capoBoard.PlacementRequested += PlaceCapo;
        _placeFull = PlacementButton("Full", false);
        _placePartial = PlacementButton("Partial", true);
        _capoChips.Children.Add(_placeFull);
        _capoChips.Children.Add(_placePartial);
        _capoFretBox = new NumericUpDown { Name = "CapoFretBox", Minimum = 0, Maximum = TrackPropertiesPresenter.MaxCapo,
            Increment = 1, FormatString = "0", Width = DialogMetrics.TrackCapoFretBoxWidth, ShowButtonSpinner = false,
            Height = DialogMetrics.TrackCapoFieldHeight, MinHeight = DialogMetrics.TrackCapoFieldHeight, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_capoFretBox, "Capo fret");
        _capoFretBox.ValueChanged += (_, _) =>
        {
            if (!_syncingCapo && _capoFretBox.Value is { } value && (int)value != (SelectedPartial?.Fret ?? _capoFret)) SetCapoFret((int)value);
        };
        _capoFrom = StringBox("CapoStringFrom", "Capo from string", true);
        _capoTo = StringBox("CapoStringTo", "Capo to string", false);
        _capoStrings = new WrapPanel { Name = "CapoStrings", VerticalAlignment = VerticalAlignment.Center, Children = { FieldLabel("From"), _capoFrom, FieldLabel("To"), _capoTo } };
        ToolTip.SetTip(_placePartial, $"Place partial capos (maximum {MaxPartialCapos})");
        _capoRemove = new Button { Name = "RemoveSelectedCapoButton", Content = "Remove selected", Height = DialogMetrics.TrackCapoFieldHeight };
        DialogMetrics.WithPadding(_capoRemove, DialogMetrics.TrackTuningButtonPadding);
        _capoRemove.Click += (_, _) => RemoveSelectedCapo();
        var removeAll = new Button { Name = "RemoveAllCaposButton", Content = "Remove all capos", Height = DialogMetrics.TrackCapoFieldHeight };
        DialogMetrics.WithPadding(removeAll, DialogMetrics.TrackTuningButtonPadding);
        removeAll.Click += (_, _) => { _capoFret = 0; _partials.Clear(); SelectCapo(FullCapo); };
        _capoRemove.HorizontalAlignment = HorizontalAlignment.Stretch;
        removeAll.HorizontalAlignment = HorizontalAlignment.Stretch;
        var buttons = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0), Children = { _capoRemove, removeAll } };
        var boardRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(buttons, 1);
        boardRow.Children.Add(_capoBoard);
        boardRow.Children.Add(buttons);
        var heading = new DockPanel();
        var title = Heading("CAPO");
        DialogMetrics.WithMargin(title, DialogMetrics.TrackCapoTitleGap);
        heading.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { title, _capoChips, FieldLabel("Fret"), _capoFretBox, _capoStrings } });
        _capoCard = Card(heading, boardRow);
        _capoCard.Name = "CapoCard";
        LoadPartialCapos();
        return _capoCard;
    }

    private ToggleButton PlacementButton(string text, bool partial)
    {
        var button = new CapoToggle { Name = $"CapoPlacement{text}", Content = text, Height = DialogMetrics.TrackCapoFieldHeight,
            VerticalAlignment = VerticalAlignment.Center, CornerRadius = AvaloniaFormControlStyles.ControlCornerRadius };
        AutomationProperties.SetName(button, $"Capo placement {text}");
        DialogMetrics.WithPadding(button, DialogMetrics.TrackCapoChipPadding);
        DialogMetrics.WithMargin(button, DialogMetrics.TrackCapoChipGap);
        button.Click += (_, _) =>
        {
            _placePartialMode = partial;
            // The fret and string fields act on the mode's capo: the full one, or the newest partial.
            if (!partial) _capoSelected = FullCapo;
            else if (SelectedPartial is null) _capoSelected = _partials.Count - 1;
            SyncCapoCard();
        };
        return button;
    }

    private static TextBlock FieldLabel(string text)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        DialogMetrics.WithMargin(label, DialogMetrics.TrackCapoFieldLabelGap);
        return label;
    }

    private ComboBox StringBox(string name, string label, bool from)
    {
        var box = new ComboBox { Name = name, Width = DialogMetrics.TrackCapoStringBoxWidth, Height = DialogMetrics.TrackCapoFieldHeight,
            MinHeight = DialogMetrics.TrackCapoFieldHeight, VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1), CornerRadius = AvaloniaFormControlStyles.ControlCornerRadius };
        box.Classes.Add(AvaloniaFormControlStyles.PrefsFieldClass);
        // The template's background is at least this wide; on a narrower box its side edges overflow and are clipped, leaving only the top and bottom of the outline.
        box.Resources["ComboBoxThemeMinWidth"] = 0.0;
        // Keep the whole field's fill steady, with one complete accent outline on hover/focus.
        foreach (var state in new[] { "", ":pointerover", ":focus", ":pressed" })
        {
            box.Styles.Add(new Style(x => string.IsNullOrEmpty(state)
                ? x.OfType<ComboBox>().Template().OfType<Border>().Name("Background")
                : x.OfType<ComboBox>().Class(state).Template().OfType<Border>().Name("Background"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Panel2Brush")),
                    new Setter(Border.BorderBrushProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(state.Length == 0 ? "BorderBrush" : "AccentBrush")),
                    new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                    new Setter(Border.CornerRadiusProperty, AvaloniaFormControlStyles.ControlCornerRadius),
                },
            });
        }
        AutomationProperties.SetName(box, label);
        box.SelectionChanged += (_, _) => { if (!_syncingCapo) PickStringRange(from); };
        return box;
    }

    // Own drawing avoids the theme's generic grey ToggleButton hover overriding the instrument accent.
    internal sealed class CapoToggle : ToggleButton
    {
        internal const string AccentToken = "Accent";   // the master accent (Appearance), as the rest of the window
        protected override Type StyleKeyOverride => typeof(ToggleButton);
        public CapoToggle()
        {
            Template = new FuncControlTemplate<CapoToggle>((button, _) =>
                new TextBlock { Text = button.Content?.ToString(), Opacity = 0 }.WithMargin(button.Padding));
            IsCheckedChanged += (_, _) => InvalidateVisual();
            PointerEntered += (_, _) => InvalidateVisual();
            PointerExited += (_, _) => InvalidateVisual();
            GotFocus += (_, _) => InvalidateVisual();
            LostFocus += (_, _) => InvalidateVisual();
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            var d = new AvaloniaDrawContext().Use(context);
            var ink = AvaloniaPalette.InkOf(ActualThemeVariant == ThemeVariant.Light);
            var accent = ink(AccentToken);
            var on = IsChecked == true;
            d.Rect(ink("PanelBackground"), new UiStroke(ink("TextMuted").WithOpacity(DialogMetrics.TrackCapoOffOpacity), 1), 0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1), CornerRadius.TopLeft);
            if (on || IsPointerOver)
                d.Rect(on ? accent : accent.WithOpacity(DialogMetrics.TrackCapoOffOpacity), null, 0, 0, Bounds.Width, Bounds.Height, CornerRadius.TopLeft);
            if (IsKeyboardFocusWithin)
                d.Rect(null, new UiStroke(accent, DialogMetrics.TrackCapoSelectionThickness), 1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2), CornerRadius.TopLeft);
            var text = d.Text(Content?.ToString() ?? "", TabForge.Avalonia.Platform.InstrumentPanelView.Font, FontSize, on ? ThemeColours.InkOn(accent) : ink("TextPrimary"), d.PixelsPerDip);
            d.DrawText(text, (Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2);
        }
    }

    /// <summary>A working partial capo: a fret plus which working strings it covers. OK converts it to a model partial.</summary>
    internal sealed class PartialCapo
    {
        public int Fret;
        public readonly List<bool> Strings = new();

        internal static PartialCapo FromModel(PartialCapoModel model, int stringCount)
        {
            var draft = new PartialCapo { Fret = model.Fret };
            for (var s = 0; s < stringCount; s++) draft.Strings.Add(PartialCapoRules.Covers(model.Fret, model.StringMask, s));
            return draft;
        }

        internal PartialCapoModel ToModel()
        {
            var mask = 0;
            for (var s = 0; s < Strings.Count && s < 31; s++) if (Strings[s]) mask |= 1 << s;
            return new PartialCapoModel { Fret = Fret, StringMask = mask };
        }
    }

    internal IReadOnlyList<PartialCapo> PartialCapos => _partials;

    /// <summary>Reads the track's partials into the working copies and selects the full capo.</summary>
    internal void LoadPartialCapos()
    {
        _partials.Clear();
        foreach (var partial in _track.PartialCapos ?? Enumerable.Empty<PartialCapoModel>())
            _partials.Add(PartialCapo.FromModel(partial, _draft.Count));
        _partialSlotsOrigin = Enumerable.Range(0, _draft.Count).ToList();
        _capoSelected = FullCapo;
        SyncCapoCard();
    }

    /// <summary>The working partials as the model holds them (off and empty ones dropped), for OK and the discard question.</summary>
    internal List<PartialCapoModel>? CurrentPartialModels() =>
        PartialCapoRules.Normalize(_partials.Select(p => p.ToModel()).ToList(), _draft.Count);

    /// <summary>The capo a working string sounds behind right now: the full capo or the highest working partial covering it.</summary>
    internal int WorkingEffectiveCapo(int stringIndex)
    {
        var capo = Math.Max(0, _capoFret);
        foreach (var partial in _partials)
            if (partial.Fret > capo && stringIndex >= 0 && stringIndex < partial.Strings.Count && partial.Strings[stringIndex]) capo = partial.Fret;
        return capo;
    }

    private PartialCapo? SelectedPartial => _capoSelected >= 0 && _capoSelected < _partials.Count ? _partials[_capoSelected] : null;

    // The selected capo's fret, kept inside the model range (FretboardCapoRules.Clamp, and the presenter's 0-12).
    private void SetCapoFret(int fret)
    {
        fret = Math.Min(FretboardCapoRules.Clamp(fret), TrackPropertiesPresenter.MaxCapo);
        if (SelectedPartial is { } partial) partial.Fret = fret;
        else _capoFret = fret;
        SyncCapoCard();
    }

    private void SelectCapo(int selected)
    {
        _capoSelected = selected;
        SyncCapoCard();
    }

    private void PlaceCapo(int fret)
    {
        if (fret <= 0 || fret > TrackPropertiesPresenter.MaxCapo) return;
        if (!_placePartialMode) { _capoSelected = FullCapo; SetCapoFret(fret); return; }
        if (_partials.Count >= MaxPartialCapos)
        {
            ToolTip.SetTip(_capoBoard, "Partial capo limit reached");
            return;
        }
        var added = new PartialCapo { Fret = fret };
        var (from, to) = NextPartialRange();
        for (var s = 0; s < _draft.Count; s++) added.Strings.Add(s >= from && s <= to);
        _partials.Add(added);
        SelectCapo(_partials.Count - 1);
    }

    private void RemoveSelectedCapo()
    {
        if (SelectedPartial is { } partial) _partials.Remove(partial);
        else _capoFret = 0;
        SelectCapo(FullCapo);
    }

    // Aligns working string flags and redraws all capos; never writes the song.
    internal void SyncCapoCard()
    {
        if (_capoBoard is null) return;
        RemapPartialStrings();
        foreach (var partial in _partials)
        {
            while (partial.Strings.Count < _draft.Count) partial.Strings.Add(false);
            while (partial.Strings.Count > _draft.Count) partial.Strings.RemoveAt(partial.Strings.Count - 1);
        }
        if (_capoSelected >= _partials.Count) _capoSelected = FullCapo;
        SyncPlacementMode();
        _syncingCapo = true;
        _capoFretBox.Value = SelectedPartial?.Fret ?? _capoFret;
        SyncStringPicker();
        _syncingCapo = false;
        _capoRemove.IsEnabled = SelectedPartial is not null || _capoFret > 0;
        _capoBoard.ShowCapos(_capoFret, _partials.Select(p => p.ToModel()).ToList(), _capoSelected, _draft.Count, _track.Kind == TrackKind.Bass);
    }

    private void SyncPlacementMode()
    {
        _placeFull.IsChecked = !_placePartialMode;
        _placePartial.IsChecked = _placePartialMode;
    }

    private void SyncStringPicker()
    {
        // Only partial placement needs a string range; keep it on the same row when shown.
        _capoStrings.IsVisible = _placePartialMode;
        _capoStrings.IsEnabled = _placePartialMode;
        ToolTip.SetTip(_capoStrings, SelectedPartial is null ? "Strings for the next partial you place" : "Strings this partial covers");
        if (_capoFrom.ItemCount != _draft.Count)
        {
            _capoFrom.Items.Clear();
            _capoTo.Items.Clear();
            for (var s = 1; s <= _draft.Count; s++) { _capoFrom.Items.Add(s); _capoTo.Items.Add(s); }
        }
        // An imported partial with gaps shows its outer strings and keeps its gaps until a box is changed.
        var (nextFrom, nextTo) = NextPartialRange();
        var strings = SelectedPartial?.Strings;
        _capoFrom.SelectedIndex = strings?.IndexOf(true) ?? nextFrom;
        _capoTo.SelectedIndex = strings?.LastIndexOf(true) ?? nextTo;
        var from = _capoFrom.SelectedIndex;
        var to = _capoTo.SelectedIndex;
        var mask = 0;
        for (var s = from; s <= to && s >= 0; s++) mask |= 1 << s;
        _capoBoard.SetPlacementStrings(_placePartialMode ? mask : null);
    }

    // The strings the next placed partial covers: what the boxes were set to, else the middle strings.
    private (int From, int To) NextPartialRange()
    {
        if (_nextPartialStrings is { } picked && picked.To < _draft.Count) return picked;
        var count = Math.Clamp(_draft.Count / 2, 1, Math.Max(1, _draft.Count - 1));
        var start = (_draft.Count - count) / 2;
        return (start, start + count - 1);
    }

    // The two boxes cover every string between them; the box not being changed gives way when they would cross.
    private void PickStringRange(bool fromChanged)
    {
        if (_capoFrom.SelectedIndex < 0 || _capoTo.SelectedIndex < 0) return;
        var from = _capoFrom.SelectedIndex;
        var to = _capoTo.SelectedIndex;
        if (from > to) { if (fromChanged) to = from; else from = to; }
        if (SelectedPartial is { } partial)
            for (var s = 0; s < partial.Strings.Count; s++) partial.Strings[s] = s >= from && s <= to;
        else _nextPartialStrings = (from, to);
        SyncCapoCard();
    }

    // Moves each flag from its old working slot to the slot that still holds the same original string; an added string
    // starts unticked and a removed string's flag is gone.
    private void RemapPartialStrings()
    {
        if (_partialSlotsOrigin.Count == 0) { _partialSlotsOrigin = Enumerable.Range(0, _draft.Count).ToList(); return; }
        if (_partialSlotsOrigin.SequenceEqual(_draft.Origin)) return;
        var oldSlotOfOrigin = new Dictionary<int, int>();
        for (var i = 0; i < _partialSlotsOrigin.Count; i++)
            if (_partialSlotsOrigin[i] >= 0) oldSlotOfOrigin[_partialSlotsOrigin[i]] = i;
        foreach (var partial in _partials)
        {
            var flags = new List<bool>(_draft.Count);
            for (var s = 0; s < _draft.Count; s++)
            {
                var origin = _draft.Origin[s];
                flags.Add(origin >= 0 && oldSlotOfOrigin.TryGetValue(origin, out var old)
                    && old < partial.Strings.Count && partial.Strings[old]);
            }
            partial.Strings.Clear();
            partial.Strings.AddRange(flags);
        }
        _partialSlotsOrigin = _draft.Origin.ToList();
    }
}
