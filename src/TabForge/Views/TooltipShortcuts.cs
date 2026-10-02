using System.Windows;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// One place that puts the current key of a command at the end of a control's tooltip, e.g. "Play / pause (Space)".
/// Give a control <c>views:TooltipShortcuts.Command="Transport.PlayPause"</c> (XAML) or call <see cref="Bind"/> (code) with its
/// plain tooltip text. The bracket follows rebinding and preset switches: the main window calls <see cref="SetHotkeys"/>
/// whenever the bindings change, which refreshes every bound control in every window (event-driven, never polled).
/// A command without a key shows no bracket. Menu items already show their key in the gesture column and do not use this.
/// </summary>
public static class TooltipShortcuts
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(string), typeof(TooltipShortcuts), new PropertyMetadata(null, OnCommandChanged));

    private static readonly DependencyProperty BaseTextProperty = DependencyProperty.RegisterAttached(
        "BaseText", typeof(string), typeof(TooltipShortcuts), new PropertyMetadata(null));

    private static readonly List<WeakReference<FrameworkElement>> Bound = new();
    private static HotkeySettings _hotkeys = new();
    private static int _pruneAt = 256;

    public static string? GetCommand(DependencyObject d) => (string?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, string? value) => d.SetValue(CommandProperty, value);

    /// <summary>The tooltip text without the key bracket (what the control's author wrote).</summary>
    public static string? GetBaseText(DependencyObject d) => (string?)d.GetValue(BaseTextProperty);

    /// <summary>The bindings the brackets are built from.</summary>
    public static HotkeySettings Hotkeys => _hotkeys;

    /// <summary>Call when the key bindings change: every bound control's tooltip is rebuilt from the new bindings.</summary>
    public static void SetHotkeys(HotkeySettings hotkeys)
    {
        _hotkeys = hotkeys;
        RefreshAll();
    }

    public static void RefreshAll()
    {
        for (var i = Bound.Count - 1; i >= 0; i--)
        {
            if (Bound[i].TryGetTarget(out var element)) Apply(element);
            else Bound.RemoveAt(i);
        }
    }

    /// <summary>Sets the control's tooltip to <paramref name="text"/> plus the command's current key, and keeps it current.</summary>
    public static void Bind(FrameworkElement element, string text, string? commandId)
    {
        element.SetValue(BaseTextProperty, text);
        if (string.IsNullOrEmpty(commandId))   // a control that has no command of its own (or reused one): plain text
        {
            element.ClearValue(CommandProperty);
            element.ToolTip = text;
            return;
        }
        if (GetCommand(element) != commandId) element.SetValue(CommandProperty, commandId);
        Apply(element);
    }

    /// <summary>Changes the plain tooltip text of an already bound control (the key bracket is kept).</summary>
    public static void SetText(FrameworkElement element, string text)
    {
        element.SetValue(BaseTextProperty, text);
        if (string.IsNullOrEmpty(GetCommand(element))) element.ToolTip = text;   // bound without a command (e.g. a group / master FX button): plain text
        else Apply(element);
    }

    /// <summary>"text (Ctrl+S)" with the command's current key, or just "text" when it has none.</summary>
    public static string Append(string text, string commandId) =>
        Compose(text, HotkeyCatalog.DisplayAll(_hotkeys, commandId));

    /// <summary>
    /// Appends the key last. A text that already ends in a bracket gets the key inside it ("Zoom (50-200%; Ctrl++)")
    /// so the tooltip never shows two bracket groups.
    /// </summary>
    public static string Compose(string text, string display) => HotkeyCatalog.TooltipWithDisplay(text, display);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if (e.NewValue is null) return;
        if (Bound.Count >= _pruneAt)   // rebuilt rows leave dead references behind: sweep now and then
        {
            Bound.RemoveAll(w => !w.TryGetTarget(out _));
            _pruneAt = Math.Max(256, Bound.Count * 2);
        }
        Bound.Add(new WeakReference<FrameworkElement>(element));
        if (element.IsInitialized || GetBaseText(element) is not null) Apply(element);
        else element.Initialized += OnInitialized;   // XAML: the ToolTip attribute may be parsed after this one
    }

    private static void OnInitialized(object? sender, EventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        element.Initialized -= OnInitialized;
        Apply(element);
    }

    private static void Apply(FrameworkElement element)
    {
        var command = GetCommand(element);
        if (string.IsNullOrEmpty(command)) return;
        var text = GetBaseText(element);
        if (text is null)
        {
            if (element.ToolTip is not string existing) return;   // rich tooltip content: leave it unchanged
            element.SetValue(BaseTextProperty, text = existing);
        }
        element.ToolTip = Append(text, command);
    }
}
