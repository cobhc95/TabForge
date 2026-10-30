namespace TabForge.Services.Features;

// Owns: the ids of the hotkey rows the feature modules add to the catalogue (the rows are in HotkeyCatalog.Features.cs), and the ids of the FX chain window's commands.
// Does not own: the rows themselves (HotkeyCatalog.Features.cs), the commands (the feature modules) or their handlers.
// Tests: no dedicated test class.
public static class FeatureHotkeyIds
{
    public const string LearnMode = "View.LearnMode", LearnToggleWait = "Learn.ToggleWait", LearnWaitSkip = "Learn.WaitSkip",
        LearnLookAheadLonger = "Learn.LookAheadLonger", LearnLookAheadShorter = "Learn.LookAheadShorter", LearnPopout = "View.LearnPopout",
        CompactMode = "View.CompactMode", LiveInput = "Transport.LiveInput", PresetManager = "Tools.PresetManager",
        ExportVideo = "File.ExportVideo", RecordVideo = "Transport.RecordVideo",
        SaveAsTemplate = "File.SaveAsTemplate", ExportPdf = "File.ExportPdf", ExportMusicXml = "File.ExportMusicXml",
        ExportMidi = "File.ExportMidi", ExportAscii = "File.ExportAscii", ExportGuitarPro = "File.ExportGuitarPro",
        ExportGp5 = "File.ExportGp5", ExportGp4 = "File.ExportGp4", ExportGp3 = "File.ExportGp3";

    public const string BandToggleTrackRow = "Band.ToggleTrackRow", BandRowsMore = "Band.RowsMore", BandRowsFewer = "Band.RowsFewer",
        BandCycleLaneContent = "Band.CycleLaneContent", BandCycleLaneLayout = "Band.CycleLaneLayout", BandCycleInstrumentSize = "Band.CycleInstrumentSize",
        BandToggleSmoothFollow = "Band.ToggleSmoothFollow", BandResetRowHeights = "Band.ResetRowHeights", BandToggleFretboardAbove = "Band.ToggleFretboardAbove",
        BandToggleColourFrame = "Band.ToggleColourFrame";

    /// <summary>The Band view rows in catalogue order: each one is a command the Band view runs.</summary>
    public static string[] BandViewRowIds() => new[]
    {
        BandToggleTrackRow, BandRowsMore, BandRowsFewer, BandCycleLaneContent, BandCycleLaneLayout,
        BandCycleInstrumentSize, BandToggleSmoothFollow, BandResetRowHeights, BandToggleFretboardAbove, BandToggleColourFrame,
    };
}

public static class FxChainIds
{
    public const string AddPlugin = "FxChain.AddPlugin", RemovePlugin = "FxChain.RemovePlugin", Save = "FxChain.Save", Load = "FxChain.Load",
        AddToEnd = "FxChain.AddToEnd", Clear = "FxChain.Clear", SaveForHosts = "FxChain.SaveForHosts", NewContainer = "FxChain.NewContainer",
        GroupSelected = "FxChain.GroupSelected", Dissolve = "FxChain.Dissolve", Rename = "FxChain.Rename", DeleteContainer = "FxChain.DeleteContainer",
        CutPlugins = "FxChain.Cut", CopyPlugins = "FxChain.Copy", PastePlugins = "FxChain.Paste", DuplicatePlugins = "FxChain.Duplicate", SelectAllPlugins = "FxChain.SelectAll", ShowGenericControls = "FxChain.ShowGenericControls",
        ActiveOn = "FxChain.ActiveOn";

    /// <summary>The commands that act on the plug-in list: a key runs them only while the list has the focus (the plug-in editor, the options and the text boxes keep their own keys).</summary>
    public static bool NeedsListFocus(string id) => id is RemovePlugin or CutPlugins or CopyPlugins or PastePlugins or DuplicatePlugins or SelectAllPlugins or ShowGenericControls or ActiveOn;
}
