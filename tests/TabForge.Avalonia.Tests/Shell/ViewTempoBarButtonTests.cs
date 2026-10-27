using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that each View and tempo bar button runs its own left-click action once it is set, and that the buttons without one stay disabled.
/// Does not own: the bar's drawing (PanelBarsTests) or the commands the zoom and time-signature buttons run (MainWindowTempoBar).
/// Tests: this class.
/// </summary>
public class ViewTempoBarButtonTests
{
    [AvaloniaFact]
    public void EachViewTempoButtonRunsItsActionOnceOrStaysDisabled()
    {
        var bar = new ViewTempoBarView();
        var presses = new int[4];
        bar.SetPressed(ViewTempoBarPresenter.Zoom, () => presses[ViewTempoBarPresenter.Zoom]++);
        bar.SetPressed(ViewTempoBarPresenter.TimeKey, () => presses[ViewTempoBarPresenter.TimeKey]++);

        bar.Buttons[ViewTempoBarPresenter.Zoom].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bar.Buttons[ViewTempoBarPresenter.TimeKey].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, presses[ViewTempoBarPresenter.Zoom]);
        Assert.Equal(1, presses[ViewTempoBarPresenter.TimeKey]);
        Assert.True(bar.Buttons[ViewTempoBarPresenter.Zoom].IsEnabled);
        Assert.False(bar.Buttons[ViewTempoBarPresenter.Speed].IsEnabled);
        Assert.False(bar.Buttons[ViewTempoBarPresenter.Tempo].IsEnabled);
    }
}
