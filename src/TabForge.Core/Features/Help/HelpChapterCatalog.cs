using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Features.Help;

// Owns: the Help chapter list: each chapter's file key (its embedded resource name), its title and the group it sits in (Start here, Writing music,
//   Playing and tools, Help), the Troubleshooting quick-link target and the chapters offered when a search finds nothing.
// Does not own: the chapter text (the embedded files), parsing (HelpDocument), searching (HelpSearchIndex), drawing (Avalonia HelpWindow).
// Tests: TabForge.Avalonia.Tests.HelpContentTests.

/// <summary>One chapter's catalogue entry. The title repeats the chapter file's '# ' line, so the list can be drawn without reading any chapter.</summary>
public sealed record HelpChapterInfo(string Key, string Title, string Group);

/// <summary>The ordered chapters and their groups.</summary>
public static class HelpChapterCatalog
{
    public const string StartHere = "Start here", WritingMusic = "Writing music", PlayingAndTools = "Playing and tools", HelpGroup = "Help";

    public static IReadOnlyList<HelpChapterInfo> All { get; } = new HelpChapterInfo[]
    {
        new("00-whats-new", "What's new in 0.9.3", StartHere),
        new("01-getting-started", "Getting started", StartHere),
        new("02-interface-tour", "Interface tour", StartHere),
        new("03-files-and-export", "Files and export", StartHere),
        new("04-tracks-and-mixer", "Tracks and mixer", StartHere),
        new("05-writing-notes", "Writing notes", WritingMusic),
        new("06-effects-and-expression", "Effects and expression", WritingMusic),
        new("07-bars-and-structure", "Bars and structure", WritingMusic),
        new("08-rhythm-and-layout", "Rhythm and layout", WritingMusic),
        new("09-playback-and-practice", "Playback and practice", PlayingAndTools),
        new("10-keyboard-and-fretboard-tools", "Keyboard and fretboard tools", PlayingAndTools),
        new("11-composer-and-left-strip", "Composer and left strip", PlayingAndTools),
        new("12-settings-themes-hotkeys", "Settings, themes and hotkeys", HelpGroup),
        new("13-troubleshooting-and-faq", "Troubleshooting and FAQ", HelpGroup),
    };

    /// <summary>The chapter the top-level Troubleshooting link opens.</summary>
    public const int TroubleshootingIndex = 13;

    /// <summary>The chapter shown when F1 opens the Help for the first time.</summary>
    public const int StartIndex = 1;

    /// <summary>The chapters offered when a search finds nothing.</summary>
    public static IReadOnlyList<int> Suggested { get; } = new[] { 1, 13, 9, 5, 12 };

    /// <summary>The group names in display order.</summary>
    public static IEnumerable<string> Groups => All.Select(c => c.Group).Distinct();

    public static IReadOnlyList<string> Titles { get; } = All.Select(c => c.Title).ToArray();

    /// <summary>The index of the chapter with this title (ignoring case), or -1.</summary>
    public static int IndexOfTitle(string title)
    {
        for (var i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Title, title, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
