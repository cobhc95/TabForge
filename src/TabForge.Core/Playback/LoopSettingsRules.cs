using System;
using System.Globalization;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the loop settings' rules with no toolkit types: the bars the loop button covers when no area was picked (the current section, bar or the
//     whole song), the loop behaviour the engine reads from the audio settings, and reading and showing the loop settings' boxes (number of loops,
//     speed trainer percents).
// Does not own: the settings values (AudioSettings), the loop region (SelectionModel.Loop), the wrap itself (PlaybackEngine), the popup controls.
// Tests: no dedicated test class.
// Threads: pure functions; any thread.
public static class LoopSettingsRules
{
    /// <summary>The loop scope setting values, as the settings file stores them.</summary>
    public const string SectionScope = "Section";
    public const string BarScope = "Bar";
    public const string SongScope = "Song";

    /// <summary>The text the number-of-loops box shows for "forever" (a count of 0).</summary>
    public const string EndlessText = "∞";

    /// <summary>The largest number of loops the box accepts.</summary>
    public const int MaxLoopCount = 9999;
    /// <summary>The speed trainer's tempo range, in percent of the song's tempo.</summary>
    public const int MinTrainerPercent = 10;
    public const int MaxTrainerPercent = 200;
    /// <summary>The largest tempo increase per loop, in percent.</summary>
    public const int MaxTrainerStep = 100;

    /// <summary>
    /// The bars the loop button covers when no area was picked, by the scope setting: the bar, the whole song, or the section around
    /// <paramref name="bar"/> (a bar before the first section loops up to it; a bar in the gap after a resized section loops the gap).
    /// </summary>
    public static (int Start, int End) DefaultArea(SongProject project, string scope, int bar)
    {
        var lastBar = Math.Max(0, BarRangeEditor.MaxMeasures(project) - 1);
        bar = Math.Clamp(bar, 0, lastBar);
        switch (scope)
        {
            case BarScope:
                return (bar, bar);
            case SongScope:
                return (0, lastBar);
            default:
                var markers = SectionLayout.Sorted(project);
                var index = markers.FindLastIndex(marker => marker.MeasureIndex <= bar);
                if (index < 0) return markers.Count > 0 ? (0, Math.Max(0, markers[0].MeasureIndex - 1)) : (0, lastBar);
                var sectionEnd = SectionLayout.End(markers, index, lastBar + 1);
                if (bar >= sectionEnd)
                    return (sectionEnd, Math.Clamp((index + 1 < markers.Count ? markers[index + 1].MeasureIndex : lastBar + 1) - 1, sectionEnd, lastBar));
                return (markers[index].MeasureIndex, Math.Clamp(sectionEnd - 1, markers[index].MeasureIndex, lastBar));
        }
    }

    /// <summary>The loop behaviour the engine reads at every wrap, from the audio settings.</summary>
    public static PlaybackEngine.LoopBehaviour Behaviour(AudioSettings audio) =>
        new(audio.LoopCount, audio.LoopCountInEachLoop, audio.LoopSpeedTrainer, audio.LoopTrainerFrom, audio.LoopTrainerTo, audio.LoopTrainerStep);

    /// <summary>The number-of-loops box text: the count, or the endless sign for 0.</summary>
    public static string CountText(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : EndlessText;

    /// <summary>A typed number of loops: a whole number clamped to 0..MaxLoopCount; anything else (empty, ∞) is 0, forever.</summary>
    public static int ParseCount(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? Math.Clamp(count, 0, MaxLoopCount) : 0;

    /// <summary>A typed trainer tempo ("80" or "80 %"), clamped to the trainer range; unreadable text keeps <paramref name="fallback"/>.</summary>
    public static int ParsePercent(string? text, int fallback) =>
        int.TryParse(text?.Trim().TrimEnd('%').Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, MinTrainerPercent, MaxTrainerPercent) : fallback;

    /// <summary>A typed tempo step per loop, clamped to 0..MaxTrainerStep; unreadable text keeps <paramref name="fallback"/>.</summary>
    public static int ParseStep(string? text, int fallback) =>
        int.TryParse(text?.Trim().TrimEnd('%').Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, MaxTrainerStep) : fallback;

    /// <summary>The loop settings' area line: the area's bars (one-based), "(loop off)" when it is not looping, or that no area is picked.</summary>
    public static string AreaText(bool hasArea, bool looping, int startBar, int endBar) =>
        hasArea || looping ? $"Loop area: bars {startBar + 1}-{endBar + 1}{(looping ? "" : " (loop off)")}" : "No loop area selected";

    /// <summary>The status line after the loop button: the bars looped, or that looping is off.</summary>
    public static string StatusText(bool looping, int startBar, int endBar) => looping ? $"Looping bars {startBar + 1}-{endBar + 1}" : "Loop off";
}
