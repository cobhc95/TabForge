using System;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The master lane between the section strip and track 1: one thin line per track, filled over the bars where the track plays, in the track's
/// colour at <see cref="Opacity"/>, on the board background with faint bar lines. Geometry comes from <see cref="MasterLaneRules"/>.
/// Owns: drawing the lane and the "does this track play in this bar" test. Does not own: whether the lane shows, its height (TimelineView),
/// its header cell (TrackHeaderColumn), audio clips in a line and the compact mode of the WPF lane.
/// Tests: TabForge.Avalonia.Tests.MasterLaneTests.
/// </summary>
public static class MasterLaneStrip
{
    public const double Opacity = 0.25;
    public const int MinOpacityPercent = 5, MaxOpacityPercent = 50;

    /// <summary>How the lines are coloured: by track colour, or one flat <paramref name="Colour"/> (null: the theme's text colour); <paramref name="Opacity"/> 0..1.</summary>
    public readonly record struct Look(bool ByTrack, UiColor? Colour, double Opacity)
    {
        public static Look Default { get; } = new(true, null, MasterLaneStrip.Opacity);

        /// <summary>The look the Timeline settings ask for; the default grey colour follows the theme, any other colour is used as chosen.</summary>
        public static Look From(TimelineSettings settings)
        {
            var chosen = Color.TryParse(settings.MasterTrackColour, out var c) && !string.Equals(settings.MasterTrackColour, new TimelineSettings().MasterTrackColour, StringComparison.OrdinalIgnoreCase)
                ? new UiColor(c.A, c.R, c.G, c.B) : (UiColor?)null;
            return new Look(settings.MasterTrackColours, chosen, Math.Clamp(settings.MasterTrackOpacityPercent, MinOpacityPercent, MaxOpacityPercent) / 100.0);
        }
    }

    /// <summary>The track has notes in the bar, or the bar repeats the previous one(s).</summary>
    public static bool Plays(TrackModel track, int bar)
    {
        if (bar < 0 || bar >= track.Measures.Count) return false;
        var measure = track.Measures[bar];
        return measure.SimileOneBar || measure.SimileTwoBar || TimelineActivityRules.Build(measure, track.Kind == TrackKind.Drums).Notes > 0;
    }

    /// <summary>
    /// <see cref="Plays"/> for every bar of every track, once per timeline reload: the drawing then reads it instead of evaluating the rules (and
    /// allocating a build) on every body paint, including every scroll tick. Same rows, same simile handling.
    /// </summary>
    public static bool[][] Activity(SongProject song)
    {
        var activity = new bool[song.Tracks.Count][];
        for (var t = 0; t < activity.Length; t++)
        {
            var track = song.Tracks[t];
            var plays = new bool[track.Measures.Count];
            for (var bar = 0; bar < plays.Length; bar++) plays[bar] = Plays(track, bar);
            activity[t] = plays;
        }
        return activity;
    }

    /// <summary>Draws the lane at <paramref name="top"/> over <paramref name="boundaries"/> (bar edges) from the prebuilt <paramref name="activity"/>; nothing when <paramref name="height"/> is 0.</summary>
    public static void Draw(IDrawContext d, Func<string, UiColor> ink, SongProject song, double[] boundaries, double top, double height, double width, bool[][] activity, Look? lookOrNull = null)
    {
        if (height <= 0) return;
        var look = lookOrNull ?? Look.Default;
        var edge = ink("TimelineLine");
        d.Rect(ink("TimelineRow"), null, 0, top, width, height);
        var bars = boundaries.Length - 1;
        var grid = new UiStroke(edge with { A = (byte)(edge.A * 0.55) }, 1);
        for (var bar = 0; bar <= bars; bar++) d.Line(grid, boundaries[bar] - 0.5, top, boundaries[bar] - 0.5, top + height);
        var line = MasterLaneRules.LineHeight(height, song.Tracks.Count);
        var gap = MasterLaneRules.LineGap(line);
        for (var t = 0; t < song.Tracks.Count; t++)
        {
            var track = song.Tracks[t];
            var played = activity[t];
            var fill = !look.ByTrack ? look.Colour ?? ink("TextPrimary")
                : Color.TryParse(track.ColorHex, out var c) ? new UiColor(c.A, c.R, c.G, c.B) : ink("InstrumentAccent");
            fill = fill with { A = (byte)Math.Round(fill.A * look.Opacity) };
            var y = top + 2 + t * line;
            var run = -1;
            for (var bar = 0; bar <= bars; bar++)
            {
                if (bar < bars && bar < played.Length && played[bar]) { if (run < 0) run = bar; continue; }
                if (run < 0) continue;
                d.Rect(fill, null, boundaries[run], y, Math.Max(1, boundaries[bar] - 1 - boundaries[run]), Math.Max(0.5, line - gap));
                run = -1;
            }
        }
        d.Line(new UiStroke(edge, 1), 0, top + height - 0.5, width, top + height - 0.5);
    }
}
