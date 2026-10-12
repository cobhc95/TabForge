using System.Windows.Controls;
using TabForge.CompactMode;

namespace TabForge.Views;

// Owns: the ArrangementPanel side of compact mode: the flag, hiding the Add-track strip with it, keeping the list's scroll position across it and switching the left column to its slim layout (CompactColumnLayout).
// Does not own: the row geometry that hides the rows (ArrangementPanel.RowGeometry.cs reads MasterLane.IsCompact) and the pane height (CompactModeController).
// Tests: TestCompactMode.
public sealed partial class ArrangementPanel : ICompactModeRows
{
    private bool _compactMode;
    private double _compactScroll;
    private CompactColumnLayout? _compactColumn;

    /// <summary>The slim left column of compact mode (null until compact mode is first turned on).</summary>
    internal CompactColumnLayout? CompactColumn => _compactColumn;

    /// <summary>Compact mode: no track rows, the master track lane at twice its height. The "Show master track" setting is not changed.</summary>
    public bool CompactMode
    {
        get => _compactMode;
        set
        {
            if (_compactMode == value) return;
            if (value) _compactScroll = _controlsScroll.VerticalOffset;
            _compactMode = value;
            _addLane.Suppressed = value;
            if (!value) _compactColumn?.Exit();   // the toolbar and the Master row are back in place before the rows are rebuilt
            MarkMasterLane();
            RefreshMasterLane();
            if (value)
            {
                if (_compactColumn is null && _controlsHeader.Parent is Grid host && _controlsHeader.Child is DockPanel { Children.Count: > 0 } stack && stack.Children[^1] is DockPanel toolbar)
                    _compactColumn = new CompactColumnLayout(host, _controlsHeader, toolbar, _playbarHost, ColumnDefinitions[0]);
                _compactColumn?.Enter();
                SyncToolbarWrap();   // the header is covered: one row, no extra height
                return;
            }
            UpdateLayout();   // the rows are back: the list can scroll to where it was
            SyncToolbarWrap();   // the toolbar's buttons are measured in place again
            _controlsScroll.ScrollToVerticalOffset(_compactScroll);
        }
    }
}
