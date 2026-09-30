using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Chromatic tuner. The audio engine detects the pitch of the armed input (YIN, ~25 readings a second, see EngineHost) and this window
/// shows the nearest note, a cents needle and the selected track's string tunings (the string closest to the sound is highlighted).
/// Lightweight: one custom-drawn gauge, redrawn only when a new reading arrives (at most 30 times a second); nothing runs while closed.
/// </summary>
public sealed class TunerWindow : Window
{
    private static TunerWindow? _open;

    private readonly AudioEngineClient _engine;
    private readonly Func<TrackModel?> _track;
    private readonly Gauge _gauge = new();
    private readonly TextBlock _note = new(), _detail = new(), _hint = new();
    private readonly StackPanel _strings = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private TrackModel? _shownTrack;
    private TextBlock[] _stringBoxes = Array.Empty<TextBlock>();
    private int[] _tunings = Array.Empty<int>();
    private int _lastSeq = -1;
    private double _smoothedMidi = double.NaN;
    private DateTime _lastVoiced = DateTime.MinValue;

    /// <summary>Shows the tuner (one window at a time; a second call brings it to the front).</summary>
    public static void Open(Window? owner, AudioEngineClient engine, Func<TrackModel?> track)
    {
        if (_open is { } existing) { existing.Activate(); return; }
        _open = new TunerWindow(owner, engine, track);
        _open.Show();
    }

    private TunerWindow(Window? owner, AudioEngineClient engine, Func<TrackModel?> track)
    {
        _engine = engine; _track = track;
        Title = "Tuner"; Width = 420; Height = 330; MinWidth = 340; MinHeight = 280;
        Owner = owner; ShowInTaskbar = false; Topmost = false;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        _note.FontSize = 54; _note.FontWeight = FontWeights.SemiBold; _note.HorizontalAlignment = HorizontalAlignment.Center; _note.Text = "--";
        _detail.HorizontalAlignment = HorizontalAlignment.Center; _detail.Opacity = 0.75;
        _hint.HorizontalAlignment = HorizontalAlignment.Center; _hint.Opacity = 0.65; _hint.TextWrapping = TextWrapping.Wrap; _hint.TextAlignment = TextAlignment.Center;
        _hint.Margin = new Thickness(0, 8, 0, 0);
        _gauge.Height = 56; _gauge.Margin = new Thickness(0, 4, 0, 8);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_note);
        root.Children.Add(_gauge);
        root.Children.Add(_detail);
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(_strings);
        root.Children.Add(_hint);
        Content = root;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _engine.TunerOn = true; _timer.Start(); Refresh(); };
        Closed += (_, _) => { _timer.Stop(); _engine.TunerOn = false; _open = null; };
        SetStrings(_track());
    }

    private void SetStrings(TrackModel? track)
    {
        _shownTrack = track;
        _strings.Children.Clear();
        _tunings = track is null || track.Kind == TrackKind.Drums ? Array.Empty<int>() : track.StringTunings.ToArray();
        _stringBoxes = new TextBlock[_tunings.Length];
        // String 1 is the highest: list low to high like the strings on the instrument.
        for (var i = _tunings.Length - 1; i >= 0; i--)
        {
            var box = new TextBlock { Text = MusicTheoryService.NoteName(_tunings[i]), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(3, 0, 3, 0), FontSize = 15 };
            _stringBoxes[i] = box;
            _strings.Children.Add(new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Child = box, Tag = i });
        }
        _lastSeq = -1;
    }

    private void Refresh()
    {
        var track = _track();
        if (!ReferenceEquals(track, _shownTrack)) SetStrings(track);
        if (!_engine.IsRunning)
        {
            _hint.Text = "The audio engine is off. Turn it on in Preferences, then arm a track to record so the input opens.";
            Display(double.NaN, 0, -1);
            return;
        }
        var (hz, clarity, seq) = _engine.TunerReading;
        if (seq == _lastSeq) return;   // nothing new: no redraw
        _lastSeq = seq;
        if (hz > 0 && clarity >= 0.6f)
        {
            var midi = 69 + 12 * Math.Log2(hz / 440.0);
            // Smooth within a note (the needle steadies), jump straight to a different one.
            _smoothedMidi = !double.IsNaN(_smoothedMidi) && Math.Abs(midi - _smoothedMidi) < 0.8 && (DateTime.UtcNow - _lastVoiced).TotalMilliseconds < 500
                ? _smoothedMidi + (midi - _smoothedMidi) * 0.4 : midi;
            _lastVoiced = DateTime.UtcNow;
            _hint.Text = "";
            Display(_smoothedMidi, hz, clarity);
        }
        else if ((DateTime.UtcNow - _lastVoiced).TotalMilliseconds > 1200)
        {
            _smoothedMidi = double.NaN;
            _hint.Text = "Play a note. The tuner listens to the armed input: arm a track to record (R) so the input is open.";
            Display(double.NaN, 0, -1);
        }
    }

    /// <summary>Displays a reading (midi NaN: nothing heard).</summary>
    private void Display(double midi, double hz, float clarity)
    {
        if (double.IsNaN(midi))
        {
            _note.Text = "--"; _detail.Text = ""; _gauge.Set(double.NaN);
            Highlight(-1);
            return;
        }
        var nearest = (int)Math.Round(midi);
        var cents = (midi - nearest) * 100;
        _note.Text = MusicTheoryService.NoteName(nearest);
        _detail.Text = string.Create(CultureInfo.InvariantCulture, $"{hz:0.0} Hz   {cents:+0;-0;0} cents");
        _gauge.Set(cents);
        var best = -1; var bestDiff = 2.0;   // a string counts as "the one" within 2 semitones
        for (var i = 0; i < _tunings.Length; i++)
        {
            var diff = Math.Abs(midi - _tunings[i]);
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        Highlight(best);
    }

    private void Highlight(int index)
    {
        foreach (Border b in _strings.Children)
        {
            var on = b.Tag is int i && i == index;
            b.Background = on ? Brushes.SeaGreen : Brushes.Transparent;
            if (b.Child is TextBlock t) t.Foreground = on ? Brushes.White : (Brush)FindResource("TextBrush");
        }
    }

    /// <summary>The cents needle: a -50..+50 scale, green when within 5 cents.</summary>
    private sealed class Gauge : FrameworkElement
    {
        private double _cents = double.NaN;
        public void Set(double cents) { if (cents.Equals(_cents)) return; _cents = cents; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth; var h = ActualHeight;
            if (w < 20 || h < 10) return;
            var text = TryFindResource("TextBrush") as Brush ?? Brushes.Gray;
            var pen = new Pen(text, 1) { Brush = text }; pen.Freeze();
            double X(double c) => 10 + (c + 50) / 100 * (w - 20);
            dc.DrawLine(pen, new Point(X(-50), h - 8), new Point(X(50), h - 8));
            for (var c = -50; c <= 50; c += 10)
                dc.DrawLine(pen, new Point(X(c), h - 8), new Point(X(c), h - (c == 0 ? 34 : 18)));
            if (double.IsNaN(_cents)) return;
            var inTune = Math.Abs(_cents) <= 5;
            var brush = inTune ? Brushes.SeaGreen : Math.Abs(_cents) <= 20 ? Brushes.Goldenrod : Brushes.IndianRed;
            var x = X(Math.Clamp(_cents, -50, 50));
            dc.DrawRoundedRectangle(brush, null, new Rect(x - 3, 4, 6, h - 12), 2, 2);
        }
    }
}
