using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// The exact track volume and pan in a clean score file (group "gp-fidelity"). The gpif stores them as floats (the channel strip
// Parameters, entries 12 = balance and 13 = volume); alphaTab's own model keeps only a 0..16 step, so TabForge ships TabForge.AlphaTab
// patch 0002 (PlaybackInformation.VolumeFraction/BalanceFraction) and maps 0..127 <-> fraction as v/128 (GpMixerScale).
public static partial class SelfTest
{
    /// <summary>Test seam for the sidecar test: the volume fraction (entry 13 of the ChannelStrip parameters) of every track in a gpif.</summary>
    private static byte[] GfSetVolumeFractionInGpif(byte[] gpif, double fraction)
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(gpif));
        foreach (var parameters in doc.Descendants("ChannelStrip").Select(c => c.Element("Parameters")).Where(p => p is not null))
        {
            var values = parameters!.Value.Split(' ');
            values[12] = fraction.ToString("R", CultureInfo.InvariantCulture);
            parameters.Value = string.Join(' ', values);
        }
        return Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
    }

    private static string[] GfChannelStrip(byte[] gp)
    {
        var doc = XDocument.Parse(GfGpif(gp));
        return doc.Descendants("ChannelStrip").Select(c => c.Element("Parameters")!.Value).ToArray();
    }

    private static void TestGpMixerExact()
    {
        // The scale itself.
        Check("mixer scale: centre 64 is exactly 0.5, 0 is 0, 127 maps back to 127",
            GpMixerScale.ToFraction(64) == 0.5 && GpMixerScale.ToFraction(0) == 0 && GpMixerScale.FromFraction(GpMixerScale.ToFraction(127)) == 127);
        Check("mixer scale: a stored 1.0 (the slider's top) is 127 and a GP step 9/16 is 72",
            GpMixerScale.FromFraction(1.0) == 127 && GpMixerScale.FromFraction(0.5625) == 72 && GpMixerScale.FromFraction(-0.2) == 0 && GpMixerScale.FromFraction(7) == 127);

        var folder = RtFolder();
        try
        {
            // Every value 0..127 of volume and of pan, in one clean .gp (128 tracks): each comes back exactly.
            var song = new SongProject { Title = "mixer exact", Tempo = 100 };
            for (var v = 0; v < 128; v++)
            {
                var t = GpFixGuitar(1);
                t.Name = "T" + v; t.Volume = v; t.Pan = 127 - v;
                GfPut(t, 0, 0, 4, RtNote(t, 1, 3));
                song.Tracks.Add(t);
            }
            var bytes = GuitarProExporter.ToBytes(song, embedProject: false);
            var back = GfReopen(bytes, folder, "all-values");
            var badVolume = Enumerable.Range(0, 128).Where(v => back.Tracks[v].Volume != v).Select(v => $"{v}->{back.Tracks[v].Volume}").Take(5).ToList();
            var badPan = Enumerable.Range(0, 128).Where(v => back.Tracks[v].Pan != 127 - v).Select(v => $"{127 - v}->{back.Tracks[v].Pan}").Take(5).ToList();
            Check("clean .gp: every track volume 0..127 comes back exactly (was steps of 8)", back.Tracks.Count == 128 && badVolume.Count == 0, string.Join(", ", badVolume));
            Check("clean .gp: every track pan 0..127 comes back exactly (was steps of 8)", badPan.Count == 0, string.Join(", ", badPan));

            // The representation: the gpif holds the fractions (balance entry 12, volume entry 13), nothing is quantised on the way.
            var strips = GfChannelStrip(bytes);
            var centre = strips[64].Split(' ');   // volume 64, pan 63
            var hundred = GfChannelStrip(GuitarProExporter.ToBytes(GfSong(1, t => { t.Volume = 100; t.Pan = 64; GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); }), embedProject: false))[0].Split(' ');
            Check("clean .gp: the gpif stores the exact fractions (volume 100 = 0.78125, pan 64 = exactly 0.5, volume 64 = 0.5)",
                hundred[12] == "0.78125" && hundred[11] == "0.5" && centre[12] == "0.5", $"{hundred[11]} {hundred[12]} / {centre[12]}");

            // A Guitar Pro 3-5 file has no fractions: its 0..16 steps map as before (13 -> 103, 8 -> 64).
            var gp5 = GuitarProImporter.ImportBytes(SyntheticGuitarPro35.Write(500, 3), "legacy.gp5");
            Check("Guitar Pro 5 import: volume and balance steps map as before (13/16 -> 103, 8/16 -> 64)", gp5.Tracks[0].Volume == 103 && gp5.Tracks[0].Pan == 64, $"{gp5.Tracks[0].Volume}/{gp5.Tracks[0].Pan}");

            // The rule for mixer groups: a clean .gp stores the EFFECTIVE level (track x group level; track pan + group pan + master pan), exactly.
            var grouped = GfSong(1, t => { t.Volume = 100; t.Pan = 60; t.MixerGroup = MixerGroups.Guitars; GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); });
            var level = grouped.Mixer.Edit(MixerGroups.Guitars); level.Volume = 110; level.Pan = 10; grouped.Mixer.MasterPan = 3;
            var effective = (Volume: MixerGroups.Volume(grouped, grouped.Tracks[0]), Pan: MixerGroups.Pan(grouped, grouped.Tracks[0]));
            var groupedBack = GfReopen(GuitarProExporter.ToBytes(grouped, embedProject: false), folder, "grouped");
            Check("mixer group: the clean .gp holds the effective volume and pan (100 x 110% = 110; 60 + 10 + 3 = 73), exactly",
                effective == (110, 73) && groupedBack.Tracks[0].Volume == 110 && groupedBack.Tracks[0].Pan == 73, $"{effective}; reopened {groupedBack.Tracks[0].Volume}/{groupedBack.Tracks[0].Pan}");

            // Native formats keep the track's own values and the group apart, exactly.
            foreach (var (label, via) in new (string, SongProject)[] { (".tforge", RtViaTforge(grouped, folder, "native")), (".gp with embedded project", RtViaGp(grouped, folder, "embedded", true)) })
                Check($"mixer group: {label} keeps the track's own volume 100 / pan 60 and the group 110% / +10 / master +3 exactly",
                    via.Tracks[0].Volume == 100 && via.Tracks[0].Pan == 60 && via.Mixer.Levels(MixerGroups.Guitars).Volume == 110 && via.Mixer.Levels(MixerGroups.Guitars).Pan == 10 && via.Mixer.MasterPan == 3,
                    $"{via.Tracks[0].Volume}/{via.Tracks[0].Pan} {via.Mixer.Levels(MixerGroups.Guitars).Volume}/{via.Mixer.Levels(MixerGroups.Guitars).Pan} {via.Mixer.MasterPan}");

            // A file written by Guitar Pro 7/8 itself keeps the slider position it stores (here 0.9, the application's default volume): no step is lost.
            var edited = GfRewriteEntry(bytes, GuitarProExporter.ScoreEntry, g => GfSetVolumeFractionInGpif(g, 0.9));
            var fromGp = GfReopen(edited, folder, "gp-authored");
            Check("a gpif volume fraction of 0.9 (as Guitar Pro 8 writes by default) imports as 115 (the old 0..16 floor gave 111)", fromGp.Tracks.All(t => t.Volume == 115), $"{fromGp.Tracks[0].Volume}");
        }
        finally { RtCleanup(folder); }
    }
}
