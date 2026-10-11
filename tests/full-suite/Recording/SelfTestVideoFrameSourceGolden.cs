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
        ["Band-dark"] = "CABFAEB8 AFC0C3CD B852807C F5FB0A6B 0A4425C1 A04FFCD7 D04A75B9 F84BFE58 05B8EE2C 1FC3DA8C 3095E185 21C4634A 7C12CE9B 07D977E9 BB6B1118 576FD95C 4101C104 3A852B45 7AD0C5EA B4D5A39D 720C4D7D BAEC3D46 4D85C2EB 726D5A75",
        ["Band-light"] = "F8706DC8 515674B4 C4F9F44F 8F2A9F6D 84E09928 96820F7D 848BCE63 55B874C9 011D2BB2 805491E4 CEBAD739 2C287C5D DB6E303F D9D18EBB E67FF5DE 67F894AE EDC6D964 F11A7628 304C384B 2A3CC92F F94E7185 676CD49D 58A140F0 3503D1E1",
        ["Focus-dark"] = "6B353BF7 10ED4C5C A93D1C05 B68CC88D 0B69B701 FD2E95FF 14234A31 92A4489D E3F187E3 B544177E 98073D91 2CAFA747 5701B57A AFDCC926 A126996C 9EE6982B C8A140E6 A6D71AFE 3C26F998 24BDDC82 1751EAD2 6AFB586E 3FE8C0C5 D8298FDA",
        ["Focus-light"] = "CC346FDA DBB88D48 14A9A683 A48199DC AC638DAE E69F787A 26B2E0FF AB2AF64D DB78C9F9 3418C56E 83E4103E 9F254798 6F5BDAA5 1044A821 64B85FDF 7C5EC51B 53B78018 1824AFAA 4937DDBB 1E4103BF 06804591 02CE4EB1 142865BF 4DBEE2AE",
        ["ScoreAndBand-dark"] = "2721B361 A9F37540 C6061E60 0ACF6810 15FA3CA7 77999C8B F02E26B1 5DA641C6 006F8FD1 7E739DEA E3EC1CDB 52A78CAA C93C2536 4FBFED87 D8BE1E68 4603F37E C36EA54C 72C1D367 93A113E5 C619CFA3 ADEEACEA 81DDBE26 91EB627B E1A47D23",
        ["ScoreAndBand-light"] = "0FDD213A 63C1E1F0 1B3CB88D 962A4BF0 18BD07FD BF947E04 1DF54915 29033432 2729F428 E5FC3260 B8CD9F0E B2DB542A 62229513 378EBBD3 C51007D9 1CBF7535 71BA1F65 70C60E86 4C9038E5 854A66D5 2BBF27C3 85722A25 CF6B0888 D1CD55C3",
        ["Band+instruments-dark"] = "DA33B08A FA6E4858 35F9DA35 0954BB39 CB076F1A EDA4D40D C2AA5E74 4D0A8C4A 8B287031 A7FB1D1F 83213F67 A1BB8D7A FD1E8105 388CBC78 BE547CBE D463D89C 02C46E71 2F6BC056 04E53072 B6FE724B B9037FE5 76AF271C AF578D5B 3B2E6BDD",
        ["Band+instruments-light"] = "93DF1BD8 1D40C604 E384AC92 C22057D2 79546260 08B3C0CA C0322656 32191034 46B7FB0E F3019F6A F625F31D FC5A8F48 C27E86E7 3F41C03E D311851E 256DFCEC 1BE2CDB4 B4B33543 16726F0E D65F4E08 7D7730FD 82CC2E3B 24DBC88B 35B46264",
        ["ScoreAndBand+instruments-dark"] = "234AB22A 3D5A0EDE 07E11D37 0005D7C2 5B809123 F6502A35 EA46A1E3 36985F8C 9EB47BDA 0F82B1EF 2DDAEA78 8F771554 A589B529 7CFA7F49 66634703 1291B250 618E46A3 99273728 3E37F95F 75D3CD87 C567B3F1 C8F3ED12 10044549 E4B33812",
        ["ScoreAndBand+instruments-light"] = "3641A5A8 6DD1F90A 1F32FA9C C8FD1594 DD442FF0 98513517 4D93EADF 400FEFE6 9169BFE6 7848302B 67E5E043 DCFCDDF3 5B85C3EF FE8C1B1D A05E57B9 2E41FFD5 DFDF9EB2 981DDD01 B77872C0 CEB68E8A E1F8DF9D 48CA65E7 15B7BC47 5F01F2E1",
    };
}
