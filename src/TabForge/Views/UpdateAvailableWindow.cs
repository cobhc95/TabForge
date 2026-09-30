using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// "A new version is available" (or "up to date" after a manual check). Shows only version numbers
/// (nothing from the network reply is displayed), offers to open the release page in the browser, and has
/// a visible opt-out box for the automatic check. Colours come from the theme resources.
/// </summary>
public static class UpdateAvailableWindow
{
    public sealed record Result(bool OpenPage, bool CheckAutomatically);

    /// <param name="release">The newer release, or null to say the installed version is the latest.</param>
    public static Result Show(Window? owner, ReleaseInfo? release, string currentVersion, bool checkAutomatically)
    {
        var open = false;
        var w = new Window
        {
            Title = release is null ? "No update available" : "Update available",
            Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");

        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        root.Children.Add(new TextBlock
        {
            Text = release is null ? "TabForge is up to date" : $"TabForge {release.Version} is available",
            FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6)
        });
        var detail = new TextBlock
        {
            Text = release is null
                ? $"You have the latest version ({currentVersion})."
                : $"You have {currentVersion}. The download page on GitHub has the installer, the portable zip and what changed.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(detail);

        var optIn = new CheckBox
        {
            Content = "Check for updates automatically (once a day)", IsChecked = checkAutomatically,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = "Also in Settings > General > Updates. Help > Check for updates always works."
        };
        root.Children.Add(optIn);
        var privacy = new TextBlock
        {
            Text = "One anonymous request to GitHub; nothing is downloaded or installed by TabForge.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 0, 0, 14)
        };
        privacy.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(privacy);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (release is not null)
        {
            var download = new Button { Content = "Open download page", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            download.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            download.Foreground = Brushes.White;
            download.Click += (_, _) => { open = true; w.DialogResult = true; };
            buttons.Children.Add(download);
        }
        var close = new Button { Content = release is null ? "OK" : "Later", Padding = new Thickness(14, 5, 14, 5), IsCancel = true, IsDefault = release is null };
        close.Click += (_, _) => w.DialogResult = false;
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        w.Content = root;
        DialogHost.ShowModal(w);
        return new Result(open, optIn.IsChecked == true);
    }
}
