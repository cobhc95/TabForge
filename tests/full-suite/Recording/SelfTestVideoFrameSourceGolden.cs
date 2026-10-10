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
        ["ScoreOnly-dark"] = "7B496E6B BB470541 86B6132E 2118C6A0 AE177B48 077556D7 A53A2615 1363A687 32C0EF29 C644B679 DA538141 0E2AE3F9 6379609E 033F94B5 BB006200 AFDD2C9C BB8C8405 9F970430 86B453ED 95762CA9 C45E9523 85737AD1 BC26C29B 74519396",
        ["ScoreOnly-light"] = "6011DAA6 86893FD4 C7AA41D2 B53E028F 26E5E081 CFCE347C 408A8D1F BD69A74A 800F1A9A AC4BF59F 749EC367 F245C96E FC6B794F 04B1DF06 67CDD810 D1E49D33 87A5E2A0 5C45F04C 1C4C8AD2 E8A1B83C 6DB599DD 3F920170 392C23CC 3D54D742",
        ["Band-dark"] = "F3869C26 83B5702E 0D95F07A 28062C2F E4882DE7 5173BA89 EC99FF7A 75FA3D33 E92054DD D3244B43 3095E185 21C4634A 7C12CE9B 07D977E9 BB6B1118 576FD95C 4101C104 3A852B45 7AD0C5EA B8023291 ADFA038E 13A48FD2 C5D62E9B 4A783272",
        ["Band-light"] = "DB2F7424 BA66B7C7 900287AA BD2EC8E2 6BC6FBE4 0D1B1EA2 6576036E D7CFAFA7 B8FF4938 4E22293B CEBAD739 2C287C5D DB6E303F D9D18EBB E67FF5DE 67F894AE EDC6D964 F11A7628 304C384B 7E034F87 DFF59079 D547DEB4 DB578DBB E38AA53D",
        ["Focus-dark"] = "17DD01FD D104803A CE0113B5 F885A78A 63B6F8E4 B721E3F4 528635F4 80F95A57 029C5FA5 516EEFB9 96759AEA E10ECF75 6121634E 89F44414 4782B276 66EBBA9C 3AE44A12 A305CCE3 DBFECF9D 8FAA39CD 0596DDD7 47508C6A 19EAFDA7 B0173E87",
        ["Focus-light"] = "1A014AFA 23DD04FF 5595A608 2C5F2BE5 AB9EB294 298FDAAC 07FCE1C7 66CE8022 64A5E9B6 8A7086C0 43AE7E9B 15590C32 C406CAEF 42302D7C A1963187 5E1877C7 DC555421 52460297 ABA4F465 633DE31A D880AC16 40DEA062 50590BD7 61623E8C",
        ["ScoreAndBand-dark"] = "083CDFF7 11D963AF A4D2AB06 4816B625 6F2470CC BA9AD280 815A0662 3B7AAEAC 1FA5E7F1 18CA3D64 F4C63579 676C6AFF 23799859 93953C8B 693EA240 F6114841 86910987 4FA38B1A 5E84EF55 EE7B255D 2508FC4E F397ADB5 CBAC4298 8A23F3CA",
        ["ScoreAndBand-light"] = "8BE3CD10 E830BD90 900E5CBA CAF79C81 A0E4DED6 50751040 652AE5F7 B263A17F D4B9D1F9 FABAE7B6 0FD52A57 886A837E 2F685384 590AA164 1B47AEB0 6789DC82 C5EAF42F 172151DE C8B03254 327D404B 822E3061 AA3C64F3 4A9CF0A7 C888A1B5",
        ["Band+instruments-dark"] = "C36030AD D4E90D13 9E309B0A E7D7EF10 EFE368F8 F4FA09B5 E2AA613D D21F08FD 9DC3A51C 14F46CC7 83213F67 A1BB8D7A FD1E8105 388CBC78 BE547CBE D463D89C 02C46E71 2F6BC056 04E53072 157A05F7 4A7E8EA5 06D8109C A1DACFC8 CB9283DF",
        ["Band+instruments-light"] = "C16B8A04 48DFDC8D 4158DCD1 33BF0174 FD3F8CE2 8D070E3C 46D815C5 0251D44E 5FB31204 F327CD00 F625F31D FC5A8F48 C27E86E7 3F41C03E D311851E 256DFCEC 1BE2CDB4 B4B33543 16726F0E 1F91216D 0DF005D4 0A9B14C6 EEA1ED7F 4540892F",
        ["ScoreAndBand+instruments-dark"] = "2FEEB520 A144DC37 9C04BF42 B8C0AFEF 5BAB035B E9B741F0 A9280A2E 110A4E33 5ED1DDFD 26B6BB8E 9911053B 1F98196E 3B41534A 04DF8444 994F7044 CDD18F27 9B9B2A1B F6544876 ACEE923A BFCCB0C0 8C877A1F D63E01D4 DEAB553E 7B1EB062",
        ["ScoreAndBand+instruments-light"] = "3ACAD5A3 B7675F69 5896CEFB AD9F2525 6F521D43 2D18D693 5AE2CA6C 59313292 04AAB238 BBB08C40 3D5349F1 0F6A4DE2 F9F1E8A6 2F9673CC 43803549 875E1CD0 1B9ADC60 DBFB6DF6 9468A572 A97B6260 A58AAFE5 AFB55324 131568D9 85092960",
    };
}
