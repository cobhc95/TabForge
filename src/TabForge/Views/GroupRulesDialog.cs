using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>Where the edited rules apply: every song (app settings), only the open song, or back to the app-wide rules.</summary>
public enum GroupRulesScope { AllSongs, ThisSong, ResetToApp }

/// <summary>The edited group rules: groups in priority order, the fallback group's name, old name -> new name for renamed groups, and where they apply.</summary>
public sealed record GroupRulesResult(List<MixerGroupDef> Groups, string Fallback, Dictionary<string, string> Renamed, GroupRulesScope Scope);

// Owns: the "Group rules" editor window (add / rename / delete / reorder groups, their colours and rules, the fallback name, reset).
// Does not own: applying the result to the song (MixerRules.Apply, called by MixerWindow), the rule matching (MixerRules).
// Tests: TestMixerGroupRules (model), the --capture "GroupRules" window (looks).
public sealed class GroupRulesDialog : Window
{
    private sealed class Entry { public string? Original; public MixerGroupDef Def = new(); }

    private readonly List<Entry> _entries = new();
    private string _fallback;
    private readonly StackPanel _list = new();
    private readonly RadioButton _allSongs = new() { Content = "All songs (app settings)", Margin = new Thickness(0, 0, 16, 0), GroupName = "scope" };
    private readonly RadioButton _thisSong = new() { Content = "This song only", GroupName = "scope" };
    private readonly TextBlock _problem = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    public GroupRulesResult? Result { get; private set; }

    public GroupRulesDialog(Window? owner, MixerSettings mixer)
    {
        Title = "Group rules";
        Owner = owner;
        Width = 640; Height = 660; MinWidth = 520; MinHeight = 400; ShowInTaskbar = false;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        foreach (var g in MixerRules.Groups(mixer)) _entries.Add(new Entry { Original = g.Name, Def = g.Clone() });
        _fallback = MixerRules.FallbackOf(mixer);
        _fallbackOriginal = _fallback;

        var root = new DockPanel { Margin = new Thickness(14) };
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "A track goes to the first group (top to bottom) that has a matching rule; a group matches when any of its rules does. " +
                   "Tracks no group matches go to the last group. A track you moved to another group by hand stays there until you choose \"Return to rule group\" in its right-click menu."
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        var add = new Button { Content = "Add group", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        var reset = new Button { Content = "Reset to defaults", Padding = new Thickness(10, 3, 10, 3) };
        DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(add); left.Children.Add(reset);
        buttons.Children.Add(left);
        _problem.SetResourceReference(ForegroundProperty, "DangerBrush");
        var scope = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(scope, Dock.Bottom);
        var apply = new StackPanel { Orientation = Orientation.Horizontal };
        apply.Children.Add(new TextBlock { Text = "Apply to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        apply.Children.Add(_allSongs); apply.Children.Add(_thisSong);
        (mixer.HasOwnRules ? _thisSong : _allSongs).IsChecked = true;
        scope.Children.Add(apply);
        if (mixer.HasOwnRules)
        {
            var own = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var back = new Button { Content = "Reset to app rules", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right };
            back.ToolTip = "Drop this song's own rules: it follows the app-wide rules again";
            back.Click += (_, _) => Accept(reset: true);
            DockPanel.SetDock(back, Dock.Right);
            var note = new TextBlock { Text = "This song has its own rules (they win over the app-wide ones). \"All songs\" replaces them.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            note.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
            own.Children.Add(back); own.Children.Add(note);
            scope.Children.Add(own);
        }
        else
        {
            var note = new TextBlock { Text = "This song follows the app-wide rules.", Margin = new Thickness(0, 4, 0, 0) };
            note.SetResourceReference(ForegroundProperty, "SecondaryTextBrush");
            scope.Children.Add(note);
        }
        DockPanel.SetDock(_problem, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_problem);
        root.Children.Add(scope);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list });
        Content = root;

        add.Click += (_, _) =>
        {
            _entries.Add(new Entry { Def = new MixerGroupDef { Name = FreshName(), Rules = { new MixerRule(MixerRules.KindFamily, MixerRules.Piano) } } });
            Rebuild();
        };
        reset.Click += (_, _) =>
        {
            var old = MixerRules.Groups(mixer).Select(g => g.Name).ToList();
            _entries.Clear();
            foreach (var g in MixerRules.Defaults()) _entries.Add(new Entry { Original = old.Contains(g.Name) ? g.Name : null, Def = g });
            _fallback = MixerRules.DefaultFallback;
            Rebuild();
        };
        ok.Click += (_, _) => Accept(reset: false);
        Rebuild();
    }

    private readonly string _fallbackOriginal;

    private string FreshName()
    {
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? "New group" : $"New group {i}";
            if (_entries.All(e => e.Def.Name != name) && name != _fallback) return name;
        }
    }

    /// <summary>Shows a problem under the list; empty hides the line so it leaves no gap.</summary>
    private void Problem(string text)
    {
        _problem.Text = text;
        _problem.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Accept(bool reset)
    {
        if (reset) { Result = new GroupRulesResult(new(), "", new(), GroupRulesScope.ResetToApp); DialogResult = true; return; }
        var names = _entries.Select(e => e.Def.Name.Trim()).Append(_fallback.Trim()).ToList();
        if (names.Any(n => n.Length == 0)) { Problem("Every group needs a name."); return; }
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) { Problem("Two groups have the same name."); return; }
        if (names.Any(n => n is MixerGroups.Audio or MixerGroups.Everything)) { Problem($"\"{MixerGroups.Audio}\" and \"{MixerGroups.Everything}\" are reserved names."); return; }
        var renamed = new Dictionary<string, string>();
        foreach (var e in _entries.Where(e => e.Original is not null)) renamed[e.Original!] = e.Def.Name.Trim();
        if (_fallback.Trim() != _fallbackOriginal) renamed[_fallbackOriginal] = _fallback.Trim();
        Result = new GroupRulesResult(_entries.Select(e => { var d = e.Def.Clone(); d.Name = d.Name.Trim(); return d; }).ToList(), _fallback.Trim(), renamed, _thisSong.IsChecked == true ? GroupRulesScope.ThisSong : GroupRulesScope.AllSongs);
        DialogResult = true;
    }

    /// <summary>Shows the editor; true when the user pressed OK (<paramref name="result"/> then holds the edit).</summary>
    public static bool? Show(Window owner, MixerSettings mixer, out GroupRulesResult result)
    {
        var dialog = new GroupRulesDialog(owner, mixer);
        var ok = dialog.ShowDialog();
        result = dialog.Result ?? new GroupRulesResult(new(), "", new(), GroupRulesScope.ThisSong);
        return ok;
    }

    private void Rebuild()
    {
        Problem("");
        _list.Children.Clear();
        for (var i = 0; i < _entries.Count; i++) _list.Children.Add(GroupCard(i));
        _list.Children.Add(FallbackCard());
    }

    private Border Card(UIElement content)
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        return card;
    }

    private static Button Small(string text, string tip)
    {
        var b = new Button { Content = text, ToolTip = tip, MinWidth = 26, Height = 24, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(2, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    private Border GroupCard(int index)
    {
        var entry = _entries[index];
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var number = new TextBlock { Text = $"{index + 1}.", Width = 22, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        number.ToolTip = "Priority: a track goes to the first matching group";
        var up = Small("▲", "Move the group up (higher priority)"); up.IsEnabled = index > 0;
        var down = Small("▼", "Move the group down (lower priority)"); down.IsEnabled = index < _entries.Count - 1;
        var remove = Small("✕", "Delete the group (its tracks fall to the next matching group)");
        var colour = new Button
        {
            Width = 28, Height = 24, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Group colour (colours the group's tracks)",
            Content = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = BrushFor(entry.Def.Colour ?? Services.TrackColouring.ColourOf(entry.Def.Name, null)) }
        };
        System.Windows.Automation.AutomationProperties.SetName(colour, "Group colour");
        var name = new TextBox { Text = entry.Def.Name, MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(name, "Group name");
        name.TextChanged += (_, _) => entry.Def.Name = name.Text;
        DockPanel.SetDock(number, Dock.Left);
        foreach (var b in new UIElement[] { remove, down, up, colour }) DockPanel.SetDock(b, Dock.Right);
        head.Children.Add(number);
        head.Children.Add(remove); head.Children.Add(down); head.Children.Add(up); head.Children.Add(colour);
        head.Children.Add(name);
        stack.Children.Add(head);
        up.Click += (_, _) => { _entries.RemoveAt(index); _entries.Insert(index - 1, entry); Rebuild(); };
        down.Click += (_, _) => { _entries.RemoveAt(index); _entries.Insert(index + 1, entry); Rebuild(); };
        remove.Click += (_, _) => { _entries.Remove(entry); Rebuild(); };
        colour.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = colour };
            foreach (var (colourName, hex) in TrackControlWidgets.TrackColourPalette)
            {
                var item = new MenuItem { Header = colourName, Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = BrushFor(hex) } };
                item.Click += (_, _) => { entry.Def.Colour = hex; Rebuild(); };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };

        var any = new TextBlock { Text = entry.Def.Rules.Count == 0 ? "No rules: nothing goes here by rule (manual moves still can)." : "A track is in this group when ANY of these match:", Margin = new Thickness(28, 0, 0, 4) };
        any.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        stack.Children.Add(any);
        foreach (var rule in entry.Def.Rules.ToList()) stack.Children.Add(RuleRow(entry, rule));
        var addRule = new Button { Content = "Add rule", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 4, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        addRule.Click += (_, _) => { entry.Def.Rules.Add(new MixerRule(MixerRules.KindFamily, MixerRules.Piano)); Rebuild(); };
        stack.Children.Add(addRule);
        return Card(stack);
    }

    private Grid RuleRow(Entry entry, MixerRule rule)
    {
        var row = new Grid { Margin = new Thickness(28, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var kind = new ComboBox { ItemsSource = MixerRules.RuleKinds, SelectedItem = rule.Kind, Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(kind, "Rule type");
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is not string k || k == rule.Kind) return;
            rule.Kind = k; rule.Value = DefaultValue(k);
            Rebuild();
        };
        Grid.SetColumn(kind, 0);
        FrameworkElement value;
        if (rule.Kind is MixerRules.KindFamily or MixerRules.KindTrack)
        {
            var combo = new ComboBox { ItemsSource = rule.Kind == MixerRules.KindFamily ? MixerRules.Families : MixerRules.TrackKinds, SelectedItem = rule.Value };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string v) rule.Value = v; };
            value = combo;
        }
        else
        {
            var box = new TextBox { Text = rule.Value, VerticalContentAlignment = VerticalAlignment.Center };
            box.ToolTip = rule.Kind switch
            {
                MixerRules.KindProgram => "A General MIDI program number 0-127, or a range such as 24-31",
                MixerRules.KindChannel => "A MIDI channel 1-16",
                _ => "Part of the track's name (not case sensitive)"
            };
            box.TextChanged += (_, _) => rule.Value = box.Text;
            value = box;
        }
        System.Windows.Automation.AutomationProperties.SetName(value, "Rule value");
        Grid.SetColumn(value, 1);
        var remove = Small("✕", "Remove the rule");
        remove.Click += (_, _) => { entry.Def.Rules.Remove(rule); Rebuild(); };
        Grid.SetColumn(remove, 2);
        row.Children.Add(kind); row.Children.Add(value); row.Children.Add(remove);
        return row;
    }

    private static string DefaultValue(string kind) => kind switch
    {
        MixerRules.KindFamily => MixerRules.Piano,
        MixerRules.KindTrack => "Keys",
        MixerRules.KindProgram => "0-7",
        MixerRules.KindChannel => "1",
        _ => "",
    };

    private Border FallbackCard()
    {
        var row = new DockPanel();
        var label = new TextBlock { Text = "Everything else goes to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var name = new TextBox { Text = _fallback, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(name, "Fallback group name");
        name.TextChanged += (_, _) => _fallback = name.Text;
        row.Children.Add(label);
        row.Children.Add(name);
        return Card(row);
    }

    private static Brush BrushFor(string hex) => TabForge.Visualization.ColourText.BrushOr(hex);
}
