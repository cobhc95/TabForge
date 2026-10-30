using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the instrument panel check: the three layouts draw in both themes with the cursor's notes lit (PNGs go to %TEMP%\tf-avalonia-tests\InstrumentPanelViewTests
/// for a look by eye), a click maps to the same note the Core rules give and raises NotePicked, the Core lit-note rule reads the demo song,
/// and the preview toggle (off shows now and recent only, with no next/upcoming markers, tags or legend rows, in every view).
/// Tests: InstrumentPanelView, InstrumentPanelStrip, InstrumentLitNotes.
/// </summary>
public class InstrumentPanelViewTests
{
    private static (TrackModel Track, InstrumentLitNotes.Lit[] Lit) FirstChord()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        for (var bar = 0; bar < track.Measures.Count; bar++)
            for (var cell = 0; cell < track.Measures[bar].Cells.Count; cell++)
            {
                var lit = InstrumentLitNotes.At(track, bar, cell).ToArray();
                if (lit.Length > 0) return (track, lit);
            }
        throw new Xunit.Sdk.XunitException("the demo song's first track has no notes");
    }

    [AvaloniaFact]
    public void LitNotesAreTheCursorCellsNotes()
    {
        var (track, lit) = FirstChord();
        Assert.All(lit, n => Assert.Equal(track.PitchOf(n.StringIndex, n.Fret), n.Midi));
        Assert.Empty(InstrumentLitNotes.At(track, -1, 0));
        Assert.Empty(InstrumentLitNotes.At(track, 0, 999));
    }

    [AvaloniaFact]
    public void PlayingNotesDrawInEveryKindInBothThemes()
    {
        // Sounding now (1000-1800), next (2000), upcoming (3000) and recently released (0-400) at song time 1200.
        var notes = new[]
        {
            new TabForge.Playback.NoteEvent { OnsetMs = 0, DurationMs = 400, StringIndex = 1, Fret = 1, Midi = 62 },
            new TabForge.Playback.NoteEvent { OnsetMs = 1000, DurationMs = 800, StringIndex = 0, Fret = 3, Midi = 67 },
            new TabForge.Playback.NoteEvent { OnsetMs = 2000, DurationMs = 300, StringIndex = 2, Fret = 2, Midi = 64 },
            new TabForge.Playback.NoteEvent { OnsetMs = 3000, DurationMs = 300, StringIndex = 4, Fret = 5, Midi = 69 },
        };
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.ShowPlayback(notes, 1200);
        var dir = DialogTestKit.RenderDirectory(nameof(InstrumentPanelViewTests));
        foreach (var (theme, variant) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
        {
            var window = new Window { Width = 1100, Height = 170, Content = view, RequestedThemeVariant = variant };
            window.Show();
            foreach (var kind in new[] { InstrumentPanelKind.Fretboard, InstrumentPanelKind.Keyboard, InstrumentPanelKind.Drums })
            {
                view.Kind = kind;
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame!.Save(Path.Combine(dir, $"playing-{kind.ToString().ToLowerInvariant()}-{theme}.png"));
            }
            window.Content = null;
            window.Close();
        }
        view.ShowPlayback(null, 0);
    }

    [AvaloniaFact]
    public void DrawsEveryKindInBothThemesAndClicksPickTheCoreRulesNote()
    {
        var (track, lit) = FirstChord();
        var view = new InstrumentPanelView { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        view.Show(track, lit);
        InstrumentNotePick? picked = null;
        view.NotePicked += p => picked = p;
        var dir = DialogTestKit.RenderDirectory(nameof(InstrumentPanelViewTests));
        foreach (var (theme, variant) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
        {
            var window = new Window { Width = 1100, Height = 170, Content = view, RequestedThemeVariant = variant };
            window.Show();
            foreach (var kind in new[] { InstrumentPanelKind.Fretboard, InstrumentPanelKind.Keyboard, InstrumentPanelKind.Drums })
            {
                view.Kind = kind;
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame!.Save(Path.Combine(dir, $"{kind.ToString().ToLowerInvariant()}-{theme}.png"));
            }
            window.Content = null;
            window.Close();
        }

        var host = new Window { Width = 1100, Height = 170, Content = view };
        host.Show();
        view.Kind = InstrumentPanelKind.Fretboard;
        // The fretboard's drawing scale depends on the pane's laid-out size, so lay out before asking for a fret's centre.
        host.UpdateLayout();
        var at = view.FretCentre(lit[0].StringIndex, lit[0].Fret);
        host.MouseDown(new Point(at.X, at.Y), MouseButton.Left);
        host.MouseUp(new Point(at.X, at.Y), MouseButton.Left);   // a fretboard click enters its note on release, as WPF does
        Assert.Equal(new InstrumentNotePick(lit[0].Midi, lit[0].StringIndex, lit[0].Fret), picked);

        view.Kind = InstrumentPanelKind.Drums;
        host.UpdateLayout();
        var grid = PercussionKeyMapRules.GridFor(view.Bounds.Width, view.Bounds.Height);
        var snare = PercussionKeyMapRules.CellRect(grid, 38 - PercussionKeyMapRules.FirstNote);
        host.MouseDown(new Point(snare.X + snare.Width / 2, snare.Y + snare.Height / 2), MouseButton.Left);
        Assert.Equal(38, picked?.Midi);
        Assert.Null(view.Pick(new UiPoint(2, view.Bounds.Height - 1)));
        Assert.Null(view.Pick(new UiPoint(2, 20)));
        Assert.Null(view.Pick(new UiPoint(20, 2)));

        view.Kind = InstrumentPanelKind.Keyboard;
        var white = view.Pick(new UiPoint(1100 / 2.0, 140));
        Assert.NotNull(white);
        Assert.True(KeyboardGeometryRules.IsWhite(white!.Value.Midi));
        Assert.Null(view.Pick(new UiPoint(-5, 40)));
        host.Content = null;
        host.Close();
    }

    [AvaloniaFact]
    public void StripBuildsWithThePreviewScore()
    {
        var score = new ScorePreviewView();
        var strip = new InstrumentPanelStrip(score);
        var window = new Window { Width = 1100, Height = 400, Content = strip };
        window.Show();
        // No kind buttons and no hint text: the kind follows the track, and the pane has the Scales button and the close X only.
        Assert.DoesNotContain(strip.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text is { } text && text.Contains("Click a fret"));
        Assert.DoesNotContain(strip.GetLogicalDescendants().OfType<Button>(), b => b.Content is string);
        Assert.Single(strip.GetLogicalDescendants().OfType<Button>(), b => b.Name == "ScalesButton");
        Assert.Single(strip.GetLogicalDescendants().OfType<Button>(), b => b.Name == "InstrumentHideButton");
        window.Content = null;
        window.Close();
    }

    [AvaloniaFact]
    public void FretboardFitsItsPaneWidthAfterAWiderMeasure()
    {
        var strip = new InstrumentPanelStrip(new ScorePreviewView());
        // Measured at the window width first, then arranged in a narrower pane, as the dock does.
        strip.Measure(new Size(1320, 400));
        foreach (var width in new[] { 1320.0, 600.0 })
        {
            strip.Arrange(new Rect(0, 0, width, 400));
            var panel = strip.Panel;
            Assert.True(panel.Bounds.Width <= width + 0.5, $"panel {panel.Bounds.Width} wider than the pane {width}");
            var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0), new UiRect(0, 0, panel.Bounds.Width, panel.Bounds.Height));
            Assert.True(layout.Board.Right <= width, $"last fret line {layout.Board.Right} outside the pane {width}");
        }
    }

    /// <summary>At a wide, short pane the strip's height cap is where the drawn board stops growing: the board's bottom plus its padding, or the
    /// natural height the legend column needs when that is taller (the board with its real proportions is then centred in it), in pane DIPs.</summary>
    [AvaloniaFact]
    public void PaneMaxHeightAtAWideShortPaneEndsAtTheDrawnBoardBottom()
    {
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = DemoSongFactory.Create() });
        var strip = new InstrumentPanelStrip(score);
        strip.Refresh();
        strip.Measure(new Size(1000, 400));
        strip.Arrange(new Rect(0, 0, 1000, 400));
        var cap = strip.MaxHeight;
        strip.Arrange(new Rect(0, 0, 1000, cap));
        var panel = strip.Panel;
        var scale = panel.FretboardScale();
        var drawnBottom = (panel.FretboardLayout().Board.Bottom + FretboardGeometryRules.BottomPad) * scale;
        var naturalHeight = FretboardScaleRules.NaturalHeight(6, 1.0) * scale;
        // The board sits centred in the natural height, so up to a few DIPs of that height may stay above and below it.
        Assert.True(System.Math.Abs(System.Math.Max(drawnBottom, naturalHeight) - cap) <= 4,
            $"cap {cap:F1} does not end at the drawn board's bottom plus padding {drawnBottom:F1} or the natural height {naturalHeight:F1}");
    }

    /// <summary>A keyboard track in a 2000-wide pane: the strip's cap ends at the drawn keys' bottom plus their inset (no empty band under the keys),
    /// and a guitar in the same pane gets the fretboard's limits again.</summary>
    [AvaloniaFact]
    public void KeyboardPaneCapEndsAtTheDrawnKeysAndFretboardLimitsReturn()
    {
        const double width = 2000;
        var song = DemoSongFactory.Create();
        var track = song.Tracks[0];
        track.Kind = TrackKind.Keys;
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = song });
        var strip = new InstrumentPanelStrip(score);
        strip.Refresh();
        strip.Measure(new Size(width, 400));
        strip.Arrange(new Rect(0, 0, width, 400));
        Assert.Equal(InstrumentPanelKind.Keyboard, strip.Panel.Kind);
        var cap = strip.MaxHeight;
        strip.Arrange(new Rect(0, 0, width, cap));
        var keysBottom = strip.Panel.KeysArea().Bottom + KeyboardPaneSizeRules.KeyInset;
        Assert.True(System.Math.Abs(keysBottom - cap) <= 4, $"cap {cap:F1} does not end at the drawn keys' bottom plus inset {keysBottom:F1}");
        Assert.Equal(KeyboardPaneSizeRules.MinPaneHeight, strip.MinHeight);

        track.Kind = TrackKind.Guitar;
        strip.Refresh();
        Assert.Equal(InstrumentPanelKind.Fretboard, strip.Panel.Kind);
        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Stretch, strip.Panel.VerticalAlignment);
        var strings = track.StringTunings.Count > 0 ? track.StringTunings.Count : 6;
        var padding = FretboardGeometryRules.TopPad + FretboardGeometryRules.BottomPad;
        var fretboardCap = System.Math.Min(FretboardSizeRules.GapCapPaneHeight(strings, padding, width, InstrumentPanelView.BoardLimits), strip.Panel.WidthLimitedPaneHeight(width));
        Assert.Equal(fretboardCap, strip.MaxHeight, 1e-6);
        Assert.Equal(FretboardSizeRules.MinPaneHeight(strings, padding), strip.MinHeight, 1e-6);
    }

    // Sounding now (1000-1800, tagged H/P), next (2000, tagged L.R.), upcoming (3000, tagged L.R.)
    // and recently released (0-400) at song time 1200: the preview toggle's on/off pair.
    private static NoteEvent PreviewNote(double onset, double duration, int stringIndex, int fret, int midi, string? technique = null) =>
        new() { OnsetMs = onset, DurationMs = duration, StringIndex = stringIndex, Fret = fret, Midi = midi, Technique = technique };

    private static NoteEvent[] PreviewNotes() => new[]
    {
        PreviewNote(0, 400, 1, 1, 62),
        PreviewNote(1000, 800, 0, 3, 67, "H/P"),
        PreviewNote(2000, 300, 2, 2, 64, "L.R."),
        PreviewNote(3000, 300, 4, 5, 69, "L.R."),
    };

    private static NoteEvent[] DrumPreviewNotes() => new[]
    {
        PreviewNote(0, 400, 0, 0, 36),
        PreviewNote(1000, 800, 0, 0, 38),
        PreviewNote(2000, 300, 0, 0, 42),
        PreviewNote(3000, 300, 0, 0, 46),
    };

    private static InstrumentPanelView PreviewPanel(InstrumentPanelKind kind, bool previewOn)
    {
        var settings = new AppSettings();
        settings.Editing.PreviewNotesEnabled = previewOn;
        var panel = new InstrumentPanelView { Kind = kind, Look = FretboardSettingsBinding.Look.From(settings) };
        panel.Show(DemoSongFactory.Create().Tracks[0], System.Array.Empty<InstrumentLitNotes.Lit>());
        panel.Measure(new Size(1100, 170));
        panel.Arrange(new Rect(0, 0, 1100, 170));
        return panel;
    }

    [AvaloniaFact]
    public void PreviewOffFretboardShowsOnlyNowAndRecent()
    {
        var on = PreviewPanel(InstrumentPanelKind.Fretboard, previewOn: true);
        on.ShowPlayback(PreviewNotes(), 1200);
        var onDraw = new InstrumentFretNumberTests.RecordingDrawContext();
        on.Draw(onDraw, light: false);
        Assert.Contains(onDraw.Texts, t => t.Text == "next");
        Assert.Contains(onDraw.Texts, t => t.Text == "upcoming");
        Assert.Contains(onDraw.Texts, t => t.Text == "L.R.");
        Assert.Contains(64, on.LitMidis());
        Assert.Contains(69, on.LitMidis());

        var off = PreviewPanel(InstrumentPanelKind.Fretboard, previewOn: false);
        off.ShowPlayback(PreviewNotes(), 1200);
        Assert.Equal(new[] { 67, 62 }, off.LitMidis().ToArray());
        var offDraw = new InstrumentFretNumberTests.RecordingDrawContext();
        off.Draw(offDraw, light: false);
        var labels = offDraw.Texts.Select(t => t.Text).ToList();
        Assert.Contains("now", labels);
        Assert.Contains("recent", labels);
        Assert.DoesNotContain("next", labels);
        Assert.DoesNotContain("upcoming", labels);
        // The sounding note keeps its tag; the next/upcoming let-ring tags are never drawn.
        Assert.Contains(offDraw.Texts, t => t.Text == "H/P");
        Assert.DoesNotContain(offDraw.Texts, t => t.Text == "L.R.");
    }

    [AvaloniaFact]
    public void PreviewOffKeyboardLightsOnlyNowAndRecent()
    {
        var next = AvaloniaPalette.Get("InstrumentNext", false);
        var accent = AvaloniaPalette.Get("InstrumentAccent", false);

        var on = PreviewPanel(InstrumentPanelKind.Keyboard, previewOn: true);
        on.ShowPlayback(PreviewNotes(), 1200);
        Assert.Equal(new[] { 67, 64, 69, 62 }, on.LitMidis().ToArray());
        var onDraw = new InstrumentFretNumberTests.RecordingDrawContext();
        on.Draw(onDraw, light: false);
        Assert.Contains(onDraw.Rects, r => r.Fill == next);

        var off = PreviewPanel(InstrumentPanelKind.Keyboard, previewOn: false);
        off.ShowPlayback(PreviewNotes(), 1200);
        Assert.Equal(new[] { 67, 62 }, off.LitMidis().ToArray());
        var offDraw = new InstrumentFretNumberTests.RecordingDrawContext();
        off.Draw(offDraw, light: false);
        // Neither the next colour nor the upcoming accent (at any fade) lights a key.
        Assert.DoesNotContain(offDraw.Rects, r => r.Fill == next);
        Assert.DoesNotContain(offDraw.Rects, r => r.Fill is { } fill && fill.R == accent.R && fill.G == accent.G && fill.B == accent.B);
    }

    [AvaloniaFact]
    public void PreviewOffDrumMapShowsOnlyNowAndRecent()
    {
        // The drum map never previews, whatever the toggle holds.
        foreach (var previewOn in new[] { true, false })
        {
            var panel = PreviewPanel(InstrumentPanelKind.Drums, previewOn);
            panel.ShowPlayback(DrumPreviewNotes(), 1200);
            Assert.Equal(new[] { 38, 36 }, panel.LitMidis().ToArray());
            panel.Draw(new InstrumentFretNumberTests.RecordingDrawContext(), light: false);
        }
    }

    [AvaloniaFact]
    public void LiveToggleOffClearsStalePreviewInEveryView()
    {
        // The menu and the Settings row both push a new look through the binding (SettingsChanged reaches the
        // main strip and every Band card); the panel drops the look-ahead lists at once, before the next frame fills.
        var offSettings = new AppSettings();
        offSettings.Editing.PreviewNotesEnabled = false;
        var offLook = FretboardSettingsBinding.Look.From(offSettings);
        Assert.Equal(-1, offLook.PreviewHorizon);
        foreach (var kind in new[] { InstrumentPanelKind.Fretboard, InstrumentPanelKind.Keyboard, InstrumentPanelKind.Drums })
        {
            var panel = PreviewPanel(kind, previewOn: true);
            panel.ShowPlayback(kind == InstrumentPanelKind.Drums ? DrumPreviewNotes() : PreviewNotes(), 1200);
            panel.Look = offLook;
            var lit = panel.LitMidis().ToArray();
            Assert.DoesNotContain(kind == InstrumentPanelKind.Drums ? 42 : 64, lit);
            Assert.DoesNotContain(kind == InstrumentPanelKind.Drums ? 46 : 69, lit);
            Assert.Contains(kind == InstrumentPanelKind.Drums ? 38 : 67, lit);
            if (kind != InstrumentPanelKind.Fretboard) continue;
            var draw = new InstrumentFretNumberTests.RecordingDrawContext();
            panel.Draw(draw, light: false);
            var labels = draw.Texts.Select(t => t.Text).ToList();
            Assert.DoesNotContain("next", labels);
            Assert.DoesNotContain("upcoming", labels);
        }
    }
}
