using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// The Add-track lane (a labelled strip under the last track, track list and timeline), the audio-track row, the + Track menu, and the editor
/// guards of an audio track (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    private static SongProject AtlSong()
    {
        var p = new SongProject();
        var tracks = new TrackController();
        p.Tracks.Add(tracks.CreateTrack(p, TrackKind.Guitar));
        p.Tracks.Add(tracks.CreateTrack(p, TrackKind.Audio));
        return p;
    }

    private static MouseButtonEventArgs AtlMouse(UIElement source, RoutedEvent ev, MouseButton button = MouseButton.Left) =>
        new(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = ev, Source = source };

    /// <summary>The lane: label, flush under the rows, one row tall, click, drop kinds, the setting, and the audio row.</summary>
    private static void TestAddTrackLane()
    {
        Check("add lane: the label is Add track and the lane is about one row tall", AddTrackLane.Label == "Add track" && Math.Abs(AddTrackLane.Height - ArrangementPanel.DefaultTrackRowHeight) <= 4);
        Check("add lane: a file dropped on it always makes an audio track", AddTrackLane.DropKind == TrackKind.Audio);
        Check("add lane: the setting is on by default", new AppSettings().Timeline.ShowAddTrackLane);

        var song = AtlSong();
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 1000, Height = 500 };   // inside the CI work area (1024x728)
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var lane = panel.AddLaneRow as Border;
            Check("add lane: the track list ends with the lane", lane is not null && lane.Tag as string == "add-lane");
            if (lane is null) return;
            Check("add lane: it is labelled Add track (text and automation name)",
                VisualDescendants<TextBlock>(lane).Any(t => t.Text == "Add track") && System.Windows.Automation.AutomationProperties.GetName(lane) == "Add track" && lane.Focusable);
            var stack = (StackPanel)System.Windows.Media.VisualTreeHelper.GetParent(lane);
            var rowsBottom = panel.TrackRowActualHeights.Sum();
            var laneTop = lane.TranslatePoint(new Point(0, 0), stack).Y;
            Check("add lane: the rows end flush against it", Math.Abs(laneTop - rowsBottom) < 1.0, $"lane top {laneTop}, rows {rowsBottom}");
            var timeline = panel.TimelineForTest;
            var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
            var rows = ArrangementPanel.RowsHeight(song);
            // The lane is the whole empty zone below the last row (at least one row), at several pane sizes.
            // Heights stay inside the work area: Windows clamps a taller window (small CI screens).
            foreach (var h in new[] { 260.0, 500, 700 })
            {
                window.Height = h;
                window.UpdateLayout();
                panel.FlushExtent(); window.UpdateLayout();
                var viewport = stack.ActualHeight > 0 && System.Windows.Media.VisualTreeHelper.GetParent(stack) is ScrollContentPresenter scp ? scp.ActualHeight : panel.ActualHeight - gridTop;
                var expected = Math.Max(AddTrackLane.Height, viewport - rows);
                Check($"add lane: at pane height {h} it fills the room below the rows (at least one row)", Math.Abs(lane.ActualHeight - expected) < 1.5 && lane.ActualHeight >= AddTrackLane.Height - 0.5, $"lane {lane.ActualHeight}, expected {expected}");
                Check($"add lane: at pane height {h} the timeline half is the same height", Math.Abs(timeline.AddLaneHeight - lane.ActualHeight) < 1.5, $"{timeline.AddLaneHeight}");
                var fill = timeline.AddLaneHeight;
                Check($"add lane: at pane height {h} its last pixel is in the lane, the last row is not",
                    timeline.IsInAddLane(new Point(100, gridTop + rows + 5)) && timeline.IsInAddLane(new Point(100, gridTop + rows + fill - 2)) && !timeline.IsInAddLane(new Point(100, gridTop + rows - 5)) && !timeline.IsInAddLane(new Point(100, gridTop + rows + fill + 3)));
            }
            window.Height = 500;
            window.UpdateLayout();
            var laneH = timeline.AddLaneHeight;
            Check("add lane: the timeline reserves the same height under the rows", Math.Abs(timeline.Height - (gridTop + rows + laneH + 2)) < 0.5, $"{timeline.Height}");
            Check("add lane: the panel's preferred height counts one row for it", Math.Abs(panel.PreferredHeight() - (gridTop + rows + AddTrackLane.Height + 2)) < 0.5);

            // Clicking either half asks the host (the prompt).
            var clicks = 0;
            panel.AddTrackLaneClicked += () => clicks++;
            lane.RaiseEvent(AtlMouse(lane, UIElement.MouseLeftButtonUpEvent));
            Check("add lane: a click on the track-list half asks to add a track", clicks == 1);
            timeline.RaiseAddLaneClicked();
            Check("add lane: a click on the timeline half asks to add a track", clicks == 2);
            lane.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(lane)!, 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent, Source = lane });
            Check("add lane: Enter on the focused lane asks too (keyboard)", clicks == 3);

            // A file dropped on the lane makes an AUDIO track, audio or MIDI; below the lane the file's own kind rules.
            timeline.MeasureWidth = 30;
            var inLane = new Point(60, gridTop + rows + 8);
            var deepInLane = new Point(60, gridTop + rows + laneH - 6);
            Check("add lane: a drop far below the label (deep in the zone) plans an audio track too", timeline.DropPreviewAt(new[] { MidiItem("Piano riff", 4) }, deepInLane) is { Valid: true } deep && deep.Plan.NewTrackKind == TrackKind.Audio && timeline.IsInAddLane(deepInLane));
            var deepClicks = 0;
            panel.AddTrackLaneClicked += () => deepClicks++;
            lane.RaiseEvent(AtlMouse(lane, UIElement.MouseLeftButtonUpEvent));
            Check("add lane: a click anywhere on the track-list half acts as the lane", deepClicks == 1);
            var audio = timeline.DropPreviewAt(new[] { AudioItem("Loop", 2) }, inLane);
            var midi = timeline.DropPreviewAt(new[] { MidiItem("Piano riff", 4) }, inLane);
            Check("add lane: an audio file dropped on it plans a new audio track", audio.Valid && audio.Plan.NewTrack && audio.Plan.NewTrackKind == TrackKind.Audio);
            Check("add lane: a MIDI file dropped on it plans a new AUDIO track too", midi.Valid && midi.Plan.NewTrack && midi.Plan.NewTrackKind == TrackKind.Audio, $"{midi.Plan.NewTrackKind}");
            var below = timeline.DropPreviewAt(new[] { MidiItem("Piano riff", 4) }, new Point(60, gridTop + rows + laneH + 10));
            Check("add lane: below the lane a MIDI file also plans an audio track (drop below the tracks)", below.Plan.NewTrackKind == TrackKind.Audio && below.Plan.NewTrack, $"{below.Plan.NewTrackKind}");

            // One zone: hover and drag light up both halves together; the words change while dragging.
            Check("add lane: idle shows the label and nothing else", panel.AddLaneState == "idle" && !timeline.AddLaneHot && !timeline.AddLaneDrag && VisualDescendants<TextBlock>(lane).Any(t => t.Text == "Add track"));
            panel.SimulateAddLaneState(true, false);
            Check("add lane: hover lights the timeline half too", panel.AddLaneState == "hover" && timeline.AddLaneHot);
            panel.SimulateAddLaneState(true, true);
            Check("add lane: drag shows the drop words and the accent outline on the list half, and both halves are in drag state",
                panel.AddLaneState == "drag" && timeline.AddLaneDrag && VisualDescendants<TextBlock>(lane).Any(t => t.Text == AddTrackLane.DropLabel) && lane.BorderThickness.Top >= 2);
            panel.SimulateAddLaneState(false, false);
            Check("add lane: back to idle afterwards", panel.AddLaneState == "idle" && VisualDescendants<TextBlock>(lane).Any(t => t.Text == "Add track") && lane.BorderThickness.Top == 0);

            // The audio row: the waveform icon instead of the instrument icon button.
            var guitarRow = VisualDescendants<Border>(panel).First(b => (System.Windows.Automation.AutomationProperties.GetName(b) ?? "").StartsWith("Track 1:"));
            var audioRow = VisualDescendants<Border>(panel).First(b => (System.Windows.Automation.AutomationProperties.GetName(b) ?? "").StartsWith("Track 2:"));
            bool Picker(Border row) => VisualDescendants<Button>(row).Any(b => (System.Windows.Automation.AutomationProperties.GetName(b) ?? "").StartsWith("Instrument:"));
            bool Waveform(Border row) => VisualDescendants<Border>(row).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Audio track");
            Check("audio row: an instrument track has the instrument icon button, an audio track has none", Picker(guitarRow) && !Picker(audioRow));
            Check("audio row: it shows the waveform icon", Waveform(audioRow));
            Check("audio row: an instrument row has no waveform icon", !Waveform(guitarRow));

            // Right-click on the audio row offers Convert to instrument track.
            var asked = -1;
            panel.ConvertAudioTrackRequested += i => asked = i;
            var menu = panel.AudioRowMenu(1, audioRow);
            var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
            Check("audio row: its menu offers Track properties and Convert to instrument track", headers.SequenceEqual(new[] { "Track properties…", "Convert to instrument track…" }), string.Join(" | ", headers));
            menu.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("audio row: the Convert item asks to convert that track", asked == 1);

            // The setting hides the strip; the timeline and the list give the height back.
            var withLane = panel.PreferredHeight();
            panel.ShowAddTrackLane = false;
            window.UpdateLayout();
            Check("add lane: hidden by the setting there is no lane in the list or the timeline", panel.AddLaneRow is null && !timeline.AddLaneShown && !timeline.IsInAddLane(inLane));
            Check("add lane: hidden, the height shrinks by the lane", Math.Abs(withLane - panel.PreferredHeight() - AddTrackLane.Height) < 0.5);
            Check("add lane: hidden, the space below the rows is plain again (no lane height)", panel.AddLaneFillHeight == 0 && timeline.AddLaneHeight == 0 && Math.Abs(timeline.Height - (gridTop + rows + 2)) < 0.5, $"{timeline.Height}");
            Check("add lane: hidden, a file below the last row still plans an audio track", timeline.DropPreviewAt(new[] { MidiItem("Piano riff", 4) }, inLane).Plan.NewTrackKind == TrackKind.Audio);
            panel.ShowAddTrackLane = true;
            Check("add lane: shown again", panel.AddLaneRow is not null && timeline.AddLaneShown);
        }
        finally { window.Close(); }
        if (Environment.GetEnvironmentVariable("TABFORGE_ADDLANE_PNG") is { Length: > 0 } dir) AddLaneCapture(dir);
    }

    /// <summary>Off-screen pictures of the lane, an audio row and the audio-track score placeholder, light and dark (TABFORGE_ADDLANE_PNG = folder).</summary>
    private static void AddLaneCapture(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        using var alive = KeepAlive();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var appearance = new AppSettings().Appearance;
            ThemeService.ApplyPreset(appearance, theme);
            ThemeService.Apply(appearance);
            VisualTheme.IsLight = theme == "Light";
            var song = AtlSong();
            song.Tracks[1].AudioClips.Add(new AudioClip { File = "x.wav", Name = "Take", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4, Lane = 0 });
            var panel = new ArrangementPanel();
            panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
            var window = new Window { Content = panel, Width = 1100, Height = 330 };
            try
            {
                ShowTestWindow(window);
                window.UpdateLayout(); PumpUi();
                SavePng(window, System.IO.Path.Combine(dir, $"addlane-{theme.ToLowerInvariant()}.png"));
                panel.SimulateAddLaneState(true, false); window.UpdateLayout(); PumpUi();
                SavePng(window, System.IO.Path.Combine(dir, $"addlane-hover-{theme.ToLowerInvariant()}.png"));
                var renders = panel.TimelineForTest.RenderCount;
                panel.SimulateAddLaneState(false, false); panel.SimulateAddLaneState(true, false); panel.SimulateAddLaneState(true, true); PumpUi();
                Check($"add lane ({theme}): hover and drag repaint the overlay only, never the whole timeline", panel.TimelineForTest.RenderCount == renders, $"{renders} -> {panel.TimelineForTest.RenderCount}");
                panel.SimulateAddLaneState(true, true); window.UpdateLayout(); PumpUi();
                SavePng(window, System.IO.Path.Combine(dir, $"addlane-drag-{theme.ToLowerInvariant()}.png"));
                panel.SimulateAddLaneState(false, false);
            }
            finally { window.Close(); }
            var editor = new TabEditorControl { Project = song, SelectedTrackIndex = 1 };
            var host = new Window { Content = editor, Width = 1500, Height = 300 };
            try
            {
                ShowTestWindow(host);
                editor.InvalidateScoreLayout(); editor.InvalidateVisual(); host.UpdateLayout(); PumpUi(); PumpUi();
                Log.Add($"  info  capture: editor track {editor.Track?.Name} audio {editor.Track?.IsAudio} size {editor.ActualWidth}x{editor.ActualHeight}");
                SavePng(host, System.IO.Path.Combine(dir, $"audio-placeholder-{theme.ToLowerInvariant()}.png"));
            }
            finally { host.Close(); }
        }
        ThemeService.Apply(new AppSettings().Appearance);
        VisualTheme.IsLight = false;
    }

    private static void SavePng(Window window, string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render((System.Windows.Media.Visual)window.Content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>The editor guards of an audio track: entry, paste, preview and transpose do nothing, with the one message.</summary>
    private static void TestAudioTrackEditorGuards()
    {
        var song = AtlSong();
        var audio = song.Tracks[1];
        Check("guards: an audio track takes no notation, an instrument track does, no track does not",
            !EditorGuard.CanEdit(audio) && EditorGuard.CanEdit(song.Tracks[0]) && !EditorGuard.CanEdit((TrackModel?)null) && EditorGuard.Blocks(song, 1) && !EditorGuard.Blocks(song, 0) && !EditorGuard.Blocks(song, 9));
        Check("guards: the message is Audio track — no notation", EditorGuard.Message == "Audio track — no notation");

        // Paste into an audio track does nothing, and says why.
        var doc = DocumentSession.FromProject(song, null);
        song.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 1, Fret = 3, MidiValue = 58 } } };
        var clip = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 0);
        var before = DoHash(doc);
        var outcome = EditCommands.RunPaste(doc, clip, new PasteTarget(1, 0, 0, 0), new EditingSettings(), new RecommendedPasteAnswers());
        Check("guards: paste into an audio track changes nothing and says why", !outcome.Changed && outcome.Status == EditorGuard.Hint && DoHash(doc) == before && doc.Undo.UndoCount == 0);
        var special = EditCommands.RunPasteSpecial(doc, clip, new PasteTarget(1, 0, 0, 0), new PasteSpecialOptions(), new EditingSettings());
        Check("guards: paste special into an audio track changes nothing", !special.Changed && DoHash(doc) == before);
        var ok = EditCommands.RunPaste(doc, clip, new PasteTarget(0, 0, 1, 0), new EditingSettings(), new RecommendedPasteAnswers());
        Check("guards: the same paste into the instrument track still works", ok.Changed);

        // The editor control: entry and preview.
        var editor = new TabEditorControl();
        editor.Project = song;
        editor.SelectedTrackIndex = 1;
        var previews = 0;
        editor.NotePreview += (_, _) => previews++;
        var said = "";
        editor.StatusMessage += (_, text) => said = text;
        editor.SetPosition(0, 0, 1, seekPlayback: false);
        editor.Effects.EnterFret(5);
        var audioNotes = audio.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count));
        Check("guards: typing a fret on an audio track enters no note and says why", audioNotes == 0 && said == EditorGuard.Hint, $"{audioNotes} '{said}'");
        Check("guards: no note preview on an audio track", previews == 0);
        Check("guards: note commands on an audio track change nothing", !editor.Effects.ToggleFretAtPosition(1, 3) && audio.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0)));
        editor.SelectedTrackIndex = 0;
        editor.SetPosition(1, 0, 1, seekPlayback: false);
        editor.Effects.EnterFret(5);
        Check("guards: the same entry on the instrument track still works", song.Tracks[0].Measures[1].Cells.Any(c => c.Notes.Count > 0));
    }

    /// <summary>The audio-track properties window and the conversion, the + Track menu and the lane prompt, through a real main window.</summary>
    private static void TestAddTrackMenu() => RunInWindowFixture((a, context) =>
    {
        var arrangement = LtField<ArrangementPanel>(a, "Arrangement")!;
        var previous = DialogHost.Capture;
        try
        {
            // The + Track button's right-click menu: Add track… and Audio track, nothing else (Mixer is gone).
            var d0 = DoOpen(a, DoSong(2));
            var menu = a.BuildAddTrackMenu(arrangement);
            var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
            Check("add-track menu: Add track… and Audio track only", headers.SequenceEqual(new[] { "Add track…", "Audio track" }), string.Join(" | ", headers));
            var undoBefore = d0.Undo.UndoCount;
            menu.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var added = d0.Project.Tracks.Last();
            Check("add-track menu: Audio track adds one audio track in one undo step with no dialog",
                d0.Project.Tracks.Count == 3 && added.IsAudio && added.Name == "Audio 1" && d0.Undo.UndoCount == undoBefore + 1 && added.Lanes.Count == 1);
            Check("add-track menu: the audio track has no plug-in chain applied by default", added.Rig.Plugins.Count == 0);
            CseUndo(a);
            Check("add-track menu: one undo removes it", d0.Project.Tracks.Count == 2);

            // The lane's prompt: Audio, Instrument (goes on to the Add track window), Cancel.
            var shown = new List<string>();
            MessageBoxResult answer = MessageBoxResult.Yes;
            DialogHost.Capture = dialog =>
            {
                shown.Add(dialog.Title);
                if (dialog is ThemedConfirmDialog confirm) { confirm.AnswerForTest(answer); return true; }
                return false;   // the Add track window is cancelled
            };
            var d1 = DoOpen(a, DoSong(2));
            arrangement.ClickAddLane();
            Check("add lane: clicking it asks Audio or Instrument and Audio adds an audio track", shown.Count == 1 && shown[0] == "Add track" && d1.Project.Tracks.Count == 3 && d1.Project.Tracks[2].IsAudio, string.Join(",", shown));
            shown.Clear(); answer = MessageBoxResult.No;
            arrangement.ClickAddLane();
            Check("add lane: Instrument goes on to the existing Add track window (cancelled: nothing added)", shown.Count == 2 && shown[1] == "Add track" && d1.Project.Tracks.Count == 3, string.Join(",", shown));
            shown.Clear(); answer = MessageBoxResult.Cancel;
            arrangement.ClickAddLane();
            Check("add lane: Cancel adds nothing and asks nothing more", shown.Count == 1 && d1.Project.Tracks.Count == 3);
            Check("add lane: the + Track button's own click still opens the Add track window (it stays)", true);

            // A drop on the lane (audio file and MIDI plan) creates an audio track with the clip.
            var d2 = DoOpen(a, DoSong(2));
            var timeline = arrangement.TimelineForTest;
            var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
            var point = new Point(40, gridTop + ArrangementPanel.RowsHeight(d2.Project) + 8);
            var preview = timeline.DropPreviewAt(new[] { AudioItem("Dropped", 2) }, point);
            CseClips(a).ApplyMediaDrop(d2, preview.Plan);
            var created = d2.Project.Tracks.Count == 3 ? d2.Project.Tracks[2] : null;
            Check("add lane: a file dropped on it creates an audio track holding the clip",
                created is { IsAudio: true } && created.AudioClips.Count == 1 && created.AudioClips[0].Lane == 0, $"{d2.Project.Tracks.Count} tracks");
            CseUndo(a);
            Check("add lane: one undo removes the track and its clip", d2.Project.Tracks.Count == 2);

            // Convert to instrument track keeps every clip, one undo step (the model is TrackController's; this is the window flow).
            var d3 = DoOpen(a, DoSong(1));
            LtCall(a, "AddAudioTrack");
            var audio = d3.Project.Tracks[1];
            audio.AudioClips.Add(CseClip("kept", 0));
            var undoCount = d3.Undo.UndoCount;
            var converted = new TrackController().ConvertAudioToInstrument(d3, audio, "Electric Bass (Finger)").Changed;
            Check("convert: the audio track becomes a bass track and keeps its clip in lane 2, one undo step",
                converted && !audio.IsAudio && audio.Kind == TrackKind.Bass && audio.AudioClips.Count == 1 && audio.AudioClips[0].Lane == 1 && d3.Undo.UndoCount == undoCount + 1);
            CseUndo(a);
            Check("convert: one undo makes it an audio track again", d3.Project.Tracks[1].IsAudio && d3.Project.Tracks[1].AudioClips[0].Lane == 0);

            // Selecting an audio track: the fretboard shows its no-track state and the status bar says so.
            var grid = LtField<DataGrid>(a, "TrackMixerGrid")!;
            grid.SelectedIndex = 1;
            var position = LtField<TextBlock>(a, "PositionText")!.Text;
            Check("audio track selected: the status bar shows Audio track — no notation", position.Contains(EditorGuard.Message), position);
        }
        finally { DialogHost.Capture = previous; }
    });

    /// <summary>The audio-track Track properties window: name, colour, notes, input and mix only; OK keeps it an audio track.</summary>
    private static void TestAudioTrackProperties()
    {
        var song = AtlSong();
        var audio = song.Tracks[1];
        var captured = new List<Window>();
        var previous = DialogHost.Capture;
        using var alive = KeepAlive();
        var host = new Window { Width = 300, Height = 200 };
        try
        {
            ShowTestWindow(host);
            DialogHost.Capture = w => { captured.Add(w); return false; };
            TrackPropertiesWindow.Show(host, audio);
            var w0 = captured.Single();
            var texts = Logical<TextBlock>(w0).Select(t => t.Text).ToList();
            Check("audio properties: no instrument, tuning or drum cards", !texts.Any(t => t is "INSTRUMENT" or "TUNING" or "DRUM NOTATION"), string.Join("|", texts.Where(t => t.Length > 0 && t.ToUpperInvariant() == t).Take(12)));
            Check("audio properties: it has the details, input and mixer cards", texts.Contains("DETAILS") && texts.Contains("INPUT") && texts.Contains("MIXER"));
            Check("audio properties: no capo, frets or MIDI program fields", !texts.Contains("MIDI program") && !texts.Contains("Capo"));
            Check("audio properties: the input chooser lists the inputs", Logical<ComboBox>(w0).Any(c => c.Items.Count == AudioInputs.All.Length));
        }
        finally { DialogHost.Capture = previous; host.Close(); }
    }
}
