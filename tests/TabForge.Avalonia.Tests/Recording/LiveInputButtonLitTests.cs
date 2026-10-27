using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the transport's live input (mic) button in the real main window lights while live input is on and goes back to normal when it is off,
/// the same as the Record and toggle buttons: the lit state is pushed to the button that the window shows.
/// Does not own: the live input logic (LiveInputSessionTests), the glow rules (TransportGlowRules) or the chain window.
/// Tests: TabForge.Avalonia.Tests.LiveInputButtonLitTests.
/// </summary>
public class LiveInputButtonLitTests
{
    private static TransportBarView.TransportButton LiveInputButton(MainWindow main) =>
        main.GetVisualDescendants().OfType<ArrangementView>().Single().Transport.GetVisualDescendants()
            .OfType<TransportBarView.TransportButton>().Single(button => button.Item.Name == TransportBarView.LiveInputItem.Name);

    [AvaloniaFact]
    public void LiveInputButtonLightsWhileLiveInputIsOnAndReturnsToNormalWhenOff()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-live-button-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = MainWindowSettings.Open(Path.Combine(folder, "settings.json"));
            // An empty MIDI device name counts as a chosen input, so live input can turn on without audio hardware.
            settings.Settings.Plugins.LiveInput.MidiDevice = "";
            var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
            // The live input command exists only when the sessions carry an engine client, as in the running app.
            using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine) { Engine = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust()) };
            var main = new MainWindow(settings, first: new SongDocument(() => null) { Project = DemoSongFactory.Create() }, playback: sessions);
            main.Show();
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var button = LiveInputButton(main);
            Assert.False(button.Lit);

            button.Pressed!.Invoke();
            Assert.True(button.Lit);
            Assert.NotNull(button.GlowAccent(light: false));

            button.Pressed!.Invoke();
            Assert.False(button.Lit);
            Assert.Null(button.GlowAccent(light: false));
            main.Close();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
