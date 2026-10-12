using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Views;

namespace TabForge.CompactMode;

// Owns: the track list's left column while compact mode is on: the toolbar's own buttons laid out in rows of five in a slim
//   column (the column header row and the inline Master row are hidden), the column's width, and putting
//   every element back in its place and order on leaving.
// Does not own: the buttons and what they do (ArrangementPanel, the window's transport bar); when compact mode is on
//   (ArrangementPanel.CompactMode.cs).
// Tests: TestCompactMode.
internal sealed class CompactColumnLayout
{
    internal const double Cell = 40, RowHeight = 38;
    internal const int PerRow = 5;

    private readonly Grid _host;
    private readonly FrameworkElement _header;
    private readonly DockPanel _toolbar;
    private readonly ContentControl _playbarHost;
    private readonly ColumnDefinition _column;
    private readonly List<(UIElement Element, Panel Parent, int Index)> _moved = new();
    private readonly WrapPanel _wrap = new() { Width = PerRow * Cell, ItemWidth = Cell, ItemHeight = RowHeight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border _cover = new() { BorderThickness = new Thickness(0, 0, 1, 0) };
    private GridLength _widthBefore;
    private Thickness _knobMargin;
    private FrameworkElement? _knob;

    /// <summary><paramref name="header"/> holds the column header row and <paramref name="toolbar"/>; the cover spans <paramref name="host"/>, over the header and the track list (the Master row).</summary>
    public CompactColumnLayout(Grid host, FrameworkElement header, DockPanel toolbar, ContentControl playbarHost, ColumnDefinition column)
    {
        _host = host; _header = header; _toolbar = toolbar; _playbarHost = playbarHost; _column = column;
        _cover.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
        _cover.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        _cover.Child = _wrap;
        Grid.SetRowSpan(_cover, 2);
    }

    public bool IsOn => _moved.Count > 0;
    /// <summary>The buttons in their compact order (rows of <see cref="PerRow"/>).</summary>
    internal IReadOnlyList<UIElement> Buttons => _wrap.Children.Cast<UIElement>().ToList();

    /// <summary>Lays the toolbar's buttons out in the slim column and narrows the column to it.</summary>
    public void Enter()
    {
        if (IsOn) return;
        var playbar = ((_playbarHost.Content as Border)?.Child as Panel)?.Children.OfType<ButtonBase>().Cast<UIElement>().ToList() ?? new List<UIElement>();
        var buttons = _toolbar.Children.OfType<Button>().ToList();           // live input, tuner, snap, mixer
        var knob = _toolbar.Children.OfType<KnobControl>().FirstOrDefault();
        var zoom = _toolbar.Children.OfType<StackPanel>().FirstOrDefault();  // zoom in over zoom out
        var order = new List<UIElement>(playbar);
        order.AddRange(buttons.Skip(1).Reverse());                           // mixer, snap, tuner
        if (knob is not null) order.Add(knob);
        if (buttons.Count > 0) order.Add(buttons[0]);                        // live input
        if (zoom is not null) order.AddRange(zoom.Children.Cast<UIElement>().ToList());
        // Every place is noted before anything moves, so each index is the one in the untouched panel.
        foreach (var element in order)
            if ((element as FrameworkElement)?.Parent is Panel parent) _moved.Add((element, parent, parent.Children.IndexOf(element)));
        foreach (var (element, parent, _) in _moved)
        {
            parent.Children.Remove(element);
            _wrap.Children.Add(element);
        }
        if (knob is not null) { _knob = knob; _knobMargin = knob.Margin; knob.Margin = new Thickness(0); }   // its toolbar margins do not fit a cell
        _header.Visibility = Visibility.Collapsed;
        _host.Children.Add(_cover);
        _widthBefore = _column.Width;
        _column.Width = new GridLength(PerRow * Cell + 12);
    }

    /// <summary>Puts every element back where it was, in its order, and the column back at its width.</summary>
    public void Exit()
    {
        if (!IsOn) return;
        _wrap.Children.Clear();
        // Lowest index first, so each element lands at the index it had.
        foreach (var (element, parent, index) in _moved.OrderBy(m => m.Index)) parent.Children.Insert(Math.Min(index, parent.Children.Count), element);
        _moved.Clear();
        if (_knob is not null) { _knob.Margin = _knobMargin; _knob = null; }
        _host.Children.Remove(_cover);
        _header.Visibility = Visibility.Visible;
        _column.Width = _widthBefore;
    }
}
