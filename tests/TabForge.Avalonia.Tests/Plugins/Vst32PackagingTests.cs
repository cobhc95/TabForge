using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the stage-7 packaging checks: the helper beside the tests, its PE machine and provenance hash, the project/script wiring that ships
//   it loose, the probe with the deterministic test plug-in, that the shipped helper has no test stall path, and the bridged FX row label.
// Does not own: building the native helper (native\build-tfvst32.ps1) or the crash/hang mechanics (Vst32BridgeCrashTests).
[Collection("Vst32Native")]
public sealed class Vst32PackagingTests
{
    private const string HelperName = "tfvst32.exe";
    private const string HelperName64 = "tfvst64.exe";
    private const string StallVariable = "TABFORGE_TFVST32_TEST_STALL_AUDIO_MS";

    private static string Repo => ArchitectureScan.RepoRoot();
    private static string OutputHelper => Path.Combine(AppContext.BaseDirectory, HelperName);
    private static string OutputHelper64 => Path.Combine(AppContext.BaseDirectory, HelperName64);
    private static string SourceHelper => Path.Combine(Repo, "Source", "src", "TabForge.AudioEngine", "native", HelperName);
    private static string SourceHelper64 => Path.Combine(Repo, "Source", "src", "TabForge.AudioEngine", "native", HelperName64);
    private static string Provenance => Path.Combine(Repo, "Source", "native", "BUILD_PROVENANCE32.md");
    private static string Provenance64 => Path.Combine(Repo, "Source", "native", "BUILD_PROVENANCE64.md");

    [WindowsFact]
    public void BothHelpersShipBesideTheAppWithTheirOwnBitness()
    {
        Assert.True(File.Exists(OutputHelper), $"{HelperName} is copied next to the tests (the publish folder gets it too)");
        Assert.True(File.Exists(OutputHelper64), $"{HelperName64} is copied next to the tests (the publish folder gets it too)");
        Assert.Equal(0x14C, Machine(OutputHelper));
        Assert.Equal(0x8664, Machine(OutputHelper64));
        Assert.Equal(Sha256(SourceHelper), Sha256(OutputHelper));
        Assert.Equal(Sha256(SourceHelper64), Sha256(OutputHelper64));
    }

    [WindowsFact]
    public void TheProvenanceRecordsEachShippedHelpersHash()
    {
        foreach (var (provenance, helper, output) in new[]
        {
            (Provenance, HelperName, OutputHelper),
            (Provenance64, HelperName64, OutputHelper64),
        })
        {
            var text = File.ReadAllText(provenance);
            var pattern = "^" + Regex.Escape(helper) + @" SHA-256:\s*`?([0-9a-fA-F]{64})`?\s*$";
            var match = Regex.Match(text, pattern, RegexOptions.Multiline);
            Assert.True(match.Success, $"{provenance} has no {helper} SHA-256 line");
            Assert.Equal(Sha256(output), match.Groups[1].Value.ToLowerInvariant());
        }
    }

    /// <summary>
    /// Regression for Stage 13 F1: the public build guide named an older MSVC version and the hash/size of a previous
    /// tfvst3.dll, so a maintainer rebuilding exactly as documented produced a file Package-Release.ps1 rejects. The
    /// guide, the provenance record and the shipped DLL must name one build.
    /// </summary>
    [Fact]
    public void TheTfvst3BuildGuideMatchesTheShippedDllAndItsProvenance()
    {
        var guide = File.ReadAllText(Path.Combine(Repo, "Source", "native", "tfvst3", "BUILD.md"));
        var provenance = File.ReadAllText(Path.Combine(Repo, "Source", "native", "BUILD_PROVENANCE.md"));
        var shipped = Path.Combine(Repo, "Source", "src", "TabForge.AudioEngine", "native", "tfvst3.dll");
        Assert.True(File.Exists(shipped), $"{shipped} is missing");

        var recorded = Regex.Match(provenance, @"^tfvst3\.dll SHA-256:\s*([0-9a-fA-F]{64})\s*$", RegexOptions.Multiline);
        Assert.True(recorded.Success, "BUILD_PROVENANCE.md has no tfvst3.dll SHA-256 line");
        var compiler = Regex.Match(provenance, @"^- Compiler:\s*MSVC\s+([0-9.]+)\s*$", RegexOptions.Multiline);
        Assert.True(compiler.Success, "BUILD_PROVENANCE.md has no MSVC compiler version");

        var guideHash = Regex.Match(guide, @"\(([0-9]{1,3}(?:,[0-9]{3})*) bytes\) has SHA-256\s+```\s*([0-9a-fA-F]{64})\s*```");
        Assert.True(guideHash.Success, "BUILD.md no longer states the checked-in DLL's size and SHA-256");
        Assert.Equal(Sha256(shipped), recorded.Groups[1].Value.ToLowerInvariant());
        Assert.Equal(recorded.Groups[1].Value.ToLowerInvariant(), guideHash.Groups[2].Value.ToLowerInvariant());
        Assert.Equal(new FileInfo(shipped).Length, long.Parse(guideHash.Groups[1].Value.Replace(",", "")));
        Assert.Contains($"MSVC {compiler.Groups[1].Value}", guide);
    }

    [Fact]
    public void TheProjectsAndReleaseScriptsShipBothHelpersLoose()
    {
        var engine = File.ReadAllText(Path.Combine(Repo, "Source", "src", "TabForge.AudioEngine", "TabForge.AudioEngine.csproj"));
        foreach (var helper in new[] { HelperName, HelperName64 })
        {
            Assert.Contains($@"native\{helper}", engine);
            Assert.Contains($"Link=\"{helper}\"", engine);
        }
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", engine);
        Assert.Contains("CopyToPublishDirectory=\"PreserveNewest\"", engine);

        var app = File.ReadAllText(Path.Combine(Repo, "Source", "src", "TabForge.Avalonia", "TabForge.Avalonia.csproj"));
        Assert.Contains("'%(ResolvedFileToPublish.Filename)' == 'tfvst32'", app);
        Assert.Contains("'%(ResolvedFileToPublish.Filename)' == 'tfvst64'", app);

        var release = File.ReadAllText(Path.Combine(Repo, "Source", "tools", "Package-Release.ps1"));
        Assert.Contains("BUILD_PROVENANCE32.md", release);
        Assert.Contains("BUILD_PROVENANCE64.md", release);
        Assert.Contains("'tfvst32.exe'", release);
        Assert.Contains("'tfvst64.exe'", release);
        Assert.Contains("-AllowUnprovenancedNative", release);
        Assert.Contains("is missing from the publish folder", release);

        // The installer copies the whole publish folder, so the loose helpers reach an installed build too.
        var installer = File.ReadAllText(Path.Combine(Repo, "Source", "installer", "TabForge.iss"));
        Assert.Contains(@"Source: ""{#SourceDir}\*""", installer);
    }

    [Fact]
    public void PackagingScriptsRefuseThePrivateMatrixManifest()
    {
        var linux = File.ReadAllText(Path.Combine(Repo, "Source", "tools", "package-linux.sh"));
        Assert.Contains("manifest.private*.json", linux);
        Assert.Contains("refusing to package", linux);
        var publish = File.ReadAllText(Path.Combine(Repo, "Source", "tools", "Publish.ps1"));
        Assert.Contains("manifest.private*.json", publish);
        Assert.Contains("refusing to package", publish);
        var mac = File.ReadAllText(Path.Combine(Repo, "Source", "tools", "package-mac.sh"));
        Assert.Contains("manifest.private*.json", mac);
        Assert.Contains("refusing to package", mac);
    }

    [Vst32Fact]
    public void ProbeIdentifiesTheDeterministicTestPlugin()
    {
        var path = Vst32TestAssets.TestPluginPath!;
        var (exit, output, error) = Vst32TestAssets.RunHelperCaptured("--probe", path, Vst32TestAssets.Sha256(path));
        Assert.True(exit == 0, $"probe exit {exit}: {error}");
        Assert.Equal("Effect|TabForge|Passthrough32", output.Trim());
    }

    [Vst64Fact]
    public void ProbeIdentifiesTheDeterministic64BitTestPlugin()
    {
        var path = Vst32TestAssets.TestPluginPath64!;
        var (exit, output, error) = Vst32TestAssets.RunHelper64Captured("--probe", path, Vst32TestAssets.Sha256(path));
        Assert.True(exit == 0, $"probe exit {exit}: {error}");
        Assert.Equal("Effect|TabForge|Passthrough32", output.Trim());
    }

    [Vst32Fact]
    public void ProbeRefusesAnEmptyOrNoneFingerprintBeforeLoading()
    {
        var path = Vst32TestAssets.TestPluginPath!;
        var (noneExit, noneOutput, noneError) = Vst32TestAssets.RunHelperCaptured("--probe", path, "none");
        Assert.Equal(5, noneExit);
        Assert.Equal("", noneOutput.Trim());
        Assert.Contains("fingerprint", noneError, StringComparison.OrdinalIgnoreCase);
        var (emptyExit, _, _) = Vst32TestAssets.RunHelperCaptured("--probe", path, "");
        Assert.Equal(5, emptyExit);
    }

    [Vst32Fact]
    public void TheShippedHelperDoesNotHonourTheTestStallVariable()
    {
        Environment.SetEnvironmentVariable(StallVariable, "3000");
        RemotePlugin plugin;
        try
        {
            plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, maxBlock: 256,
                bufferSlots: 2, helperPath: Vst32TestAssets.HelperPath!);
        }
        finally { Environment.SetEnvironmentVariable(StallVariable, null); }

        using (plugin)
        {
            var input = Enumerable.Repeat(0.25f, 256).ToArray();
            var output = new[] { new float[256], new float[256] };
            for (var block = 0; block < 5; block++)
            {
                plugin.Process(new[] { input, input }, output, input.Length, ReadOnlySpan<BlockMidi>.Empty,
                    Vst32TestAssets.Transport(block, input.Length));
                Assert.Equal(input, output[0]);
            }
            Assert.Equal(0, plugin.MissedBlocks);   // the test copy would miss every block for 3 s
            Assert.False(plugin.LinkFailed);
            Assert.False(Vst32TestAssets.WaitsForExit(plugin.HostProcessId, 10));
        }
    }

    [AvaloniaTheory]
    [InlineData("Bridged32", "Acme · 32-bit (bridged)")]
    [InlineData("Bridged64", "Acme · 64-bit (bridged)")]
    public void TheFxChainRowShowsTheBridgedFormat(string kind, string expected)
    {
        var format = kind == "Bridged64" ? PluginFormats.Bridged64 : PluginFormats.Bridged32;
        var chain = new List<PluginSlot>
        {
            new() { Name = kind, Vendor = "Acme", Format = format },
        };
        var window = FxTestKit.Window(FxTestKit.Track(chain));
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            var row = FxTestKit.List(window).GetLogicalDescendantsOf<TextBlock>().Single(t => t.Name == "RowVendor");
            Assert.Equal(expected, row.Text);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AMissingHelperIsAReadableLoadFailure()
    {
        var folder = Directory.CreateTempSubdirectory("tf-vst32-missing").FullName;
        try
        {
            var path = Path.Combine(folder, "Plug32.dll");
            var spec = new PluginSpec(path, PluginFormats.Bridged32, IsInstrument: false, Enabled: true, Wet: 100, State: null,
                Name: "Plug", Id: "plug", LocationTrusted: true);

            var error = Assert.Throws<InvalidOperationException>(() => Vst32Bridge.Launch(spec, folder));
            var text = EngineSession.LoadFailureText(spec, error);

            Assert.Contains("32-bit helper", text);
            Assert.Contains(Vst32Bridge.HelperName, text);
            Assert.Contains("missing from the TabForge folder", text);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [WindowsFact]
    public void AMissing64BitHelperIsAReadableLoadFailure()
    {
        var folder = Directory.CreateTempSubdirectory("tf-vst64-missing").FullName;
        try
        {
            var path = Path.Combine(folder, "Plug64.dll");
            File.WriteAllBytes(path, Vst32TestAssets.PeImage(x86: false));
            var spec = new PluginSpec(path, PluginFormats.Bridged64, IsInstrument: false, Enabled: true, Wet: 100, State: null,
                Name: "Plug", Id: "plug", LocationTrusted: true);

            var error = Assert.Throws<InvalidOperationException>(() => Vst32Bridge.Launch(spec, folder));
            var text = EngineSession.LoadFailureText(spec, error);

            Assert.Contains("64-bit Windows VST2", text);
            Assert.Contains("loads natively", text);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static ushort Machine(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        return BitConverter.ToUInt16(bytes, peOffset + 4);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
