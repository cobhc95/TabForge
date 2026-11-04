using System;
using System.IO;
using System.Linq;
using TabForge.AudioEngine.Isolation;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 17 upgrade proof: settings files written by an older version still load with
// their values kept, and a Wine prefix marker from a previous build is recognised and
// brought up to the current marker without losing the prefix.
// Does not own: the migration rules (SettingsMigration) or first-run setup (WineRuntime).
// Tests: this class.
public sealed class Stage17UpgradeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-stage17-upgrade-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public Stage17UpgradeTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    // A settings file from before the Editing/Audio sections existed keeps its values
    // and gains the current sections instead of failing.
    [Fact]
    public void OlderSettingsWithoutSectionsMigrateWithValuesKept()
    {
        var path = Path.Combine(_folder, "old-settings.json");
        File.WriteAllText(path, "{\"LeftHanded\":true}");

        var loaded = SettingsFileService.Load(path);

        Assert.True(loaded.Editing.LeftHanded);
        Assert.NotNull(loaded.Audio);
        Assert.NotNull(loaded.Appearance);
        Assert.NotNull(loaded.Follow);
        _output.WriteLine("settings upgrade legacy-top-level kept=True sections=filled");
    }

    // An empty (oldest-shape) settings file loads with defaults and stays loadable.
    [Fact]
    public void EmptyOlderSettingsLoadWithDefaults()
    {
        var path = Path.Combine(_folder, "empty-settings.json");
        File.WriteAllText(path, "{}");

        var loaded = SettingsFileService.Load(path);

        Assert.NotNull(loaded.Editing);
        Assert.NotNull(loaded.Audio);
        var bytes = SettingsFileService.SerializeToBytes(loaded);
        Assert.NotEmpty(bytes);
        _output.WriteLine("settings upgrade empty defaults=True");
    }

    // A prefix marker written by a previous build (older Wine version, same layout) triggers
    // one re-setup and ends with the current version line; the prefix itself is kept.
    [Fact]
    public void OldMarkerWithPreviousWineVersionTriggersSetupAndKeepsPrefix()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var log = Path.Combine(folder.Path, "wine-calls.txt");
        var runtime = Runtime(folder, Script(folder, log, winebootExit: 0));
        Directory.CreateDirectory(runtime.PrefixPath);
        Directory.CreateDirectory(Path.Combine(runtime.PrefixPath, "drive_c"));
        File.WriteAllText(Path.Combine(runtime.PrefixPath, ".tabforge-wine"),
            "wineVersion=wine-9.0-old\narch=win64\ncrashDialog=off\n");
        var sentinel = Path.Combine(runtime.PrefixPath, "drive_c", "keep-me.txt");
        File.WriteAllText(sentinel, "user prefix content");

        runtime.EnsureReady(5000, 5000);

        var marker = File.ReadAllText(Path.Combine(runtime.PrefixPath, ".tabforge-wine"));
        Assert.Contains("wineVersion=wine-9.21-fake", marker);
        Assert.Contains("crashDialog=off", marker);
        Assert.False(File.Exists(Path.Combine(runtime.PrefixPath, ".tabforge-failed")));
        Assert.True(File.Exists(sentinel), "the upgrade must keep the existing prefix content");
        Assert.Contains("wineboot -u", File.ReadAllText(log));
        _output.WriteLine("prefix upgrade old-version re-setup=True kept=True");
    }

    // A marker that already names the current build passes without another wineboot.
    [Fact]
    public void CurrentMarkerPassesWithoutAnotherWineboot()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var folder = new Vst32TestAssets.TempFolder();
        var log = Path.Combine(folder.Path, "wine-calls.txt");
        var runtime = Runtime(folder, Script(folder, log, winebootExit: 0));

        runtime.EnsureReady(5000, 5000);
        var boots = CountBoots(log);
        runtime.EnsureReady(5000, 5000);

        Assert.Equal(boots, CountBoots(log));
        _output.WriteLine("prefix upgrade current-marker no-reboot=True");
    }

    private static int CountBoots(string log) =>
        File.Exists(log) ? File.ReadAllText(log).Split('\n').Count(line => line.Contains("wineboot -u", StringComparison.Ordinal)) : 0;

    private static WineRuntime Runtime(Vst32TestAssets.TempFolder folder, string script)
    {
        var helper = Path.Combine(folder.Path, "tfvst32.exe");
        var helper64 = Path.Combine(folder.Path, "tfvst64.exe");
        File.WriteAllBytes(helper, []);
        File.WriteAllBytes(helper64, []);
        return new WineRuntime(script, helper, Path.Combine(folder.Path, "prefix"), helperPath64: helper64);
    }

    private static string Script(Vst32TestAssets.TempFolder folder, string log, int winebootExit)
    {
        var wine = Path.Combine(folder.Path, "bundle", "wine", "bin", "wine");
        Directory.CreateDirectory(Path.GetDirectoryName(wine)!);
        var text = string.Join("\n", new[]
        {
            "#!/bin/sh",
            "printf '%s\\n' \"$*\" >> '" + log + "'",
            "if [ \"$1\" = \"--version\" ]; then echo wine-9.21-fake; exit 0; fi",
            "if [ \"$1\" = \"wineboot\" ]; then",
            "  if [ " + winebootExit + " -eq 0 ]; then mkdir -p \"$WINEPREFIX/drive_c\" || exit 7; exit 0; fi",
            "  echo 'mock wineboot failure' >&2; exit " + winebootExit,
            "fi",
            "if [ \"$1\" = \"reg\" ]; then exit 0; fi",
            "exit 0",
            "",
        });
        File.WriteAllText(wine, text);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(wine, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return wine;
    }
}
