using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Lines between tracks: hidden by default, the Preferences row and bindable command, persistence, and the timeline and track list redrawing when toggled.</summary>
public static partial class SelfTest
{
    /// <summary>The setting: hidden by default, saved and read back, validated, a Preferences row and an unbound command.</summary>
    private static void TestTrackLinesSetting()
    {
        Check("lines between tracks are hidden by default", !new AppSettings().Timeline.ShowTrackLines);
        var on = new AppSettings();
        on.Timeline.ShowTrackLines = true;
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(on));
        Check("the lines-between-tracks setting survives a save and load", back!.Timeline!.ShowTrackLines && SettingsValidator.Normalize(back).Timeline.ShowTrackLines);
        var row = SettingsCatalog.Build(new AppSettings()).FirstOrDefault(d => d.Key == "timeline.tracklines");
        Check("the setting is a searchable Bool row in the Timeline group, off by default",
            row is not null && row.Category == SettingsCatalog.Timeline && row.Kind == SettingKind.Bool && Equals(row.Get(), false)
            && SettingsCatalog.Matches(row, "lines between tracks") && SettingsCatalog.Matches(row, "track lines"));
        var action = HotkeyCatalog.ById("View.ToggleTrackLines");
        Check("View.ToggleTrackLines is a bindable command, unbound by default", action is not null && action.DefaultGesture == "");
    }

    /// <summary>Pixels of the whole panel (timeline and track list) as they are drawn now.</summary>
    private static int[] TrackLinePixels(FrameworkElement element)
    {
        var width = (int)element.ActualWidth;
        var height = (int)element.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new int[width * height];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>Turning the lines on draws them and turning them off draws the hidden picture again; the panel starts hidden.</summary>
    private static void TestTrackLinesRedraw()
    {
        var song = HoverSong(10, 12);
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 900, Height = 520 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            PumpUi();
            Check("the panel starts with the lines hidden", !panel.ShowTrackLines);
            panel.SetSelectedTrack(1);
            PumpUi();
            var looks = panel.TrackRowLooksForTest;
            Check("with the lines hidden, selecting a track leaves no line under any track row and keeps the alternate shade",
                looks.All(l => l.Bottom == 0) && looks.Count > 3 && !ReferenceEquals(looks[3].Background, Brushes.Transparent),
                string.Join(",", looks.Select(l => l.Bottom)));
            var hidden = TrackLinePixels(panel);
            panel.ShowTrackLines = true;
            PumpUi();
            var shown = TrackLinePixels(panel);
            var changed = Enumerable.Range(0, hidden.Length).Count(i => hidden[i] != shown[i]);
            Check("turning the lines on redraws the timeline and the track list", changed > 0, $"{changed} pixels changed");
            panel.ShowTrackLines = false;
            PumpUi();
            var hiddenAgain = TrackLinePixels(panel);
            var differences = Enumerable.Range(0, hidden.Length).Count(i => hidden[i] != hiddenAgain[i]);
            Check("turning the lines off again draws the hidden picture again", differences == 0, $"{differences} pixels differ");
        }
        finally { window.Close(); }
    }
}
