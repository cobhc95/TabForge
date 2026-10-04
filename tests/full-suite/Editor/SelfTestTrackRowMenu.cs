using System.Windows;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>The track row's right-click menu (every track kind), the whole-track clipboard and the track-row hotkeys.</summary>
public static partial class SelfTest
{
    private sealed class TrackFlowHost : ITrackClipboardHost
    {
        public TrackFlowHost(DocumentSession document) => Document = document;
        public DocumentSession Document { get; }
        public List<string> Asked { get; } = new();
        public bool Answer { get; set; } = true;
        public int Changed { get; private set; }
        public int Selected { get; private set; } = -1;
        public string Status { get; private set; } = "";
        public bool ConfirmDeleteTrack(string trackName) { Asked.Add(trackName); return Answer; }
        public void TracksChanged(int selectIndex, string status) { Changed++; Selected = selectIndex; Status = status; }
        public void SetStatus(string text) => Status = text;
    }

    private static void TestTrackRowMenu()
    {
        var settings = new HotkeySettings();
        string Key(string id) => HotkeyCatalog.DisplayAll(settings, id);

        // Menu contents: the same Cut / Copy / Paste / Duplicate / Delete / Rename / Colour / Properties for every kind, audio adds Convert.
        var instrument = TrackRowMenus.Build(new TrackRowMenuState(false, true, true, "#F61A16"), Key);
        var audio = TrackRowMenus.Build(new TrackRowMenuState(true, false, true, "#7CC4F2"), Key);
        var common = new[] { "Cut", "Copy", "Paste (after this track)", "Duplicate", "Delete…", "Rename", "Colour", "Properties…" };
        Check("track row menu: an instrument track offers cut, copy, paste, duplicate, delete, rename, colour, properties",
            instrument.Where(i => !i.IsSeparator).Select(i => i.Header).SequenceEqual(common), string.Join(" | ", instrument.Where(i => !i.IsSeparator).Select(i => i.Header)));
        Check("track row menu: an audio track has the same items plus Convert to instrument track",
            audio.Where(i => !i.IsSeparator).Select(i => i.Header).SequenceEqual(common.Append("Convert to instrument track…")));
        Check("track row menu: Paste is disabled with nothing copied, enabled with a copied track",
            !audio.First(i => i.Id == TrackRowMenus.Paste).Enabled && instrument.First(i => i.Id == TrackRowMenus.Paste).Enabled);
        Check("track row menu: Cut and Delete are off for the last track",
            TrackRowMenus.Build(new TrackRowMenuState(false, false, false, ""), Key).Where(i => i.Id is TrackRowMenus.Cut or TrackRowMenus.Delete).All(i => !i.Enabled));
        Check("track row menu: the shortcuts are the live bindings (none typed into the menu)",
            instrument.First(i => i.Id == TrackRowMenus.Copy).Shortcut == Key("TrackRow.Copy") && Key("TrackRow.Copy").Length > 0
            && instrument.First(i => i.Id == TrackRowMenus.Properties).Shortcut == Key("Track.Properties"));
        var rebound = new HotkeySettings();
        rebound.Bindings["TrackRow.Copy"] = "Ctrl+Alt+K";
        Check("track row menu: a rebound key shows in the menu",
            TrackRowMenus.Build(new TrackRowMenuState(false, true, true, ""), id => HotkeyCatalog.DisplayAll(rebound, id)).First(i => i.Id == TrackRowMenus.Copy).Shortcut.Contains("K"));
        Check("track row menu: the Colour submenu marks the track's colour",
            TrackRowMenus.Build(new TrackRowMenuState(false, true, true, TrackControlWidgets.TrackColourPalette[0].Hex), Key).First(i => i.Id is null && i.Header == "Colour").Children!.Count(c => c.Checked) == 1);

        // The right-click on the row asks the host for this menu (all kinds), instead of opening Track properties.
        var song = OrderedSong();
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 1000, Height = 600 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var asked = -1; var properties = 0;
            panel.TrackRowMenuRequested += (_, index) => asked = index;
            panel.TrackOptionsRequested += (_, _) => properties++;
            Check("track row menu: right-click on an instrument row asks for the row menu, not the properties window",
                panel.RightClickTrackRow(0) && asked == 0 && properties == 0);

            // The opened menu stays open: nothing queued by the right-click may take the focus from it afterwards.
            System.Windows.Controls.ContextMenu? opened = null;
            panel.TrackRowMenuRequested += (_, _) =>
            {
                opened = new System.Windows.Controls.ContextMenu { PlacementTarget = panel };
                opened.Items.Add(new System.Windows.Controls.MenuItem { Header = "Copy" });
                opened.IsOpen = true;
            };
            panel.RightClickTrackRow(1);
            PumpUi();
            Check("track row menu: the menu is still open after the right-click's queued work has run", opened is { IsOpen: true });
            if (opened is not null) opened.IsOpen = false;
        }
        finally { window.Close(); }

        // Clipboard flow: copy, cut, paste, duplicate, delete each one undo step; confirmation before delete.
        var document = DocumentSession.FromProject(DoSong(3), null);
        document.MarkClean();
        var host = new TrackFlowHost(document);
        var flow = new TrackClipboardFlow(host, new ClipboardService(null), new TrackController());
        var tracks = document.Project.Tracks;
        tracks[1].Mute = true; tracks[1].Volume = 77; tracks[1].ColorHex = "#35B954";
        tracks[1].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 3, MidiValue = tracks[1].PitchOf(1, 3) });
        tracks[1].AudioClips.Add(CseClip("clip", 0));
        var before = DoHash(document);

        flow.Copy(1);
        Check("track clipboard: copy changes nothing in the song", DoHash(document) == before && document.Undo.UndoCount == 0 && flow.CanPaste);
        flow.Paste(1);
        var pasted = document.Project.Tracks.Count == 4 ? document.Project.Tracks[2] : null;
        Check("track clipboard: paste inserts after the selected track with a unique name, one undo step",
            pasted is not null && pasted.Name == "T2 copy" && document.Undo.UndoCount == 1 && host.Selected == 2, pasted?.Name);
        Check("track clipboard: the copy keeps notation, clips, mix, colour and gets its own ids",
            pasted is not null && pasted.Mute && pasted.Volume == 77 && pasted.ColorHex == "#35B954" && pasted.AudioClips.Count == 1
            && pasted.Measures[0].Cells[0].Notes.Count == 1 && pasted.Id != document.Project.Tracks[1].Id && pasted.AudioClips[0].Id != document.Project.Tracks[1].AudioClips[0].Id
            && !ReferenceEquals(pasted.Measures, document.Project.Tracks[1].Measures));
        DocumentEdits.Undo(document);
        Check("track clipboard: one undo removes the pasted track", document.Project.Tracks.Count == 3 && DoHash(document) == before);
        flow.Paste(2);
        flow.Paste(2);
        Check("track clipboard: pasting twice numbers the names", document.Project.Tracks.Count == 5 && document.Project.Tracks.Select(t => t.Name).Distinct().Count() == 5 && document.Project.Tracks.Any(t => t.Name == "T2 copy 2"));
        DocumentEdits.Undo(document); DocumentEdits.Undo(document);

        flow.Duplicate(0);
        Check("track clipboard: duplicate inserts right after the track, one undo step",
            document.Project.Tracks.Count == 4 && document.Project.Tracks[1].Name == "T1 copy" && document.Undo.UndoCount == 1);
        DocumentEdits.Undo(document);

        var undoBefore = document.Undo.UndoCount;
        flow.Cut(1);
        Check("track clipboard: cut removes the track, one undo step, and pastes back",
            document.Project.Tracks.Count == 2 && document.Undo.UndoCount == undoBefore + 1 && flow.CanPaste && host.Asked.Count == 0);
        flow.Paste(0);
        Check("track clipboard: the cut track pastes back with its name and settings", document.Project.Tracks.Count == 3 && document.Project.Tracks[1].Name == "T2" && document.Project.Tracks[1].Volume == 77);
        DocumentEdits.Undo(document); DocumentEdits.Undo(document);
        Check("track clipboard: undo restores the song after cut and paste", DoHash(document) == before);

        host.Answer = false;
        flow.Delete(1);
        Check("track delete: the confirmation names the track and a refusal deletes nothing", host.Asked.SequenceEqual(new[] { "T2" }) && document.Project.Tracks.Count == 3 && document.Undo.UndoCount == 0);
        host.Answer = true;
        flow.Delete(1);
        Check("track delete: after yes the track is gone in one undo step", document.Project.Tracks.Count == 2 && document.Undo.UndoCount == 1 && host.Asked.Count == 2);
        DocumentEdits.Undo(document);
        Check("track delete: undo restores the track", document.Project.Tracks.Count == 3 && DoHash(document) == before);
        var single = DocumentSession.FromProject(DoSong(1), null);
        var singleHost = new TrackFlowHost(single);
        new TrackClipboardFlow(singleHost, new ClipboardService(null), new TrackController()).Delete(0);
        Check("track delete: the last track is never deleted and nothing is asked", single.Project.Tracks.Count == 1 && singleHost.Asked.Count == 0);

        // Focus routing: track-row keys only act while a track row has the focus and no clip is active.
        var map = HotkeyCatalog.BuildMap(settings, trackRowContext: true);
        var global = HotkeyCatalog.BuildMap(settings);
        Check("track hotkeys: Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D and Delete are track-row commands",
            map["Ctrl+C"] == "TrackRow.Copy" && map["Ctrl+X"] == "TrackRow.Cut" && map["Ctrl+V"] == "TrackRow.Paste" && map["Ctrl+D"] == "TrackRow.Duplicate" && map["Delete"] == "TrackRow.Delete");
        Check("track hotkeys: the global map keeps Ctrl+C as the score's Copy and has no track-row command",
            global["Ctrl+C"] == "Edit.Copy" && !global.Values.Any(HotkeyCatalog.IsTrackRowAction));
        Check("track hotkeys: Ctrl+C on a focused track row copies the track", TrackClipboardFlow.Route(true, false, map, "Ctrl+C") == "TrackRow.Copy");
        Check("track hotkeys: with the score or timeline focused the key is not a track command (it copies notes)", TrackClipboardFlow.Route(false, false, map, "Ctrl+C") is null);
        Check("track hotkeys: an active clip keeps its own keys", TrackClipboardFlow.Route(true, true, map, "Delete") is null);
        Check("track hotkeys: the contexts do not share a collision set", HotkeyCatalog.SameContext("TrackRow.Copy", "TrackRow.Cut") && !HotkeyCatalog.SameContext("TrackRow.Copy", "Edit.Copy") && !HotkeyCatalog.SameContext("TrackRow.Copy", "Clip.Copy"));
    }
}
