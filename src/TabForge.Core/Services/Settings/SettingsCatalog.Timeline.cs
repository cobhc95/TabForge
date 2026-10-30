using TabForge.Models;

namespace TabForge.Services;

// Owns: the Timeline part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> TimelineRows(AppSettings s)
    {
        var g = s.General!;
        var a = s.Appearance!;
        var fv = s.Follow!;
        var timeline = s.Timeline!;
        return new List<SettingDescriptor>
        {
            // Timeline & sections
            Bool(Timeline, "Sections", "timeline.similarcolours", "Same colour for similar sections", v => timeline.MatchSimilarSectionColours = v, () => timeline.MatchSimilarSectionColours,
                "Sections with the same base name (Verse 1, Verse 2, Chorus x2) share a colour. Changing it in the section editor updates that group.", "section colour similar verse chorus match same"),
            Bool(Timeline, "Sections", "timeline.brackets", "Show section brackets", v => timeline.ShowSectionBrackets = v, () => timeline.ShowSectionBrackets,
                "Draw [ ] brackets around the section being played or edited.", "section brackets indicator highlight outline"),
            Bool(Timeline, "Sections", "timeline.names", "Show section names", v => timeline.ShowSectionNames = v, () => timeline.ShowSectionNames,
                "Show titles inside section blocks.", "section title marker label"),
            Bool(Timeline, "Timeline", "appearance.arrangement", "Show the arrangement overview", v => a.ShowArrangementOverview = v, () => a.ShowArrangementOverview,
                "Show the track arrangement overview.", "timeline arrangement overview panel"),
            Bool(Timeline, "Ruler", "timeline.numbers", "Show bar numbers in the ruler",v => timeline.ShowBarNumbers = v, () => timeline.ShowBarNumbers,
                "Show measure numbers in the timeline ruler.", "bar measure ruler number"),
            Number(Timeline, "Sections", "follow.sectionglow", "Section glow intensity", v => fv.SectionGlowIntensity = v / 100, () => fv.SectionGlowIntensity * 100, 0, 100,
                "How strong the highlight is on the playing section and the one under the mouse.", "section glow active hover", "%", 1, 0),
            Number(Timeline, "Sections", "appearance.sectionbracket", "Section bracket thickness", v => a.SectionBracketThickness = v, () => a.SectionBracketThickness, 1, 24,
                "How thick the bracket around the active section is, in pixels.", "active bracket width", "px"),
            Bool(Timeline, "Sections", "timeline.confirmdelete", "Confirm section deletion", v => g.ConfirmDeleteSection = v, () => g.ConfirmDeleteSection,
                "Ask before removing the section and its musical content from every track.", "confirm destructive remove"),
            // Timeline appearance: the four toggles that used to live only in the timeline's right-click menu.
            Bool(Timeline, "Timeline appearance", "timeline.individualnotes", "Show the notes in each track lane", v => { timeline.ShowIndividualNotes = v; if (v) timeline.ShowContinuousLine = false; }, () => timeline.ShowIndividualNotes,
                "Draw every note as a small mark in its track's lane. Turning this on turns off the continuous line.",
                "timeline individual notes marks lane draw appearance"),
            Bool(Timeline, "Timeline appearance", "timeline.continuousline", "Show one continuous line per track lane", v => { timeline.ShowContinuousLine = v; if (v) timeline.ShowIndividualNotes = false; }, () => timeline.ShowContinuousLine,
                "Draw each track's music as one continuous line instead of separate notes. Turning this on turns off the individual notes.",
                "timeline continuous line blocks lane draw appearance"),
            Bool(Timeline, "Timeline appearance", "timeline.hideemptygrid", "Hide the grid in empty bars", v => timeline.HideEmptyGrid = v, () => timeline.HideEmptyGrid,
                "Leave out the bar lines in bars that have no notes, so the timeline only shows where there is music.",
                "timeline grid empty bars hide lines appearance"),
            Bool(Timeline, "Timeline appearance", "timeline.tracklines", "Show lines between tracks", v => timeline.ShowTrackLines = v, () => timeline.ShowTrackLines,
                "Draw a thin line between track rows in the timeline and the track list. Off: rows are told apart by their tints and a faint gap.",
                "timeline track lines separator row divider between tracks horizontal"),
            Bool(Timeline, "Timeline appearance", "timeline.shadeselected", "Shade the selected track", v => timeline.ShadeSelectedTrack = v, () => timeline.ShadeSelectedTrack,
                "A soft grey overlay over the selected track's lane in the timeline and its row in the track list. Off: only the track list marks the selected track.",
                "timeline selected track shade grey overlay highlight lane row"),
            Bool(Timeline, "Timeline appearance", "timeline.barglow", "Soft glow on bars that have notes", v => timeline.BarGlow = v, () => timeline.BarGlow,
                "A faint glow around bars that contain notes. Turn it off for a flatter, plainer timeline.",
                "timeline subtle bar glow soft outline appearance"),
            Choice(Timeline, "Timeline appearance", "timeline.playheadstyle", "Playback position marker", v => timeline.PlayheadStyle = PlayheadStyles.Normalize(v), () => PlayheadStyles.Normalize(timeline.PlayheadStyle),
                PlayheadStyles.All,
                "How the playback position shows on the timeline. Line is the white vertical line. Bar marker is a small dark square in the current bar of the selected track (it moves bar by bar). Both shows the line and the marker.",
                "timeline playhead playback position marker line bar square cursor style"),
            Bool(Timeline, "Track list", "timeline.trackgroups", "Show tracks in groups in new songs", v => timeline.ShowGroupsInNewSongs = v, () => timeline.ShowGroupsInNewSongs,
                "New songs list their tracks under a header per group (guitars, basses, drums...), which can be collapsed. For the open song use the track list's right-click menu or the Mixer (Groups in track list).",
                "track list groups headers collapse guitars basses drums mixer group tracks new song"),
            Bool(Timeline, "Track list", "timeline.addtracklane", "Show the Add-track lane", v => timeline.ShowAddTrackLane = v, () => timeline.ShowAddTrackLane,
                "Show a strip labelled Add track under the last track, across the track list and the timeline. Click or double-click it to add a track, or drop an audio or MIDI file on it to get an audio/MIDI track. The + Track button next to the transport stays either way.",
                "add track lane strip new audio instrument audio midi drop plus button"),
            Bool(Timeline, "Track list", "timeline.mastertrack", "Show master track", v => timeline.ShowMasterTrack = v, () => timeline.ShowMasterTrack,
                "Show an overview lane labelled Master above the first track, across the track list and the timeline. It has one thin line per track, filled in the bars where that track plays. It is a picture only: it has no controls and is not a track. Also in the track list's right-click menu.",
                "master track lane overview summary activity lines top arrangement"),
            Bool(Timeline, "Track list", "timeline.mastertrackcolours", "Master track lines use track colours", v => timeline.MasterTrackColours = v, () => timeline.MasterTrackColours,
                "On: each line takes the colour of its track. Off: every line uses the master track colour below.", "master track lane lines colour track color overview", dependsOn: "timeline.mastertrack"),
            Colour(Timeline, "Track list", "timeline.mastertrackcolour", "Master track colour", v => timeline.MasterTrackColour = v, () => timeline.MasterTrackColour,
                "The one colour of every line when the lines do not use track colours. Leave at the default to follow the theme's text colour.", "master track lane colour color overview", dependsOn: "timeline.mastertrack"),
            Int(Timeline, "Track list", "timeline.mastertrackopacity", "Master track strength", v => timeline.MasterTrackOpacityPercent = v, () => timeline.MasterTrackOpacityPercent, 5, 50,
                "How strongly the master track's lines show.", "master track lane opacity transparency strength overview", unit: "%", dependsOn: "timeline.mastertrack"),
            Bool(Timeline, "Clip lanes", "timeline.removeemptylanes", "Remove empty clip lanes automatically", v => timeline.AutoRemoveEmptyLanes = v, () => timeline.AutoRemoveEmptyLanes,
                "When a lane under a track has no clips left (you moved, cut or deleted the last one), remove it and close the lanes below it. Turn it off to keep empty lanes until you remove them yourself. A track that is armed for recording keeps its lanes.",
                "clip lanes empty remove delete auto close takes audio midi tidy"),
            Bool(Timeline, "Dragging", "timeline.draganimation", "Animate section dragging", v => timeline.SectionDragAnimation = v, () => timeline.SectionDragAnimation,
                "Animate sections moving aside while dragging.", "drag transition animation"),
        };
    }
}
