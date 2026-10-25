using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the render transforms and the drag look of one strip's tabs: the follow offset of the dragged tab, the neighbour slides of a
//   reorder preview, the settle of a dropped tab into place, and the lift and its reset. ResetAll puts every tab back at rest and stops
//   the running animations. An animation targets its tab object (never an index) and leaves no fill, so the value it ends on is the
//   tab's own value and a later direct write is never overridden by it.
// Does not own: the press and drag state and the drop preview (TabStripView), the reorder rules (TabDragRules).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests. UI thread only.
internal sealed class TabStripMotion
{
    /// <summary>The palette key whose colour the lifted tab's shadow is drawn in, at <see cref="DragShadowAlpha"/> of its alpha.</summary>
    private const string DragShadowPaletteKey = "InstrumentKeysShadow";
    private const double DragShadowAlpha = 0.5;

    private readonly Panel _tabs;
    private CancellationTokenSource _stop = new();

    /// <param name="tabs">The panel holding the strip's tab borders, in tab order.</param>
    public TabStripMotion(Panel tabs) => _tabs = tabs;

    /// <summary>Moves a tab to <paramref name="offset"/> DIPs from its laid-out place (the dragged tab following the pointer).</summary>
    public void Follow(Control tab, double offset) => TransformOf(tab).X = offset;

    /// <summary>Moves each tab except <paramref name="skip"/> by its slide offset; the slides ease over the slide time when <paramref name="animate"/> is set.</summary>
    public void Slide(IReadOnlyList<double> offsets, bool animate, int? skip)
    {
        FreezeRunning();
        for (var i = 0; i < _tabs.Children.Count && i < offsets.Count; i++)
        {
            if (i == skip) continue;
            var tab = _tabs.Children[i];
            var transform = TransformOf(tab);
            if (animate) Animate(tab, transform, transform.X, offsets[i], TabStripView.SlideMilliseconds);
            else transform.X = offsets[i];
        }
    }

    /// <summary>Eases a dropped tab from <paramref name="fromOffset"/> to its laid-out place; with motion reduced it is placed at once.</summary>
    public void Settle(Control tab, double fromOffset)
    {
        var transform = TransformOf(tab);
        if (fromOffset == 0) transform.X = 0;
        else Animate(tab, transform, fromOffset, 0, TabStripView.DropSettleMilliseconds);
    }

    /// <summary>Puts every tab back on its laid-out place, lifts it off and stops the running slides and settles.</summary>
    public void ResetAll()
    {
        _stop.Cancel();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
        foreach (var tab in _tabs.Children)
        {
            if (tab.RenderTransform is TranslateTransform transform) transform.X = 0;
            Unlift(tab);
        }
    }

    /// <summary>Lifts the dragged tab: its opacity, accent border and shadow.</summary>
    public static void Lift(Control tab)
    {
        if (tab is not Border border) return;
        border.Opacity = TabStripView.DragLiftOpacity;
        border[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        var shadow = AvaloniaPalette.Get(DragShadowPaletteKey, tab.ActualThemeVariant == ThemeVariant.Light);
        var colour = AvaloniaPalette.ColorOf(shadow.WithOpacity(DragShadowAlpha));
        border.BoxShadow = BoxShadows.Parse($"0 0 {TabStripView.DragShadowBlur.ToString(CultureInfo.InvariantCulture)} 0 {colour}");
    }

    /// <summary>Takes the lift off a tab.</summary>
    public static void Unlift(Control tab)
    {
        if (tab is not Border border) return;
        border.Opacity = 1;
        border.BorderBrush = AvaloniaPalette.TransparentBrush;
        border.ClearValue(Border.BoxShadowProperty);
    }

    // The running animations keep the values they show: each tab's effective value is copied into its local value before they stop,
    // so a stopped slide does not jump back to where it started.
    private void FreezeRunning()
    {
        var current = _tabs.Children.Select(tab => TransformOf(tab).X).ToArray();
        _stop.Cancel();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
        for (var i = 0; i < current.Length; i++) TransformOf(_tabs.Children[i]).X = current[i];
    }

    private void Animate(Control tab, TranslateTransform transform, double from, double to, double milliseconds)
    {
        var duration = UiMotion.DurationMilliseconds(milliseconds);
        if (duration <= 0)
        {
            transform.X = to;
            return;
        }
        transform.X = from;
        _ = RunAnimation(tab, transform, from, to, duration, _stop.Token);
    }

    private static async Task RunAnimation(Control tab, TranslateTransform transform, double from, double to, double milliseconds, CancellationToken stop)
    {
        try
        {
            await SlideAnimation(from, to, milliseconds).RunAsync(tab, stop);
        }
        catch (OperationCanceledException)
        {
            return;   // a reset or a new slide replaced this animation
        }
        if (!stop.IsCancellationRequested) transform.X = to;
    }

    private static TranslateTransform TransformOf(Control tab)
    {
        if (tab.RenderTransform is TranslateTransform existing) return existing;
        var transform = new TranslateTransform();
        tab.RenderTransform = transform;
        return transform;
    }

    private static Animation SlideAnimation(double from, double to, double milliseconds) => new()
    {
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        Easing = new CubicEaseOut(),
        FillMode = FillMode.None,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, to) } },
        },
    };
}
