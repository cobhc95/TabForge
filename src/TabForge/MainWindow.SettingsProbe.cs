using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TabForge.Services;

namespace TabForge;

// MainWindow, settings probe: `TabForge.exe <song> --probe-settings <report.txt>` changes every Settings row
// in turn through the same live-preview path the Settings window uses, records errors and whether the
// window visibly changed, then restores the original settings. Nothing is saved.
public partial class MainWindow
{
    public void RunSettingsProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "settings probe report");
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            _probeNoSave = true;
            var original = JsonSerializer.Serialize(_settings);
            AppSettings Fresh() => JsonSerializer.Deserialize<AppSettings>(original)!;
            try
            {
                await Task.Delay(1500);
                // A bar selection so the selection colour/intensity rows have something to show.
                if (Environment.GetEnvironmentVariable("TF_PROBE_SELECT") is { Length: > 0 }) { Editor.SelectMeasureRange(1, 2); await Task.Delay(200); }
                var baseline = WindowHash();
                var rows = SettingsCatalog.Build(Fresh()).Select(d => d.Key).ToList();
                int changed = 0, unchanged = 0, failed = 0;
                var only = Environment.GetEnvironmentVariable("TF_PROBE_ONLY");
                if (!string.IsNullOrEmpty(only)) rows = rows.Where(k => k.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var key in rows)
                {
                    var copy = Fresh();
                    var row = SettingsCatalog.Build(copy).First(d => d.Key == key);
                    var value = ProbeValue(row);
                    if (value is null) { report.AppendLine($"SKIP      {key}"); continue; }
                    var before = WindowHash();
                    if (Environment.GetEnvironmentVariable("TF_PROBE_SHOTS") is { Length: > 0 } shotsDir)
                    {
                        System.IO.Directory.CreateDirectory(shotsDir);
                        SaveWindowPng(System.IO.Path.Combine(shotsDir, key + "-before.png"));
                    }
                    await Task.Delay(60);
                    if (WindowHash() != before) { report.AppendLine($"NOISY     {key} (window changes by itself)"); continue; }
                    try
                    {
                        row.Set(value);
                        PreviewPreferences(copy);
                        await Task.Delay(120);
                        var visible = WindowHash() != before;
                        if (Environment.GetEnvironmentVariable("TF_PROBE_SHOTS") is { Length: > 0 } shots)
                        {
                            SaveWindowPng(System.IO.Path.Combine(shots, key + "-after.png"));
                        }
                        if (visible) changed++; else unchanged++;
                        report.AppendLine($"{(visible ? "VISIBLE" : "no-visual")} {key} = {value}   [{row.Category} / {row.Title}]");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        report.AppendLine($"ERROR     {key} = {value}: {ex.GetBaseException().Message}");
                    }
                    finally
                    {
                        PreviewPreferences(Fresh());
                        await Task.Delay(120);
                    }
                }
                report.Insert(0, $"{rows.Count} rows: {changed} visibly changed the window, {unchanged} no visual change, {failed} errors\n");
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex}"); }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }

    private void SaveWindowPng(string file)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(file);
        encoder.Save(stream);
    }

    private int WindowHash()
    {
        UpdateLayout();
        var width = Math.Max(1, (int)ActualWidth); var height = Math.Max(1, (int)ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var hash = new HashCode();
        for (var i = 0; i < pixels.Length; i += 16) hash.Add(pixels[i]);
        return hash.ToHashCode();
    }

    private static object? ProbeValue(SettingDescriptor row)
    {
        var current = row.Get();
        switch (row.Kind)
        {
            case SettingKind.Bool: return !(current is bool b && b);
            case SettingKind.Choice:
                return row.Choices.FirstOrDefault(c => !string.Equals(c, current?.ToString(), StringComparison.OrdinalIgnoreCase));
            case SettingKind.Number:
            {
                var now = Convert.ToDouble(current ?? row.Min, CultureInfo.InvariantCulture);
                // A large step so a visual effect, if any, is noticeable.
                var span = row.Max - row.Min;
                var candidate = now + span / 3 <= row.Max ? now + span / 3 : now - span / 3;
                candidate = Math.Round(Math.Clamp(candidate, row.Min, row.Max), row.Decimals);
                return current is int ? (object)(int)Math.Round(candidate) : candidate;
            }
            case SettingKind.Colour:
                return string.Equals(current?.ToString(), "#D0306E", StringComparison.OrdinalIgnoreCase) ? "#2DB36A" : "#D0306E";
            default: return null;
        }
    }

    /// <summary>`--probe-playback-visuals <folder>`: screenshots mid-playback with default settings, then with
    /// every playback colour set to magenta and thicker/stronger values, to confirm they reach the screen.</summary>
    public void RunPlaybackVisualsProbe(string folder)
    {
        var dir = FilePathPolicy.OutputDirectory(folder, "probe folder");
        System.IO.Directory.CreateDirectory(dir);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            _probeNoSave = true;
            var original = JsonSerializer.Serialize(_settings);
            try
            {
                await Task.Delay(1500);
                async Task Shot(string name)
                {
                    PlayFromStart();
                    await Task.Delay(2200);
                    SaveWindowPng(System.IO.Path.Combine(dir, name + ".png"));
                    StopPlayback();
                    await Task.Delay(400);
                }
                await Shot("playback-default");
                var copy = JsonSerializer.Deserialize<AppSettings>(original)!;
                var f = copy.Follow;
                f.HighlightPlayedBeat = true; f.HighlightColour = "#FF00FF"; f.HighlightBackground = "#5A005A";
                f.PlayheadColour = "#FF00FF"; f.PlayheadThickness = 6; f.DurationTintEnabled = true;
                f.DurationGlowColour = "#FF00FF"; f.DurationGlowOpacity = 0.9; f.SectionGlowIntensity = 1;
                PreviewPreferences(copy);
                await Task.Delay(300);
                await Shot("playback-magenta");
            }
            catch (Exception ex) { DiagnosticFileService.WriteText(System.IO.Path.Combine(dir, "error.txt"), ex.ToString()); }
            PreviewPreferences(JsonSerializer.Deserialize<AppSettings>(original)!);
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }
}
