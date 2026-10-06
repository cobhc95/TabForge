using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge;

/// <summary>Deferred engine sync requests are coalesced for one explicit open document and dropped when that document is no longer current.</summary>
public static partial class SelfTest
{
    private sealed class EngineSyncTestHost : IEngineSyncHost
    {
        private DocumentSession _activeDocument = DocumentSession.Blank();
        private readonly List<DocumentSession> _documents = new();
        public bool ThrowOnReadActive { get; set; }
        public DocumentSession ActiveDocument
        {
            get => ThrowOnReadActive ? throw new InvalidOperationException("active document read after disposal") : _activeDocument;
            set => _activeDocument = value;
        }
        public IReadOnlyList<DocumentSession> Documents => _documents;
        public AppSettings Settings { get; } = new();
        public List<Action> Posted { get; } = new();
        public List<string> StatusUpdates { get; } = new();
        public bool IsInitialized => false;
        public EngineSyncTestHost() => _documents.Add(ActiveDocument);
        public bool ShowSlowLoadPrompt(string pluginName, Action<bool> answered) => false;
        public void CloseSlowLoadPrompt() { }
        public void Post(Action work, DispatcherPriority priority) => Posted.Add(work);
        public void RebuildMidi() { }
        public void RearmChannelSetup() { }
        public void SetStatus(string text) { }
        public void CheckpointUndo() { }
        public void SaveSettings() { }
        public void UpdateTitle() { }
        public void RefreshTracks() { }
        public void RefreshArrangement() { }
        public void RefreshArrangementAll() { }
        public void RefreshMixer() { }
        public void UpdateAudioDeviceStatus() => StatusUpdates.Add("device");
        public void UpdatePluginTrustBar() => StatusUpdates.Add("plugins");
        public void UpdateMediaApprovalBar() => StatusUpdates.Add("media");
        public void AddDocument(DocumentSession document) => _documents.Add(document);
        public void RemoveDocument(DocumentSession document) => _documents.Remove(document);
        public void DrainPosted()
        {
            while (Posted.Count > 0)
            {
                var work = Posted[0];
                Posted.RemoveAt(0);
                work();
            }
        }
    }

    private static void TestEngineSyncDeferredRequests()
    {
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;
        client.AttachFakeForTest();
        var host = new EngineSyncTestHost();
        host.Settings.Plugins.PlayAllThroughEngine = false;
        var first = host.ActiveDocument;
        DocumentSession? second = null;
        var controller = new EngineSyncController(host, client);
        try
        {
            controller.ScheduleSync(first);
            controller.ScheduleSync(first);
            controller.ScheduleSync(first);
            var onePost = host.Posted.Count == 1;
            host.DrainPosted();
            var firstSync = host.StatusUpdates.Count == 3 && host.StatusUpdates.SequenceEqual(new[] { "device", "plugins", "media" });
            var firstUpdates = string.Join(",", host.StatusUpdates);

            second = DocumentSession.Blank();
            host.AddDocument(second);
            host.ActiveDocument = first;
            controller.ScheduleSync(first);
            host.ActiveDocument = second;
            controller.ScheduleSync(second);
            host.DrainPosted();
            var latestWins = host.StatusUpdates.Count == 6 && ReferenceEquals(client.CurrentOwner, second);
            var latestUpdates = host.StatusUpdates.Count;

            host.ActiveDocument = first;
            controller.ScheduleSync(first);
            host.ActiveDocument = second;
            controller.ScheduleSync(second);
            host.ActiveDocument = first;
            controller.ScheduleSync(first);
            host.DrainPosted();
            var roundTripWins = host.StatusUpdates.Count == 9 && ReferenceEquals(client.CurrentOwner, first);

            host.StatusUpdates.Clear();
            host.ActiveDocument = second;
            controller.ScheduleSync(second);
            host.ActiveDocument = first;
            host.DrainPosted();
            var switchedAwayDrops = host.StatusUpdates.Count == 0;

            host.ActiveDocument = second;
            controller.ScheduleSync(second);
            host.RemoveDocument(second);
            host.DrainPosted();
            var closedWhileCurrentDrops = host.StatusUpdates.Count == 0;
            host.AddDocument(second);
            host.ActiveDocument = first;

            host.StatusUpdates.Clear();
            var released = QueueClosedDocument(host, controller, first);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var closedTargetCollected = !released.TryGetTarget(out _);
            host.DrainPosted();
            var closedTargetDrops = host.StatusUpdates.Count == 0;

            controller.ScheduleSync(first);
            controller.Sync();
            host.DrainPosted();
            var immediateSatisfies = host.StatusUpdates.Count == 3;

            host.StatusUpdates.Clear();
            controller.ScheduleSync(first);
            controller.Dispose();
            host.ThrowOnReadActive = true;
            controller.Sync();
            controller.ScheduleSync();
            host.DrainPosted();
            var disposeDrops = host.StatusUpdates.Count == 0;

            Check("engine sync: repeated same-document requests post one deferred action and sync once in routing/status order", onePost && firstSync,
                $"one queued post {onePost}, first updates {firstUpdates}");
            Check("engine sync: a newer active-document request survives an older queued request", latestWins,
                $"updates {latestUpdates}, owner is latest {ReferenceEquals(client.CurrentOwner, second)}");
            Check("engine sync: A to B to A requests cannot let an older queued callback erase the newest request", roundTripWins,
                $"updates {roundTripWins}, owner returned to A {ReferenceEquals(client.CurrentOwner, first)}");
            Check("engine sync: switching away from the captured document drops its deferred sync", switchedAwayDrops);
            Check("engine sync: removing the active captured document from the open documents drops its deferred sync", closedWhileCurrentDrops);
            Check("engine sync: a queued deferred request does not keep a closed document alive", closedTargetCollected && closedTargetDrops,
                $"collected {closedTargetCollected}, sync dropped {closedTargetDrops}");
            Check("engine sync: an immediate sync satisfies and cancels a pending request", immediateSatisfies, $"updates {host.StatusUpdates.Count}");
            Check("engine sync: disposing the controller drops queued work and later wrapper calls do not read the closed host", disposeDrops);
        }
        finally
        {
            controller.Dispose();
            client.Stop();
            first.DisposePlayback();
            second?.DisposePlayback();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference<DocumentSession> QueueClosedDocument(EngineSyncTestHost host, EngineSyncController controller, DocumentSession active)
    {
        var closed = new DocumentSession(new PlaybackEngine(new NullMidiOutput()));
        host.AddDocument(closed);
        host.ActiveDocument = closed;
        controller.ScheduleSync(closed);
        host.ActiveDocument = active;
        host.RemoveDocument(closed);
        closed.DisposePlayback();
        return new WeakReference<DocumentSession>(closed);
    }
}
