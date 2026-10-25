using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

// Owns: combo popup reshaping.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public static partial class AvaloniaFormControlStyles
{
    private static void ReshapeComboPopup(object? sender, TemplateAppliedEventArgs e)
    {
        ReshapeComboArrow(e);
        if (sender is ComboBox combo && e.NameScope.Find<Popup>("PART_Popup") is { } popup) AttachComboPopup(combo, popup);
    }

    private static void ReshapeComboArrow(TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<PathIcon>("DropDownGlyph") is not { } glyph) return;
        glyph.Data = Geometry.Parse(ComboArrowPath);
        glyph.Width = ComboArrowWidth;
        glyph.Height = ComboArrowHeight;
        glyph.HorizontalAlignment = HorizontalAlignment.Right;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 0, ComboArrowRightGap, 0);
    }

    // The popup is in the template, so the hook owns it: it inherits the combo's scale, and each open sets its minimum width afresh from the preset and the combo's width.
    // The Fluent template already sets both, so these writes are idempotent there; a re-templated or stale-bound combo is corrected.
    private static void AttachComboPopup(ComboBox combo, Popup popup)
    {
        popup.InheritsTransform = true;
        if (ComboHooks.TryGetValue(combo, out var old)) { old.Release(); ComboHooks.Remove(combo); }   // a re-template replaces the hook; the old one unsubscribes everything
        var hook = new ComboHook(combo, popup);
        ComboHooks.Add(combo, hook);
        hook.Attach();
    }

    // The named Opened handler of one combo's popup: removed when the combo leaves the tree (so a detached combo is not kept alive by its popup).
    // It also holds the popup's own minimum width (read once, the floor of every open).
    private sealed class ComboHook
    {
        private readonly ComboBox _combo;
        private bool _on;
        public readonly Popup Popup;
        public readonly double Preset;
        private object? _source;
        private double _widest, _fontSize;
        private FontFamily? _family;
        private bool _measured;
        public ComboHook(ComboBox combo, Popup popup)
        {
            _combo = combo; Popup = popup; Preset = popup.MinWidth;
            combo.AttachedToVisualTree += TreeAttached;
            combo.DetachedFromVisualTree += TreeDetached;
        }
        private void Opened(object? sender, EventArgs e) => ApplyComboPopupWidth(_combo, Popup);
        private void TreeAttached(object? sender, VisualTreeAttachmentEventArgs e) => Attach();
        private void TreeDetached(object? sender, VisualTreeAttachmentEventArgs e) => Detach();
        public void Attach() { if (_on) return; _on = true; Popup.Opened += Opened; }
        public void Detach()
        {
            if (!_on) return;
            _on = false; Popup.Opened -= Opened;
            if (_source is INotifyCollectionChanged old) old.CollectionChanged -= SourceChanged;   // a shared source must not keep a detached combo alive
            (_source, _measured) = (null, false);
        }
        /// <summary>The widest item text, measured once per items source: a new source, a change notification or a new font measures it again.</summary>
        public double WidestText(Func<double> measure)
        {
            var source = (object?)_combo.ItemsSource ?? _combo.Items;
            if (_measured && ReferenceEquals(source, _source) && _fontSize == _combo.FontSize && Equals(_family, _combo.FontFamily)) return _widest;
            if (_source is INotifyCollectionChanged old) old.CollectionChanged -= SourceChanged;
            if (source is INotifyCollectionChanged now) now.CollectionChanged += SourceChanged;
            (_source, _fontSize, _family, _measured) = (source, _combo.FontSize, _combo.FontFamily, true);
            return _widest = measure();
        }
        private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => _measured = false;
        /// <summary>Drops every subscription (the combo was re-templated and a new hook took over).</summary>
        public void Release() { Detach(); _combo.AttachedToVisualTree -= TreeAttached; _combo.DetachedFromVisualTree -= TreeDetached; }
    }

    /// <summary>The template popup the hook attached to <paramref name="combo"/>, or null when its template has none (the headless test seam).</summary>
    internal static Popup? ComboPopupFor(ComboBox combo) => ComboHooks.TryGetValue(combo, out var hook) ? hook.Popup : null;

    /// <summary>
    /// Sets the popup's width from scratch at each open: at least the combo's width in inner DIPs (and the popup's own preset minimum, read once),
    /// at most the cap (or the combo's own width when that is wider). It never carries a width over from an earlier open.
    /// </summary>
    internal static void ApplyComboPopupWidth(ComboBox combo, Popup popup)
    {
        ComboHooks.TryGetValue(combo, out var hook);
        var own = hook is not null && ReferenceEquals(hook.Popup, popup);
        var preset = own ? hook!.Preset : popup.MinWidth;
        var minimum = Math.Max(preset, Math.Max(combo.Bounds.Width, combo.MinWidth));
        // The list is virtualised, so the popup would otherwise widen as long rows scroll into view: the width is settled once per items source, from every item's text.
        var width = Math.Max(minimum, Math.Min((own ? hook!.WidestText(() => WidestItemText(combo)) : WidestItemText(combo)) + ChromeMetrics.ComboItemChrome, Math.Max(ChromeMetrics.ComboPopupMaxWidth, minimum)));
        popup.MinWidth = minimum;
        popup.Width = width;
    }

    // The widest item label of the combo in DIPs (items shown as controls other than a plain content row are skipped).
    private static double WidestItemText(ComboBox combo)
    {
        var typeface = new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight);
        double widest = 0;
        foreach (var item in (System.Collections.IEnumerable?)combo.ItemsSource ?? combo.Items)
        {
            var text = item switch { null => null, ContentControl { Content: var content } => content as string, Control => null, _ => item.ToString() };
            if (string.IsNullOrEmpty(text)) continue;
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, combo.FontSize, null);
            widest = Math.Max(widest, formatted.Width);
        }
        return widest;
    }

    private static Setter Bound(AvaloniaProperty property, string brushName) =>
        new(property, new DynamicResourceExtension(brushName));
}
