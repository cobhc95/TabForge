using System.IO;
using System.Linq;
using System.Windows;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Essential-action smoke checks of playback on a silent engine, the windows and prompts, and the exports.</summary>
public static partial class SelfTest
{
    private static void TestEssentialPlayback()
    {
        var w = SmNewWindow();
        try
        {
            var output = new SmOutput();
            var doc = SmOpenSong(w, SmDemoSong(), output);
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            var engine = doc.Playback.Engine;
            SmStep("play", () => { SmCall(w, "TogglePlayback"); Check("play: the engine starts", SmUntil(() => engine.IsPlaying, 5000)); });
            SmStep("live edit while playing", () =>
            {
                var tempo = doc.Project.Tempo;
                DocumentEdits.Run(doc, p => { p.Tempo = tempo + 10; return true; });
                SmUntil(() => false, 150);
                Check("live edit while playing: the song changed and keeps playing", doc.Project.Tempo == tempo + 10 && engine.IsPlaying);
            });
            SmStep("seek while playing", () => { SmCall(w, "JumpKeepingPlayback", new Action(() => ed.SetBar(2))); Check("seek: playback continues", engine.IsPlaying); });
            SmStep("pause", () => { SmCall(w, "TogglePlayback"); Check("pause: the engine is paused", SmUntil(() => engine.IsPaused, 5000)); });
            SmStep("loop toggle", () => { SmCall(w, "SetLoopActive", true); SmCall(w, "SetLoopActive", false); });
            SmStep("play from start and stop", () =>
            {
                SmCall(w, "PlayFromStart"); var started = SmUntil(() => engine.IsPlaying, 5000);
                SmCall(w, "StopPlayback");
                Check("stop: playing started and then stopped", started && SmUntil(() => !engine.IsPlaying, 5000));
            });
            Check("playback: the silent output received messages", output.Sent > 0, $"sent {output.Sent}");
        }
        finally { SmCloseWindow(w); }
    }

    /// <summary>Runs <paramref name="open"/>; every modal dialog it raises is built, shown off screen, laid out and closed, and every window it leaves open is closed.</summary>
    private static int SmOpenAndClose(Action open)
    {
        var known = Application.Current.Windows.OfType<Window>().ToHashSet();
        var shown = 0;
        SmWithDialogs<int>(d =>
        {
            d.ShowActivated = false; d.ShowInTaskbar = false; d.WindowStartupLocation = WindowStartupLocation.Manual; d.Left = -20000; d.Top = -20000;
            d.Owner = null;
            d.Show(); d.UpdateLayout(); SmSettle();
            shown++;
            d.Close();
            return false;
        }, () => { open(); SmSettle(); return 0; });
        foreach (var window in Application.Current.Windows.OfType<Window>().Where(x => !known.Contains(x)).ToList())
        {
            shown++;
            window.UpdateLayout(); window.Close();
        }
        SmSettle();
        return shown;
    }

    private static void TestEssentialWindowsAndPrompts()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            void Opens(string name, Action open) => SmStep(name + " opens and closes", () => Check($"{name}: a window appeared", SmOpenAndClose(open) >= 1));
            Opens("Settings", () => SmClick(w, "Prefs_Click"));
            Opens("Mixer", () => SmCall(w, "OpenMixer"));
            Opens("track properties", () => SmCall(w, "AddTrackWithWindow"));
            Opens("plug-in list (track effect chain)", () => w.OpenFxChain(doc.Project.Tracks[0]));
            Opens("add track prompt", () => AddTrackPrompt.Ask(w));
            Opens("confirm prompt", () => ConfirmPrompt.Ask(w, "TabForge", "Smoke check prompt", "Yes", true));
            Opens("delete track prompt", () => DeleteTrackPrompt.Ask(w, "Guitar"));
            Opens("bar range prompt", () => BarRangePrompt.Ask(w, "Bars 1-2", BarRangeAction.Clear, true, s => s));
            Opens("convert to audio prompt", () => ConvertTrackPrompts.AskToAudio(w, "Guitar"));
            Opens("convert clips prompt", () => ConvertTrackPrompts.AskClips(w, "Audio", true, out _));
            Opens("discard prompt", () => DiscardPrompt.Confirm(w, "TabForge", "Smoke check", new[] { "one change" }, "No"));
            Opens("save changes prompt", () => { var d = new ThemedConfirmDialog("TabForge", "Save changes?") { Owner = w }; DialogHost.ShowModal(d); });
            Opens("text prompt", () => GpDialogs.Prompt("Go to bar", "Bar number:", "1"));
            SmStep("dialog prewarm leaves no hidden window", () =>
            {
                var before = Application.Current.Windows.Count;
                ThemedConfirmDialog.Prewarm();
                Check("prewarm: no hidden window keeps the app running after the last window closes", Application.Current.Windows.Count == before, $"{before} -> {Application.Current.Windows.Count}");
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestEssentialExports()
    {
        var dir = SmScratch();
        try
        {
            var song = SmDemoSong();
            void Exports(string name, string file, Action write) =>
                SmStep($"export {name}", () => { var path = Path.Combine(dir, file); write(); Check($"export {name}: a non-empty file is written", File.Exists(path) && new FileInfo(path).Length > 0); });
            Exports("MIDI", "out.mid", () => MidiExportService.Export(song, Path.Combine(dir, "out.mid")));
            Exports("GP", "out.gp", () => GuitarProExporter.Save(song, Path.Combine(dir, "out.gp")));
            Exports("PDF", "out.pdf", () => ScorePdfExporter.Export(song, 0, Path.Combine(dir, "out.pdf")));
        }
        finally { SmClean(dir); }
    }
}
