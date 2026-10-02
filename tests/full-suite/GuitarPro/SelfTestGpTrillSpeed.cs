using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

// R5 row A17 (group "gp-fidelity"): the trill speed in a clean .gp. Guitar Pro stores it in a note XProperty (id 688062467, an Int: the note value
// in ticks of a 960-tick quarter; a real Guitar Pro 7 file holds 471-474 for an 8th and 240 for a 16th). TabForge.AlphaTab patch 0003 writes and reads it.
public static partial class SelfTest
{
    private const string TrillSpeedXProperty = "688062467";

    private static string[] GfTrillTicks(byte[] gp) =>
        XDocument.Parse(GfGpif(gp)).Descendants("Note").Where(n => n.Element("Trill") is not null)
            .Select(n => (string?)n.Descendants("XProperty").FirstOrDefault(x => (string?)x.Attribute("id") == TrillSpeedXProperty)?.Element("Int") ?? "-").ToArray();

    private static void TestGpTrillSpeed()
    {
        var folder = RtFolder();
        try
        {
            var speeds = new[] { 8, 16, 32, 64 };
            var song = GfSong(1, t =>
            {
                var slot = 0;
                foreach (var d in speeds) { GfPut(t, 0, slot, 4, RtNote(t, 1, 3, 95, "Trill")).Notes[0].TrillDurationDenominator = d; slot += 4; }
            });
            var bytes = GuitarProExporter.ToBytes(song, embedProject: false);
            Check("trill speed: the gpif holds the speed as ticks in note XProperty 688062467 (1/8 = 480, 1/16 = 240, 1/32 = 120, 1/64 = 60)",
                GfTrillTicks(bytes).SequenceEqual(new[] { "480", "240", "120", "60" }), string.Join(",", GfTrillTicks(bytes)));
            var back = GfReopen(bytes, folder, "trill-speeds");
            Check("trill speed: 1/8, 1/16, 1/32 and 1/64 trills come back from a clean .gp exactly (a 1/32 trill used to reopen as 1/16)",
                GfBeats(back).Select(b => b.Notes[0].TrillDurationDenominator).SequenceEqual(speeds), string.Join(",", GfBeats(back).Select(b => b.Notes[0].TrillDurationDenominator)));
            var again = GfReopen(GuitarProExporter.ToBytes(back, embedProject: false), folder, "trill-speeds-twice");
            Check("trill speed: a second export and reopen keeps them", GfBeats(again).Select(b => b.Notes[0].TrillDurationDenominator).SequenceEqual(speeds));

            // A trill with no speed set is written as 1/16 (the exporter's default) and reads back as 1/16.
            var plain = GfReopen(GuitarProExporter.ToBytes(GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Trill"))), embedProject: false), folder, "trill-plain");
            Check("trill speed: a trill without a speed reopens at 1/16", GfBeats(plain)[0].Notes[0].TrillDurationDenominator == 16);

            // Values as Guitar Pro writes them (a few ticks of jitter) and a file without the XProperty (written before this change, or by other programs).
            void SetTicks(string ticks, int note)
            {
                var edited = GfRewriteEntry(bytes, GuitarProExporter.ScoreEntry, g =>
                {
                    var doc = XDocument.Parse(Encoding.UTF8.GetString(g));
                    var trills = doc.Descendants("Note").Where(n => n.Element("Trill") is not null).ToList();
                    trills[note].Descendants("XProperty").First(x => (string?)x.Attribute("id") == TrillSpeedXProperty).Element("Int")!.Value = ticks;
                    return Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
                });
                var r = GfReopen(edited, folder, "trill-ticks-" + ticks);
                Check($"trill speed: a stored value of {ticks} ticks reads as the nearest note value ({GfBeats(r)[note].Notes[0].TrillDurationDenominator})",
                    GfBeats(r)[note].Notes[0].TrillDurationDenominator == (int.Parse(ticks, CultureInfo.InvariantCulture) switch { > 360 => 8, > 180 => 16, > 90 => 32, _ => 64 }));
            }
            SetTicks("471", 0); SetTicks("474", 0); SetTicks("237", 1); SetTicks("240", 2); SetTicks("118", 2); SetTicks("62", 3);

            var withoutProperty = GfRewriteEntry(bytes, GuitarProExporter.ScoreEntry, g =>
            {
                var doc = XDocument.Parse(Encoding.UTF8.GetString(g));
                foreach (var x in doc.Descendants("XProperty").Where(x => (string?)x.Attribute("id") == TrillSpeedXProperty).ToList()) x.Remove();
                return Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
            });
            var old = GfReopen(withoutProperty, folder, "trill-no-xproperty");
            Check("trill speed: a file without the speed (written by an earlier release or another program) still reads every trill as 1/16",
                GfBeats(old).All(b => b.Notes[0].TrillDurationDenominator == 16));

            // The preflight no longer lists a trill speed: it is kept.
            var preflight = GpExportPreflight.Analyze(song);
            Check("trill speed: the export preflight does not list a 1/32 trill as a loss", !preflight.Losses.Any(i => i.Feature.Contains("Trill", StringComparison.OrdinalIgnoreCase)),
                string.Join("; ", preflight.Losses.Select(i => i.Feature)));
        }
        finally { RtCleanup(folder); }
    }
}
