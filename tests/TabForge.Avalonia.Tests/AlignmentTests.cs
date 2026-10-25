using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The settings rows' vertical centring and the shared tooltip placement helper.</summary>
public class AlignmentTests
{
    [AvaloniaFact]
    public void CheckNowButtonIsCentredOnItsDescriptionBlock()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.ShowPage(PreferencesCatalog.General);
        window.Show();
        window.UpdateLayout();

        var editor = window.EditorOf("general.checknow");
        var grid = Assert.IsType<Grid>(editor.Parent);
        var label = Assert.IsType<StackPanel>(grid.Children.OfType<StackPanel>().Single());
        Assert.True(editor.Bounds.Height > 0 && label.Bounds.Height > 0);
        var editorCentre = editor.TranslatePoint(new Point(0, 0), grid)!.Value.Y + editor.Bounds.Height / 2;
        var labelCentre = label.TranslatePoint(new Point(0, 0), grid)!.Value.Y + label.Bounds.Height / 2;
        Assert.InRange(editorCentre - labelCentre, -1.5, 1.5);
        window.Close();
    }

    [AvaloniaFact]
    public void PasteSpecialRowChildrenAreVerticallyCentred()
    {
        var window = new PasteSpecialWindow(bars: true);
        var boxes = window.GetLogicalDescendants().OfType<Control>().Where(c => c.Name is "RepeatBox" or "RepeatUp").ToList();
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, control => Assert.Equal(VerticalAlignment.Center, control.VerticalAlignment));
    }

    [AvaloniaFact]
    public void TooltipHelperSetsTheTipAndPointerPlacement()
    {
        var button = new Button();
        TooltipPlacement.SetTip(button, "A long description that must stay on screen");
        Assert.Equal("A long description that must stay on screen", ToolTip.GetTip(button));
        Assert.Equal(PlacementMode.Pointer, ToolTip.GetPlacement(button));
    }
}
