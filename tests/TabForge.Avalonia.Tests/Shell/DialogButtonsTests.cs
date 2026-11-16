using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The shared footer buttons: the primary wears the accent look and answers Enter, Cancel answers Escape, all share one least width.</summary>
public class DialogButtonsTests
{
    [AvaloniaFact]
    public void OkIsTheAccentDefaultAndCancelThePlainEscape()
    {
        var ok = DialogButtons.Ok();
        var cancel = DialogButtons.Cancel();
        Assert.True(ok.IsDefault);
        Assert.Contains(AvaloniaDefaultLooks.AccentLook, ok.Classes);
        Assert.True(cancel.IsCancel);
        Assert.DoesNotContain(AvaloniaDefaultLooks.AccentLook, cancel.Classes);
        Assert.Equal(DialogMetrics.DialogButtonMinWidth, ok.MinWidth);
        Assert.Equal(DialogMetrics.DialogButtonMinWidth, cancel.MinWidth);
        Assert.Equal(DialogMetrics.ActionButtonPadding, ok.Padding);
    }

    [AvaloniaFact]
    public void CloseAnswersBothKeysAndTheRowKeepsOneGap()
    {
        var close = DialogButtons.Close();
        Assert.True(close.IsDefault && close.IsCancel);
        var row = DialogButtons.Row(DialogButtons.Apply(), DialogButtons.Cancel());
        Assert.Equal(DialogMetrics.FooterButtonSpacing, row.Spacing);
        Assert.Equal(2, row.Children.Count);
    }
}
