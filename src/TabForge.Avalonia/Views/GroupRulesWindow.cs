using TabForge.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Group rules editor: one card per group (priority arrows, colour menu, name, rule rows with type and value), the fallback card,
/// the scope radio buttons and the problem line. OK closes with the built <see cref="GroupRulesResult"/>, Reset to app rules and Reset to
/// defaults with their results, Cancel and Esc with null. The window has no discard question: the WPF editor has none either.
/// Owns: the window's controls, their order and texts, and the wiring of each control to <see cref="GroupRulesPresenter"/>.
/// Does not own: the rules, their checks and the result (GroupRulesPresenter), the rule matching (MixerRules), applying the result to a song.
/// Sample state: the preview has no song, so the caller passes a song with no rules of its own (it follows the app-wide rules).
/// Tests: TabForge.Avalonia.Tests.GroupRulesAndColourPickerTests.
/// </summary>
public sealed class GroupRulesWindow : Window
{
    private readonly GroupRulesPresenter _presenter;
    private readonly StackPanel _list = new();
    private readonly RadioButton _allSongs = new() { Content = "All songs (app settings)", Margin = new Thickness(0, 0, 16, 0), GroupName = "scope", VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _thisSong = new() { Content = "This song only", GroupName = "scope", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _problem = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly bool _hasOwnRules;

    /// <param name="mixer">The song's mixer settings; its own rules are edited when it has them, else the app-wide rules.</param>
    public GroupRulesWindow(MixerSettings mixer)
    {
        _presenter = new GroupRulesPresenter(mixer);
        _hasOwnRules = mixer.HasOwnRules;
        Title = "Group rules";
        Width = 640; Height = 660; MinWidth = 520; MinHeight = 400; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _problem[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");

        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "A track goes to the first group (top to bottom) that has a matching rule; a group matches when any of its rules does. " +
                   "Tracks no group matches go to the last group. A track you moved to another group by hand stays there until you choose \"Return to rule group\" in its right-click menu.",
        };
        var list = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list };
        Grid.SetRow(list, 1);
        var scope = ScopeRow();
        Grid.SetRow(scope, 2);
        Grid.SetRow(_problem, 3);
        var buttons = ButtonRow();
        Grid.SetRow(buttons, 4);

        var root = new Grid { Margin = new Thickness(14) };
        for (var row = 0; row < 5; row++)
            root.RowDefinitions.Add(new RowDefinition(row == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
        root.Children.Add(intro);
        Grid.SetRow(intro, 0);
        root.Children.Add(list);
        root.Children.Add(scope);
        root.Children.Add(_problem);
        root.Children.Add(buttons);
        Content = root;
        Rebuild();
    }

    private Grid ButtonRow()
    {
        var add = new Button { Content = "Add group", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        var reset = new Button { Content = "Reset to defaults", Padding = new Thickness(10, 3, 10, 3) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = DialogMetrics.DialogButtonMinWidth, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = DialogMetrics.DialogButtonMinWidth, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        add.Click += (_, _) => { _presenter.AddGroup(); Rebuild(); };
        reset.Click += (_, _) => { _presenter.ResetToDefaults(); Rebuild(); };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        var grid = new Grid { Margin = DialogMetrics.FooterGap };
        grid.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { add, reset } });
        grid.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } });
        return grid;
    }

    private StackPanel ScopeRow()
    {
        (_hasOwnRules ? _thisSong : _allSongs).IsChecked = true;
        _allSongs.IsCheckedChanged += (_, _) => { if (_allSongs.IsChecked == true) _presenter.AppliesToThisSongOnly = false; };
        _thisSong.IsCheckedChanged += (_, _) => { if (_thisSong.IsChecked == true) _presenter.AppliesToThisSongOnly = true; };
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new TextBlock { Text = "Apply to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, _allSongs, _thisSong },
        });
        if (_hasOwnRules)
        {
            var back = new Button { Content = "Reset to app rules", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            ToolTip.SetTip(back, "Drop this song's own rules: it follows the app-wide rules again");
            back.Click += (_, _) => Close(GroupRulesPresenter.ResetToAppResult());
            stack.Children.Add(back);
            stack.Children.Add(SecondaryText("This song has its own rules (they win over the app-wide ones). \"All songs\" replaces them."));
        }
        else
        {
            stack.Children.Add(SecondaryText("This song follows the app-wide rules."));
        }
        return stack;
    }

    private static TextBlock SecondaryText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        return block;
    }

    /// <summary>Shows a problem under the list; empty hides the line so it leaves no gap.</summary>
    private void Problem(string text)
    {
        _problem.Text = text;
        _problem.IsVisible = text.Length > 0;
    }

    private void Accept()
    {
        var result = _presenter.TryBuild(out var problem);
        if (result is null) { Problem(problem); return; }
        Close(result);
    }

    private void Rebuild()
    {
        Problem("");
        _list.Children.Clear();
        for (var i = 0; i < _presenter.Groups.Count; i++) _list.Children.Add(GroupCard(i));
        _list.Children.Add(FallbackCard());
    }

    private static Border Card(Control content)
    {
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), Child = content };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("ChromeBorder");
        return card;
    }

    private static Button Small(string text, string tip)
    {
        var button = new Button { Content = text, MinWidth = 26, Height = 24, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private Border GroupCard(int index)
    {
        var entry = _presenter.Groups[index];
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var number = new TextBlock { Text = $"{index + 1}.", Width = 22, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        ToolTip.SetTip(number, "Priority: a track goes to the first matching group");
        var up = Small("▲", "Move the group up (higher priority)"); up.IsEnabled = index > 0;
        var down = Small("▼", "Move the group down (lower priority)"); down.IsEnabled = index < _presenter.Groups.Count - 1;
        var remove = Small("✕", "Delete the group (its tracks fall to the next matching group)");
        var colour = new Button
        {
            Width = 28, Height = 24, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Content = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = BrushFor(entry.Def.Colour ?? TrackColouring.ColourOf(entry.Def.Name, null)) },
        };
        ToolTip.SetTip(colour, "Group colour (colours the group's tracks)");
        var name = new TextBox { Text = entry.Def.Name, MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) _presenter.SetGroupName(index, name.Text ?? ""); };
        DockPanel.SetDock(number, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right); DockPanel.SetDock(down, Dock.Right); DockPanel.SetDock(up, Dock.Right); DockPanel.SetDock(colour, Dock.Right);
        head.Children.Add(number);
        head.Children.Add(remove); head.Children.Add(down); head.Children.Add(up); head.Children.Add(colour);
        head.Children.Add(name);
        stack.Children.Add(head);
        up.Click += (_, _) => { _presenter.MoveGroup(index, index - 1); Rebuild(); };
        down.Click += (_, _) => { _presenter.MoveGroup(index, index + 1); Rebuild(); };
        remove.Click += (_, _) => { _presenter.RemoveGroup(index); Rebuild(); };
        colour.Click += (_, _) => OpenColourMenu(colour, index);

        var hint = new TextBlock { Text = GroupRulesPresenter.RulesHint(entry.Def.Rules.Count), Margin = new Thickness(28, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        stack.Children.Add(hint);
        for (var ruleIndex = 0; ruleIndex < entry.Def.Rules.Count; ruleIndex++) stack.Children.Add(RuleRow(index, ruleIndex));
        var addRule = new Button { Content = "Add rule", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(28, 4, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        addRule.Click += (_, _) => { _presenter.AddRule(index); Rebuild(); };
        stack.Children.Add(addRule);
        return Card(stack);
    }

    private void OpenColourMenu(Button anchor, int index)
    {
        var menu = MenuStability.CreateContextMenu();
        foreach (var (colourName, hex) in TrackColoursPresenter.Palette)
        {
            var item = new MenuItem { Header = colourName, Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = BrushFor(hex) } };
            item.Click += (_, _) => { _presenter.SetGroupColour(index, hex); Rebuild(); };
            menu.Items.Add(item);
        }
        menu.Open(anchor);
    }

    private Grid RuleRow(int group, int ruleIndex)
    {
        var rule = _presenter.Groups[group].Def.Rules[ruleIndex];
        var row = new Grid { Margin = new Thickness(28, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var kind = new ComboBox { ItemsSource = MixerRules.RuleKinds, SelectedItem = rule.Kind, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is string chosen && _presenter.ChangeRuleKind(group, ruleIndex, chosen)) Rebuild();
        };
        Grid.SetColumn(kind, 0);
        Control value;
        var choices = GroupRulesPresenter.ValueChoices(rule.Kind);
        if (choices is not null)
        {
            var combo = new ComboBox { ItemsSource = choices, SelectedItem = rule.Value, VerticalAlignment = VerticalAlignment.Center };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string chosen) _presenter.SetRuleValue(group, ruleIndex, chosen); };
            value = combo;
        }
        else
        {
            var box = new TextBox { Text = rule.Value, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(box, GroupRulesPresenter.ValueHint(rule.Kind));
            box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) _presenter.SetRuleValue(group, ruleIndex, box.Text ?? ""); };
            value = box;
        }
        Grid.SetColumn(value, 1);
        var remove = Small("✕", "Remove the rule");
        remove.Click += (_, _) => { _presenter.RemoveRule(group, ruleIndex); Rebuild(); };
        Grid.SetColumn(remove, 2);
        row.Children.Add(kind); row.Children.Add(value); row.Children.Add(remove);
        return row;
    }

    private Border FallbackCard()
    {
        var row = new DockPanel();
        var label = new TextBlock { Text = "Everything else goes to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var name = new TextBox { Text = _presenter.Fallback, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
        name.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) _presenter.Fallback = name.Text ?? ""; };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(name);
        return Card(row);
    }

    private static IBrush BrushFor(string hex) =>
        new SolidColorBrush(TabForge.Avalonia.Platform.AvaloniaPalette.ColorOf(ColourValues.FromHex(hex) ?? new Rgba(0x80, 0x80, 0x80)));

}
