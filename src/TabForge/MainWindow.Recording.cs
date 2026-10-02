using System.Windows;
using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

// MainWindow: record-arm and recording. The state and logic live in Controllers/RecordingController (A-01); the
// window keeps the XAML wiring (Record button, record pulse) and forwards to it through IRecordingHost.
public partial class MainWindow : IRecordingHost
{
    private RecordingController? _recorder;
    private RecordingController Recorder => _recorder ??= new RecordingController(this, _engine);

    /// <summary>A recording is in progress.</summary>
    internal bool IsRecording => _recorder?.IsRecording == true;

    /// <summary>The red button on a track row: monitor the track's input (audio or MIDI) and record it.</summary>
    internal void ToggleArm(TrackModel track) => Recorder.ToggleArm(track);

    /// <summary>Arm or input changed: engine monitoring, MIDI input, meters, rows.</summary>
    private void ArmChanged() => Recorder.ArmChanged();

    private void Record_Click(object sender, RoutedEventArgs e) => ToggleRecording();

    /// <summary>Transport Record (Ctrl+R): starts playback if needed and records every armed track; again to stop.</summary>
    internal void ToggleRecording() => Recorder.ToggleRecording();

    private void SetRecordIcon(bool recording)
    {
        if (RecordButton is { } button) button.Opacity = 1;
        RecordPulse.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- IRecordingHost ----
    ArrangementPanel IRecordingHost.Arrangement => Arrangement;
    string? IRecordingHost.CurrentPath => _currentPath;
    bool IRecordingHost.LoopOn => _loop;
    int IRecordingHost.LoopStartCell => _loopStartCell;
    (int start, int end) IRecordingHost.GetLoopRange() => GetLoopRange();
    (int Measure, int Cell, int String) IRecordingHost.Cursor => (Editor.SelectedMeasure, Editor.SelectedCell, Editor.SelectedString);
    void IRecordingHost.RestoreCursor(int measure, int cell, int stringIndex)
    {
        Editor.SetPosition(measure, cell, stringIndex);
        ScrollToCursor();
    }
    void IRecordingHost.SetStatus(string text) => StatusText.Text = text;
    void IRecordingHost.ShowNotice(string text) => MessageBox.Show(this, text + "\n\nThe take keeps its length: the lost parts are silent. Record again with the disk less busy, or on another drive.", "Recording", MessageBoxButton.OK, MessageBoxImage.Warning);
    void IRecordingHost.SyncAudioEngine() => SyncAudioEngine();
    void IRecordingHost.RefreshTracks() => RefreshTracks();
    void IRecordingHost.RefreshArrangement() => RefreshArrangement();
    void IRecordingHost.UpdateTitle() => UpdateTitle();
    void IRecordingHost.StartPlayback() => StartPlayback();
    DocumentSession IRecordingHost.Document => Doc;
    IReadOnlyList<DocumentSession> IRecordingHost.OpenDocuments => OpenDocuments;
    void IRecordingHost.StopPlayback() => StopPlayback();
    void IRecordingHost.ClipsChanged(bool refreshRows) => _clips.Changed(Doc, refreshRows);
    void IRecordingHost.SetRecordIcon(bool recording) => SetRecordIcon(recording);
}
