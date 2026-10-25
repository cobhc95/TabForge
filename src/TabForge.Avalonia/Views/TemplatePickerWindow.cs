using System.Collections.Generic;
using TabForge.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// File > New from template: a drop-down of starting points (built-in first, then the saved templates) with Create and Cancel.
/// Closing returns the chosen name, or null on Cancel / Esc.
/// Owns: the window's controls and the chosen name. Does not own: the list of names (UserTemplates) or opening the song (MainWindowFile).
/// Deviation: the window uses the platform title bar; the WPF picker (GpDialogs.PickTemplate) draws the system one.
/// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.EveryMenuTableCommandIsHandledOrDisabled.
/// </summary>
public sealed class TemplatePickerWindow : Window
{
    private const double PickerWidth = 340;
    private const double ContentMarginHorizontal = 20;
    private const double ContentMarginVertical = 16;
    private const double ContentSpacing = 10;
    private const double ButtonSpacing = 8;
    private const string PickerTitle = "New from template";
    private const string PickerLabel = "Choose a starting point";
    private const string CreateText = "Create";
    private const string CancelText = "Cancel";

    private readonly ComboBox _choices;

    /// <param name="startingPoints">The names to offer, in order; the first one is selected.</param>
    public TemplatePickerWindow(IReadOnlyList<string> startingPoints)
    {
        Title = PickerTitle;
        Width = PickerWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        _choices = new ComboBox { Name = "TemplateChoices", ItemsSource = startingPoints, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var create = new Button { Name = "CreateButton", Content = CreateText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsDefault = true };
        create.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        create.Click += (_, _) => Close(_choices.SelectedItem as string);
        var cancel = new Button { Name = "CancelButton", Content = CancelText, MinWidth = DialogMetrics.PromptButtonMinWidth, IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(ContentMarginHorizontal, ContentMarginVertical), Spacing = ContentSpacing,
            Children =
            {
                new TextBlock { Name = "TemplateLabel", Text = PickerLabel, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                _choices,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = ButtonSpacing, Children = { create, cancel } },
            },
        };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { Close(null); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Opened += (_, _) => _choices.Focus();
    }
}
