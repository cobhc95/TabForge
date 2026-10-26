using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the frame every slim strip under the FX window's menu row shares: the rounded bordered panel, the wrapping panel of whole groups (one row of one control height when wide;
//   groups drop to the next row, never overlapping, when narrow), and the group and caption building blocks.
// Does not own: what a strip shows or does (LiveInputStrip, TrackFxStrip) or the parts (LiveStripMeters, LiveStripKnobs, LiveStripTuner).
// Tests: TabForge.Avalonia.Tests.LiveInputStripTests, TrackFxStripTests.
/// <summary>The shared frame of the live input strip and the track strip.</summary>
public abstract class StripFrame : Border
{
    protected StripFrame(string name)
    {
        Name = name;
        MinHeight = ChromeMetrics.LiveStripHeight;
        CornerRadius = new CornerRadius(ChromeMetrics.LiveStripCorner);
        BorderThickness = new(1);
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Puts these groups in the strip's wrapping panel, in order.</summary>
    protected void Fill(IEnumerable<Control> groups, LiveCentsBar? flex = null)
    {
        var panel = new FlexWrapPanel(flex) { Name = "LiveStripGroups", Orientation = Orientation.Horizontal, ItemSpacing = ChromeMetrics.LiveStripGroupGap, LineSpacing = ChromeMetrics.LiveStripGap };
        foreach (var group in groups) panel.Children.Add(group);
        Child = panel.WithMargin(ChromeMetrics.LiveStripInset, ChromeMetrics.LiveStripRowInset, ChromeMetrics.LiveStripInset, ChromeMetrics.LiveStripRowInset);
    }

    /// <summary>The one composition of both strips: name with meters | Options (track only) | tuner block | Tuner and Window | levels | M and S.</summary>
    protected void Compose(string captionGroup, Control caption, LiveStripMeters meters, Control? options, LiveStripTuner tuner, LiveStripKnobs knobs)
    {
        var groups = new List<Control> { Group(captionGroup, caption, meters) };
        if (options is not null) groups.Add(options);
        groups.AddRange(new Control[] { tuner, tuner.Controls, knobs, knobs.Buttons });
        Fill(groups, tuner.Scale);
    }

    protected static StackPanel Group(string name, params Control[] parts)
    {
        var group = new StackPanel { Name = name, Orientation = Orientation.Horizontal, Spacing = ChromeMetrics.LiveStripGap, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in parts) group.Children.Add(part);
        return group;
    }

    protected static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = ChromeMetrics.LiveStripFontSize, VerticalAlignment = VerticalAlignment.Center, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
}

/// <summary>The wrapping panel of a strip: before any group wraps to a second row, the cents scale shrinks (down to its minimum) to take the deficit.</summary>
internal sealed class FlexWrapPanel : WrapPanel
{
    private readonly LiveCentsBar? _flex;
    public FlexWrapPanel(LiveCentsBar? flex) => _flex = flex;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_flex is not null && !double.IsInfinity(availableSize.Width))
        {
            _flex.Fit = ChromeMetrics.LiveStripCentsScaleWidth;
            var total = 0.0;
            foreach (var child in Children) { child.Measure(Size.Infinity); total += child.DesiredSize.Width; }
            total += ItemSpacing * Math.Max(0, Children.Count - 1);
            _flex.Fit = ChromeMetrics.LiveStripCentsScaleWidth - Math.Max(0, total - availableSize.Width);
        }
        return base.MeasureOverride(availableSize);
    }
}
