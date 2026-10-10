namespace TabForge.Services;

// Owns: the "Band view" rows of the settings catalogue (Timeline page); BandFeatureModule hands them to the catalogue.
// Does not own: page layout (BandFeatureModule.Layout) or the stored values (TimelineSettings.Band).
// Tests: TestPreferencesCatalog, TestBandSettings, TestFeatureModuleContributions.
public static partial class SettingsCatalog
{
    internal static List<SettingDescriptor> BandRows(AppSettings s)
    {
        var timeline = s.Timeline ??= new TimelineSettings();
        var band = timeline.Band ??= new BandSettings();
        return new List<SettingDescriptor>
        {
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
