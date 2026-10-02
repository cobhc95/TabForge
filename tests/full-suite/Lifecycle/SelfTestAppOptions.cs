using System.Reflection;
using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// The application's shared options (Documents/AppOptions, AudioEngineClient.Mixer): the settings applier writes them once and every open
/// song, in every window, reads them; no option type keeps a shared static instance.
/// </summary>
public static partial class SelfTest
{
    /// <summary>AppOptionsHaveNoSharedDefault: no option type holds a static instance of itself (a hidden global every engine could fall back to).</summary>
    private static void AppOptionsHaveNoSharedDefaultCase()
    {
        foreach (var type in new[] { typeof(PlaybackPreferences), typeof(MixerOptions), typeof(VisualOptions) })
        {
            var statics = type.GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m is FieldInfo f && f.FieldType == type || m is PropertyInfo p && p.PropertyType == type).Select(m => m.Name).ToList();
            Check($"app options: {type.Name} has no shared static instance", statics.Count == 0, string.Join(", ", statics));
        }
    }

    /// <summary>AppOptionsReachEverySong: a settings change reaches every open song's engine, also in a second window that shares the options.</summary>
    private static void AppOptionsReachEverySongCase()
    {
        var options = new AppOptions();
        var a = new MainWindow(AudioEngineClient.Instance, options);
        var b = new MainWindow(AudioEngineClient.Instance, options);
        try
        {
            foreach (var w in new[] { a, b }) { w.Width = 1000; w.Height = 700; ShowTestWindow(w); }
            var songs = new List<DocumentSession>();
            foreach (var w in new[] { a, a, b })
            {
                var s = IxOpen(w, IxDemoSong(), out _);
                songs.Add(s);
            }
            Check("app options: every song of both windows reads the shared playback preferences",
                songs.All(s => ReferenceEquals(s.Playback.Engine.Preferences, options.Playback)));
            Check("app options: the window's own first tab reads them too",
                a.OpenDocuments.Concat(b.OpenDocuments).All(s => ReferenceEquals(s.Playback.Engine.Preferences, options.Playback)));

            // The window's settings applier writes the options; every song hears it.
            var settings = (AppSettings)typeof(MainWindow).GetProperty("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var loopBefore = settings.Audio.LoopCount;
            settings.Audio.LoopCount = loopBefore + 3;
            settings.Audio.CountInSound = "37,38";
            LtField<TransportControlsController>(a, "_transport")!.ApplyLoopBehaviour();
            LtField<TransportControlsController>(a, "_transport")!.ApplyCountInSound();
            var all = a.OpenDocuments.Concat(b.OpenDocuments).ToList();
            Check("app options: a loop setting applied in one window reaches the songs of both windows",
                all.All(s => s.Playback.Engine.Preferences.Loop.Count == loopBefore + 3));
            Check("app options: the count-in sound reaches the songs of both windows",
                all.All(s => s.Playback.Engine.Preferences is { CountInAccentNote: 37, CountInClickNote: 38 }));

            // A song that joins later (a tab dragged in, a new tab) reads the same options.
            var late = IxOpen(b, IxDemoSong(), out _);
            Check("app options: a song opened later reads the applied options", ReferenceEquals(late.Playback.Engine.Preferences, options.Playback) && late.Playback.Engine.Preferences.Loop.Count == loopBefore + 3);
            songs.Add(late);

            // The view options are the same object in the window and its views.
            LtCall(a, "ApplyFretboardStyle", "GP5: Bar");
            Check("app options: the fretboard style lands in the shared visual options", options.Visual.Gp5Mode == Gp5FretboardMode.Bar);
            Check("app options: the arrangement panel reads the shared visual options", ReferenceEquals(LtField<TabForge.Views.ArrangementPanel>(a, "Arrangement")!.ViewOptions, options.Visual));

            // Songs of a window with its own options are not touched by this window's settings.
            var other = new AppOptions();
            var c = new MainWindow(AudioEngineClient.Instance, other);
            try
            {
                c.Width = 1000; c.Height = 700; ShowTestWindow(c);
                var alone = IxOpen(c, IxDemoSong(), out _);
                songs.Add(alone);
                Check("app options: options made for one application are not shared with another instance", !ReferenceEquals(alone.Playback.Engine.Preferences, options.Playback));
            }
            finally { foreach (var d in c.OpenDocuments) d.MarkClean(); c.Close(); }
            settings.Audio.LoopCount = loopBefore;
            LtField<TransportControlsController>(a, "_transport")!.ApplyLoopBehaviour();
            foreach (var s in songs) IxRelease(s);
        }
        finally
        {
            foreach (var w in new[] { a, b }) { foreach (var d in w.OpenDocuments) d.MarkClean(); w.Close(); }
            SettleLifetimeDispatcher();
        }
    }
}
