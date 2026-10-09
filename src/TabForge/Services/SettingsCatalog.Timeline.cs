using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Timeline part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> TimelineRows(AppSettings s)
    {
        var g = s.General!;
        var a = s.Appearance!;
        var fv = s.Follow!;
        var timeline = s.Timeline!;
        var band = timeline.Band ??= new BandSettings();
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
                "Intensity of active and hovered section highlights.", "section glow active hover", "%", 1, 0),
            Number(Timeline, "Sections", "appearance.sectionbracket", "Section bracket thickness", v => a.SectionBracketThickness = v, () => a.SectionBracketThickness, 1, 24,
                "Stroke width of the active section indicator.", "active bracket width", "px"),
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
                "Show a strip labelled Add track under the last track, across the track list and the timeline. Click or double-click it to add a track, or drop an audio or MIDI file on it to get an audio track. The + Track button next to the transport stays either way.",
                "add track lane strip new audio instrument drop plus button"),
            Bool(Timeline, "Clip lanes", "timeline.removeemptylanes", "Remove empty clip lanes automatically", v => timeline.AutoRemoveEmptyLanes = v, () => timeline.AutoRemoveEmptyLanes,
                "When a lane under a track has no clips left (you moved, cut or deleted the last one), remove it and close the lanes below it. Turn it off to keep empty lanes until you remove them yourself. A track that is armed for recording keeps its lanes.",
                "clip lanes empty remove delete auto close takes audio midi tidy"),
            Bool(Timeline, "Dragging", "timeline.draganimation", "Animate section dragging", v => timeline.SectionDragAnimation = v, () => timeline.SectionDragAnimation,
                "Animate sections moving aside while dragging.", "drag transition animation"),

            // Band view
            Choice(Timeline, "Band view", "band.instrumentsize", "Instrument size in rows", v => band.InstrumentSize = BandChoices.NormalizeSize(v), () => BandChoices.NormalizeSize(band.InstrumentSize),
                BandChoices.Sizes, "How much of each instrument a Band view row shows: the full neck (or keyboard), only the first 12 frets, or a small keyboard.",
                "band view instrument size full neck 12 frets keyboard small rows"),
            Choice(Timeline, "Band view", "band.lanecontent", "Lane content", v => band.LaneContent = BandChoices.NormalizeContent(v), () => BandChoices.NormalizeContent(band.LaneContent),
                BandChoices.Contents, "What each Band view lane shows beside its instrument: the tab, the notation, or both.",
                "band view lane tab notation staff both content"),
            Choice(Timeline, "Band view", "band.lanelayout", "Lane layout", v => band.LaneLayout = BandChoices.NormalizeLayout(v), () => BandChoices.NormalizeLayout(band.LaneLayout),
                BandChoices.Layouts, "Vertical: each lane wraps its song into lines that stack down the lane, like the score's vertical scrolling, so a tall row shows several lines. Horizontal: one line that slides sideways.",
                "band view lane layout vertical horizontal wrap lines scroll"),
            Bool(Timeline, "Band view", "band.followscore", "Follow like the score", v => band.FollowLikeScore = v, () => band.FollowsScore,
                "The Band view lanes follow the playback position with the score's own Follow settings (mode, page turns, look-ahead, stop at the end), so there is one place to set it. Off: the lanes use the choice below.", "band view follow score jump page scroll lanes same"),
            Bool(Timeline, "Band view", "band.smoothfollow", "Band view own follow: smooth", v => band.SmoothFollow = v, () => band.SmoothFollow,
                "Only when the Band view does not follow like the score: the lanes slide smoothly with the playback position. Off: they turn a page at a time.", "band view follow smooth page scroll lanes own"),
            Int(Timeline, "Band view", "band.rowsperscreen", "Rows per screen", v => band.RowsPerScreen = v, () => band.RowsPerScreen, 1, 5,
                "How many Band view rows fit the screen at once in a song that has not set its own (1 to 5). Songs remember their own choice.", "band view rows per screen visible count"),
            Number(Timeline, "Band view", "band.lanezoom", "Lane zoom", v => band.LaneZoom = BandChoices.ClampZoom(v), () => BandChoices.ClampZoom(band.LaneZoom), BandChoices.MinLaneZoom, BandChoices.MaxLaneZoom,
                "How large the tab or notation is drawn in every Band view lane, as a multiple of the normal size. Ctrl + mouse wheel over a lane changes it too.", "band view lane zoom size bigger smaller tab notation", step: 0.1, decimals: 1),
            Choice(Timeline, "Band view", "band.playheadline", "Playhead line", v => band.PlayheadLine = BandChoices.NormalizePlayhead(v), () => BandChoices.NormalizePlayhead(band.PlayheadLine),
                BandChoices.PlayheadLines, "How far the green playhead line reaches in a Band view lane: only across the engraved tab or staff, or the full height of the row.",
                "band view playhead line tab only full row green length"),
            Bool(Timeline, "Band view", "band.syncorder", "Keep Band order in sync with the timeline track order", v => band.KeepOrderInSync = v, () => band.KeepOrderInSync,
                "Moving a Band view row also moves the track on the timeline, and moving a track on the timeline moves its Band view row. Off: the Band view keeps its own order.", "band view order sync timeline track reorder"),
        };
    }
}
