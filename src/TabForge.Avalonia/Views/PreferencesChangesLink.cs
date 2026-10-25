using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>What the Settings window's changes link asks of the window: the unkept changes, a live revert of one, and a jump to one setting.</summary>
public interface IPreferencesChangesHost
{
    /// <summary>The settings whose shown value differs from the saved snapshot, in page order.</summary>
    IReadOnlyList<PreferencesPagePresenter.PreferenceChange> Changes();

    /// <summary>Reverts one setting to its saved value, live, as an editor write does.</summary>
    void UndoChange(string key);

    /// <summary>Shows the setting's page, opens its "More options" group if needed, and focuses its editor.</summary>
    void JumpToChange(string key);
}

// Owns: the Settings footer link "N changes · Review" (hidden at N = 0), its hover tooltip listing every change, and its popup: the changes grouped by
//   page, an undo arrow per line and the setting name as a jump link.
// Does not own: which settings changed (PreferencesPagePresenter.Changes over the saved snapshot), the revert and the jump (the host window), the
//   status text, and OK, Apply and Cancel.
// Tests: TabForge.Avalonia.Tests.PreferencesChangesLinkTests.
public sealed class PreferencesChangesLink
{
    /// <summary>The name of the link button, for the automation tree and the tests.</summary>
    public const string LinkName = "PreferencesChangesReview";
    private const string UndoGlyph = "↶";
    private const string UndoTooltip = "Undo this change";
    private const string ReviewSuffix = "Review";
    private const double LineSpacing = 6;
    private const double LineGap = 8;
    private const double PopupMaxHeight = 320;
    private const double PopupCornerRadius = 6;
    private static readonly Thickness PopupPadding = new(14, 10);

    private readonly IPreferencesChangesHost _host;
    private readonly Button _link = new() { Name = LinkName, IsVisible = false };
    private readonly StackPanel _list = new() { Spacing = LineSpacing };
    private readonly Popup _popup;

    public PreferencesChangesLink(IPreferencesChangesHost host)
    {
        _host = host;
        TextLinkButton.Apply(_link);
        _link[!Button.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        AutomationProperties.SetName(_link, "Review changes");
        var panel = new Border { Padding = PopupPadding, CornerRadius = new CornerRadius(PopupCornerRadius), BorderThickness = new Thickness(1), Child = new ScrollViewer { MaxHeight = PopupMaxHeight, Content = _list } };
        panel[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        panel[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        _popup = new Popup { PlacementTarget = _link, Placement = PlacementMode.TopEdgeAlignedLeft, IsLightDismissEnabled = true, Child = panel };
        _link.Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;
        View = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _link, _popup } };
    }

    /// <summary>The footer control: the link and its popup.</summary>
    public Control View { get; }

    /// <summary>The number of unkept changes at the last <see cref="Refresh"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Reads the changes from the host and shows the link, its tooltip and, while the popup is open, its list.</summary>
    public void Refresh()
    {
        var changes = _host.Changes();
        Count = changes.Count;
        _link.IsVisible = changes.Count > 0;
        _link.Content = changes.Count == 1 ? $"1 change · {ReviewSuffix}" : $"{changes.Count} changes · {ReviewSuffix}";
        ToolTip.SetTip(_link, string.Join("\n", changes.Select(change => change.Line)));
        if (changes.Count == 0) _popup.IsOpen = false;
        RebuildList(changes);
    }

    private void RebuildList(IReadOnlyList<PreferencesPagePresenter.PreferenceChange> changes)
    {
        _list.Children.Clear();
        foreach (var page in changes.GroupBy(change => change.Page))
        {
            _list.Children.Add(new TextBlock { Text = page.Key, FontWeight = FontWeight.SemiBold });
            foreach (var change in page) _list.Children.Add(LineRow(change));
        }
    }

    private Control LineRow(PreferencesPagePresenter.PreferenceChange change)
    {
        var undo = new Button { Content = UndoGlyph, Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        TextLinkButton.Apply(undo);
        ToolTip.SetTip(undo, UndoTooltip);
        AutomationProperties.SetName(undo, $"Undo {change.Setting}");
        undo.Click += (_, _) => _host.UndoChange(change.Key);
        var name = new Button { Content = change.Setting, Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        TextLinkButton.Apply(name);
        name.Click += (_, _) => { _popup.IsOpen = false; _host.JumpToChange(change.Key); };
        var detail = new TextBlock { Text = $": {change.OldText} → {change.NewText}", VerticalAlignment = VerticalAlignment.Center };
        detail[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = LineGap, Children = { undo, name, detail } };
    }
}
