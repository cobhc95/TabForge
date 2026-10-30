using System;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Features.Recording.Presenter;

// Owns: the window's recording state (RecordingSession): the transport ends a recording through StopRecording, the
//   arrangement's Record button reads IsRecording, and disposing it (the window closed) ends a recording in progress.
// Does not own: the Record command and the engine hooks (RecordingPresenter) or the recording logic (TransportRecording).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests, MidiTakeCaptureTests, CountInRecordingTests.
internal sealed partial class RecordingPresenter
{
    /// <summary>The window's recording state: the transport ends a recording through StopRecording, and the arrangement's
    /// Record button reads IsRecording. Disposing it (the window closed) ends a recording in progress.</summary>
    internal sealed class RecordingSession : IDisposable
    {
        private readonly EngineHooks _hooks;
        private readonly Action _stop;
        private readonly Action<SongDocument, TrackModel, AudioClip>? _reRecord;

        internal RecordingSession(EngineHooks hooks, Action stop, Action<SongDocument, TrackModel, AudioClip>? reRecord = null)
        {
            _hooks = hooks;
            _stop = stop;
            _reRecord = reRecord;
        }

        /// <summary>True from a successful start until the recording is stopped (by Record again, the transport, or the window closing).</summary>
        public bool IsRecording => _hooks.IsRecording;

        /// <summary>Ends a recording in progress the same way Record again does (the takes are kept); nothing when not recording. True when one ended.</summary>
        public bool StopRecording()
        {
            if (!IsRecording) return false;
            _stop();
            return true;
        }

        /// <summary>True while <paramref name="track"/> is one of the tracks this recording still captures (not one disarmed mid-take).</summary>
        public bool IsTrackRecording(TrackModel track) => _hooks.IsCapturing(track);

        /// <summary>A record arm/disarm changed: a disarm while recording ends that track's capture at that moment (its lane stays until the stop).</summary>
        public void ArmChanged(SongDocument song) => _hooks.ArmChanged(song);

        /// <summary>
        /// Starts the one-pass re-record of <paramref name="clip"/>'s pass on <paramref name="song"/>: seeks to the group's loop start,
        /// runs the normal count-in when enabled, captures only that track for one loop length and replaces only that pass on landing.
        /// False while another recording runs or when the clip is not a loop pass.
        /// </summary>
        public bool ReRecordPass(SongDocument song, TrackModel track, AudioClip clip)
        {
            if (IsRecording || _reRecord is null || !TakeGroups.IsGrouped(clip)) return false;
            _reRecord(song, track, clip);
            return true;
        }

        public void Dispose() => _hooks.Dispose();
    }
}
