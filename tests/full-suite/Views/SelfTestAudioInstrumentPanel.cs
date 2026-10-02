using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

/// <summary>An audio track has no instrument view: no legend/Scales anchor, no state; a notation track brings it back.</summary>
public static partial class SelfTest
{
    private static void TestAudioInstrumentPanel()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var state = InstrumentVisualizer.Build(p, p.Tracks[0], tl, 0, false, false, 4, false, false, null);
        using var alive = KeepAlive();
        var panel = new InstrumentPanel();
        Point? anchor = null;
        panel.LegendAnchorChanged += a => anchor = a;
        void Draw()
        {
            panel.Measure(new Size(900, 220)); panel.Arrange(new Rect(0, 0, 900, 220)); panel.UpdateLayout();
            var bmp = new RenderTargetBitmap(900, 220, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(panel);
        }
        panel.SetState(state); Draw();
        Check("instrument panel: a notation track draws the legend", anchor is not null);
        panel.AudioTrack = true; Draw();
        Check("instrument panel: an audio track draws no legend or instrument", anchor is null);
        panel.AudioTrack = false; Draw();
        Check("instrument panel: selecting a notation track again brings the instrument back", anchor is not null);
    }
}
