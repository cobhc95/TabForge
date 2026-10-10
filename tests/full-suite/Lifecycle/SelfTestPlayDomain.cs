using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The tool actions and the transport settings popups run against fake hosts, without a main window: the song edits are one undo step,
/// the status and refresh calls reach the host, and a popup control is not touched before the window has finished starting.
/// </summary>
public static partial class SelfTest
{
    private sealed class FakeToolsHost : IToolActionsHost
    {
        public FakeToolsHost(DocumentSession document) => ActiveDocument = document;
        public DocumentSession ActiveDocument { get; }
        public int SelectedTrackRow => 0;
        public ArrangementController Arrangement { get; } = new();
        public ToolRefresh Refreshed = ToolRefresh.None;
        public int TempoRefreshes;
        public string Status = "";
        public int FocusedCalls;
        public MarkerModel? Focused;
        public void RefreshAfterEdit(ToolRefresh refresh) => Refreshed |= refresh;
        public void RefreshAfterTempoChange() => TempoRefreshes++;
        public void FocusMarkerList(MarkerModel? marker) { FocusedCalls++; Focused = marker; }
        public StackPanel ToolsPaletteHost { get; } = new();
        public Panel ToolsTabStrip { get; } = new StackPanel();
        public Panel PinnedToolStrip { get; } = new StackPanel();
        public MeasureModel? CurrentBar => null;
        public int MaxMeasures => 1;
        public void RunPaletteTool(object sender, RoutedEventArgs e) { }
        public Window Window { get; } = new();
        public AppSettings Settings { get; } = new();
        public TabEditorControl Editor { get; } = new();
        public SongProject Project => ActiveDocument.Project;
        public TrackModel? SelectedTrack { get; set; }
        public void SaveSettings() { }
        public void SetStatus(string text) => Status = text;
    }

    private static void TestPlayDomainFlows()
    {
        ToolActionsOnAFakeHost();
        TransportSettingsOnAFakeHost();
    }

    private static void ToolActionsOnAFakeHost()
    {
        var document = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = TemplateFactory.Blank() };
        var host = new FakeToolsHost(document);
        var flow = new ToolActionsFlow(host);

        var gray = document.Project.GrayInactiveVoice;
        flow.ToggleInactiveVoiceGray();
        Check("tool actions: dimming the inactive voice is one undo step and reaches the host",
            document.Project.GrayInactiveVoice != gray && document.Undo.UndoCount == 1 &&
            host.Refreshed == (ToolRefresh.Repaint | ToolRefresh.Palette) && host.Status.StartsWith("Inactive voice"), host.Status);

        // A bar tool without a bar under the cursor does nothing and says nothing.
        host.Status = "";
        flow.CycleTripletFeel();
        flow.ToggleSimile(1);
        flow.ToggleLineBreak(force: true);
        Check("tool actions: a bar tool with no bar under the cursor changes nothing", document.Undo.UndoCount == 1 && host.Status == "");

        flow.FocusMarkerList();
        Check("tool actions: the marker list focus with no markers focuses the list and says so",
            host.FocusedCalls == 1 && host.Focused is null && host.Status == "No markers yet");

        document.Project.Markers.Add(new MarkerModel { MeasureIndex = 5, Title = "B" });
        document.Project.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "A" });
        flow.FocusMarkerList();
        Check("tool actions: the marker list focus picks the first marker at or after the cursor bar",
            host.FocusedCalls == 2 && host.Focused?.Title == "A" && host.Status == "Marker list focused");

        host.SelectedTrack = new TrackModel { Name = "Kit", Kind = TrackKind.Drums };
        flow.Transpose();
        Check("tool actions: a drum track is never transposed", host.Status == "Kit is a drum track: drums are not transposed" && document.Undo.UndoCount == 1, host.Status);
        host.SelectedTrack = new TrackModel { Name = "Clips", Kind = TrackKind.Audio };
        flow.Transpose();
        Check("tool actions: an audio track says why it cannot be transposed", host.Status == EditorGuard.Hint, host.Status);
        Check("tool actions: a chord name needs a cursor beat", !flow.InsertChordName("Am") && document.Undo.UndoCount == 1);
    }

    private sealed class FakeTransportHost : ITransportControlsHost
    {
        public IReadOnlyList<DocumentSession> Documents { get; } = new List<DocumentSession>();
        public AppSettings Settings { get; } = new();
        public PlaybackPreferences Preferences { get; } = new();
        public ComboBox? SpeedCombo => null;
        public bool IsInteractive => true;
        public void SaveSettings() { }
        public void SetEngineSpeed(double speed) { }
        public void SetStatus(string text) { }
    }

    private sealed class FakeSettingsHost : ITransportSettingsHost
    {
        public FakeSettingsHost()
        {
            var transportHost = new FakeTransportHost();
            Settings = transportHost.Settings;
            Transport = new TransportControlsController(transportHost);
            Built = MakeControls();
            Document = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = TemplateFactory.Blank() };
        }
        public bool Initialized;
        public int Saves;
        public string Status = "";
        public DocumentSession Document { get; }
        public bool IsInitialized => Initialized;
        /// <summary>The popups as the window has them once it is initialised (the test reads and writes them directly).</summary>
        public TransportPopupControls Built { get; }
        /// <summary>Like the window: before it is initialised the popup fields are all unset, so the record holds nulls.</summary>
        public TransportPopupControls Controls => Initialized ? Built : NullControls();
        public TransportControlsController Transport { get; }
        public PlaybackEngine Engine => Document.Playback.Engine;
        public Window Window { get; } = new();
        public AppSettings Settings { get; }
        public TabEditorControl Editor => throw new NotSupportedException();
        public SongProject Project => Document.Project;
        public TrackModel? SelectedTrack => null;
        public void SaveSettings() => Saves++;
        public void SetStatus(string text) => Status = text;

        private static ComboBox Combo(params string[] tags)
        {
            var combo = new ComboBox();
            foreach (var tag in tags) combo.Items.Add(new ComboBoxItem { Tag = tag, Content = tag });
            return combo;
        }

        private static TransportPopupControls NullControls() => new(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

        private static TransportPopupControls MakeControls() => new(
            new Button { Name = "MetronomeButton" }, new MenuItem(), new Popup(),
            new Slider { Maximum = 100, Tag = "Master" }, new Slider { Maximum = 100, Tag = "Accent" }, new Slider { Maximum = 100, Tag = "Click" },
            new TextBlock(), new TextBlock(), new TextBlock(),
            Combo("classic", "woodblock", "sidestick", "clap", "custom"), Combo("1", "2", "3", "4"), new CheckBox(),
            new Button { Name = "CountInButton" }, new MenuItem(), new Popup(),
            Combo("1", "2"), Combo("classic"), new Slider { Maximum = 100 }, new TextBlock(),
            new CheckBox(), new CheckBox(), new CheckBox(),
            new Button { Name = "LoopButton" }, new Popup(), new TextBlock(), Combo("Bar", "Song"),
            new CheckBox(), new CheckBox(), new TextBlock(),
            new TextBox(), new CheckBox(), new RadioButton(), new RadioButton(),
            new TextBox(), new TextBox(), new TextBox());
    }

    private static void TransportSettingsOnAFakeHost()
    {
        var host = new FakeSettingsHost();
        var controller = new TransportSettingsController(host);
        var controls = host.Built;

        // Before the window has finished starting a control's change is not the user's: nothing is read, nothing is saved.
        controls.MetronomeVolumeSlider.Value = 42;
        controller.OnMetronomeSlider(controls.MetronomeVolumeSlider);
        controller.OnCountInVolume(33);
        controller.OnCountInOption();
        controller.SyncMetronomePopup();
        Check("transport settings: nothing changes before the window is initialised",
            host.Saves == 0 && host.Settings.Audio.MetronomeVolume != 42 && controls.CountInVolumeValue.Text == "");

        // The popup handlers that read the popups do nothing before the window is initialised, and that caches nothing: the first use after start-up works.
        controller.ToggleMetronome();
        controller.ToggleCountIn();
        controller.OnCountInChecks();
        controller.OnLoopBehaviour();
        controller.OpenLoopSettings("Bar 1");
        controller.OpenCountInSettings();
        controller.OpenMetronomeSettings();
        Check("transport settings: the popup handlers do nothing before the window is initialised",
            !host.Transport.Metronome && !host.Transport.CountIn && host.Saves == 0 && host.Status == "" && !controls.LoopSettingsPopup.IsOpen);

        host.Initialized = true;
        controller.OnMetronomeSlider(controls.MetronomeVolumeSlider);
        Check("transport settings: a metronome slider sets its volume and shows it",
            host.Settings.Audio.MetronomeVolume == 42 && controls.MetronomeVolumeValue.Text == "42%" && host.Transport.SavePending);
        host.Transport.Dispose();

        controller.ToggleMetronome();
        Check("transport settings: the metronome switch toggles, checks its menu item and saves",
            host.Transport.Metronome && controls.MetronomeMenu.IsChecked && host.Status == "Metronome on" && host.Saves == 1);
        controller.ToggleMetronome();
        Check("transport settings: the metronome switch toggles back", !host.Transport.Metronome && host.Status == "Metronome off");

        controller.ToggleCountIn();
        Check("transport settings: the count-in switch toggles and checks its menu item", host.Transport.CountIn && controls.CountInMenu.IsChecked);

        controls.LoopCountBox.Text = "3";
        controls.LoopTrainerFromBox.Text = "60";
        controls.LoopTrainerToBox.Text = "100";
        controls.LoopTrainerStepBox.Text = "5";
        controller.OnLoopBehaviour();
        Check("transport settings: the loop count box is read, shown back and sends the loop to the engine",
            host.Settings.Audio.LoopCount == 3 && controls.LoopCountBox.Text == "3" && host.Transport.Preferences.Loop.Count == 3 &&
            controls.LoopCountBadge.Visibility == Visibility.Visible && controls.LoopCountBadge.Text == "3");
        controller.ShowLoopCountBadge(2);
        Check("transport settings: the loops-left badge counts down", controls.LoopCountBadge.Text == "1" && (string)controls.LoopCountBadge.ToolTip == "1 loop left");
        controls.LoopCountBox.Text = "";
        controller.OnLoopBehaviour();
        Check("transport settings: an empty loop count is endless and hides the badge",
            host.Settings.Audio.LoopCount == 0 && controls.LoopCountBox.Text == "∞" && controls.LoopCountBadge.Visibility == Visibility.Collapsed);
    }
}
