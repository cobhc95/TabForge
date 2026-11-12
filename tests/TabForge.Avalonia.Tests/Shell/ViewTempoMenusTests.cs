using Avalonia.Headless.XUnit;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the View and tempo bar's preset menus: the tempo menu lists the presets with the current tempo marked and the beat steps,
//   and the time / key menu runs the time signature and key signature commands.
// Does not own: the tempo edit and the bar's wiring (MainWindowTempoBarTests).
public sealed class ViewTempoMenusTests
{
    [AvaloniaFact]
    public void TempoMenuMarksTheCurrentTempoAndTimeKeyMenuRunsBothSignatures()
    {
        var headers = ViewTempoMenus.Tempo(123, _ => { }).Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToList();
        Assert.Contains("✓ 123", headers);
        Assert.Contains("+5", headers);
        Assert.Contains("−5", headers);

        var ran = new List<string>();
        var items = ViewTempoMenus.TimeKey(ran.Add).Items.OfType<MenuItem>().ToList();
        Assert.Equal(2, items.Count);
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(new[] { ViewTempoMenus.KeySignatureCommandId }, ran);
    }

    [AvaloniaFact]
    public void SpeedMenuMarksTheCurrentSpeedAndSetsTheChosenFraction()
    {
        double? chosen = null;
        var items = ViewTempoMenus.Speed(0.75, value => chosen = value).Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, item => item.Header?.ToString() == "✓ 75%");
        items.First(item => item.Header?.ToString() == "50%").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(0.5, chosen);
    }

    [AvaloniaFact]
    public void ZoomMenuMarksTheCurrentZoomAndSetsTheChosenFactor()
    {
        double? chosen = null;
        var items = ViewTempoMenus.Zoom(1.0, value => chosen = value).Items.OfType<MenuItem>().ToList();
        Assert.Contains(items, item => item.Header?.ToString() == "✓ 100%");
        items.First(item => item.Header?.ToString() == "150%").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(1.5, chosen);
    }

    [AvaloniaFact]
    public void TempoPopupStepButtonsSetTheTempoOneBeatAtATime()
    {
        var tempo = 120;
        var popup = (Border)ViewTempoPopups.Tempo(() => tempo, value => tempo = value);
        var buttons = ((StackPanel)popup.Child!).Children.OfType<Button>().ToList();
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(121, tempo);
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(119, tempo);
    }

    [AvaloniaFact]
    public void TempoPopupSliderSpansTheSongTempoRange()
    {
        // The slider covers the same 20-400 BPM as the menu steps and the project settings (it used to stop at 30-300).
        var popup = (Border)ViewTempoPopups.Tempo(() => 350, _ => { });
        var slider = ((StackPanel)popup.Child!).Children.OfType<Slider>().Single();
        Assert.Equal(TabForge.Services.InputLimits.MinTempo, slider.Minimum);
        Assert.Equal(TabForge.Services.InputLimits.MaxTempo, slider.Maximum);
        Assert.Equal(20, slider.Minimum);
        Assert.Equal(400, slider.Maximum);
        Assert.Equal(350, slider.Value);
    }

}
