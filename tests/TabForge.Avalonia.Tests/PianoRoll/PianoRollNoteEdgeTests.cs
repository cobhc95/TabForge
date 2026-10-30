using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the plain piano roll note's edge: a thin outline drawn inside each note so two touching notes on one row read as separate blocks.
//   In both themes the boundary columns differ from the fill and carry the edge over it; a selected note keeps its selected outline and a muted
//   note stays hollow and dashed, next to the same edge.
// Does not own: the note fill (PianoRollSurface.NoteFill), the velocity line (PianoRollViewTests) or the literal-size guards (ArchitectureGuardTests).
// Tests: PianoRollSurface.DrawNote, the PianoRollNoteEdge palette token and DialogMetrics.PianoRollNoteEdge.
public sealed class PianoRollNoteEdgeTests
{
    private const int Tolerance = 3;

    /// <summary>Two C4 notes back to back on the MIDI track (beats 1 and 2 at 120 bpm): the second is muted when asked.</summary>
    private static SongDocument TouchingNotes(bool muteSecond = false)
    {
        var project = SongDocument.BlankProject();
        project.Tracks[0].AudioClips.Add(new AudioClip
        {
            Name = "take", StartSec = 0, SourceLengthSec = 8, FileLengthSec = 8,
            Notes = new List<ClipNote> { new(0.5, 0.5, 60, 90), new(1.0, 0.5, 60, 90, muteSecond) },
        });
        return new SongDocument(() => null) { Project = project };
    }

    private static (PianoRollView View, Window Window) Show(SongDocument document, bool light)
    {
        var view = new PianoRollView(new PianoRollHost { Document = () => document, Snap = () => new SnapSettings() });
        var window = new Window { Width = 900, Height = 600, Content = view, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    /// <summary>The surface painted alone into a headless bitmap, origin at the surface's top-left (so pixels are surface coordinates).</summary>
    private static RenderTargetBitmap Render(PianoRollView view)
    {
        view.UpdateLayout();
        view.Surface.InvalidateVisual();
        var size = view.Surface.Bounds;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        bitmap.Render(view.Surface);
        return bitmap;
    }

    private static int IndexAt(PianoRollView view, double beat)
    {
        for (var i = 0; i < view.Presenter.Notes.Count; i++)
            if (Math.Abs(view.Presenter.Notes[i].Start - beat) < 1e-6) return i;
        throw new InvalidOperationException($"No note starts at beat {beat}.");
    }

    private static Rect RectAt(PianoRollView view, double beat) => view.Surface.NoteRect(view.Presenter.Notes[IndexAt(view, beat)]);

    private static UiColor Pixel(RenderTargetBitmap bitmap, int x, int y) => AvaloniaDrawContextTests.Pixel(bitmap, x, y);

    /// <summary>Asserts <paramref name="actual"/> is <paramref name="over"/> laid on <paramref name="under"/> (its alpha), within rounding.</summary>
    private static void AssertOver(UiColor under, UiColor over, UiColor actual)
    {
        var alpha = over.A / 255.0;
        static int Mix(byte below, byte above, double a) => (int)Math.Round(below * (1 - a) + above * a);
        Assert.True(Math.Abs(actual.R - Mix(under.R, over.R, alpha)) <= Tolerance, $"red {actual} over {under} with {over}");
        Assert.True(Math.Abs(actual.G - Mix(under.G, over.G, alpha)) <= Tolerance, $"green {actual} over {under} with {over}");
        Assert.True(Math.Abs(actual.B - Mix(under.B, over.B, alpha)) <= Tolerance, $"blue {actual} over {under} with {over}");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TouchingNotesOnOneRowKeepAnEdgeLineBetweenThem(bool light)
    {
        var (view, window) = Show(TouchingNotes(), light);
        var first = RectAt(view, 1);
        var second = RectAt(view, 2);
        Assert.Equal(first.Right, second.X, 6);   // touching: no gap between them
        var edge = AvaloniaPalette.Get("PianoRollNoteEdge", light);

        using var bitmap = Render(view);
        var y = (int)Math.Floor(first.Y) + 6;   // below the velocity line, clear of the rounded corners
        var fill = Pixel(bitmap, (int)(first.X + first.Width / 2), y);
        var lastColumnOfFirst = Pixel(bitmap, (int)first.Right - 1, y);
        var firstColumnOfSecond = Pixel(bitmap, (int)second.X, y);

        Assert.NotEqual(fill, lastColumnOfFirst);
        Assert.NotEqual(fill, firstColumnOfSecond);
        AssertOver(fill, edge, lastColumnOfFirst);
        AssertOver(fill, edge, firstColumnOfSecond);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedAndMutedNotesKeepTheirOwnLooksNextToTheEdge(bool light)
    {
        var (view, window) = Show(TouchingNotes(muteSecond: true), light);
        var first = RectAt(view, 1);
        var second = RectAt(view, 2);
        var edge = AvaloniaPalette.Get("PianoRollNoteEdge", light);
        var y = (int)Math.Floor(first.Y) + 6;
        var midFirst = (int)(first.X + first.Width / 2);
        var topRow = (int)Math.Floor(first.Y);

        using var plain = Render(view);
        var fill = Pixel(plain, midFirst, y);
        var plainTop = Pixel(plain, midFirst, topRow);
        AssertOver(fill, edge, Pixel(plain, (int)first.Right - 1, y));   // the plain note keeps its edge next to a muted one

        // Muted: hollow and dashed, so its middle shows the row, never the plain fill.
        Assert.NotEqual(fill, Pixel(plain, (int)(second.X + second.Width / 2), y));

        // Selected: the bright outline replaces the plain edge on that note only.
        view.Presenter.Select(new[] { IndexAt(view, 1) });
        using var selected = Render(view);
        Assert.NotEqual(plainTop, Pixel(selected, midFirst, topRow));
        Assert.NotEqual(fill, Pixel(selected, midFirst, y));   // the selected fill and outline, not the plain note
        window.Close();
    }
}
