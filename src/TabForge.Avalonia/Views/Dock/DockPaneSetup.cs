using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Docking;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the registry of dock panes by pane id (DockPaneTable rows) and the editor content: id -> content factory.
// Does not own: the pane rows (DockPaneTable), the layout (DockWorkspaceController) or the rendering (DockWorkspaceView).
// Tests: TabForge.Avalonia.Tests.DockWorkspaceControllerTests.
/// <summary>Panes are registered by id, never found by element name. A factory makes a fresh control the first time the dock rebuilds and hands back the same (detached) control after that; see RegisterShared.</summary>
public sealed class DockPaneSetup
{
    private readonly Dictionary<string, Func<Control>> _content = new(StringComparer.Ordinal);
    private readonly HashSet<string> _naturalHeight = new(StringComparer.Ordinal);
    private Func<Control>? _editor;

    /// <summary>The registered pane ids (the editor is not a pane).</summary>
    public IReadOnlyCollection<string> PaneIds => _content.Keys;

    /// <summary>The panes whose content keeps its own height: a tab group holding one gets a fixed row (DockSizeRules.FixedHostHeight).</summary>
    public IReadOnlySet<string> NaturalHeightIds => _naturalHeight;

    /// <summary>Registers the pane with a table row id; an id the table does not know is a mistake and throws.
    /// <paramref name="keepsNaturalHeight"/> locks the pane's row to its content's height.</summary>
    public void Register(string id, Func<Control> factory, bool keepsNaturalHeight = false)
    {
        if (keepsNaturalHeight) _naturalHeight.Add(id);
        if (DockPaneTable.Rows.All(r => r.Id != id)) throw new ArgumentException($"'{id}' is not a dock pane id.", nameof(id));
        _content[id] = factory;
    }

    public void RegisterEditor(Func<Control> factory) => _editor = factory;

    /// <summary>Changes whether a registered pane keeps its natural height; the set is shared with the dock views, so they read the change at once.</summary>
    public void SetKeepsNaturalHeight(string id, bool keeps)
    {
        if (keeps) _naturalHeight.Add(id);
        else _naturalHeight.Remove(id);
    }

    /// <summary>Registers one control that lives across dock rebuilds: each rebuild takes it out of the old tab body and hands it to the new one.</summary>
    public void RegisterShared(string id, Control control, bool keepsNaturalHeight = false) =>
        Register(id, () => Detached(control), keepsNaturalHeight);

    /// <summary>Registers a labelled "(not ported yet)" placeholder for every table pane nothing registered.</summary>
    public void RegisterPlaceholdersForTheRest()
    {
        foreach (var row in DockPaneTable.Rows.Where(r => !_content.ContainsKey(r.Id)))
        {
            var title = row.PaneTitle;
            Register(row.Id, () => Placeholder(title));
        }
    }

    /// <summary>The control, taken out of the tab body (a Border) that showed it before a rebuild.</summary>
    public static Control Detached(Control control)
    {
        if (control.Parent is Border old) old.Child = null;
        return control;
    }

    private static Control Placeholder(string title)
    {
        var label = new TextBlock { Text = title + " (not ported yet)", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return label;
    }

    /// <summary>The content of a pane id, or of the editor for <see cref="DockWorkspaceView.EditorContentId"/>.</summary>
    public Control ContentOf(string id)
    {
        var factory = id == DockWorkspaceView.EditorContentId ? _editor : _content.GetValueOrDefault(id);
        return factory?.Invoke() ?? throw new InvalidOperationException($"No dock content is registered for '{id}'.");
    }
}
