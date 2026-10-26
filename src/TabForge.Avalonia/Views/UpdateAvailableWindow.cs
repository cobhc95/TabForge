using TabForge.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "update available" window, the Avalonia window for it: the same texts, buttons and
/// defaults, all read from UpdateAvailablePresenter. The preview runs no update check, so it opens on <see cref="SampleRelease"/>.
/// Opening the download page needs the full app: its button says so on the status line and changes nothing.
/// Owns: the layout, the controls and their event wiring. Does not own: the texts and button rules (UpdateAvailablePresenter), the
/// update check (UpdateService) and opening the page.
/// Tests: TabForge.Avalonia.Tests.UpdateAvailableWindowTests.
/// </summary>
public sealed class UpdateAvailableWindow : Window
{
    /// <summary>What the window returns when it closes: whether to open the release page, and the opt-out box's value.</summary>
    public sealed record Answer(bool OpenPage, bool CheckAutomatically);

    /// <summary>A newer release for the preview, with its page address built the way the update check builds it.</summary>
    public static ReleaseInfo SampleRelease { get; } = new("0.2.0", UpdateService.PageFor("0.2.0"));

    private readonly bool _previewOnly;

    private readonly CheckBox _checkAutomatically = new() { Name = "CheckAutomaticallyBox" };
    private readonly TextBlock _status = new() { Name = "StatusText", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    /// <param name="previewOnly">True in the preview: the download button only says the full app is needed. False: it closes with <see cref="Answer"/> and OpenPage set.</param>
    public UpdateAvailableWindow(UpdateAvailablePresenter presenter, bool previewOnly = true)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _previewOnly = previewOnly;
        Title = presenter.Title;
        Width = 460; SizeToContent = SizeToContent.Height; CanResize = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        root.Children.Add(new TextBlock
        {
            Name = "HeadingText", Text = presenter.Heading, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(Muted(new TextBlock
        {
            Name = "DetailText", Text = presenter.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        }));

        _checkAutomatically.Content = UpdateAvailablePresenter.CheckBoxText;
        _checkAutomatically.IsChecked = presenter.CheckAutomatically;
        _checkAutomatically.Margin = new Thickness(0, 0, 0, 4);
        ToolTip.SetTip(_checkAutomatically, UpdateAvailablePresenter.CheckBoxToolTip);
        root.Children.Add(_checkAutomatically);
        root.Children.Add(Muted(new TextBlock
        {
            Name = "PrivacyText", Text = UpdateAvailablePresenter.PrivacyText, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 0, 0, 14)
        }));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (presenter.ShowsDownloadButton)
        {
            var download = new Button
            {
                Name = "DownloadButton", Content = UpdateAvailablePresenter.DownloadButtonText, Padding = new Thickness(14, 5, 14, 5),
                Margin = new Thickness(0, 0, 8, 0), IsDefault = presenter.DownloadIsDefault
            };
            if (presenter.DownloadIsDefault) download.Classes.Add(AvaloniaDefaultLooks.AccentLook);
            download.Click += (_, _) =>
            {
                if (_previewOnly) _status.Text = "Opening the download page needs the full app; the preview cannot open a browser.";
                else Close(new Answer(OpenPage: true, CheckAutomatically));
            };
            buttons.Children.Add(download);
        }
        var close = new Button
        {
            Name = "CloseButton", Content = presenter.CloseButtonText, Padding = new Thickness(14, 5, 14, 5),
            IsCancel = true, IsDefault = presenter.CloseIsDefault
        };
        close.Click += (_, _) => Close(new Answer(OpenPage: false, CheckAutomatically));
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        root.Children.Add(_status);

        Content = root;
    }

    /// <summary>The opt-out box's value: the automatic check setting the window would return.</summary>
    public bool CheckAutomatically => _checkAutomatically.IsChecked == true;

    /// <summary>The text on the status line, empty until a button that needs the full app is pressed.</summary>
    public string StatusText => _status.Text ?? "";

    private static TextBlock Muted(TextBlock block)
    {
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return block;
    }
}
