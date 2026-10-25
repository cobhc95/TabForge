using TabForge.Avalonia.Platform;
using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;

namespace TabForge.Avalonia.Views;

// Owns: the preset menus of the View and tempo bar's tempo button (the tempo presets with the current tempo marked, then the one
//   and five beat steps) and of its speed button (the speed presets with the current one marked), of its zoom button (the zoom presets with the current one marked), of its time / key button (the time signature and key signature commands), with their texts.
// Does not own: opening a menu on the bar (MainWindowTempoBar), the tempo edit (the setter the caller passes), the commands' bodies.
// Tests: TabForge.Avalonia.Tests.ViewTempoMenusTests.
public static class ViewTempoMenus
{
    public const int MinimumTempo = 20;
    public const int MaximumTempo = 400;
    public const string TimeSignatureCommandId = "Bar.TimeSignature";
    public const string KeySignatureCommandId = "Bar.KeySignature";

    private const string CurrentMark = "✓ ";
    private const string MinusSign = "−";
    private static readonly int[] TempoPresets = { 60, 80, 100, 120, 140, 160, 180, 200 };
    private static readonly int[] SpeedPresets = { 25, 50, 75, 90, 100, 110, 125, 150, 200 };
    private static readonly int[] TempoSteps = { -5, -1, 1, 5 };
    private static readonly int[] ZoomPresets = { 50, 75, 100, 125, 150, 200 };

    /// <summary>The tempo menu: the presets with the current tempo in order (the current one marked), a separator, then the steps.</summary>
    public static ContextMenu Tempo(int current, Action<int> setTempo)
    {
        var menu = MenuStability.CreateContextMenu();
        foreach (var bpm in TempoPresets.Append(current).Distinct().OrderBy(value => value))
            menu.Items.Add(Item(bpm == current ? CurrentMark + bpm : bpm.ToString(CultureInfo.InvariantCulture), () => setTempo(bpm)));
        menu.Items.Add(new Separator());
        foreach (var step in TempoSteps)
            menu.Items.Add(Item((step < 0 ? MinusSign : "+") + Math.Abs(step), () => setTempo(Math.Clamp(current + step, MinimumTempo, MaximumTempo))));
        return menu;
    }

    /// <summary>The speed menu: the speed presets in percent, the current one marked; each sets the speed as a fraction (1 is the written tempo).</summary>
    public static ContextMenu Speed(double current, Action<double> setSpeed)
    {
        var menu = MenuStability.CreateContextMenu();
        foreach (var percent in SpeedPresets)
        {
            var text = percent.ToString(CultureInfo.InvariantCulture) + "%";
            var isCurrent = Math.Abs(current * 100 - percent) < 0.5;
            menu.Items.Add(Item(isCurrent ? CurrentMark + text : text, () => setSpeed(percent / 100.0)));
        }
        return menu;
    }

    /// <summary>The zoom menu: the zoom presets in percent, the current one marked; each sets the zoom as a factor (1 is 100%).</summary>
    public static ContextMenu Zoom(double current, Action<double> setZoom)
    {
        var menu = MenuStability.CreateContextMenu();
        foreach (var percent in ZoomPresets)
        {
            var text = percent.ToString(CultureInfo.InvariantCulture) + "%";
            var isCurrent = Math.Abs(current * 100 - percent) < 0.5;
            menu.Items.Add(Item(isCurrent ? CurrentMark + text : text, () => setZoom(percent / 100.0)));
        }
        return menu;
    }

    /// <summary>The time / key menu: the time signature and key signature commands, each run through the callback with its command id.</summary>
    public static ContextMenu TimeKey(Action<string> run)
    {
        var menu = MenuStability.CreateContextMenu();
        menu.Items.Add(Item("Time signature…", () => run(TimeSignatureCommandId)));
        menu.Items.Add(Item("Key signature…", () => run(KeySignatureCommandId)));
        return menu;
    }

    private static MenuItem Item(string header, Action run)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => run();
        return item;
    }
}
