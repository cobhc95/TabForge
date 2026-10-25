// Owns: the hotkey rows the feature modules add to the catalogue (Band view, video, export, Keyboard mode and its pop-out, compact mode,
//   live input, the preset manager, the FX chain window), each after its anchor row, in the order the modules had them.
// Does not own: the ids (FeatureHotkeyIds, FxChainIds), the commands and handlers (the feature modules, MainWindow.Commands.cs), the presets.
// Tests: no dedicated test class.
using TabForge.Services.Features;

namespace TabForge.Services;

public static partial class HotkeyCatalog
{
    /// <summary>The feature rows with the catalogue row each one follows. Band rows follow one another, so each anchors on the row before it.</summary>
    private static (HotkeyAction Action, string AfterId)[] FeatureRows() => new (HotkeyAction Action, string AfterId)[]
    {
        (new HotkeyAction(FeatureHotkeyIds.BandToggleTrackRow, HotkeyCatalog.CategoryView, "Band view: show / hide the selected track's row", "", "Add the selected track's row to the Band view, or take it out (the same as clicking its pill at the top of the Band view)."), "View.BandView"),
        (new HotkeyAction(FeatureHotkeyIds.BandRowsMore, HotkeyCatalog.CategoryView, "Band view: more rows per screen", "", "Show one more Band view row on the screen at once (1 to 5, 3 by default); further rows scroll."), FeatureHotkeyIds.BandToggleTrackRow),
        (new HotkeyAction(FeatureHotkeyIds.BandRowsFewer, HotkeyCatalog.CategoryView, "Band view: fewer rows per screen", "", "Show one fewer Band view row on the screen at once (1 to 5, 3 by default), so each row is taller."), FeatureHotkeyIds.BandRowsMore),
        (new HotkeyAction(FeatureHotkeyIds.BandCycleLaneContent, HotkeyCatalog.CategoryView, "Band view: lane content (Tab / Notation / Both)", "", "Switch what the Band view lanes show: the tab, the notation, or both."), FeatureHotkeyIds.BandRowsFewer),
        (new HotkeyAction(FeatureHotkeyIds.BandCycleLaneLayout, HotkeyCatalog.CategoryView, "Band view: lane layout (Vertical / Horizontal)", "", "Switch the Band view lanes between lines stacked down the lane (vertical) and one line that slides sideways (horizontal)."), FeatureHotkeyIds.BandCycleLaneContent),
        (new HotkeyAction(FeatureHotkeyIds.BandCycleInstrumentSize, HotkeyCatalog.CategoryView, "Band view: instrument size", "", "Switch the Band view instruments between the full neck (or keyboard), the first 12 frets, and a small keyboard."), FeatureHotkeyIds.BandCycleLaneLayout),
        (new HotkeyAction(FeatureHotkeyIds.BandToggleSmoothFollow, HotkeyCatalog.CategoryView, "Band view: smooth / page follow", "", "Switch the Band view lanes between following like the score (its Follow settings) and the Band view own smooth or page-by-page follow."), FeatureHotkeyIds.BandCycleInstrumentSize),
        (new HotkeyAction(FeatureHotkeyIds.BandResetRowHeights, HotkeyCatalog.CategoryView, "Band view: reset row heights", "", "Give every Band view row the shared height again (undoes rows resized one by one)."), FeatureHotkeyIds.BandToggleSmoothFollow),
        (new HotkeyAction(FeatureHotkeyIds.BandToggleFretboardAbove, HotkeyCatalog.CategoryView, "Band view: switch fretboard beside / above tab", "", "Switch every Band view row between the instrument beside its tab and the instrument above its tab at the full row width."), FeatureHotkeyIds.BandResetRowHeights),
        (new HotkeyAction(FeatureHotkeyIds.BandToggleColourFrame, HotkeyCatalog.CategoryView, "Band view: track colour frame", "", "Show or hide the rounded frame and dim glow in the track's colour around every Band view row."), FeatureHotkeyIds.BandToggleFretboardAbove),
        (new HotkeyAction(FeatureHotkeyIds.ExportVideo, HotkeyCatalog.CategoryFile, "Export video (MP4)", "", "Export the song as an MP4 video of the score, its instrument and the Band view, with the full audio mix, in 1080p or 4K at 30, 60 or 120 frames per second."), "File.Render"),
        (new HotkeyAction(FeatureHotkeyIds.RecordVideo, HotkeyCatalog.CategoryTransport, "Record video", "Ctrl+Alt+V", "Record the screen and the sound to an MP4 (press again to finish; playback keeps playing or stays stopped)."), "Transport.Record"),
        (new HotkeyAction(FeatureHotkeyIds.SaveAsTemplate, HotkeyCatalog.CategoryFile, "Save as template", "", "Save a copy of the active score as a template in the templates folder."), "File.NewFromTemplate"),
        (new HotkeyAction(FeatureHotkeyIds.ExportPdf, HotkeyCatalog.CategoryFile, "Export PDF", "", "Export the engraved score (notation and tab) as a PDF file."), "File.CancelImport"),
        (new HotkeyAction(FeatureHotkeyIds.ExportMusicXml, HotkeyCatalog.CategoryFile, "Export MusicXML", "", "Export the song as uncompressed MusicXML (.musicxml): a part per track with notation and a tab staff."), FeatureHotkeyIds.ExportPdf),
        (new HotkeyAction(FeatureHotkeyIds.ExportMidi, HotkeyCatalog.CategoryFile, "Export MIDI", "", "Export the song as a standard MIDI file (File > Export MIDI)."), FeatureHotkeyIds.ExportMusicXml),
        (new HotkeyAction(FeatureHotkeyIds.ExportAscii, HotkeyCatalog.CategoryFile, "Export ASCII tab", "", "Export the tablature as a plain-text ASCII tab file (File > Export ASCII tab)."), FeatureHotkeyIds.ExportMidi),
        (new HotkeyAction(FeatureHotkeyIds.ExportGuitarPro, HotkeyCatalog.CategoryFile, "Export compatible .gp file", "", "Write a compatible .gp copy for other programs. If the song uses something that file cannot hold, you choose first: keep a full TabForge copy, export the compatible file only, or cancel."), FeatureHotkeyIds.ExportAscii),
        (new HotkeyAction(FeatureHotkeyIds.LearnMode, HotkeyCatalog.CategoryView, "Show / hide Keyboard mode (experimental)", "Ctrl+Alt+L",
            "Show or hide the Keyboard mode pane: the notes of the selected track fall onto a key strip in time with playback (same as View > Keyboard mode (experimental))."), FeatureHotkeyIds.BandToggleColourFrame),
        (new HotkeyAction(FeatureHotkeyIds.LearnToggleWait, HotkeyCatalog.CategoryView, "Keyboard mode: wait for the right notes", "Ctrl+Alt+W",
            "Turn on or off the pause at each chord in Keyboard mode until its notes are pressed (the Preferences setting of the same name)."), FeatureHotkeyIds.LearnMode),
        (new HotkeyAction(FeatureHotkeyIds.LearnWaitSkip, HotkeyCatalog.CategoryView, "Keyboard mode: skip the awaited chord", "Ctrl+Alt+Q",
            "Give up the chord the song is waiting for (a miss) and carry on."), FeatureHotkeyIds.LearnToggleWait),
        (new HotkeyAction(FeatureHotkeyIds.LearnLookAheadLonger, HotkeyCatalog.CategoryView, "Keyboard mode: more seconds ahead (shorter notes)", "Ctrl+Alt+OemPlus",
            "Show one more second of notes above the keys in Keyboard mode: the notes get shorter and you see further ahead (the + of its control bar)."), FeatureHotkeyIds.LearnWaitSkip),
        (new HotkeyAction(FeatureHotkeyIds.LearnLookAheadShorter, HotkeyCatalog.CategoryView, "Keyboard mode: fewer seconds ahead (longer notes)", "Ctrl+Alt+OemMinus",
            "Show one second fewer of notes above the keys in Keyboard mode: the notes get longer and easier to read (the - of its control bar)."), FeatureHotkeyIds.LearnLookAheadLonger),
        (new HotkeyAction(FeatureHotkeyIds.LearnPopout, HotkeyCatalog.CategoryView, "Keyboard mode: view in its own window", "Ctrl+Alt+O",
            "Open the falling-notes view in its own window, or bring it back (Keyboard mode on; F11 in the window toggles full screen, Esc leaves it)."), FeatureHotkeyIds.LearnWaitSkip),
        (new HotkeyAction(FeatureHotkeyIds.CompactMode, HotkeyCatalog.CategoryView, "Compact mode", "Ctrl+Alt+C",
            "Hide every track row and show the master track lane at twice its height, so the score and the other panes get the room; again brings the tracks back (same as View > Compact mode)."), FeatureHotkeyIds.LearnPopout),
        (new HotkeyAction(FeatureHotkeyIds.LiveInput, HotkeyCatalog.CategoryTransport, "Live input", "",
            "Hear your instrument (the chosen audio input) through the live FX chain and the master, without a track and without recording; again turns it off. One for the whole program: it stays on across tabs and windows."), "Transport.Record"),
        (new HotkeyAction(FeatureHotkeyIds.PresetManager, HotkeyCatalog.CategoryView, "Preset manager", "",
            "Open the preset manager: every saved plug-in preset, FX chain, container, MIDI chain, MIDI processor preset and drum map in one window, to rename, copy, reorder, delete and search (same as Tools > Preset manager)."), "Tools.Tuner"),
        (new HotkeyAction(FxChainIds.AddPlugin, HotkeyCatalog.CategoryFxChain, "Add plug-in", "", "Add a plug-in to the end of this FX chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.RemovePlugin, HotkeyCatalog.CategoryFxChain, "Remove plug-in", "", "Remove the selected plug-ins from this FX chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.Save, HotkeyCatalog.CategoryFxChain, "Save FX chain", "Ctrl+S", "Save this FX chain as a chain file in the chains folder."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.Load, HotkeyCatalog.CategoryFxChain, "Load FX chain", "Ctrl+O", "Load a saved FX chain, replacing this chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.AddToEnd, HotkeyCatalog.CategoryFxChain, "Add FX chain to the end", "", "Load a saved FX chain and add its plug-ins after the ones in this chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.Clear, HotkeyCatalog.CategoryFxChain, "Clear FX chain", "", "Remove every plug-in and container from this FX chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.SaveForHosts, HotkeyCatalog.CategoryFxChain, "Save FX chain for other hosts", "", "Write this chain as a .RfxChain file other hosts can load."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.NewContainer, HotkeyCatalog.CategoryFxChain, "New container", "", "Add an empty container behind the focused row in this FX chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.GroupSelected, HotkeyCatalog.CategoryFxChain, "Put selected in a new container", "Ctrl+G", "Move the selected rows into a new container."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.Dissolve, HotkeyCatalog.CategoryFxChain, "Dissolve container", "Ctrl+Shift+G", "Remove the focused container; its plug-ins stay in the chain."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.Rename, HotkeyCatalog.CategoryFxChain, "Rename container", "F2", "Type a new name for the focused container."), "View.Fullscreen"),
        (new HotkeyAction(FxChainIds.DeleteContainer, HotkeyCatalog.CategoryFxChain, "Delete container with its plug-ins", "", "Remove the selected containers and every plug-in inside them."), "View.Fullscreen"),
    };

    /// <summary>Inserts every feature row after its anchor row; a missing anchor fails at start-up.</summary>
    private static void AddFeatureRows(List<HotkeyAction> all)
    {
        foreach (var (action, afterId) in FeatureRows())
        {
            var at = all.FindIndex(a => a.Id == afterId);
            if (at < 0) throw new InvalidOperationException($"Hotkey '{action.Id}': anchor '{afterId}' not found.");
            all.Insert(at + 1, action);
        }
    }
}
