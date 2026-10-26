using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.LiveInput;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the tuner part of the live input strip: the reading group (note name, cents scale with its needle, cents text; fixed-width cells, so text never re-wraps the strip) and
//   the controls group (the lit Tuner switch and the window dropdown).
// Does not own: the reading (the engine), what to show of it (LiveTunerReadout), turning the tuner of the engine on (LiveInputStrip) or the needle drawing (LiveCentsBar).
// Tests: TabForge.Avalonia.Tests.LiveInputStripTests, LiveStripSliderTests, LiveTunerUiTests.
/// <summary>The tuner view of the strip. It raises <see cref="TunerSwitched"/> and <see cref="WindowChosen"/> when the user changes them; the strip does the rest. Nothing here changes size.</summary>
public sealed class LiveStripTuner : Grid
{
    public const string SwitchText = "Tuner", OffNote = "--";
    private readonly TextBlock _note = new() { Name = "TunerNote", FontSize = ChromeMetrics.LiveStripNoteFontSize, FontWeight = FontWeight.SemiBold, Text = OffNote, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Width = ChromeMetrics.LiveStripNoteWidth };
    private readonly TextBlock _cents = new() { Name = "TunerCents", VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Width = ChromeMetrics.LiveStripCentsTextWidth, FontSize = ChromeMetrics.LiveStripFontSize };
    private readonly LiveCentsBar _bar = new() { Name = "TunerNeedle", Height = ChromeMetrics.LiveStripControlHeight, VerticalAlignment = VerticalAlignment.Center };
    private readonly LiveStripToggle _switch = new(LiveStripToggle.Look.Lit, SwitchText, ChromeMetrics.LiveStripTunerToggleWidth, ChromeMetrics.LiveStripControlHeight) { Name = "TunerSwitch", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _window = new() { Name = "TunerWindow", Height = ChromeMetrics.LiveStripControlHeight, MinHeight = ChromeMetrics.LiveStripControlHeight, FontSize = ChromeMetrics.LiveStripFontSize, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private string _pendingCents = "", _shownCents = "";
    private double _centsAt = double.NegativeInfinity;
    private bool _setting;
    private int _shownMs = -1;
    private string _tone = "TextMuted";

    public LiveStripTuner()
    {
        Name = "LiveTuner";
        ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto");
        ColumnSpacing = ChromeMetrics.LiveStripGap;
        VerticalAlignment = VerticalAlignment.Center;
        _note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _cents[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        ToolTip.SetTip(_bar, "How far the note is from true, in cents");
        ToolTip.SetTip(_switch, "Tune by the raw live input, before the FX chain");
        foreach (var ms in LiveStripRules.WindowChoicesMs) _window.Items.Add(LiveStripRules.WindowItemText(ms));
        _window.WithPadding(ChromeMetrics.LiveStripWindowPad, 0);
        _window.SelectionBoxItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = NumberOf(text), VerticalAlignment = VerticalAlignment.Center });   // the closed box shows the number alone; the open list keeps "100 ms"
        _window.Width = LongestNumberWidth() + ChromeMetrics.LiveStripWindowChrome;
        ShowWindowTip(LiveStripRules.DefaultWindowMs);
        SetColumn(_cents, 1); SetColumn(_bar, 2);   // the fixed cents cell sits by the note, so the scale ends next to the Tuner switch with no dead gap
        Children.Add(_note); Children.Add(_cents); Children.Add(_bar);
        Controls.Children.Add(_switch); Controls.Children.Add(_window);
        _switch.Toggled += on => { if (!_setting) TunerSwitched?.Invoke(on); };
        _window.SelectionChanged += (_, _) => { if (!_setting && _window.SelectedItem is string text) WindowChosen?.Invoke(LiveStripRules.ParseWindow(text)); };
    }

    /// <summary>The Tuner switch and the window dropdown: a second group of the strip, so the strip can wrap it apart from the reading.</summary>
    public StackPanel Controls { get; } = new() { Name = "LiveTunerControls", Orientation = Orientation.Horizontal, Spacing = ChromeMetrics.LiveStripGap, VerticalAlignment = VerticalAlignment.Center };

    public event Action<bool>? TunerSwitched;
    public event Action<int>? WindowChosen;

    public bool IsOn => _switch.IsChecked;
    public LiveCentsBar Scale => _bar;
    public LiveStripToggle Switch => _switch;
    public ComboBox Window => _window;
    public string NoteText => _note.Text ?? "";
    public string CentsText => _cents.Text ?? "";
    public double NeedleCents => _bar.Cents;

    private static string NumberOf(string? text) => text?.Replace(" ms", "") ?? "";

    private void ShowWindowTip(int ms)
    {
        ToolTip.SetTip(_window, new TextBlock { Text = $"Tuner window: {LiveStripRules.WindowText(ms)}. Shorter reacts faster, longer is steadier.", TextWrapping = TextWrapping.Wrap, MaxWidth = ChromeMetrics.LiveStripTipMaxWidth });
        ToolTip.SetPlacement(_window, PlacementMode.Bottom);   // a popup that flips to stay on screen
    }

    /// <summary>The width of the widest number the closed box shows, at the dropdown's font; <see cref="ChromeMetrics.LiveStripWindowChrome"/> adds the padding, border and arrow.</summary>
    private double LongestNumberWidth()
    {
        var probe = new TextBlock { FontSize = _window.FontSize };
        var widest = 0.0;
        foreach (var item in _window.Items.OfType<string>())
        {
            probe.Text = NumberOf(item);
            probe.Measure(Size.Infinity);
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }
        return Math.Ceiling(widest);
    }

    /// <summary>Shows the window in the dropdown without raising <see cref="WindowChosen"/>.</summary>
    public void ShowWindow(int ms)
    {
        if (ms == _shownMs) return;   // the strip asks every frame: no string per frame
        ShowWindowTip(ms);
        var text = LiveStripRules.WindowItemText(ms);
        if (Equals(_window.SelectedItem, text)) { _shownMs = ms; return; }
        _setting = true;
        _window.SelectedIndex = _window.Items.IndexOf(text);   // no lambda: a closure would be allocated on entry, before the early return
        _setting = false;
        _shownMs = ms;
    }

    /// <summary>Shows the reading: the needle and note at once, the cents text at most a few times a second (see <see cref="Flush"/>); with no clear pitch the needle is hidden.</summary>
    public void Show(LiveTunerReadout readout, double nowMs)
    {
        if (_note.Text != readout.Note) _note.Text = readout.Note;
        _bar.Cents = readout.Cents;
        _pendingCents = readout.CentsText;
        Flush(nowMs);
    }

    /// <summary>Puts the latest cents text on screen when the interval since the last change has passed (or the text clears); the strip calls it every frame.</summary>
    public void Flush(double nowMs)
    {
        if (_pendingCents == _shownCents) return;
        if (_pendingCents.Length > 0 && nowMs - _centsAt < ChromeMetrics.LiveStripTextIntervalMs) return;
        _shownCents = _pendingCents;
        _centsAt = nowMs;
        _cents.Text = _shownCents;
        if (_shownCents.Length > 0) ShowTone(LiveCentsBar.ToneToken(_bar.Cents));
    }

    /// <summary>Shows the tuner as off: no note, no needle, no cents.</summary>
    public void ShowOff()
    {
        _note.Text = OffNote;
        _bar.Cents = double.NaN;
        _pendingCents = _shownCents = "";
        _cents.Text = "";
        ShowTone("TextMuted");
    }

    /// <summary>Colours the cents text by theme token; only a change of token touches the binding, so a frame does not.</summary>
    private void ShowTone(string token)
    {
        if (token == _tone) return;
        _tone = token;
        _cents[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(token);
    }
}
