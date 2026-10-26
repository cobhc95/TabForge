using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Preferences header bar: the search box and the category filter in their own strip, sized like the window's.
/// Owns: the two controls, their sizes and the Changed event. Does not own: what the search shows (PreferencesWindow.Rebuild).
/// Tests: TabForge.Avalonia.Tests.PreferencesSearchBarTests.
/// </summary>
public sealed class PreferencesSearchBar : Border
{
    public TextBox Box { get; } = new()
    {
        Name = "SearchBox", Classes = { AvaloniaFormControlStyles.PrefsFieldClass }, Padding = new Thickness(6, 0), VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        Watermark = "Search settings, e.g. theme, audio device, metronome",
    };

    public ComboBox Category { get; } = new()
    {
        Name = "CategoryFilter", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 0, 0),
        Classes = { AvaloniaFormControlStyles.PrefsFieldClass }, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
    };

    // Typing waits this long after the last keystroke before Changed is raised, so the results rebuild once per pause.
    private const int SearchDebounceMilliseconds = 150;

    private readonly IUiTimer _debounce = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication().CreateTimer(TimeSpan.FromMilliseconds(SearchDebounceMilliseconds));

    // The normalised query and category last raised through Changed; a repeat of them raises nothing.
    private (string Query, string? Category)? _raised;

    /// <summary>Raised when the normalised query or the chosen category changes: at once for Enter, clearing and category changes, 150 ms after typing pauses.</summary>
    public event Action? Changed;

    public PreferencesSearchBar()
    {
        Category.ItemsSource = PreferencesSearchRules.Categories();
        Category.SelectedIndex = 0;
        _debounce.Tick += (_, _) => RaiseChanged();
        Box.TextChanged += (_, _) => OnTextTyped();
        Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) RaiseChanged(); };
        // The search field takes the Panel2Brush field fill of the settings search box (the TextBox style), not the window shade.
        Box[!TextBox.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        Category.SelectionChanged += (_, _) => RaiseChanged();
        ToolTip.SetTip(Box, "Search every category, setting description, keyword and keyboard shortcut.");
        var grid = new Grid { MaxWidth = PreferencesSearchRules.SearchMaxWidth, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(PreferencesSearchRules.CategoryColumnWidth + 8, GridUnitType.Pixel));
        Grid.SetColumn(Category, 1);
        grid.Children.Add(Box);
        grid.Children.Add(Category);
        Padding = new Thickness(PreferencesSearchRules.HeaderLeft, PreferencesSearchRules.HeaderTop, PreferencesSearchRules.HeaderRight, PreferencesSearchRules.HeaderBottom);
        BorderThickness = new Thickness(0, 0, 0, 1);
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        Child = grid;
    }

    // An empty query applies at once (clearing the box); anything else waits for the typing pause.
    private void OnTextTyped()
    {
        if (NormalisedQuery().Length == 0) { RaiseChanged(); return; }
        _debounce.Stop();
        _debounce.Start();
    }

    private void RaiseChanged()
    {
        _debounce.Stop();
        var key = (NormalisedQuery(), SelectedCategory);
        if (_raised == key) return;
        _raised = key;
        Changed?.Invoke();
    }

    // The search text lower-cased with its words separated by single spaces, as the search matches them.
    private string NormalisedQuery() => string.Join(' ', (Box.Text ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The chosen category, or null for all.</summary>
    public string? SelectedCategory => Category.SelectedIndex > 0 ? Category.SelectedItem as string : null;

    /// <summary>Clears the text and shows all categories.</summary>
    public void Reset() { Box.Text = ""; Category.SelectedIndex = 0; }
}
