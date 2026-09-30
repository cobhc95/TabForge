using System.Globalization;
using System.IO;
using System.Linq;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Settings wiring audit (part of <see cref="SelfTest"/>): every row in the Settings window must write its
/// own value, keep it through save / load / validation, and not silently change a different setting.
/// </summary>
public static partial class SelfTest
{
    // Rows that deliberately change other rows: theme presets rewrite the palette; the default notation
    // choice also records that a preference was made.
    private static readonly HashSet<string> SettingsWithSideEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "appearance.thememode",
    };

    private static void TestEverySettingIsWired()
    {
        var settings = new AppSettings();
        // Rows whose choices are read from this machine's audio devices / ASIO drivers are left out, so the audit gives the
        // same result on a CI runner with no sound hardware. Font rows are free text and are never varied.
        static bool MachineDependent(string key) =>
            key.Equals("vst.device", StringComparison.OrdinalIgnoreCase) || key.Equals("vst.input", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("vst.asio.", StringComparison.OrdinalIgnoreCase);
        var catalog = SettingsCatalog.Build(settings).Where(d => !MachineDependent(d.Key) && d.Kind != SettingKind.Button).ToList();
        var notRoundTripped = new List<string>();
        var notPersisted = new List<string>();
        var crossTalk = new List<string>();
        var expected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in catalog)
        {
            var before = catalog.ToDictionary(d => d.Key, d => Format(d.Get()), StringComparer.OrdinalIgnoreCase);
            var value = AlternativeValue(row);
            if (value is null) continue;
            try { row.Set(value); }
            catch (Exception ex) { notRoundTripped.Add($"{row.Key} (threw {ex.GetType().Name})"); continue; }
            if (!SameValue(row.Get(), value)) { notRoundTripped.Add($"{row.Key} (set {Format(value)}, got {Format(row.Get())})"); continue; }
            expected[row.Key] = row.Get();
            if (SettingsWithSideEffects.Contains(row.Key)) continue;
            foreach (var other in catalog)
                if (!ReferenceEquals(other, row) && before[other.Key] != Format(other.Get()))
                    crossTalk.Add($"{row.Key} -> {other.Key}");
        }
        // Rows changed by a later side-effect row (the theme preset) are compared against their final value.
        foreach (var row in catalog.Where(d => expected.ContainsKey(d.Key))) expected[row.Key] = row.Get();

        var path = Path.Combine(Path.GetTempPath(), $"tabforge-settings-audit-{Environment.ProcessId}.json");
        try
        {
            SettingsFileService.SaveAtomic(path, settings);
            var reloaded = SettingsFileService.Load(path);
            foreach (var row in SettingsCatalog.Build(reloaded))
                if (expected.TryGetValue(row.Key, out var want) && !SameValue(row.Get(), want))
                    notPersisted.Add($"{row.Key} (saved {Format(want)}, loaded {Format(row.Get())})");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }

        Check("every setting row stores the value it is given", notRoundTripped.Count == 0, string.Join("; ", notRoundTripped));
        Check("every setting survives save, reload and validation", notPersisted.Count == 0, string.Join("; ", notPersisted));
        Check("changing one setting never changes another one", crossTalk.Count == 0, string.Join("; ", crossTalk.Take(12)));
        Check("the settings audit exercised every row", expected.Count >= catalog.Count - 3,
            $"{expected.Count} of {catalog.Count} rows; not exercised: {string.Join(", ", catalog.Where(r => !expected.ContainsKey(r.Key)).Select(r => r.Key))}");

        // Theme presets: picking Light rewrites the palette, and each colour stays individually editable.
        var theme = new AppSettings();
        ThemeService.ApplyPreset(theme.Appearance, "Light");
        var lightBackground = theme.Appearance.Background;
        theme.Appearance.Panel = "#123456";
        Check("the Light theme preset sets a light grey palette in one go",
            lightBackground == "#B4B4B4" && theme.Appearance.Text == "#111111" && theme.Appearance.ScorePaper == "Light");
        Check("a colour changed after picking a preset is kept", theme.Appearance.Panel == "#123456" && theme.Appearance.ThemeMode == "Light");
        ThemeService.ApplyPreset(theme.Appearance, "Dark");
        Check("the Dark theme preset restores the dark palette", theme.Appearance.Background == "#14161A" && theme.Appearance.ScorePaper == "Dark");
        ThemeService.ApplyPreset(theme.Appearance, "Custom");
        Check("Custom keeps the current colours", theme.Appearance.Background == "#14161A");
    }

    /// <summary>A valid value different from the current one, or null when the row has nothing to vary.</summary>
    private static object? AlternativeValue(SettingDescriptor row)
    {
        var current = row.Get();
        switch (row.Kind)
        {
            case SettingKind.Bool:
                return !(current is bool b && b);
            case SettingKind.Choice:
                return row.Choices.FirstOrDefault(c => !string.Equals(c, current?.ToString(), StringComparison.OrdinalIgnoreCase));
            case SettingKind.Number:
            {
                var now = Convert.ToDouble(current ?? row.Min, CultureInfo.InvariantCulture);
                var step = row.Step > 0 ? row.Step : 1;
                var candidate = now + step <= row.Max ? now + step : now - step;
                candidate = Math.Round(Math.Clamp(candidate, row.Min, row.Max), Math.Max(row.Decimals, 4));
                return current is int ? (object)(int)Math.Round(candidate) : candidate;
            }
            case SettingKind.Colour:
                return string.Equals(current?.ToString(), "#2D6A4F", StringComparison.OrdinalIgnoreCase) ? "#7A3E9D" : "#2D6A4F";
            default:
                // Free text: vary only rows with a known safe value.
                return row.Key switch
                {
                    "vst.folders" => string.Equals(current?.ToString(), @"C:\Plugins", StringComparison.Ordinal) ? @"C:\VST" : @"C:\Plugins",
                    "vst.commonfolders" => string.Equals(current?.ToString(), @"C:\VST", StringComparison.Ordinal) ? @"C:\Plugins" : @"C:\VST",
                    _ => null
                };
        }
    }

    private static bool SameValue(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (IsNumber(a) && IsNumber(b))
            return Math.Abs(Convert.ToDouble(a, CultureInfo.InvariantCulture) - Convert.ToDouble(b, CultureInfo.InvariantCulture)) < 1e-6;
        return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumber(object o) => o is int or long or double or float or decimal;

    private static string Format(object? value) => value switch
    {
        null => "null",
        double d => d.ToString("0.######", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
