using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Every small dialog closes on Esc however it was opened: DialogHost presses the dialog's Cancel button itself.</summary>
public static partial class SelfTest
{
    private static void TestDialogEscape()
    {
        var clicks = 0;
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => clicks++;
        var window = new Window { Content = new StackPanel { Children = { new TextBox(), new Button { Content = "OK", IsDefault = true }, cancel } }, Width = 200, Height = 120, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.UpdateLayout();
            var pressed = DialogHost.PressCancel(window);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // automation Invoke is queued
            Check("dialog Esc: the fallback presses the dialog's Cancel button", pressed && clicks == 1, clicks.ToString());
            cancel.IsEnabled = false;
            var pressedDisabled = DialogHost.PressCancel(window);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Check("dialog Esc: a disabled Cancel button is not pressed", !pressedDisabled && clicks == 1);
            cancel.IsEnabled = true;
            var plain = new Window { Content = new Button { Content = "OK" }, ShowInTaskbar = false };
            plain.Show();
            Check("dialog Esc: a dialog with no Cancel button is left alone", !DialogHost.PressCancel(plain));
            plain.Close();
        }
        finally { window.Close(); }

        var previousCapture = DialogHost.Capture;
        var previousScale = ThemeService.UiScaleTransform.ScaleX;
        try
        {
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                DialogHost.Capture = dialog =>
                {
                    ThemeService.ApplyLayoutScale(dialog, scale);
                    dialog.Left = -20000;
                    dialog.Top = -20000;
                    dialog.ShowActivated = false;
                    dialog.ShowInTaskbar = false;
                    var root = (FrameworkElement)dialog.Content;
                    dialog.Show();
                    dialog.UpdateLayout();
                    var buttons = (StackPanel)((StackPanel)root).Children[^1];
                    var okButton = (Button)buttons.Children[0];
                    var cancelButton = (Button)buttons.Children[1];
                    var labels = ((StackPanel)root).Children.OfType<TextBlock>().ToArray();
                    var barLabel = labels.FirstOrDefault(text => text.Text.StartsWith("Starts at bar ", StringComparison.Ordinal));
                    var hint = labels.FirstOrDefault(text => text.Text.StartsWith("Colour changes apply", StringComparison.Ordinal));
                    var bounds = cancelButton.TransformToAncestor(dialog).TransformBounds(new Rect(cancelButton.RenderSize));
                    Check($"marker dialog at {scale:0.##} scale: Cancel fits the WPF client area", bounds.Bottom <= dialog.RenderSize.Height + 0.5,
                        $"window {dialog.ActualHeight:0.0}/{dialog.RenderSize.Height:0.0}, root {root.ActualHeight:0.0}/{root.RenderSize.Height:0.0}, cancel bottom {bounds.Bottom:0.0}");
                    Check($"marker dialog at {scale:0.##} scale: action is default and Cancel is available", okButton.IsDefault && cancelButton.IsCancel && cancelButton.IsEnabled);
                    Check($"marker dialog at {scale:0.##} scale: starting bar is shown", barLabel?.Text == "Starts at bar 120", barLabel?.Text ?? "missing");
                    Check($"marker dialog at {scale:0.##} scale: colour hint is shown", hint?.Text == "Colour changes apply to similar-named sections.", hint?.Text ?? "missing");
                    if (scale == 1.5 && Environment.GetEnvironmentVariable("TABFORGE_DIALOG_EVIDENCE") is { Length: > 0 } imagePath)
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            (int)Math.Ceiling(dialog.RenderSize.Width * 1.5), (int)Math.Ceiling(dialog.RenderSize.Height * 1.5), 144, 144,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(dialog);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                        using var stream = File.Create(imagePath);
                        encoder.Save(stream);
                    }
                    dialog.Close();
                    return false;
                };
                Check($"marker dialog at {scale:0.##} scale: cancel returns no marker",
                    GpDialogs.Marker("Intro", action: "Save", bar: 119, colourHint: "Colour changes apply to similar-named sections.") is null);
            }

            DialogHost.Capture = dialog =>
            {
                ThemeService.ApplyLayoutScale(dialog, 2.0);
                dialog.Left = -20000;
                dialog.Top = -20000;
                dialog.ShowActivated = false;
                dialog.ShowInTaskbar = false;
                var root = (FrameworkElement)dialog.Content;
                dialog.Show();
                dialog.UpdateLayout();
                var buttons = (StackPanel)((StackPanel)root).Children[^1];
                var cancelButton = (Button)buttons.Children[1];
                var bounds = cancelButton.TransformToAncestor(dialog).TransformBounds(new Rect(cancelButton.RenderSize));
                Check("text prompt at 2 scale: Cancel fits the WPF client area", bounds.Bottom <= dialog.RenderSize.Height + 0.5,
                    $"window {dialog.ActualHeight:0.0}/{dialog.RenderSize.Height:0.0}, cancel bottom {bounds.Bottom:0.0}");
                dialog.Close();
                return false;
            };
            Check("text prompt at 2 scale: cancel returns no value", GpDialogs.Prompt("Prompt", "Enter a value") is null);
        }
        finally
        {
            DialogHost.Capture = previousCapture;
            ThemeService.ApplyLayoutScale(new Window { Content = new Grid() }, previousScale);
        }

    }
}
