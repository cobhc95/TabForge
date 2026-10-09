using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace TabForge.Views.EffectEditors;

public enum EditorAnswer { Cancel, Ok, Clean }

// Owns: the shared frame of every note-effect editor: themed title bar and footer, OK / Clean / Cancel, Enter = OK, Esc = Cancel, tab order.
// Does not own: the editor body (a caller-built element), presets (EffectPresetStore), or the edit that follows the answer (EffectEditorFlow).
// Tests: TestEffectEditors.
/// <summary>The themed frame (same look as <see cref="ThemedConfirmDialog"/>, via the same theme brushes) around one effect editor's controls.</summary>
internal sealed class ThemedEditorDialog : Window
{
    private readonly Button _ok;
    private readonly Button _clean;

    public ThemedEditorDialog(string title, UIElement body, bool canClean = true)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Res("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 34, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = new Border
        {
            Background = Res("PanelBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Margin = new Thickness(12), Child = layout,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black }
        };
        KeyboardNavigation.SetTabNavigation(layout, KeyboardNavigationMode.Cycle);

        var titleBar = new Border { Background = Res("ChromeStripBrush"), CornerRadius = new CornerRadius(8, 8, 0, 0), Padding = new Thickness(14, 0, 2, 0) };
        WindowChrome.SetIsHitTestVisibleInChrome(titleBar, true);
        titleBar.Child = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Res("TextBrush") };
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        layout.Children.Add(titleBar);

        var bodyHost = new Border { Margin = new Thickness(16), Child = body };
        Grid.SetRow(bodyHost, 1);
        layout.Children.Add(bodyHost);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 10, 14, 12) };
        _ok = MakeButton("OK", EditorAnswer.Ok, tab: 100, isDefault: true, toolTip: "Apply the effect  (Enter)");
        _clean = MakeButton("Clean", EditorAnswer.Clean, tab: 101, toolTip: "Remove the effect from the note");
        _clean.IsEnabled = canClean;
        actions.Children.Add(_ok);
        actions.Children.Add(_clean);
        actions.Children.Add(MakeButton("Cancel", EditorAnswer.Cancel, tab: 102, isCancel: true, toolTip: "Close without changing anything  (Esc)"));
        var footer = new Border
        {
            Background = Res("Panel2Brush"), BorderBrush = Res("BorderSoftBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 8, 8), Child = actions
        };
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Answer(EditorAnswer.Cancel);
            else if (e.Key == Key.Enter && e.OriginalSource is not (Button or TextBox)) Answer(EditorAnswer.Ok);   // a focused button keeps its own Enter
            else return;
            e.Handled = true;
        };
    }

    public EditorAnswer Result { get; private set; } = EditorAnswer.Cancel;

    /// <summary>Shows the dialog modally (through <see cref="DialogHost"/>, so the screenshot tour and tests can adopt it).</summary>
    public EditorAnswer Ask()
    {
        DialogHost.ShowModal(this);
        return Result;
    }

    /// <summary>Test seam: answers as a click on that button would, without showing the dialog.</summary>
    internal void AnswerForTest(EditorAnswer answer) => Result = answer;
    internal Button OkButton => _ok;
    internal Button CleanButton => _clean;

    private void Answer(EditorAnswer answer)
    {
        Result = answer;
        DialogResult = true;
    }

    private Button MakeButton(string text, EditorAnswer answer, int tab, bool isDefault = false, bool isCancel = false, string? toolTip = null)
    {
        var button = new Button
        {
            Content = text, MinWidth = 76, Height = 28, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0),
            IsDefault = isDefault, IsCancel = isCancel, ToolTip = toolTip, TabIndex = tab
        };
        if (isDefault) button.SetResourceReference(StyleProperty, "AccentButton");   // label ink reads on the accent in every theme
        button.Click += (_, _) => Answer(answer);
        return button;
    }

    private Brush Res(string key) => (Brush)FindResource(key);
}
