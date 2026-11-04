using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

/// <summary>
/// The one confirmation for "Approve all…" / "Approve selected…" in the plug-in browser: "Approve plug-ins?", the sentence with the count, a collapsible scrolling list of
/// the plug-ins (name and folder) and Cancel / "Approve N". There is no "also approve future files" option. <see cref="Confirmed"/> is false on Cancel or Esc.
/// Owns: the controls. Does not own: the texts (PluginApprovalPresenter), the approval or the probes (PluginBrowserSession).
/// Tests: TabForge.Avalonia.Tests.PluginApprovalWindowTests.
/// </summary>
public sealed class PluginApprovalWindow : Window
{
    public bool Confirmed { get; private set; }

    public PluginApprovalWindow(IReadOnlyList<VstPluginInfo> plugins)
    {
        PromptChrome.Setup(this, PluginApprovalPresenter.Title, DialogMetrics.PluginApprovalWidth);
        var column = PromptChrome.Column(PluginApprovalPresenter.Title, PromptChrome.Muted("MessageText", PluginApprovalPresenter.Body(plugins.Count)));
        var list = new StackPanel { Name = "PluginNames" };
        foreach (var plugin in plugins) list.Children.Add(new TextBlock { Text = PluginApprovalPresenter.ItemText(plugin), TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis });
        column.Children.Add(new Expander
        {
            Name = "PluginListExpander", Header = $"The {plugins.Count} {(plugins.Count == 1 ? "plug-in" : "plug-ins")}", IsExpanded = true,
            Content = new ScrollViewer { MaxHeight = DialogMetrics.PluginApprovalListMaxHeight, Content = list },
        });
        var cancel = PromptChrome.Action("CancelButton", "Cancel", primary: false, isCancel: true);
        cancel.Click += (_, _) => Close();
        var approve = PromptChrome.Action("ApproveButton", PluginApprovalPresenter.ApproveLabel(plugins.Count), primary: false);
        approve.Click += (_, _) => { Confirmed = true; Close(); };
        column.Children.Add(PromptChrome.Footer(null, null, cancel, approve));
        Content = column;
    }

    /// <summary>Shows the question over <paramref name="owner"/>; true only when the user pressed Approve.</summary>
    public static async Task<bool> AskAsync(Window? owner, IReadOnlyList<VstPluginInfo> plugins) =>
        await DialogLauncher.AskFrom<PluginApprovalWindow, bool>(owner, () => new PluginApprovalWindow(plugins), window => window.Confirmed);
}
