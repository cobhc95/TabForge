using System;

namespace TabForge.Avalonia;

// Owns: the window's long-running activities the status bar's state word names (a render, a video export, a live video recording, a transport recording),
//     and the one Changed event the status bar refreshes its state on.
// Does not own: the activities themselves (MainWindowFileExports, MainWindowLiveVideo, RecordingPresenter) or the state word's text (MainWindowStatusBar).
// Tests: TabForge.Avalonia.Tests.StatusBarTransportTests.
/// <summary>UI thread only. The activities set their flag when they start and clear it when they end.</summary>
public sealed class StatusActivity
{
    private bool _rendering, _exportingVideo, _videoRecording;

    /// <summary>Raised when an activity started or ended.</summary>
    public event Action? Changed;

    /// <summary>Asks whether a transport recording runs now (set once the recording presenter exists); null counts as not recording.</summary>
    public Func<bool>? TransportRecording { get; set; }

    public bool Rendering { get => _rendering; set => Set(ref _rendering, value); }

    public bool ExportingVideo { get => _exportingVideo; set => Set(ref _exportingVideo, value); }

    public bool VideoRecording { get => _videoRecording; set => Set(ref _videoRecording, value); }

    /// <summary>True while a transport recording or a live video recording runs.</summary>
    public bool Recording => _videoRecording || TransportRecording?.Invoke() == true;

    /// <summary>Tells the status bar the recording probe may read differently now.</summary>
    public void Notify() => Changed?.Invoke();

    private void Set(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Changed?.Invoke();
    }
}
