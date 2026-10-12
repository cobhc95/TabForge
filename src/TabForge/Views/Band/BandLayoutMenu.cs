using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views.Band;

// Owns: the Band view's layout choice: the roll-down button after the "BAND VIEW" caption, its menu (fretboard beside or above the
//   tab, track colour frame) and the two commands behind it (Band.ToggleFretboardAbove, Band.ToggleColourFrame).
// Does not own: how a row arranges itself (BandRow.Arrange), the frame's drawing (BandRowFrame), the stored values
//   (BandSettings.FretboardAbove, BandSettings.ColourFrame) or saving them (the remember callback).
// Tests: TestBandPillsAndRows.
internal sealed class BandLayoutMenu
{
    internal const string ToggleAboveId = "Band.ToggleFretboardAbove", ToggleFrameId = "Band.ToggleColourFrame";
    internal const string PlaceId = "bandplace", FrameId = "bandframe", Beside = "beside", Above = "above";
    private readonly Func<BandSettings> _band;
    private readonly Action<string> _remember;
    private readonly Action? _reset;
    internal const string ResetId = "bandresetview";
    private readonly Button _button;

    public BandLayoutMenu(Button button, Func<BandSettings> band, Action<string> remember, Action? reset = null)
    {
        _button = button; _band = band; _remember = remember; _reset = reset;
        button.Click += (_, _) => Open();
    }

    /// <summary>The small square button with a chevron that opens the menu.</summary>
    public static Button Button()
    {
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 1,1 L 5,5 L 9,1"), Width = 10, Height = 6, IsHitTestVisible = false };
        icon.SetResourceReference(FrameworkElement.StyleProperty, "IconPath");
        icon.Width = 10; icon.Height = 6;
        var button = new Button { Content = icon, Width = 22, Height = 22, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 0), Focusable = false };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ToolButton");
        System.Windows.Automation.AutomationProperties.SetName(button, "Band view layout");
        TooltipShortcuts.Bind(button, "Band view layout", ToggleAboveId);
        return button;
    }

    /// <summary>Makes the caption and the chevron one button: the caption moves inside the button, so the whole of it has the toolbar buttons' hover shade and pressed look, the normal pointer, and one click target, with no box at rest.</summary>
    public static void AlsoOpens(TextBlock caption, Button button)
    {
        (caption.Parent as Panel)?.Children.Remove(caption);
        var chevron = button.Content as UIElement;
        button.Content = null;
        caption.Margin = new Thickness(0, 0, 8, 0);
        caption.IsHitTestVisible = false;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(caption);
        if (chevron is not null) row.Children.Add(chevron);
        button.Content = row;
        button.Width = double.NaN;
        button.Padding = new Thickness(8, 0, 8, 0);
        button.Margin = new Thickness(4, 2, 8, 0);
    }

    /// <summary>The menu's rows for the current choice; each carries its command's key.</summary>
    internal static List<MenuSpec> Build(BandSettings band, Func<string, string> keys) => new()
    {
        new() { Header = "Fretboard beside tab", Id = PlaceId, Arg = Beside, Checkable = true, Radio = true, Checked = !band.FretboardAbove, SettingKey = "band.fretboardabove", Shortcut = keys(ToggleAboveId) },
        new() { Header = "Fretboard above tab", Id = PlaceId, Arg = Above, Checkable = true, Radio = true, Checked = band.FretboardAbove, SettingKey = "band.fretboardabove", Shortcut = keys(ToggleAboveId) },
        MenuSpec.Separator(),
        new() { Header = "Track colour frame", Id = FrameId, Checkable = true, Checked = band.ColourFrame, SettingKey = "band.colourframe", Shortcut = keys(ToggleFrameId) },
        MenuSpec.Separator(),
        new() { Header = "Reset view", Id = ResetId, ToolTip = "Back to the Band view's defaults in every tab: fretboard above the tab, the first row's fretboard only, the colour frame, and rows as tall as their lines of music." },
    };

    /// <summary>The last menu built (for tests and --capture).</summary>
    internal ContextMenu? LastMenu { get; private set; }

    private void Open()
    {
        var menu = SpecMenus.New("Band view layout", Build(_band(), id => HotkeyCatalog.DisplayAll(TooltipShortcuts.Hotkeys, id)), RunMenu, _button, PlacementMode.Bottom);
        LastMenu = menu;
        for (DependencyObject? d = _button; d is not null; d = LogicalTreeHelper.GetParent(d)) if (d is BandView view) { view.RaiseLayoutMenuOpening(menu); break; }
        SpecMenus.Open(menu, _button, null, false);
    }

    internal void RunMenu(MenuSpec spec)
    {
        if (spec.Id == PlaceId) SetAbove(spec.Arg == Above);
        else if (spec.Id == FrameId) Run(ToggleFrameId);
        else if (spec.Id == ResetId) _reset?.Invoke();
    }

    /// <summary>Runs one of the two commands by its hotkey id; false for any other id.</summary>
    public bool Run(string id)
    {
        var band = _band();
        if (id == ToggleAboveId) { SetAbove(!band.FretboardAbove); return true; }
        if (id != ToggleFrameId) return false;
        band.ColourFrame = !band.ColourFrame;
        _remember("Band view track colour frame: " + (band.ColourFrame ? "on" : "off"));
        return true;
    }

    private void SetAbove(bool above)
    {
        _band().FretboardAbove = above;
        _remember("Band view: fretboard " + (above ? "above" : "beside") + " the tab");
    }
}
