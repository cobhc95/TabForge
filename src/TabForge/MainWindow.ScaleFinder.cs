using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow: scale finder (Tools menu, fretboard right-click, the Scales button under the fretboard legend)
// and the instrument panel's view choice (fretboard / keyboard / drums).
public partial class MainWindow
{
    /// <summary>Right-click "Show all tracks as" choice for this session; null follows Settings.</summary>
    private string? _instrumentViewOverride;
    /// <summary>Right-click "Show this track as" choices for this session (per track).</summary>
    private readonly Dictionary<TrackModel, string> _trackInstrumentViews = new(ReferenceEqualityComparer.Instance);
    private string? _appliedInstrumentViewSetting;

    private string EffectiveInstrumentView =>
        SelectedTrack is { } track && _trackInstrumentViews.TryGetValue(track, out var own) ? own
        : _instrumentViewOverride ?? _settings.Editing.InstrumentView;

    /// <summary>Opens the scale finder on the current selection (score or timeline), else the whole song.</summary>
    private void OpenScaleFinder()
    {
        var chosen = ScaleFinderWindow.Show(this, _project, SelectedTrack, CurrentScaleRange(), _scaleHighlight,
            _settings.Editing.ScaleHighlightStyle, _settings.Editing.ScaleHighlightColour,
            (style, colour) => SetInstrumentAppearance(scaleStyle: style, scaleColour: colour));
        if (chosen is null) return;
        SetScaleHighlight(chosen == "Off" ? null : chosen);
        StatusText.Text = chosen == "Off" ? "Scale highlight cleared" : $"Scale highlight: {chosen}";
    }

    private ScaleFinderWindow.Range? CurrentScaleRange()
    {
        if (Editor.HasSelection)
        {
            var (m1, c1, m2, c2) = Editor.SelectionCellRange;
            return new ScaleFinderWindow.Range(m1, c1, m2, c2, m1 == m2 ? $"bar {m1 + 1}" : $"bars {m1 + 1}–{m2 + 1}");
        }
        if (_loopHasArea)
            return new ScaleFinderWindow.Range(_loopStartBar, 0, _loopEndBar, int.MaxValue - 1,
                _loopStartBar == _loopEndBar ? $"bar {_loopStartBar + 1}" : $"bars {_loopStartBar + 1}–{_loopEndBar + 1}");
        return null;
    }

    /// <summary>Highlights a scale ("E Natural Minor") on the fretboard, or clears it with null.</summary>
    private void SetScaleHighlight(string? scale)
    {
        var item = scale ?? "Off";
        if (!ScaleHighlightCombo.Items.Contains(item)) ScaleHighlightCombo.Items.Add(item);
        ScaleHighlightCombo.SelectedItem = item; // runs PracticeOption_Changed: stores, redraws and saves
        if (!Equals(_scaleHighlight, scale)) { _scaleHighlight = scale; RefreshInstrument(); SaveSettings(); }
    }

    /// <summary>Applies the view choice (or the instrument's natural view) and the keyboard size to a state.</summary>
    private void ApplyInstrumentView(InstrumentVisualState state)
    {
        var natural = InstrumentVisualizer.NaturalKind(SelectedTrack);
        state.Kind = EffectiveInstrumentView switch
        {
            InstrumentViews.Keyboard => InstrumentKind.Keyboard,
            InstrumentViews.Drums => InstrumentKind.Drums,
            InstrumentViews.Fretboard => natural == InstrumentKind.Bass ? InstrumentKind.Bass : InstrumentKind.Guitar,
            _ => natural,
        };
        state.KeyboardKeys = _settings.Editing.KeyboardKeys;
        state.GreyKeys = _settings.Editing.KeyboardKeyColours switch
        {
            KeyboardKeyStyles.Grey => true,
            KeyboardKeyStyles.White => false,
            _ => !VisualTheme.IsLight,
        };
        var ed = _settings.Editing;
        state.ScaleStyle = ed.ScaleHighlightStyle;
        state.ScaleColour = ThemeService.ScaleHighlightColour(ed.ScaleHighlightColour, VisualTheme.IsLight);
        state.ScaleStrength = ScaleHighlightStyles.StrengthFactor(ed.ScaleHighlightStrength);
        state.MarkerColour = ThemeService.FretMarkerColour(ed.FretMarkerColour);
        state.MarkerBrightness = FretMarkerLevels.Level(ed.FretMarkerBrightness);
        state.NumberScale = FretNumberSizes.Scale(ed.FretNumberSize);
        state.StringSpacing = FretStringSpacings.Factor(ed.FretStringSpacing);
    }

    /// <summary>Appearance choices from the instrument panel's menu or the scale finder; saved for every song and window.</summary>
    internal void SetInstrumentAppearance(string? scaleStyle = null, string? scaleColour = null, string? markerColour = null, string? markerBrightness = null, string? numberSize = null, string? stringSpacing = null)
    {
        var ed = _settings.Editing;
        if (stringSpacing is not null) ed.FretStringSpacing = stringSpacing;
        if (numberSize is not null) ed.FretNumberSize = numberSize;
        if (scaleStyle is not null) ed.ScaleHighlightStyle = scaleStyle;
        if (scaleColour is not null) ed.ScaleHighlightColour = scaleColour;
        if (markerColour is not null) ed.FretMarkerColour = markerColour;
        if (markerBrightness is not null) ed.FretMarkerBrightness = markerBrightness;
        RefreshInstrument();
        SaveSettings();
    }

    /// <summary>Hotkey / menu: clears the scale highlight.</summary>
    private void ClearScaleHighlight()
    {
        SetScaleHighlight(null);
        StatusText.Text = "Scale highlight cleared";
    }

    /// <summary>Sets the view for one track (track given) or for every track (null track).</summary>
    private void SetInstrumentView(string? view, TrackModel? track = null)
    {
        if (track is not null)
        {
            if (view is null) _trackInstrumentViews.Remove(track); else _trackInstrumentViews[track] = view;
        }
        else
        {
            _instrumentViewOverride = view;
            _trackInstrumentViews.Clear();
        }
        RefreshInstrument();
        StatusText.Text = $"Instrument panel: {EffectiveInstrumentView}" + (track is null ? " (all tracks)" : $" ({track.Name})");
    }

    private void SetKeyboardKeyColours(string style)
    {
        _settings.Editing.KeyboardKeyColours = style;
        RefreshInstrument();
        SaveSettings(); // a preference: every song and window uses it
    }

    private void SetKeyboardKeys(int keys)
    {
        _settings.Editing.KeyboardKeys = keys;
        RefreshInstrument();
        SaveSettings();
    }

    /// <summary>Hotkey: this track's view, fretboard, keyboard, drums, match the instrument.</summary>
    private void CycleInstrumentView()
    {
        var order = new[] { InstrumentViews.Fretboard, InstrumentViews.Keyboard, InstrumentViews.Drums, InstrumentViews.MatchInstrument };
        var at = Array.IndexOf(order, EffectiveInstrumentView);
        SetInstrumentView(order[(at + 1) % order.Length], SelectedTrack);
    }

    /// <summary>A changed default in Settings replaces any right-click choice made this session.</summary>
    private void SyncInstrumentViewSetting()
    {
        var setting = _settings.Editing.InstrumentView;
        if (_appliedInstrumentViewSetting is not null && _appliedInstrumentViewSetting != setting)
        {
            _instrumentViewOverride = null;
            _trackInstrumentViews.Clear();
            if (_mainWindowInitialized) RefreshInstrument();
        }
        _appliedInstrumentViewSetting = setting;
    }

    private void ScaleFinderButton_Click(object sender, RoutedEventArgs e) => OpenScaleFinder();

    private Point? _scaleButtonAnchor;

    /// <summary>Under the fretboard's colour legend; top-right corner for the keyboard and drum views.</summary>
    private void PlaceScaleFinderButton(Point? anchor)
    {
        _scaleButtonAnchor = anchor;
        var width = ScaleFinderButton.ActualWidth > 0 ? ScaleFinderButton.ActualWidth : 70;
        // Keep clear of the pane's hide (X) button in the top-right corner.
        var reserve = TabForge.Visualization.FretboardGeometry.CornerReserve;
        var left = anchor?.X ?? InstrumentOverlay.ActualWidth - width - 8 - reserve;
        System.Windows.Controls.Canvas.SetLeft(ScaleFinderButton, Math.Max(0, Math.Min(left, InstrumentOverlay.ActualWidth - width - 2 - reserve)));
        System.Windows.Controls.Canvas.SetTop(ScaleFinderButton, anchor?.Y ?? 8);
    }

    /// <summary>`--probe-instrument-menu &lt;report&gt;`: the instrument panel's right-click menu in each view.</summary>
    public void RunInstrumentMenuProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "instrument menu report");
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            try
            {
                await Task.Delay(1500);
                foreach (var view in new[] { InstrumentViews.Fretboard, InstrumentViews.Keyboard, InstrumentViews.Drums })
                {
                    SetInstrumentView(view);
                    await Task.Delay(300);
                    Instrument.ContextMenu = null;
                    var args = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
                        { RoutedEvent = UIElement.MouseRightButtonUpEvent, Source = Instrument };
                    Instrument.RaiseEvent(args);
                    await Task.Delay(200);
                    report.AppendLine($"== {view}: shows keyboard={Instrument.ShowsKeyboard}");
                    void Walk(System.Windows.Controls.ItemsControl items, string indent)
                    {
                        foreach (var item in items.Items.OfType<System.Windows.Controls.MenuItem>())
                        {
                            report.AppendLine($"{indent}{item.Header}{(item.IsChecked ? " [x]" : "")}");
                            if (indent.Length < 4) Walk(item, indent + "  ");
                        }
                    }
                    if (Instrument.ContextMenu is { } menu) { Walk(menu, ""); menu.IsOpen = false; }
                    else report.AppendLine("  (no menu)");
                }
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex}"); }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }
}
