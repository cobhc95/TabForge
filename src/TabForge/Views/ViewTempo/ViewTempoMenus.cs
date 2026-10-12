using System.Globalization;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views.ViewTempo;

// Owns: the preset menu of each View and tempo button (zoom, speed, tempo, time / key), with the current value checked and
//       the live key shown beside rows that run a command.
// Does not own: opening or placing the menu (ViewTempoBar), applying a value (IViewTempoHost), the fine-tune popups (ViewTempoPopups).
// Tests: TestToolbarZoomAndSpeed.
public static class ViewTempoMenus
{
    public static readonly string[] ZoomPresets = { "Fit width", "50%", "60%", "75%", "80%", "90%", "100%", "110%", "125%", "150%", "175%", "200%" };
    public static readonly int[] SpeedPresets = { 50, 75, 100, 125, 150, 200 };
    public static readonly int[] TempoPresets = { 60, 80, 100, 120, 140, 160, 180, 200 };

    public static ContextMenu Build(int kind, IViewTempoHost host)
    {
        var menu = new ContextMenu();
        switch (kind)
        {
            case ViewTempoBar.Zoom:
                foreach (var z in ZoomPresets) Add(menu, z, z == host.ZoomText, () => host.SetZoom(z));
                menu.Items.Add(new Separator());
                AddCommand(menu, "Zoom in", "View.ZoomIn", host);
                AddCommand(menu, "Zoom out", "View.ZoomOut", host);
                break;
            case ViewTempoBar.Speed:
                var speed = Math.Round(host.SpeedPercent);
                foreach (var s in SpeedPresets) Add(menu, s + "%", s == speed, () => host.SetSpeedPercent(s));
                break;
            case ViewTempoBar.Tempo:
                var tempo = host.Tempo;
                foreach (var t in TempoPresets.Append(tempo).Distinct().OrderBy(x => x))
                    Add(menu, t.ToString(CultureInfo.InvariantCulture), t == tempo, () => host.SetTempo(t));
                menu.Items.Add(new Separator());
                foreach (var step in new[] { -5, -1, 1, 5 })
                    Add(menu, (step < 0 ? "−" : "+") + Math.Abs(step), false, () => host.SetTempo(host.Tempo + step));
                break;
            default:
                AddCommand(menu, "Time signature…", "Bar.TimeSignature", host);
                AddCommand(menu, "Key signature…", "Bar.KeySignature", host);
                break;
        }
        return menu;
    }

    private static void Add(ContextMenu menu, string header, bool current, Action run)
    {
        var item = new MenuItem { Header = header, IsChecked = current };
        item.Click += (_, _) => run();
        menu.Items.Add(item);
    }

    private static void AddCommand(ContextMenu menu, string header, string id, IViewTempoHost host)
    {
        var item = new MenuItem { Header = header, InputGestureText = HotkeyCatalog.Display(HotkeyCatalog.GestureFor(TooltipShortcuts.Hotkeys, id)) };
        MenuHotkey.SetId(item, id);
        item.Click += (_, _) => host.Run(id);
        menu.Items.Add(item);
    }
}
