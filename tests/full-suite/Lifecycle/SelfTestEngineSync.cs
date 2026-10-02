using System.Reflection;
using System.Runtime.CompilerServices;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge;

/// <summary>
/// The engine sync of a window (Controllers/EngineSyncController): what it attaches to the shared audio engine client is detached when the window
/// has closed, a window opened afterwards still hears the engine, and an engine restart makes every open window sync its song again.
/// </summary>
public static partial class SelfTest
{
    private static readonly string[] EngineSyncEvents = { "ChainLoaded", "ChainAcknowledged", "PluginCrashed", "PluginFailed", "PluginMisbehaved", "DeviceError", "PluginSlow", "PluginEdited" };

    private static int EngineSyncHandlers(AudioEngineClient client) => EngineSyncEvents.Sum(name => InstanceEventCount(client, name));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (MainWindow Window, DocumentSession Session) EngineSyncOpen(string name)
    {
        var window = NewLifetimeWindow();
        var session = DocumentSession.FromProject(LifetimePluginSong(name), null);
        session.MarkClean();
        LtCall(window, "AdoptDroppedDocument", session, window.OpenDocuments.Count);   // the window shows it: its sync hooks the engine and loads its chain on the (fake) engine
        return (window, session);
    }

    /// <summary>EngineHooksReleasedOnClose: the window's engine handlers are gone with the window; a window opened afterwards is still told about the engine.</summary>
    private static void EngineHooksReleasedOnCloseCase()
    {
        var client = AudioEngineClient.Instance;
        SettleLifetimeDispatcher();
        var before = EngineSyncHandlers(client);
        var (b, sessionB) = EngineSyncOpen("HooksB");
        var attached = EngineSyncHandlers(client);
        foreach (var document in b.OpenDocuments) document.MarkClean();
        b.Close();
        SettleLifetimeDispatcher();
        var after = EngineSyncHandlers(client);
        Check("engine sync: a window attaches its engine handlers when its first sync runs, and closing the window detaches exactly those",
            attached > before && after == before && b.AttachmentCount == 0, $"handlers {before} -> {attached} -> {after}, window attachments left {b.AttachmentCount}");

        var (c, sessionC) = EngineSyncOpen("HooksC");
        try
        {
            LtField<Action<string, string>>(client, "PluginFailed")?.Invoke(@"C:\NoSuch\Late.vst3", "no such file");
            SettleLifetimeDispatcher();
            var status = LtField<System.Windows.Controls.TextBlock>(c, "StatusText")!.Text;
            Check("engine sync: a window opened after another one closed still receives the engine's events", status.Contains("Late could not be loaded", StringComparison.Ordinal), $"status '{status}'");
        }
        finally
        {
            foreach (var document in c.OpenDocuments) document.MarkClean();
            c.Close();
            SettleLifetimeDispatcher();
        }
        Check("engine sync: the handlers are back to the start after the second window closed too", EngineSyncHandlers(client) == before, $"{EngineSyncHandlers(client)} vs {before}");
    }

    /// <summary>After an engine restart every open window syncs its displayed song again (the engine lost every chain), and the song that owned the engine owns it again.</summary>
    private static void EngineRestartResyncsEveryWindowCase()
    {
        var client = AudioEngineClient.Instance;
        var (b, sessionB) = EngineSyncOpen("RestartB");
        var (c, sessionC) = EngineSyncOpen("RestartC");
        try
        {
            var (trackB, trackC) = (sessionB.Project.Tracks[0], sessionC.Project.Tracks[0]);
            var loaded = client.SlotOf(trackB) >= 0 && client.SlotOf(trackC) >= 0 && ReferenceEquals(client.CurrentOwner, sessionC);
            // The restart: the client forgets every chain (as its crash path does) and tells the windows.
            LtCall(client, "Cleanup");
            client.AttachFakeForTest();
            var lost = client.SlotOf(trackB) == -1 && client.SlotOf(trackC) == -1;
            LtField<Action<string>>(client, "PluginCrashed")?.Invoke("");
            SettleLifetimeDispatcher();
            Check("engine sync: before the restart both windows' chains are loaded and window C's song owns the engine", loaded && lost,
                $"loaded {loaded}, slots lost after cleanup {lost}");
            Check("engine sync: after an engine restart the window that does not own the engine syncs its song again too",
                client.SlotOf(trackB) >= 0 && client.SlotOf(trackC) >= 0, $"slots B {client.SlotOf(trackB)}, C {client.SlotOf(trackC)}");
            Check("engine sync: after an engine restart the song that owned the engine owns it again", ReferenceEquals(client.CurrentOwner, sessionC), $"owner is window C's song: {ReferenceEquals(client.CurrentOwner, sessionC)}");
        }
        finally
        {
            foreach (var window in new[] { b, c })
            {
                foreach (var document in window.OpenDocuments) document.MarkClean();
                window.Close();
            }
            SettleLifetimeDispatcher();
        }
    }

    /// <summary>A single plug-in crash (the engine keeps running) syncs only the windows whose song uses that plug-in; an engine restart syncs every window.</summary>
    private static void PluginCrashSyncsOnlyUsingWindowsCase()
    {
        var client = AudioEngineClient.Instance;
        var (b, sessionB) = EngineSyncOpen("CrashB");
        var (c, sessionC) = EngineSyncOpen("CrashC");
        try
        {
            var (trackB, trackC) = (sessionB.Project.Tracks[0], sessionC.Project.Tracks[0]);
            var pathB = trackB.Rig.Plugins[0].Path;
            var slotCrash = typeof(AudioEngineClient).GetProperty("CrashLeftEngineRunning", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            LtCall(client, "Cleanup");
            client.AttachFakeForTest();
            slotCrash.SetValue(client, true);
            LtField<Action<string>>(client, "PluginCrashed")?.Invoke(@"C:\NoSuch\Nobody.vst3");   // a plug-in no open song uses
            SettleLifetimeDispatcher();
            Check("engine sync: a plug-in crash that no open song uses syncs no window", client.SlotOf(trackB) == -1 && client.SlotOf(trackC) == -1, $"slots B {client.SlotOf(trackB)}, C {client.SlotOf(trackC)}");
            LtField<Action<string>>(client, "PluginCrashed")?.Invoke(pathB);
            SettleLifetimeDispatcher();
            Check("engine sync: a single plug-in crash syncs the window whose song uses it and not the other window", client.SlotOf(trackB) >= 0 && client.SlotOf(trackC) == -1, $"slots B {client.SlotOf(trackB)}, C {client.SlotOf(trackC)}");
            slotCrash.SetValue(client, false);
        }
        finally
        {
            foreach (var window in new[] { b, c })
            {
                foreach (var document in window.OpenDocuments) document.MarkClean();
                window.Close();
            }
            SettleLifetimeDispatcher();
        }
    }
}
