using TabForge.Platform.Theme;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Tuner (Tools > Tuner…): the nearest note, its Hz and cents text, a cents needle, and the track's string tunings with the string closest
/// to the sound lit. The live window follows the selected track and reads the engine about 30 times a second while it is open; it turns the
/// engine's tuner on only while open. The preview window shows one sample reading (<see cref="SampleHz"/>) and reads nothing.
/// Owns: the controls, their layout, the poll timer and the highlight binding. Does not own: the reading rules (TunerPresenter), the needle's drawing
/// (<see cref="TunerNeedle"/>) or the palette values (AvaloniaPalette).
/// Tests: TabForge.Avalonia.Tests.TunerWindowTests.
/// </summary>
public sealed class TunerWindow : Window
{
    /// <summary>The open A string of a guitar (A2): the sample reading the preview shows.</summary>
    public const double SampleHz = 110.0;
    private const float SampleClarity = 0.9f;
    // Gated on visibility: the shared 33 ms meter tick calls this only while the window shows.
    private readonly Action? _meteredRefresh = null;

    private readonly Func<TrackModel?> _track;
    private readonly double _sampleHz;
    private readonly AudioEngineClient? _engine;
    /// <summary>True for the live window (engine-off when it has no engine); false for the preview, which always shows its sample.</summary>
    private readonly bool _live;
    private readonly TunerPresenter _presenter;
    private readonly TextBlock _note = new() { Name = "NoteText", FontSize = 54, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _detail = new() { Name = "DetailText", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _hint = new()
    {
        Name = "HintText", HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly TunerNeedle _needle = new() { Name = "Needle", Height = 56, Margin = new Thickness(0, 4, 0, 8) };
    private readonly StackPanel _strings = new() { Name = "Strings", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    // The two box outlines, resolved from the theme keys: the lit green and the idle chrome border. Re-read when the theme variant changes.
    private IBrush _litBrush = Brushes.Transparent;
    private IBrush _idleBrush = Brushes.Transparent;
    // The highlight the presenter last asked for and the one the boxes currently paint; equal means Highlight does no work.
    private int _currentIndex = int.MinValue;
    private int _appliedIndex = int.MinValue;

    /// <param name="track">The track whose string tunings are listed; null or a track without strings lists none.</param>
    /// <param name="sampleHz">The pitch the window shows as its one reading.</param>
    public TunerWindow(TrackModel? track, double sampleHz) : this(() => track, sampleHz, engine: null, live: false, meter: null)
    {
    }

    /// <summary>The live tuner: shows the track <paramref name="track"/> returns and the engine's readings while the window is open.</summary>
    /// <param name="track">The track whose tunings are listed; read on each poll, so the window follows the selected track.</param>
    /// <param name="engine">The engine that detects the pitch; it listens only while this window is open. Null (no playback host) shows the engine-off state.</param>
    /// <param name="meter">The app's shared meter tick the refresh rides on; null gives this window its own.</param>
    public TunerWindow(Func<TrackModel?> track, AudioEngineClient? engine, SharedMeterTick? meter = null) : this(track, SampleHz, engine, live: true, meter)
    {
    }

    private TunerWindow(Func<TrackModel?> track, double sampleHz, AudioEngineClient? engine, bool live, SharedMeterTick? meter)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _track = track; _sampleHz = sampleHz; _engine = engine; _live = live;
        _presenter = new TunerPresenter(track());
        Title = "Tuner"; Width = ChromeMetrics.TunerWindowWidth; Height = ChromeMetrics.TunerWindowHeight; MinWidth = ChromeMetrics.TunerWindowMinWidth; MinHeight = ChromeMetrics.TunerWindowMinHeight;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _detail[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        _hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        RefreshBrushes();
        BuildStringBoxes(_presenter.StringNames);
        // A live theme switch re-resolves the two outlines and repaints the boxes with them, keeping the current highlight.
        ActualThemeVariantChanged += (_, _) => { RefreshBrushes(); _appliedIndex = int.MinValue; ApplyHighlight(); };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_note);
        root.Children.Add(_needle);
        root.Children.Add(_detail);
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(_strings);
        root.Children.Add(_hint);
        Content = root;
        Refresh();
        if (engine is null) return;

        var ownsMeter = meter is null;
        var ticks = meter ?? new SharedMeterTick();
        _meteredRefresh = () => { if (IsVisible) Refresh(); };
        Opened += (_, _) => { engine.SetTunerWanted(this, true); ticks.Subscribe(_meteredRefresh); };
        Closed += (_, _) => { ticks.Unsubscribe(_meteredRefresh); if (ownsMeter) ticks.Dispose(); engine.SetTunerWanted(this, false); };
    }

    /// <summary>Reads the engine (or the preview's sample) and shows the presenter's frame; a reading already on screen is not redrawn.</summary>
    private void Refresh()
    {
        var (hz, clarity, seq) = _engine?.TunerReading ?? ((float)_sampleHz, SampleClarity, 1);
        var frame = _presenter.Update(_track(), _engine?.IsRunning ?? !_live, hz, clarity, seq, DateTime.UtcNow);
        if (frame.StringsChanged)
        {
            _strings.Children.Clear();
            BuildStringBoxes(_presenter.StringNames);
        }
        if (!frame.Changed) return;
        _hint.Text = frame.Hint;
        _note.Text = frame.NoteText;
        _detail.Text = frame.DetailText;
        _needle.Cents = frame.NeedleCents;
        Highlight(frame.StringIndex);
    }

    /// <summary>Boxes for the strings, listed low to high like the strings on the instrument (string 1, the highest, is last).</summary>
    private void BuildStringBoxes(IReadOnlyList<string> names)
    {
        for (var i = names.Count - 1; i >= 0; i--)
        {
            var label = new TextBlock { Text = names[i], FontSize = 15, Padding = new Thickness(8, 3, 8, 3) };
            label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
            var box = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), Margin = new Thickness(3, 0, 3, 0), Child = label, Tag = i, BorderBrush = _idleBrush };
            _strings.Children.Add(box);
        }
        _appliedIndex = int.MinValue;   // the new boxes carry only the idle brush; the next Highlight paints them
    }

    /// <summary>
    /// Lights the box of the string at <paramref name="index"/> (-1 lights none) with a green outline. The outline, not a green fill, keeps
    /// the label at 4.5:1 or better in both themes (white on the green fill measures about 4.2:1). Setting the cached brushes directly keeps
    /// an unchanged highlight free of work: the same index returns before touching any box.
    /// </summary>
    private void Highlight(int index)
    {
        _currentIndex = index;
        if (index == _appliedIndex) return;
        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        _appliedIndex = _currentIndex;
        foreach (Border box in _strings.Children)
            box.BorderBrush = box.Tag is int i && i == _currentIndex ? _litBrush : _idleBrush;
    }

    /// <summary>Re-reads the two box outlines from the theme keys (SeaGreen and ChromeBorder) for the current theme variant.</summary>
    private void RefreshBrushes()
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        _litBrush = new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("SeaGreen", light)));
        _idleBrush = new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("ChromeBorder", light)));
    }
}
