namespace TabForge.Docking;

/// <summary>Serializable workspace layout. It contains panel placement only, never document data.</summary>
public sealed class DockWorkspaceState
{
    public int Version { get; set; } = 1;
    public DockNodeState? Root { get; set; }
    public List<DockFloatingState> Floating { get; set; } = new();
    public List<string> ClosedPanels { get; set; } = new();

    public static IEnumerable<string> EnumeratePanelIds(DockNodeState? node)
    {
        if (node is null) yield break;
        if (node.Kind == "tabs")
            foreach (var id in node.Panels) yield return id;
        if (node.Kind == "split")
        {
            foreach (var id in EnumeratePanelIds(node.First)) yield return id;
            foreach (var id in EnumeratePanelIds(node.Second)) yield return id;
        }
    }
}

/// <summary>A split or tab host in the workspace tree.</summary>
public sealed class DockNodeState
{
    /// <summary>"editor", "tabs", or "split".</summary>
    public string Kind { get; set; } = "tabs";
    /// <summary>Stable target identity; kept when panels are closed.</summary>
    public string HostId { get; set; } = Guid.NewGuid().ToString("N");
    public List<string> Panels { get; set; } = new();
    public string? SelectedPanel { get; set; }
    /// <summary>"Horizontal" lays out left/right; "Vertical" lays out top/bottom.</summary>
    public string Orientation { get; set; } = "Horizontal";
    /// <summary>Fraction assigned to First, excluding the splitter.</summary>
    public double Ratio { get; set; } = 0.5;
    public DockNodeState? First { get; set; }
    public DockNodeState? Second { get; set; }
}

/// <summary>One independent floating workspace root in device-independent coordinates.</summary>
public sealed class DockFloatingState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DockNodeState? Root { get; set; }
    public double Left { get; set; } = 180;
    public double Top { get; set; } = 140;
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 260;
}

public enum DockDropZone
{
    Center,
    Left,
    Right,
    Top,
    Bottom
}
