using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll toolbar's two-row geometry: the one control height, corner, gaps, group frames, row assembly and sizing helpers.
// Does not own: the buttons' paint (PianoRollToolLook), which control is active (PianoRollView.RefreshTools), the clicks (PianoRollView.BuildToolbar).
// Tests: TabForge.Avalonia.Tests.PianoRollToolbarLayoutTests.
internal static class PianoRollToolbarLayout
{
    /// <summary>The rows' names, which the layout test finds them by.</summary>
    public const string Row1Name = "PianoRollToolbarRow1", Row2Name = "PianoRollToolbarRow2";

    /// <summary>Every toolbar control's height, the dialog button height token, so buttons, dropdowns and value boxes share one baseline.</summary>
    public static double ControlHeight => DialogMetrics.MediaButtonHeight;

    /// <summary>Every toolbar control and group frame's corner, the app's control corner.</summary>
    public static CornerRadius Corner => AvaloniaFormControlStyles.ControlCornerRadius;

    /// <summary>The gap inside a group frame and between its buttons, between groups in a row, and between the two rows.</summary>
    public const double InnerGap = DialogMetrics.PianoRollInnerGap, GroupGap = DialogMetrics.PianoRollGroupGap, RowGap = DialogMetrics.PianoRollRowGap;

    /// <summary>An icon button's least width, a dropdown's least width, and the track chip dot's span.</summary>
    public const double ToolMinWidth = DialogMetrics.PianoRollToolMinWidth, ComboMinWidth = DialogMetrics.PianoRollComboMinWidth, ChipDot = DialogMetrics.PianoRollChipDot;

    /// <summary>A group frame: a thin rounded border in the border token around controls spaced the inner gap apart.</summary>
    public static Border Frame(params Control[] items)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = InnerGap, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items) inner.Children.Add(item);
        var frame = new Border { Child = inner, CornerRadius = Corner, VerticalAlignment = VerticalAlignment.Center };
        DialogMetrics.StylePianoRollFrame(frame);
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        return frame;
    }

    /// <summary>The first row's group captions in order (each shown in upper case above its group).</summary>
    public static string[] GroupCaptions => new[] { "Tool", "Snap", "Scale", "Step input", "New notes" };

    /// <summary>The name a group's cell (<c>PianoRollGroup</c> + key) and its caption (<c>PianoRollCaption</c> + key) carry, key being the caption without spaces.</summary>
    public static string GroupName(string caption) => "PianoRollGroup" + caption.Replace(" ", "");

    /// <summary>One group of row 1: a tiny upper-case muted caption above <paramref name="content"/>, a thin divider before it (all but the first group), moving as a whole when the row wraps.</summary>
    public static Border Group(string caption, bool divider, params Control[] content)
    {
        var label = new TextBlock { Name = "PianoRollCaption" + caption.Replace(" ", ""), Text = caption.ToUpperInvariant(), FontSize = DialogMetrics.PianoRollCaptionSize };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        label.WithMargin(DialogMetrics.PianoRollCaptionGap);
        AutomationProperties.SetName(label, caption);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GroupGap, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in content) row.Children.Add(item);
        var cell = new Border { Name = GroupName(caption), Child = new StackPanel { Children = { label, row } }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(cell, caption);
        if (divider)
        {
            cell.BorderThickness = DialogMetrics.PianoRollDivider;
            cell.WithPadding(DialogMetrics.PianoRollDividerInset, 0);
            cell[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
        }
        return cell;
    }

    /// <summary>A row whose <paramref name="fill"/> wraps in whatever width <paramref name="right"/> (docked at the right end, never clipped) leaves.</summary>
    public static DockPanel FillRow(string name, Control fill, Control? right)
    {
        var row = new DockPanel { Name = name, LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); row.Children.Add(right); }
        row.Children.Add(fill);
        return row;
    }

    /// <summary>A row's flowing run of groups: wraps as whole groups at narrow widths, with the group gap between them.</summary>
    public static WrapPanel Flow()
    {
        var flow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        flow.ItemSpacing = GroupGap;
        flow.LineSpacing = RowGap;
        return flow;
    }

    /// <summary>A toolbar row: <paramref name="left"/> docked left, <paramref name="right"/> docked right, <paramref name="middle"/> filling between.</summary>
    public static DockPanel Row(string name, Control left, Control middle, Control right)
    {
        var row = new DockPanel { Name = name, LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(left);
        row.Children.Add(right);
        row.Children.Add(middle);
        return row;
    }

    /// <summary>The flexible space between a row's left and right ends.</summary>
    public static Border Flex() => new() { MinWidth = 0 };

    /// <summary>Gives a toolbar button the shared height, least width, corner and centred content.</summary>
    public static T StyleButton<T>(T button) where T : Button
    {
        button.Height = ControlHeight;
        button.MinWidth = ToolMinWidth;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        return button;
    }

    /// <summary>Gives a toolbar dropdown the shared height, least width and centred content (its paint comes from PianoRollToolLook).</summary>
    public static T StyleCombo<T>(T combo) where T : ComboBox
    {
        combo.Height = ControlHeight;
        combo.MinWidth = ComboMinWidth;
        combo.VerticalAlignment = VerticalAlignment.Center;
        combo.VerticalContentAlignment = VerticalAlignment.Center;
        PianoRollToolLook.Apply(combo);
        return combo;
    }

    /// <summary>Gives a value box the shared height and centred content.</summary>
    public static TextBox StyleField(TextBox box, double minWidth)
    {
        box.Height = ControlHeight;
        box.MinWidth = minWidth;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        return box;
    }

    /// <summary>Gives a check the shared height and centred content.</summary>
    public static CheckBox StyleCheck(CheckBox check)
    {
        check.Height = ControlHeight;
        check.VerticalAlignment = VerticalAlignment.Center;
        check.VerticalContentAlignment = VerticalAlignment.Center;
        return check;
    }
}
