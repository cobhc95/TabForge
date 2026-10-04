using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TabForge.Views;

/// <summary>What the global tuning button raises on the arrangement panel (implemented by <see cref="ArrangementPanel"/>).</summary>
internal interface ITuningButtonHost
{
    void RaiseTuningIconClicked();
    void RaiseTuningNumberClicked();
    void RaiseTuningMenuRequested();
    void RaiseTuningShiftEdited(int semitones);
}

// Owns: the tuning button's content (fork icon + signed shift), its single / double / right-click gestures and the inline editor.
// Does not own: the button itself and its placement (ArrangementPanel header), what the clicks do (MainWindow via the panel's events).
// Tests: TestTimelineContextMenus.
internal sealed class TuningButtonController
{
    private readonly ITuningButtonHost _host;
    private readonly TextBlock _text = new() { Text = "0", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 3, 0) };
    private System.Windows.Shapes.Path? _fork;
    private DispatcherTimer? _clickTimer;
    private bool _clickOnIcon;
    private TextBox? _editor;
    private Button? _button;

    public TuningButtonController(ITuningButtonHost host) => _host = host;

    public void SetLabel(string text) => _text.Text = text;

    // Tuning-fork icon + signed semitone shift (e.g. -2, 0, +1).
    public StackPanel BuildContent()
    {
        var fork = _fork = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,1 V7 A3,3 0 0 0 9,7 V1 M6,10 V15"),
            Stroke = (Brush)Application.Current.FindResource("TextBrush"),
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 12, Height = 16, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center,
            Fill = Brushes.Transparent, ToolTip = "Global tuning window",
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(fork);
        content.Children.Add(_text);
        return content;
    }

    public void Attach(Button button)
    {
        _button = button;
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // single vs double click is resolved here, not by Button.Click
            if (_editor is not null) return;
            _clickTimer ??= new DispatcherTimer(DispatcherPriority.Input, button.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime())
            };
            _clickTimer.Stop();
            if (e.ClickCount >= 2) { BeginInlineEdit(); return; }
            _clickOnIcon = _fork is not null && _fork.IsMouseOver;
            _clickTimer.Tick -= SingleClick;
            _clickTimer.Tick += SingleClick;
            _clickTimer.Start();
        };
        button.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; _host.RaiseTuningMenuRequested(); };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void SingleClick(object? sender, EventArgs e)
    {
        _clickTimer?.Stop();
        if (_clickOnIcon) _host.RaiseTuningIconClicked();
        else _host.RaiseTuningNumberClicked();
    }

    // Double-click: the number becomes a text box with its value selected; Enter or clicking away saves.
    private void BeginInlineEdit()
    {
        if (_button?.Content is not StackPanel content) return;
        var index = content.Children.IndexOf(_text);
        var editor = new TextBox
        {
            Text = _text.Text.TrimStart('+'), MinWidth = 30, Margin = _text.Margin, Padding = new Thickness(1, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
        };
        var done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            content.Children.Remove(editor);
            content.Children.Insert(index, _text);
            _editor = null;
            if (commit && int.TryParse(editor.Text.Trim().TrimStart('+'), out var value))
                _host.RaiseTuningShiftEdited(Math.Clamp(value, -24, 24));
        }
        editor.KeyDown += (_, k) =>
        {
            if (k.Key == Key.Enter) { Finish(true); k.Handled = true; }
            else if (k.Key == Key.Escape) { Finish(false); k.Handled = true; }
        };
        editor.LostKeyboardFocus += (_, _) => Finish(true);
        editor.PreviewTextInput += (_, t) => t.Handled = !t.Text.All(c => char.IsDigit(c) || c is '-' or '+');
        content.Children.RemoveAt(index);
        content.Children.Insert(index, editor);
        _editor = editor;
        editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { editor.Focus(); Keyboard.Focus(editor); editor.SelectAll(); }));
    }
}
