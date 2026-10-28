using TabForge.Models;

namespace TabForge.Services;

// Owns: the list of what a legacy-format copy (.gp3, .gp4, .gp5) leaves out of a song: what the writer reports, what the exporter's own simplifications drop, and the audio and plug-in data no such file can hold.
// Does not own: the dialog (GpExportPreflightPresenter, GpExportPreflightWindow) or the bytes (LegacyGpWriter).
// Tests: TestLegacyGpExport.
internal static class LegacyGpPreflight
{
    /// <summary>The wording this report uses for a loss of the current-format export that also applies here; null for a loss that does not.</summary>
    private static string? SharedFeature(string feature) => feature switch
    {
        "Bend curve with more turns than a .gp file keeps" => "Bend curve with more turns than the format keeps",
        "Whammy-bar curve over four points" => "Whammy-bar curve over four points",
        "Tremolo picking at 1/64" => "Tremolo picking at 1/64",
        "Mix-table change on a beat (volume, pan, sound)" => "Volume, pan and sound changes on a beat",
        _ => null,
    };

    public static GpPreflightReport Report(SongProject project, LegacyGpFormat format, LegacyGpLossLog writerLoss)
    {
        var report = new GpPreflightReport();
        report.Losses.AddRange(writerLoss.ToLosses());
        foreach (var loss in GpExportPreflight.Analyze(project).Losses)
            if (SharedFeature(loss.Feature) is { } feature)
                report.Losses.Add(loss with { Feature = feature, Result = loss.Result.Replace(".gp", "the file", StringComparison.Ordinal) });
        AddAudioLosses(project, report);
        return report;
    }

    /// <summary>Plug-ins, FX chains, audio clips, mixer groups and audio tracks: none of these has a place in a notation-only file.</summary>
    private static void AddAudioLosses(SongProject project, GpPreflightReport report)
    {
        var audioTracks = project.Tracks.Where(t => t.IsAudio).Select(Name).ToList();
        if (audioTracks.Count > 0) report.Losses.Add(new GpLoss("Audio tracks", "not written; the file holds the notation tracks only", audioTracks.Count, string.Join("; ", audioTracks.Take(3))));
        var plugins = project.NotationTracks.Where(t => t.Rig.Plugins.Count > 0 || t.SoundSource != SoundSources.Midi).Select(Name).ToList();
        if (plugins.Count > 0) report.Losses.Add(new GpLoss("Plug-ins, FX chains and plug-in instruments", "not written; the tracks reopen with their MIDI sound", plugins.Count, string.Join("; ", plugins.Take(3))));
        var clips = project.Tracks.Where(t => !t.IsAudio && t.AudioClips.Count > 0).Select(Name).ToList();
        if (clips.Count > 0) report.Losses.Add(new GpLoss("Audio clips", "not written", clips.Count, string.Join("; ", clips.Take(3))));
        if (MixerGroups.HasAudioData(project)) report.Losses.Add(new GpLoss("Mixer groups, group and master chains", "not written", 1, "whole song"));
    }

    private static string Name(TrackModel track) => string.IsNullOrWhiteSpace(track.Name) ? "Track" : track.Name;
}
