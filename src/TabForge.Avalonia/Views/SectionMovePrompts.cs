using System;
using System.Threading.Tasks;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the questions and remembered settings a section drag or stretch on the timeline may need, as one object the window gives the timeline input
//   controller (TimelineInputController.Prompts): audio crossing a moved section, closing a non-empty gap, stretching a section past the next one, and
//   stretching it backwards. Each setting is read at each drop; a ticked Remember stores the answer.
// Does not own: the drag and stretch rules (TimelineInputController, SectionStretch in Core), the prompt windows, or the settings file (the window's
//   ClipEditFinisher fills this from MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.SectionAudioMoveTests, SectionGapCloseTests, SectionStretchTests.
public sealed class SectionMovePrompts
{
    /// <summary>The "when a moved section cuts through audio" setting (EditingSettings.SectionMoveAudio), read at each drop; null asks.</summary>
    public Func<string>? AudioSetting { get; init; }

    /// <summary>Stores the answer a ticked "Remember my choice" gives (Split or Leave) in the settings.</summary>
    public Action<string>? AudioRemember { get; init; }

    /// <summary>Asks what to do with audio crossing a section being moved (null answer = cancel the move). Without it crossing audio is left where it is and nothing is asked.</summary>
    public Func<SectionAudioCrossing, Task<SectionAudioAnswer?>>? AudioPrompt { get; init; }

    /// <summary>Asks how to close a gap that holds something when a section is dragged left into it (null answer = Cancel, the section snaps back). Without it a non-empty gap is not closed.</summary>
    public Func<int, int, GapContents, Task<GapCloseMode?>>? GapClosePrompt { get; init; }

    /// <summary>The "when stretching a section past the next one" setting (EditingSettings.SectionStretch), read at each release; null asks.</summary>
    public Func<string>? StretchSetting { get; init; }

    /// <summary>Stores the choice a ticked "Remember my choice" gives in the settings.</summary>
    public Action<string>? StretchRemember { get; init; }

    /// <summary>Asks how to stretch (null answer = Cancel, nothing changes). Without it a stretch only closes the gap up to the next section.</summary>
    public Func<SectionStretchQuestion, Task<SectionStretchAnswer?>>? StretchPrompt { get; init; }

    /// <summary>The "when stretching a section backwards" setting (EditingSettings.SectionStretchBack), read at each release; null asks.</summary>
    public Func<string>? StretchBackSetting { get; init; }

    /// <summary>Stores the backward choice a ticked "Remember my choice" gives in the settings.</summary>
    public Action<string>? StretchBackRemember { get; init; }

    /// <summary>Asks how to stretch a section back (null answer = Cancel). Without it a backward stretch only closes the gap up to the previous section.</summary>
    public Func<SectionStretchBackQuestion, Task<SectionStretchBackAnswer?>>? StretchBackPrompt { get; init; }
}
