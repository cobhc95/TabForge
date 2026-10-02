using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Mute / solo is instant: the button restyles on the click, and the engine gets the new track levels at once
/// (one SetTrackMix per loaded track, no chain, clip or route sync). Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    private static void TestMutedTrackDimmingSetting()
    {
        var settings = new Services.AppSettings();
        var row = TabForge.Services.SettingsCatalog.Build(settings).FirstOrDefault(r => r.Key == "appearance.mutedtrackdim");
        Check("Muted track dimming: the Appearance row exists, defaults to a clearly visible 80 % and stores what is set",
            row is not null && settings.Appearance.MutedTrackDimPercent == 80 && Round(row, 35) && settings.Appearance.MutedTrackDimPercent == 35);
        var o = new TabForge.Visualization.VisualOptions();
        var before = o.MutedDim;
        try
        {
            o.MutedDim = 0;
            var none = (ArrangementPanel.MutedRowOpacity(0), TrackTimeline.FadeBy(0.8, 0.3, 0));
            o.MutedDim = 1;
            var full = (ArrangementPanel.MutedRowOpacity(1), TrackTimeline.FadeBy(0.8, 0.3, 1));
            var half = (ArrangementPanel.MutedRowOpacity(0.5), TrackTimeline.FadeBy(0.8, 0.3, 0.5));
            Check("Muted track dimming: 0 % leaves rows and lanes as they were; more dims both the list row and the timeline lane, monotonically",
                none.Item1 == 1 && none.Item2 == 0.8 && full.Item1 < half.Item1 && half.Item1 < 1 && full.Item2 < half.Item2 && half.Item2 < 0.8 && Math.Abs(full.Item2 - 0.3) < 1e-9,
                $"row {none.Item1}/{half.Item1}/{full.Item1}, lane {none.Item2}/{half.Item2}/{full.Item2}");
        }
        finally { o.MutedDim = before; }

        static bool Round(SettingDescriptor r, int value) { r.Set((double)value); return true; }
    }

    private static void TestMuteDimmingIsInstant()
    {
        var song = TemplateFactory.Blank();
        song.Tracks.Add(new TrackModel { Name = "B", Measures = TemplateFactory.Measures(1) });
        var panel = new ArrangementPanel();
        panel.ViewOptions.MutedDim = 0.8;
        panel.Bind(song, Array.Empty<TabForge.Playback.MidiOutputDeviceInfo>());
        var before = panel.RowNameOpacityForTest(song.Tracks[0]);
        song.Tracks[0].Mute = true;
        panel.ApplyMuteVisualsNow();   // what the click runs before any deferred rebuild
        var muted = panel.RowNameOpacityForTest(song.Tracks[0]);
        var other = panel.RowNameOpacityForTest(song.Tracks[1]);
        song.Tracks[0].Mute = false;
        panel.ApplyMuteVisualsNow();
        var after = panel.RowNameOpacityForTest(song.Tracks[0]);
        Check("Mute dimming: the row greys and un-greys inside the click (no list rebuild), the other row is untouched",
            before == 1 && muted < 0.5 && other == 1 && after == 1, $"{before} {muted} {other} {after}");
    }

    private static void TestMuteSoloIsInstant()
    {
        // The button shows its new state before the toggle callback (and any follow-up work) runs.
        var seen = new List<string>();
        Button? mute = null, solo = null;
        mute = ArrangementPanel.ToggleIconButton("IconMute", false, () => seen.Add($"M:{mute!.ToolTip}"), "Mute track");
        solo = ArrangementPanel.ToggleIconButton("IconSolo", false, () => seen.Add($"S:{solo!.ToolTip}"), "Solo track");
        mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        solo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var labels = ((TextBlock)mute.Content).Text + ((TextBlock)solo.Content).Text;
        Check("Mute/solo: the M and S buttons show Muted / Soloed already inside the click (no waiting for a list rebuild); mute is a text box, not an icon",
            seen.Count == 2 && seen[0].Contains("(Muted)") && seen[1].Contains("(Soloed)") && labels == "MS", string.Join("; ", seen) + " " + labels);

        // The engine side: a toggle sends only per-track levels.

        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;
        try
        {
            client.AttachFakeForTest();
            var settings = new Services.PluginSettings();
            var song = TemplateFactory.Blank();
            var track = song.Tracks[0];
            track.SoundSource = SoundSources.Plugins;
            track.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
            client.Sync(song.Tracks, settings, null, song, new object());
            var sent = new List<EngineCommand>();
            client.SentForTest = c => sent.Add(c);
            track.Mute = true;
            AudioRouting.ApplyLevelsNow(song, client, 100);
            var level = AudioRouting.Levels(song, 100)(track).Volume;
            Check("Mute/solo: muting sends the engine one SetTrackMix at level 0 and nothing else (no chain load, clip or route resync)",
                sent.Count == 1 && sent[0] == EngineCommand.SetTrackMix && level == 0, $"{string.Join(",", sent)} level {level}");
            sent.Clear();
            track.Mute = false;
            AudioRouting.ApplyLevelsNow(song, client, 100);
            Check("Mute/solo: unmuting restores the level the same way", sent.Count == 1 && AudioRouting.Levels(song, 100)(track).Volume > 0);
        }
        finally { client.SentForTest = null; }
    }
}
