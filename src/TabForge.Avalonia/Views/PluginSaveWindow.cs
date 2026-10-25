using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// "Save song with audio settings": the question, one button per save option (the recommended one is the default and outlined) and Cancel.
/// An option closes with its value (<see cref="PluginSaveChoicePresenter.GpWithData"/> and the others); Cancel and Esc close with null.
/// Owns: the window's controls and layout. Does not own: the texts and options (<see cref="PluginSaveChoicePresenter"/>) or the save.
/// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
/// </summary>
public sealed class PluginSaveWindow : Window
{
    public PluginSaveWindow(string fileName)
    {
        Title = PluginSaveChoicePresenter.Title;
        Width = 540; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = PluginSaveChoicePresenter.Headline, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(Tinted(new TextBlock { Text = PluginSaveChoicePresenter.Detail(fileName), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }, "TextMuted"));
        foreach (var option in PluginSaveChoicePresenter.Options)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6),
                Content = new StackPanel { Children =
                {
                    new TextBlock { Text = option.Title, FontWeight = FontWeight.SemiBold },
                    Tinted(new TextBlock { Text = option.Text, TextWrapping = TextWrapping.Wrap, FontSize = 11 }, "TextSecondary"),
                } },
            };
            if (option.Recommended)
            {
                button.IsDefault = true;
                button.BorderThickness = new Thickness(2);
                button[!BorderBrushProperty] = new DynamicResourceExtension("Accent");
            }
            button.Click += (_, _) => Close(option.Value);
            root.Children.Add(button);
        }
        var cancel = new Button { Content = PluginSaveChoicePresenter.CancelButton, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = DialogMetrics.ActionButtonPadding };
        cancel.Click += (_, _) => Close(null);
        root.Children.Add(cancel);
        Content = root;
    }

    // The option explanations sit on the button fill, where the muted text colour reads too faintly, so they use the secondary one.
    private static TextBlock Tinted(TextBlock text, string brush)
    {
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(brush);
        return text;
    }
}
