using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that plain controls take the master layer's WPF look: a button the Button look, a default button the accent look and fill, a text box the 24 DIP field size.
/// Tests: this class.
/// </summary>
public class MasterStyleTests
{
    [AvaloniaFact]
    public void PlainButtonsTakeTheButtonLookAndDefaultButtonsTheAccentLook()
    {
        var plain = new Button { Content = "Apply" };
        var ok = new Button { Content = "OK", IsDefault = true };
        ok.Classes.Add(TabForge.Avalonia.Platform.AvaloniaDefaultLooks.AccentLook);
        var neutralOk = new Button { Content = "OK", IsDefault = true };
        var field = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { plain, ok, neutralOk, field } } };
        window.Show();
        Assert.Contains("Button", plain.Classes);
        Assert.Contains("AccentButton", ok.Classes);
        Assert.DoesNotContain("Button", ok.Classes);
        Assert.Contains("Button", neutralOk.Classes);
        Assert.DoesNotContain("AccentButton", neutralOk.Classes);
        Assert.Equal(24, plain.MinHeight);
        Assert.Equal(24, field.MinHeight);
        var accent = (SolidColorBrush)ok.Background!;
        Assert.NotEqual(((SolidColorBrush)plain.Background!).Color, accent.Color);
    }
}
