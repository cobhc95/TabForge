using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>The mixer host of a real main window: Mixer and FX chain windows are single-instance, reopen after close, and leave nothing behind (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestMixerHost()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var host = w.MixerHost;
            var track = doc.Project.Tracks[0];
            var other = doc.Project.Tracks[1];

            // Mixer: one window, brought forward again on a second request, gone from the host when it closes.
            Check("mixer host: no mixer before it is asked for", host.Windows.Mixer is null);
            host.OpenMixer();
            var mixer = host.Windows.Mixer;
            Check("mixer host: the mixer opens, owned by the main window", mixer is not null && ReferenceEquals(mixer.Owner, w));
            host.OpenMixer();
            Check("mixer host: a second request reuses the open mixer", ReferenceEquals(host.Windows.Mixer, mixer));
            mixer!.Close(); SmSettle();
            Check("mixer host: closing the mixer clears it", host.Windows.Mixer is null);
            host.OpenMixer();
            Check("mixer host: the mixer opens again after a close", host.Windows.Mixer is not null && !ReferenceEquals(host.Windows.Mixer, mixer));
            host.Windows.Mixer!.Close(); SmSettle();

            // FX chain windows: one per track, reused, removed on close (a closed window is not kept by the host).
            host.OpenFxChain(track);
            var fx = host.Windows.FxWindowOf(track);
            Check("mixer host: the FX window opens, owned by the main window", fx is not null && ReferenceEquals(fx.Owner, w));
            host.OpenFxChain(track);
            Check("mixer host: a second request reuses the track's FX window", ReferenceEquals(host.Windows.FxWindowOf(track), fx));
            host.OpenFxChain(other);
            Check("mixer host: another track gets its own FX window", host.Windows.FxWindowOf(other) is { } o && !ReferenceEquals(o, fx));
            host.OpenFxChain(null);
            fx!.Close(); SmSettle();
            Check("mixer host: closing an FX window removes it from the host", host.Windows.FxWindowOf(track) is null);
            Check("mixer host: the other FX window is untouched", host.Windows.FxWindowOf(other) is not null);
            host.OpenFxChain(track);
            Check("mixer host: the FX window opens again after a close", host.Windows.FxWindowOf(track) is { } again && !ReferenceEquals(again, fx));

            // A removed track's window is closed by the stale sweep; windows of tracks that stay are kept.
            host.Windows.CloseStaleFxWindows(doc.Project);
            Check("mixer host: the stale sweep keeps the windows of tracks that are in the song", host.Windows.FxWindowOf(track) is not null && host.Windows.FxWindowOf(other) is not null);
            var stranger = new TrackModel { Name = "Removed" };
            host.OpenFxChain(stranger); SmSettle();
            Check("mixer host: a window opens for a track outside the song", host.Windows.FxWindowOf(stranger) is not null);
            host.Windows.CloseStaleFxWindows(doc.Project); SmSettle();
            Check("mixer host: the stale sweep closes the window of a track that left the song", host.Windows.FxWindowOf(stranger) is null);

            // The window closing takes its owned mixer and FX windows with it.
            host.OpenMixer();
            var owned = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().Count(x => ReferenceEquals(x.Owner, w));
            Check("mixer host: the open mixer and FX windows are owned by the main window", owned >= 3, $"{owned}");
        }
        finally { SmCloseWindow(w); }
        Check("mixer host: closing the main window closes its mixer and FX windows",
            !System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().Any(x => x is MixerWindow or FxChainWindow));
    }
}
