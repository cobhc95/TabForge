using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Docking;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the "Dock preview..." window: the default layout of the dock rendered with placeholder pane content.
// Does not own: the dock rendering (DockWorkspaceView), the layout edits (DockWorkspaceController), real pane content or the main window.
// Tests: TabForge.Avalonia.Tests.DockWorkspaceViewTests.
/// <summary>The default dock layout with a label for each pane, opened from the preview's Tools menu. Tabs drag, close and reset as in the app.</summary>
public sealed class DockPreviewWindow : Window
{
    public DockPreviewWindow()
    {
        Title = "Dock preview";
        ShowInTaskbar = false;
        Width = 1100;
        Height = 700;
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => Placeholder(id));
        setup.RegisterEditor(() => Placeholder(DockWorkspaceView.EditorContentId));
        View = new DockWorkspaceView(setup.ContentOf);
        Controller = new DockWorkspaceController(View, setup);
        Content = View;
    }

    public DockWorkspaceView View { get; }

    public DockWorkspaceController Controller { get; }

    public DockWorkspaceState State => Controller.State;

    /// <summary>A pane's stand-in: its id centred on the panel colour.</summary>
    private static Control Placeholder(string id)
    {
        var label = new TextBlock { Text = id, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return new Border { Name = "Pane_" + id, Child = label };
    }
}
