// Owns: the one look of a dialog's footer buttons (OK, Cancel, Apply, Close and any other action): the primary button wears the accent look
//   and is the default, the others are plain; all share one least width and padding, and the footer row lines them up on the right with one gap.
//   A new dialog builds its buttons here instead of setting widths, margins or paddings by hand.
// Does not own: what a button does (the window), the compact prompt buttons (PromptChrome.Action), the sizes (DialogMetrics) or the colours (theme keys).
// Tests: TabForge.Avalonia.Tests.DialogButtonsTests.
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Views.Dialogs;

/// <summary>Builds the footer buttons and footer row every dialog shares.</summary>
public static class DialogButtons
{
    /// <summary>A footer action button. <paramref name="primary"/> takes the accent look and answers Enter; <paramref name="isCancel"/> answers Escape.</summary>
    public static Button Action(string text, bool primary = false, bool isCancel = false, string? name = null)
    {
        var button = new Button
        {
            Content = text, IsDefault = primary, IsCancel = isCancel, MinWidth = DialogMetrics.DialogButtonMinWidth,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        }.WithPadding(DialogMetrics.ActionButtonPadding);
        if (name is not null) button.Name = name;
        if (primary) button.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        return button;
    }

    /// <summary>The OK button: primary, answers Enter.</summary>
    public static Button Ok(string? text = null, string? name = "OkButton") => Action(text ?? CommonText.Ok, primary: true, name: name);

    /// <summary>The Apply button: primary, answers Enter.</summary>
    public static Button Apply(string? text = null, string? name = "ApplyButton") => Action(text ?? CommonText.Apply, primary: true, name: name);

    /// <summary>The Cancel button: plain, answers Escape.</summary>
    public static Button Cancel(string? text = null, string? name = "CancelButton") => Action(text ?? CommonText.Cancel, isCancel: true, name: name);

    /// <summary>The Close button of a dialog with nothing to confirm: answers both Enter and Escape, plain look.</summary>
    public static Button Close(string? text = null, string? name = "CloseButton")
    {
        var button = Action(text ?? CommonText.Close, isCancel: true, name: name);
        button.IsDefault = true;
        return button;
    }

    /// <summary>The right-aligned row of footer buttons, one gap apart, in the order given (primary first, as the app's dialogs read).</summary>
    public static StackPanel Row(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = DialogMetrics.FooterButtonSpacing };
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }

    /// <summary>The footer: <see cref="Row"/> spaced from the content above by the footer gap.</summary>
    public static StackPanel Footer(params Button[] buttons) => Row(buttons).WithMargin(DialogMetrics.FooterGap);
}
