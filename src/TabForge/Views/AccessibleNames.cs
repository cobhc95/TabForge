using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TabForge.Views;

/// <summary>
/// Screen-reader names and stable automation ids for every interactive control, including ones built in code.
/// When such a control loads without them: the first line of its tooltip (else its text) becomes the
/// AutomationProperties.Name, and an <c>Area.Control</c> AutomationId is derived from its x:Name (else from its label and the
/// nearest named ancestor). Registered once at start-up; it costs one check per control load.
/// </summary>
internal static class AccessibleNames
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // Loaded does not bubble and class handlers registered on base types miss already-initialised subclasses, so each window
        // is watched instead: when its layout settles (debounced, idle otherwise) the visual tree is walked once.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window) Watch(window); }));
        foreach (var type in new[] { typeof(TextBox), typeof(PasswordBox), typeof(TabItem), typeof(MenuItem), typeof(ListBoxItem) })
            EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) Apply(element);
    }

    private static void Watch(Window window)
    {
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ContextIdle)
        { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); Walk(window); };
        window.LayoutUpdated += (_, _) => { timer.Stop(); timer.Start(); };
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    /// <summary>Applies names and ids to every interactive control under <paramref name="root"/> (idempotent, cheap).</summary>
    internal static void Walk(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase or ComboBox or Slider or TextBoxBase or PasswordBox or TabItem or MenuItem or ListBoxItem
                or FxSplitButton or KnobControl or RecordArmButton or MonitorButton)
                Apply((FrameworkElement)child);
            Walk(child);
        }
    }

    /// <summary>Gives <paramref name="element"/> a name (from its tooltip or content) and a stable id when it has none.</summary>
    internal static bool Apply(FrameworkElement element)
    {
        var changed = false;
        var hasName = !string.IsNullOrEmpty(AutomationProperties.GetName(element)) || AutomationProperties.GetLabeledBy(element) is not null;
        string? label = null;
        if (!hasName)
        {
            label = FirstLine(TooltipShortcuts.GetBaseText(element) ?? element.ToolTip as string) ?? (element is KnobControl { Label.Length: > 0 } knob ? knob.Label : null) ??
                    (element is ContentControl { Content: string text } ? text : null) ??
                    (element is HeaderedItemsControl { Header: string header } ? header : null);
            if (!string.IsNullOrWhiteSpace(label))
            {
                AutomationProperties.SetName(element, label.Trim());
                hasName = true;
                changed = true;
            }
        }
        if (string.IsNullOrEmpty(AutomationProperties.GetAutomationId(element)) && IdFor(element, label ?? AutomationProperties.GetName(element)) is { } id)
        {
            AutomationProperties.SetAutomationId(element, id);
            if (!hasName) AutomationProperties.SetName(element, Humanise(id[(id.LastIndexOf('.') + 1)..]));
            changed = true;
        }
        return changed;
    }

    private static readonly (string[] Words, string Area)[] Areas =
    {
        (new[] { "Play", "Stop", "Record", "Rewind", "Loop", "Metronome", "CountIn", "NextSection", "PrevSection", "Speed", "Transport" }, "Transport"),
        (new[] { "Zoom", "Toolbar" }, "Toolbar"),
        (new[] { "Fx", "Plugin", "Chain" }, "Fx"),
        (new[] { "Mixer", "Fader", "Pan", "Volume", "Bus" }, "Mixer"),
        (new[] { "Marker", "Section" }, "Sections"),
        (new[] { "Instrument", "Fretboard", "Scale", "Chord" }, "Instrument"),
        (new[] { "TitleSettings", "Min", "Max", "Close", "TitleBar" }, "Window"),
        (new[] { "Tab" }, "Tabs"),
    };

    /// <summary>Area.Control for the element: x:Name when it has one, else its label; the area from keywords or the nearest named ancestor.</summary>
    internal static string? IdFor(FrameworkElement element, string? label)
    {
        var name = element.Name;
        var leaf = Slug(!string.IsNullOrEmpty(name) ? StripSuffix(name) : label);
        if (leaf.Length == 0) leaf = element.GetType().Name + SiblingIndex(element);
        // A control with an x:Name is filed by that name's keywords; an unnamed one by the panel it sits in.
        var area = (name is { Length: > 0 } ? AreaFromKeywords(name) : null) ?? AncestorArea(element) ?? WindowArea(element);
        return $"{area}.{leaf}";
    }

    private static string? AreaFromKeywords(string text)
    {
        foreach (var (words, area) in Areas)
            foreach (var word in words)
                if (text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return area;
        return null;
    }

    private static string? AreaAlias(string ancestorName) => ancestorName switch
    {
        "ToolsPaletteHost" or "ToolsPanelContent" or "ToolsPanel" => "Tools",
        "MainToolbar" => "Toolbar",
        "MainStatusBar" => "Status",
        "Scroll" or "TabStrip" or "Tabs" => "Tabs",
        "PlaybarControls" or "ControllerPanel" => "Transport",
        "SectionsPanelContent" or "MarkerList" => "Sections",
        "ScoreScroll" or "ScorePage" => "Score",
        _ => null
    };

    private static string? AncestorArea(FrameworkElement element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is FrameworkElement { Name.Length: > 0 } fe && !fe.Name.StartsWith("PART_", StringComparison.Ordinal))
                return AreaAlias(fe.Name) ?? AreaFromKeywords(fe.Name) ?? Slug(fe.Name);
        return null;
    }

    private static string WindowArea(FrameworkElement element)
    {
        var window = Window.GetWindow(element)?.GetType().Name ?? "App";
        return window.EndsWith("Window", StringComparison.Ordinal) && window.Length > 6 ? window[..^6] : window;
    }

    private static string SiblingIndex(FrameworkElement element)
    {
        if (VisualTreeHelper.GetParent(element) is not { } parent) return "";
        var index = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (ReferenceEquals(child, element)) return index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (child.GetType() == element.GetType()) index++;
        }
        return "";
    }

    private static string StripSuffix(string name)
    {
        foreach (var suffix in new[] { "Button", "Combo", "Slider", "Box", "Check", "Toggle" })
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal)) return name[..^suffix.Length];
        return name;
    }

    /// <summary>PascalCase of the first three words of a label, without bracketed text ("Zoom out (Ctrl+-)" -> "ZoomOut").</summary>
    internal static string Slug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var depth = 0;
        var sb = new StringBuilder();
        var words = 0;
        var startOfWord = true;
        foreach (var c in text)
        {
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && char.IsLetterOrDigit(c))
            {
                if (startOfWord) { if (words >= 3) break; words++; sb.Append(char.ToUpperInvariant(c)); }
                else sb.Append(c);
                startOfWord = false;
                continue;
            }
            if (!(depth == 0 && char.IsLetterOrDigit(c))) startOfWord = true;
        }
        return sb.ToString();
    }

    private static string Humanise(string pascal)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]) && !char.IsUpper(pascal[i - 1])) sb.Append(' ');
            sb.Append(i > 0 && sb.Length > 0 && char.IsUpper(pascal[i]) ? char.ToLowerInvariant(pascal[i]) : pascal[i]);
        }
        return sb.ToString();
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var end = text.IndexOfAny(new[] { (char)10, (char)13 });
        return end < 0 ? text : text[..end];
    }
}
