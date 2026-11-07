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
    public void LegendActionsHaveIdenticalTypographyAndGeometry()
    {
        var strip = new InstrumentPanelStrip(new ScorePreviewView());
        var window = new Window { Width = 1100, Height = 400, FontSize = 20, Content = strip };
        window.Show();
        try
        {
            var buttons = strip.GetLogicalDescendants().OfType<Button>()
                .Where(b => b.Name is "ScalesButton" or "FretboardCustomizeButton").ToList();
            Assert.Equal(2, buttons.Count);
            var labels = buttons.Select(b => ((StackPanel)b.Content!).Children.OfType<TextBlock>().Single()).ToList();
            Assert.Equal(labels[0].FontSize, labels[1].FontSize);   // one size for both; the legend's unit sets it
            Assert.Equal(labels[0].FontFamily, labels[1].FontFamily);
            Assert.Equal(labels[0].FontWeight, labels[1].FontWeight);
            Assert.Equal(labels[0].FontStyle, labels[1].FontStyle);
            Assert.Equal(buttons[0].Width, buttons[1].Width);
            Assert.Equal(buttons[0].Height, buttons[1].Height);
            Assert.Equal(buttons[0].Padding, buttons[1].Padding);
        }
        finally { window.Close(); }
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
        settings.Editing.PreviewPreviousNotesEnabled = true;
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
        Assert.Contains(onDraw.Texts, t => t.Text == "Next");
        Assert.Contains(onDraw.Texts, t => t.Text == "Upcoming");
        Assert.Contains(onDraw.Texts, t => t.Text == "L.R.");
        Assert.Contains(64, on.LitMidis());
        Assert.Contains(69, on.LitMidis());

        var off = PreviewPanel(InstrumentPanelKind.Fretboard, previewOn: false);
        off.ShowPlayback(PreviewNotes(), 1200);
        Assert.Equal(new[] { 67, 62 }, off.LitMidis().ToArray());
        var offDraw = new InstrumentFretNumberTests.RecordingDrawContext();
        off.Draw(offDraw, light: false);
        var labels = offDraw.Texts.Select(t => t.Text).ToList();
        Assert.Contains("Now", labels);
        Assert.Contains("Recent", labels);
        Assert.DoesNotContain("Next", labels);
        Assert.DoesNotContain("Upcoming", labels);
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
            Assert.DoesNotContain("Next", labels);
            Assert.DoesNotContain("Upcoming", labels);
        }
    }

    /// <summary>The wood is never stretched: every tile keeps the shipped texture's aspect (rosewood Detailed
    /// 2022x778, maple Detailed 2157x729) at narrow, wide and very wide panes, and the tiles cover the board.</summary>
    [Fact]
    public void WoodTilesKeepSourceAspectAtEveryWidth()
    {
        foreach (var (imageWidth, imageHeight) in new[] { (2022.0, 778.0), (2157.0, 729.0) })
        {
            var aspect = imageWidth / imageHeight;
            foreach (var paneWidth in new[] { 800.0, 1600.0, 4000.0 })
            {
                var input = new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0);
                var layout = FretboardGeometryRules.Compute(input, new UiRect(0, 0, paneWidth, 300), limits: InstrumentPanelView.BoardLimits);
                var board = layout.Board;
                var tiles = FretboardWoodTiling.TileCount(board.Width, board.Height, imageWidth, imageHeight);
                Assert.True(tiles >= 1, $"no tile covers the board at pane width {paneWidth}");
                var covered = 0.0;
                for (var i = 0; i < tiles; i++)
                {
                    var tile = FretboardWoodTiling.TileRect(i, board.X, board.Y, board.Width, board.Height, imageWidth, imageHeight);
                    Assert.Equal(aspect, tile.Width / tile.Height, 9);
                    Assert.Equal(board.Height, tile.Height, 9);
                    covered += tile.Width;
                }
                Assert.True(covered >= board.Width, $"tiles cover {covered:F1} of the {board.Width:F1} board at pane width {paneWidth}");
            }
        }
    }

    /// <summary>The neck widens with the string count: 3 to 10 strings give strictly taller boards in the same pane.</summary>
    [Fact]
    public void NeckWidensWithStringCount()
    {
        var heights = new System.Collections.Generic.List<double>();
        for (var strings = 3; strings <= 10; strings++)
        {
            var layout = FretboardGeometryRules.Compute(new FretboardGeometryRules.Input(strings, 24, 24, 1, false, 0),
                new UiRect(0, 0, 1600, 4000), limits: InstrumentPanelView.BoardLimits);
            heights.Add(layout.Board.Height);
        }
        for (var i = 1; i < heights.Count; i++)
            Assert.True(heights[i] > heights[i - 1], $"board for {i + 3} strings ({heights[i]:F1}) is not taller than for {i + 2} ({heights[i - 1]:F1})");
    }

    /// <summary>Fret spacing is 12-TET: each cell is 2^(-1/12) of the previous one, centres sit between their wires,
    /// and the hit test maps each wire midpoint back to its fret.</summary>
    [Fact]
    public void FretCellsNarrowByTwelfthRootOfTwo()
    {
        var input = new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0);
        var content = new UiRect(0, 0, 1600, 300);
        var layout = FretboardGeometryRules.Compute(input, content, limits: InstrumentPanelView.BoardLimits);
        var ratio = System.Math.Pow(2, -1 / 12.0);
        for (var fret = 2; fret <= layout.LastFret; fret++)
        {
            var previous = FretboardGeometryRules.WireX(layout, fret - 1) - FretboardGeometryRules.WireX(layout, fret - 2);
            var cell = FretboardGeometryRules.WireX(layout, fret) - FretboardGeometryRules.WireX(layout, fret - 1);
            Assert.Equal(ratio, cell / previous, 6);
        }
        for (var fret = layout.FirstFret; fret <= layout.LastFret; fret++)
        {
            var centre = (FretboardGeometryRules.WireX(layout, fret - 1) + FretboardGeometryRules.WireX(layout, fret)) / 2;
            Assert.Equal(centre, FretboardGeometryRules.FretX(layout, fret), 9);
            var middle = layout.Board.Y + layout.Board.Height / 2;
            Assert.True(FretboardGeometryRules.HitTest(input, content, new UiPoint(centre, middle), out _, out var hit, limits: InstrumentPanelView.BoardLimits));
            Assert.Equal(fret, hit);
        }
    }

    /// <summary>A 50-step capo drag previews without writing and commits exactly once, with the final fret.</summary>
    [Fact]
    public void CapoDragCommitsOnceOverAFiftyStepDrag()
    {
        var previews = new System.Collections.Generic.List<int>();
        var commits = new System.Collections.Generic.List<int>();
        var drag = new FretboardCapoDrag(2, previews.Add, fret => { commits.Add(fret); return true; });
        for (var i = 0; i < 50; i++) drag.Move(3 + (i % 5));
        Assert.Equal(50, drag.PreviewCount);
        Assert.Equal(0, drag.CommitCount);
        Assert.True(drag.End());
        Assert.Equal(1, drag.CommitCount);
        Assert.Equal(new[] { 3 + (49 % 5) }, commits);
        // A drag released where it started writes nothing: still one undo entry (none) and no sync.
        var idle = new FretboardCapoDrag(4, _ => { }, _ => { Assert.Fail("an unchanged drag must not commit"); return false; });
        Assert.False(idle.End());
        Assert.Equal(0, idle.CommitCount);
    }

    /// <summary>The capo fret clamps to 0..24 on the drag path; an unchanged release commits nothing.</summary>
    [Fact]
    public void CapoFretClampsToZeroThroughTwentyFour()
    {
        Assert.Equal(0, FretboardCapoRules.Clamp(-3));
        Assert.Equal(24, FretboardCapoRules.Clamp(99));
        Assert.Equal(7, FretboardCapoRules.Clamp(7));
        var drag = new FretboardCapoDrag(2, _ => { }, _ => true);
        drag.Move(99);
        Assert.Equal(24, drag.CurrentFret);
        drag.Move(-5);
        Assert.Equal(0, drag.CurrentFret);
    }

    /// <summary>The capo grabs at its fret's centre within the board's span, never at 0 (no image) or off the board;
    /// left of the nut lands on 0 (off).</summary>
    [Fact]
    public void CapoGrabHitsOnlyTheDrawnCapo()
    {
        var input = new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0);
        var content = new UiRect(0, 0, 1600, 300);
        var layout = FretboardGeometryRules.Compute(input, content, limits: InstrumentPanelView.BoardLimits);
        var middle = layout.Board.Y + layout.Board.Height / 2;
        Assert.True(FretboardCapoRules.HitTest(layout, FretboardGeometryRules.FretX(layout, 5), middle, 5));
        Assert.False(FretboardCapoRules.HitTest(layout, FretboardGeometryRules.FretX(layout, 5), middle, 0));
        Assert.False(FretboardCapoRules.HitTest(layout, FretboardGeometryRules.FretX(layout, 5), middle, 25));
        Assert.False(FretboardCapoRules.HitTest(layout, FretboardGeometryRules.FretX(layout, 5), layout.Board.Bottom + 99, 5));
        Assert.Equal(0, FretboardCapoRules.FretAtX(layout, layout.Board.X - 5));
        Assert.Equal(5, FretboardCapoRules.FretAtX(layout, FretboardGeometryRules.FretX(layout, 5)));
    }

    /// <summary>The full capo draws at its fret's 12-TET centre, clipped to the board (never spilling past its alpha edges).</summary>
    [AvaloniaFact]
    public void CapoDrawsAtItsFretOverhangingTheWood()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        track.Capo = 5;
        var view = new InstrumentPanelView();
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        var layout = view.FretboardLayout();
        var wood = FretboardArtist.Wood(layout, bass: false);
        var centre = FretboardGeometryRules.FretX(layout, 5);
        // The bar art has asymmetric transparent margins, so the check is that the drawn bar covers its fret's centre and, like a real capo
        // wrapping the neck, reaches past the wood at the top and the bottom.
        Assert.Contains(draw.Images, image => image.X <= centre && centre <= image.X + image.Width
            && image.Width / image.Height < 1.5 && image.Y <= wood.Y && image.Y + image.Height >= wood.Bottom);
    }

    /// <summary>A partial capo is grabbed only on the strings it covers, and its band stays inside those strings.</summary>
    [AvaloniaFact]
    public void PartialCapoGrabAndBandCoverOnlyItsStrings()
    {
        var input = new FretboardGeometryRules.Input(6, 24, 24, 1, false, 0);
        var content = new UiRect(0, 0, 1600, 300);
        var layout = FretboardGeometryRules.Compute(input, content, limits: InstrumentPanelView.BoardLimits);
        var x = FretboardGeometryRules.FretX(layout, 5);
        const int mask = 0b000111;

        Assert.True(FretboardCapoRules.HitTestPartial(layout, x, FretboardGeometryRules.StringY(layout, 0), 5, mask));
        Assert.True(FretboardCapoRules.HitTestPartial(layout, x, FretboardGeometryRules.StringY(layout, 2), 5, mask));
        Assert.False(FretboardCapoRules.HitTestPartial(layout, x, FretboardGeometryRules.StringY(layout, 3), 5, mask));
        Assert.False(FretboardCapoRules.HitTestPartial(layout, FretboardGeometryRules.FretX(layout, 6), FretboardGeometryRules.StringY(layout, 0), 5, mask));

        var band = FretboardCapoRules.PartialBand(layout, mask);
        Assert.NotNull(band);
        Assert.True(band!.Value.Top <= FretboardGeometryRules.StringY(layout, 0));
        Assert.True(band.Value.Bottom >= FretboardGeometryRules.StringY(layout, 2));
        Assert.True(band.Value.Bottom < FretboardGeometryRules.StringY(layout, 3));
    }

    /// <summary>A partial capo draws at its fret, clipped to the strings it covers (not the whole board).</summary>
    [AvaloniaFact]
    public void PartialCapoDrawsAcrossItsStringsOnly()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        track.Capo = 0;
        track.PartialCapos = new System.Collections.Generic.List<TabForge.Models.PartialCapo> { new() { Fret = 5, StringMask = 0b000111 } };
        var view = new InstrumentPanelView();
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        var draw = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(draw, light: false);
        var layout = view.FretboardLayout();
        var centre = FretboardGeometryRules.FretX(layout, 5);

        Assert.Contains(draw.Images, image => image.X <= centre && centre <= image.X + image.Width && image.Width / image.Height < 1.5);
        var wood = FretboardArtist.Wood(layout, bass: false);
        Assert.Contains(draw.Clips, clip => clip.Y >= wood.Y - 0.001 && clip.Y + clip.Height <= wood.Bottom + 0.001 && clip.Height < wood.Height);
    }

    /// <summary>Sounding strings vibrate for their note's duration only: the sounding string's line moves with the
    /// frame phase while the others and every text stay put (no per-frame allocation, no text changes).</summary>
    [AvaloniaFact]
    public void SoundingStringsVibrateForTheirDurationOnly()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView();
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        view.Measure(new Size(1100, 170));
        view.Arrange(new Rect(0, 0, 1100, 170));
        view.ShowPlayback(new[] { new NoteEvent { OnsetMs = 1000, DurationMs = 800, StringIndex = 1, Fret = 3, Midi = 67 } }, 1200);
        var layout = view.FretboardLayout();
        var board = layout.Board;
        static double? StringLineY(InstrumentFretNumberTests.RecordingDrawContext draw, UiRect board, double stringY)
        {
            // A string is several horizontal lines now (cast shadows below, the body, the glint above): the body is the one nearest its own position.
            double? best = null;
            foreach (var line in draw.LineOps)
                if (line.X1 <= board.X && line.X2 == board.Right && System.Math.Abs(line.Y1 - stringY) < 5 && line.Y1 == line.Y2
                    && (best is null || System.Math.Abs(line.Y1 - stringY) < System.Math.Abs(best.Value - stringY)))
                    best = line.Y1;
            return best;
        }
        view.TestSetVibrationPhase(0);
        var initialOffset = view.StringWobble(1).Offset;
        var still = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(still, light: false);
        view.TestSetVibrationPhase(37);
        var movedOffset = view.StringWobble(1).Offset;
        var moved = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(moved, light: false);
        var sounding = FretboardGeometryRules.StringY(layout, 1);
        var quiet = FretboardGeometryRules.StringY(layout, 4);
        Assert.NotEqual(initialOffset, movedOffset);
        Assert.Equal(StringLineY(still, board, quiet), StringLineY(moved, board, quiet));
        Assert.Equal(still.Texts.Select(t => t.Text), moved.Texts.Select(t => t.Text));
        // Past the note's duration the string rests again.
        view.ShowPlayback(new[] { new NoteEvent { OnsetMs = 1000, DurationMs = 800, StringIndex = 1, Fret = 3, Midi = 67 } }, 2000);
        view.TestSetVibrationPhase(37);
        var rested = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(rested, light: false);
        Assert.Equal(sounding, StringLineY(rested, board, sounding));
    }
}
