using System;
using Avalonia.Controls;
using Avalonia.Threading;
using TabForge.Presets;

namespace TabForge.Avalonia.Views.Presets;

// Owns: the one label rule of every preset selector (edited " *", deleted " (missing)") and the debounced watch that refreshes a selector when
//   the object's values stop matching the preset it was given. The existing "★ " prefix stays the caller's (a user's own preset), never the marker.
// Does not own: the rows, the stores or what picking a preset does.
// Tests: PresetComboTests, PresetMemoryTests.
/// <summary>The shared preset selector behaviour: the remembered preset, its label and the live edited marker.</summary>
public static class PresetCombo
{
    public const string Edited = " *", Missing = " (missing)";
    private const int DebounceMs = 400;

    /// <summary>The row text: the stored name once, then " (missing)" when the store no longer has it, else " *" while the values differ.</summary>
    public static string Decorate(string shown, string name, AppliedPreset? applied, string? current, bool exists)
    {
        if (applied is null || !string.Equals(applied.Name, name, StringComparison.OrdinalIgnoreCase)) return shown;
        if (!exists) return shown?.EndsWith(Missing, StringComparison.Ordinal) == true ? shown : shown + Missing;
        return applied.IsEdited(current) ? shown + Edited : shown;
    }

    /// <summary>The record a pick stores: the preset's name, its kind and the values it just gave the object.</summary>
    public static AppliedPreset? Remember(string name, string kind, string? values) => AppliedPreset.Create(name, kind, values);

    /// <summary>True when the object still matches the remembered preset (false without one).</summary>
    public static bool Matches(AppliedPreset? applied, string? current) => applied is not null && !applied.IsEdited(current);

    /// <summary>
    /// Refreshes a selector when the edited state changes after an edit (never on every pointer move: one cheap compare per debounce tick,
    /// skipped while the list is open). The returned handle stops the watch.
    /// </summary>
    public static IDisposable Watch(Control host, Func<AppliedPreset?> applied, Func<string?> current, Action refresh)
    {
        var last = applied() is { } start ? start.IsEdited(current()) : false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        timer.Tick += (_, _) =>
        {
            if (host is ComboBox { IsDropDownOpen: true }) return;
            if (applied() is not { } now) { last = false; return; }
            var edited = now.IsEdited(current());
            if (edited == last) return;
            last = edited;
            refresh();
        };
        host.DetachedFromVisualTree += (_, _) => timer.Stop();   // a closed window's selector stops watching
        host.AttachedToVisualTree += (_, _) => timer.Start();
        timer.Start();
        return new WatchHandle(timer);
    }

    private sealed class WatchHandle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
