using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Capture;

namespace TabForge.Avalonia.Capture;

// Owns: the verbs for windows and menus: window (open a tool window by name), close, menu (open a main menu or a submenu so a shot can take its popup).
// Does not own: the window list (ToolWindowCatalog), the shot itself (ShotSteps).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal sealed class ToolSteps(CaptureSession session)
{
    /// <summary>The name MainWindowMenu gives the menu bar.</summary>
    private const string MainMenuName = "MainMenu";

    private readonly ToolWindowCatalog _catalog = new(session);
    private ColourPickerPopup? _picker;   // the colour popup this run opened last, closed before the next one

    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs)
    {
        verbs["window"] = OpenWindow;
        verbs["close"] = Close;
        verbs["menu"] = OpenMenuAsync;
        verbs["colour-popup"] = ColourPopup;
        verbs["set"] = SetValue;
    }

    private async Task OpenWindow(CaptureStep step)
    {
        var window = _catalog.Build(step.Text, step);
        CaptureMetrics.PlaceOffScreen(window);
        window.Show();
        session.Tools[step.Text] = window;
        await session.SettleAsync(CaptureSession.SettleMs * 2);
    }

    private Task Close(CaptureStep step)
    {
        var names = step.Text == "all" ? session.Tools.Keys.ToList() : new List<string> { step.Text };
        foreach (var name in names)
            if (session.Tools.Remove(name, out var window)) window.Close();
            else foreach (var dialog in session.AllWindows().Where(w => w != session.Window && w.GetType().Name == name).ToList()) dialog.Close();   // a dialog a command opened (the Delete bars question)
        if (_picker?.Popup is { } popup) popup.IsOpen = false;
        session.OpenMenu = null;
        return session.SettleAsync();
    }

    /// <summary>Opens the main menu "File" or a submenu "View/Layouts" and keeps its popup content as the target "menu".</summary>
    private async Task OpenMenuAsync(CaptureStep step)
    {
        var path = step.Text.Split('/');
        var menu = session.Window.GetLogicalDescendants().OfType<Menu>().Single(m => m.Name == MainMenuName);
        foreach (var open in menu.Items.OfType<MenuItem>()) open.IsSubMenuOpen = false;
        var item = menu.Items.OfType<MenuItem>().Single(i => Normal(i.Header) == path[0]);
        item.IsSubMenuOpen = true;
        await session.SettleAsync();
        for (var level = 1; level < path.Length; level++)
        {
            item = item.Items.OfType<MenuItem>().Single(i => Normal(i.Header) == path[level]);
            item.IsSubMenuOpen = true;
            await session.SettleAsync();
        }
        session.OpenMenu = PopupContent(item);
    }

    /// <summary>Opens the colour picker popup under the main window (or the "anchor" element) and keeps its panel as the target "menu"; "more":true expands the square, hue and code part.</summary>
    private async Task ColourPopup(CaptureStep step)
    {
        var anchor = step.Option("anchor") is { } name ? new CaptureTargets(session).Resolve("element:" + name) : session.Window;
        if (_picker?.Popup is { } previous) previous.IsOpen = false;
        var picker = _picker = new ColourPickerPopup(step.Text, step.Option("default", step.Text)!, new List<string>());
        _ = picker.AskAsync(anchor, PlacementMode.Center);
        if (step.Option("more", 0) == 1) picker.ShowMore();
        session.OpenMenu = picker.Panel;
        await session.SettleAsync();
    }

    /// <summary>Sets a slider or text box found by name or automation id to a value, as a user dragging it would (the Mixer window must be open).</summary>
    private Task SetValue(CaptureStep step)
    {
        var element = session.Tools.Values.SelectMany(w => w.GetLogicalDescendants().OfType<Control>())
            .Where(c => c.Name == step.Text).Skip(step.Option("nth", 0)).FirstOrDefault() ?? throw new InvalidOperationException($"No element named \"{step.Text}\".");
        var value = step.Option("value", 0.0);
        switch (element)
        {
            case Slider slider: slider.Value = value; break;
            case TextBox box: box.Text = step.Option("text"); break;
            default: throw new InvalidOperationException($"Cannot set {element.GetType().Name}.");
        }
        return session.SettleAsync(80);
    }

    private static string Normal(object? header) => (header as string ?? "").Replace("_", "");

    private static Control PopupContent(ItemsControl menu)
    {
        var first = menu.ContainerFromIndex(0) ?? throw new InvalidOperationException("The menu opened with no item container.");
        var top = TopLevel.GetTopLevel(first) ?? throw new InvalidOperationException("The menu item is not in a window.");
        Control panel = first;
        while (panel.GetVisualParent() is Control parent && parent is not OverlayLayer && !ReferenceEquals(parent, top)) panel = parent;
        return panel;
    }
}
