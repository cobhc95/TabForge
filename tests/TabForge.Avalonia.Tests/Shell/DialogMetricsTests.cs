using Avalonia;
using TabForge.Avalonia.Views.Dialogs;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the values of the dialog frame metrics every dialog window takes from <see cref="DialogMetrics"/>, so a silent change to the most common
/// frame (text size, content inset, footer gap and button widths/padding) is caught.
/// Does not own: the dialogs themselves or how each window lays its content out.
/// Tests: this class.
/// </summary>
public class DialogMetricsTests
{
    [Fact]
    public void DialogFrameMetricsKeepTheirValues()
    {
        Assert.Equal(13, DialogMetrics.FontSize);
        Assert.Equal(420, DialogMetrics.SmallDialogWidth);
        Assert.Equal(80, DialogMetrics.DialogButtonMinWidth);
        Assert.Equal(76, DialogMetrics.PromptButtonMinWidth);
        Assert.Equal(new Thickness(12), DialogMetrics.ContentInset);
        Assert.Equal(new Thickness(0, 10, 0, 0), DialogMetrics.FooterGap);
        Assert.Equal(new Thickness(0, 2, 0, 2), DialogMetrics.RowMargin);
        Assert.Equal(new Thickness(12, 3, 12, 3), DialogMetrics.ActionButtonPadding);
    }
}
