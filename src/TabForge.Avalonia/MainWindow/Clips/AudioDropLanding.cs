using System;
using System.Globalization;
using System.IO;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: where on the timeline a file dragged in from outside lands in time: the pointer x to a song second with the clip drag's snapping and its repeat-pass
//   reading, the "Bar 17.3 · 0:32.4" readout of that second, and the cheap length probe of a dragged file (audio header or MIDI file).
//   The drag preview and the drop both call StartSec, so the ghost shows exactly the second the clip lands on.
// Does not own: the drag events (MainWindowAudioDrop), the plan and the edit (AudioDropPlanner), the snap targets (TimelineClipSnap) or the ghost drawing (TimelineAudioClips).
// Tests: TabForge.Avalonia.Tests.AudioDropLandingTests.
internal static class AudioDropLanding
{
    /// <summary>The largest MIDI file the probe reads (a score file is a few hundred KB; this stops a mistaken pick).</summary>
    private const long MaxMidiBytes = 16L * 1024 * 1024;

    /// <summary>
    /// The song second a file dropped at timeline x <paramref name="x"/> starts at. The pointer is read at the repeat pass nearest the playhead, then the start snaps like a
    /// clip edge (grid, other clips, playhead; Alt or snapping off: not) and is read along that same pass, so a drop over a repeat never jumps to another pass.
    /// </summary>
    public static double StartSec(TimelineView view, double x, bool altHeld)
    {
        if (view.BarCount == 0) return 0;
        var near = Finite(view.SecondsOfXNear(x, PlayheadSec(view)));
        var snappedX = TimelineClipSnap.SnapX(view, x, null, altHeld, out var distancePx);
        return double.IsFinite(distancePx) ? Finite(view.SecondsOfXAlong(snappedX, near)) : near;
    }

    private static double PlayheadSec(TimelineView view) =>
        view.PlayheadBar >= 0 && view.PlayheadBar < view.BarCount ? AudioClipTimelineRules.SecondsAtBar(view.Song, view.PlayheadBar, view.PlayheadFraction) : 0;

    private static double Finite(double sec) => double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0 ? 0 : sec;

    /// <summary>"Bar 17.3 · 0:32.4": the bar and quarter-note beat of <paramref name="sec"/>, and the song time as minutes:seconds. Past the song's end the bar counts on from the last one.</summary>
    public static string Readout(SongProject song, double sec)
    {
        sec = Finite(sec);
        var (bar, fraction) = AudioClipTimelineRules.BarAtSeconds(song, sec);
        var beats = Math.Max(1, MusicTime.BarSlots(song, Math.Min(bar, Math.Max(0, AudioClipTimelineRules.BarCount(song) - 1))) / MusicTime.SlotsPerQuarter);
        var beat = Math.Clamp((int)Math.Floor(fraction * beats), 0, beats - 1) + 1;
        var tenths = (long)Math.Round(sec * 10);
        return string.Create(CultureInfo.InvariantCulture, $"Bar {bar + 1}.{beat} · {tenths / 600}:{tenths % 600 / 10.0:00.0}");
    }

    /// <summary>The length in seconds of a dragged file: a MIDI file through the clip import, anything else through <paramref name="audioLengthOf"/> (the decoder's header read). 0 when it cannot be read.</summary>
    public static double LengthOf(string path, Func<string, double> audioLengthOf)
    {
        if (!FileTypes.IsMidi(Path.GetExtension(path))) return audioLengthOf(path);
        try
        {
            if (new FileInfo(path).Length > MaxMidiBytes) return 0;
            return MidiClipImport.Read(File.ReadAllBytes(path), "")?.SourceLengthSec ?? 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
