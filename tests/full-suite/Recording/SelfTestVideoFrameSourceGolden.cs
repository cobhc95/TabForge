using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.Video;

namespace TabForge;

/// <summary>
/// A guard on the pixels of the video frame source: 24 frames of the demo song at 640x360 in each layout and theme, hashed, must equal the expected hashes,
/// so how frames are cached or composed cannot alter a pixel. TABFORGE_VIDEO_GOLDEN_OUT=&lt;file&gt; writes the current table; TABFORGE_VIDEO_GOLDEN_DUMP=&lt;folder&gt; keeps the raw frames.
/// </summary>
public static partial class SelfTest
{
    private static void TestVideoFrameSourceGolden()
    {
        var bytes = FuzzFindSample("TabForge Demo - Ashen Meridian.gp5") ?? FuzzFindSample("TabForge Demo - Ashen Meridian.gp");
        if (bytes is null) { Check("video golden: the demo song is present", false); return; }
        var project = GuitarProImporter.ImportBytes(bytes, "demo.gp5");
        var timeline = RenderSpecBuilder.Compile(project);
        var table = new StringBuilder();
        // Band and Score+Band run twice: with their instruments off, and with them on (the frame clock keeps those frames repeatable).
        var cases = new[] { (VideoLayout.ScoreOnly, false), (VideoLayout.Band, false), (VideoLayout.Focus, true), (VideoLayout.ScoreAndBand, false), (VideoLayout.Band, true), (VideoLayout.ScoreAndBand, true) };
        foreach (var (layout, instruments) in cases)
            foreach (var dark in new[] { true, false })
            {
                using var source = new VideoFrameSource(project, timeline, new AppSettings(), new VideoViewSpec { Layout = layout, Tracks = new[] { 0, 1 }, Dark = dark, ShowInstrument = instruments, Width = 640, Height = 360 });
                var key = $"{layout}{(instruments && layout != VideoLayout.Focus ? "+instruments" : "")}-{(dark ? "dark" : "light")}";
                var dump = Environment.GetEnvironmentVariable("TABFORGE_VIDEO_GOLDEN_DUMP");   // a folder: keeps every frame's raw pixels, to see where two builds differ
                var hashes = Enumerable.Range(0, 24).Select(i =>
                {
                    var pixels = source.Render(i * 160.0);
                    if (!string.IsNullOrEmpty(dump)) { Directory.CreateDirectory(dump); File.WriteAllBytes(Path.Combine(dump, $"{key}-{i:00}.bin"), pixels.ToArray()); }
                    return Convert.ToHexString(SHA256.HashData(pixels)).Substring(0, 8);
                }).ToList();
                table.Append(key).Append('=').AppendLine(string.Join(' ', hashes));
                var expected = VideoSourceGoldenHashes.TryGetValue(key, out var e) ? e : "";
                Check($"video golden {key}: all 24 frames equal the expected pixels", expected == string.Join(' ', hashes), expected.Length == 0 ? "no expected hashes" : "pixels changed");
            }
        if (Environment.GetEnvironmentVariable("TABFORGE_VIDEO_GOLDEN_PERF") is { Length: > 0 } perfFile)   // ms per frame at 1080p, 90 frames at 30 fps
            foreach (var layout in new[] { VideoLayout.ScoreOnly, VideoLayout.Band, VideoLayout.Focus, VideoLayout.ScoreAndBand })
            {
                using var source = new VideoFrameSource(project, timeline, new AppSettings(), new VideoViewSpec { Layout = layout, Tracks = new[] { 0, 1 }, Dark = true, ShowInstrument = layout == VideoLayout.Focus });
                source.Render(0);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 1; i <= 90; i++) source.Render(i * 33.3);
                File.AppendAllText(perfFile, $"{layout}: {clock.Elapsed.TotalMilliseconds / 90:0.0} ms/frame, bakes {source.Timings.Bakes}{Environment.NewLine}");
            }
        if (Environment.GetEnvironmentVariable("TABFORGE_VIDEO_GOLDEN_OUT") is { Length: > 0 } outFile) File.WriteAllText(outFile, table.ToString());
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> VideoSourceGoldenHashes = new()
    {
        ["ScoreOnly-dark"] = "6407B880 D0ECD609 C7E3BC92 BB9750A2 B259D8A6 48AA93BB C21E382E 7765D9B5 080D78B9 674BCF32 E5E43770 69491D20 A7460200 D74CA7D8 B73275EE 151C4FAE FB17095C 5FC153FB 028EA4A4 96BFC2B3 23F69091 055058E1 E078CF37 CBA55932",
        ["ScoreOnly-light"] = "4C924016 9DEA422A E8ECC8D4 15EA7B75 0F7C58AE 4C57E77A F23EFA30 43025BBB 6B10A325 561D9BAB 60FA8722 F69382D7 50A6695D 7940FF44 8DA2A38C 8D272D69 36C6E190 C4EEDE1D 6BA65FF6 DC4933AF BAB8AF20 B5691AF4 C0D6D3EA 8DA5FCB8",
        ["Band-dark"] = "5D91883D 52787840 C58F1776 DA35A670 9FC96256 1223338B B40C4A62 B07F38C5 63D660CC 2BAF84B0 1FFB0F5A E235340E 3F57C7E5 DA9313F6 5B7836DC B15D9C15 0AE953F6 9F98AD83 FDCAC82A 3DFE1EE3 11E85E70 DD44A806 63CCEE0F 0593A803",
        ["Band-light"] = "75F3C7BF 3B043D74 5282CD8E 7F24EF88 7B1ADE6A AF2CDEA0 0886A747 2905D145 B9C9CED1 0F6DB566 E0B249DE 3007D8AB 308453F6 9074E9A9 C069F011 9F013E15 9DA9DF86 409633F9 ABFBF23D D05B60CD 89E99156 FAC60B65 1F592348 83C57B21",
        ["Focus-dark"] = "6B353BF7 10ED4C5C A93D1C05 B68CC88D 0B69B701 FD2E95FF 14234A31 92A4489D E3F187E3 B544177E 98073D91 2CAFA747 5701B57A AFDCC926 A126996C 9EE6982B C8A140E6 A6D71AFE 3C26F998 24BDDC82 1751EAD2 6AFB586E 3FE8C0C5 D8298FDA",
        ["Focus-light"] = "CC346FDA DBB88D48 14A9A683 A48199DC AC638DAE E69F787A 26B2E0FF AB2AF64D DB78C9F9 3418C56E 83E4103E 9F254798 6F5BDAA5 1044A821 64B85FDF 7C5EC51B 53B78018 1824AFAA 4937DDBB 1E4103BF 06804591 02CE4EB1 142865BF 4DBEE2AE",
        ["ScoreAndBand-dark"] = "6F0584DB 49D045D6 A6F09019 DF3B2873 F11C000A B436909B 809C44A7 12F73306 A5CCE99C 6A0EB43E FF9911E5 D5C08FD2 B0C1520F D3474FB7 04543044 B031E3BB 98038268 516D03BC E938C1FE 888F66D2 9EBB92A9 0562AC98 DDA205DB B62761D7",
        ["ScoreAndBand-light"] = "856C9A06 D3255B34 3288BE1F D2D71A15 84E991D1 66795C55 9B69B218 A234BFBC 20BFA286 9504B5FD EFC99552 62699F47 E855BA7A 98B01717 878E35B4 3BF27A7D AE4496B1 C90B0A33 03CE67F2 A63B5E87 2C5EFD63 C6590687 BC56C331 C035FC9B",
        ["Band+instruments-dark"] = "42926EF1 80102B53 3648546A 398F58B8 152250A4 36387CB5 B5EDF1D5 EBDACF77 983C1CFD D6CAB5E3 5354300B 9BAD652D 150A0ACA 9E68CFA8 48A92BEE 3CEBD4E2 2FD576AC 35E3A060 62268989 E2F94360 B47758D5 D7FCF43D F67531D9 2C9329E1",
        ["Band+instruments-light"] = "8A4AD593 BDB7E8A2 341663B3 C30BD5DC 9F6D5770 674DF452 8BDD9770 4EA77495 87F014CF DE60F648 53ED5E5E 531DEFA5 E7DA4836 80190EC9 E9B4C93F E12C9D5C 91C724CE 97BEF4BE D24C3F8C 943F0FB0 3DFC51A6 ED2B335C 62EE9751 16EC38C2",
        ["ScoreAndBand+instruments-dark"] = "4AEA9A63 E2296CA0 222545DE 2EAFA4B0 3C640B6F 01A68082 5E0A156A 779DF67C 5DE7AA5E 230CD57E 4D69F7EA 9199AECF A042C46A 579EF9E8 6F8044AB 5FBF87D5 80A054E6 9D4DFA5A DF6A4714 EE684A15 69E2D984 A38FD02F 783F2AFB C405C615",
        ["ScoreAndBand+instruments-light"] = "641303EF 5B7C8A36 AD528409 C830A641 E6527C8A 56FB4FB1 8C2E58DA 566CFF1E EBAADC1B BDBCE1E2 78437999 FE8A4DB1 B8E62342 901A62BD 2D29FB07 81C5FC29 FCB92B08 7106A909 0250AFE1 DDE42A74 342CEFC5 3094D331 5CF399C4 1DD99306",
    };
}
