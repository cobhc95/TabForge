using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a dropdown keeps one width while it scrolls (the virtualised list must not widen it) and the Preferences category filter lists only the pages.
/// Tests: this class.
/// </summary>
public class ComboPopupWidthTests
{
    private static double OpenScrollAndMeasure(ComboBox combo, Window window, object last, Control? root = null)
    {
        window.Content = root ?? combo;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var popup = AvaloniaFormControlStyles.ComboPopupFor(combo)!;
        var before = popup.Width;
        combo.SelectedItem = last;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, popup.Width);
        return before;
    }

    [AvaloniaFact]
    public void ALongItemFarDownTheListDoesNotWidenThePopupWhileScrolling()
    {
        var items = Enumerable.Range(0, 80).Select(i => i == 79 ? new string('W', 70) : $"Item {i}").ToArray();
        var combo = new ComboBox { Width = 160, ItemsSource = items };
        var window = new Window { Width = 600, Height = 400 };
        try
        {
            var width = OpenScrollAndMeasure(combo, window, items[^1]);
            Assert.True(width >= 160, $"popup width {width}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnItemsSourcePopupIsWiderThanTheBoxForLongLabels()
    {
        var combo = new ComboBox { Width = 80, ItemsSource = new System.Collections.Generic.List<string> { "Short", new string('W', 30) } };
        var window = new Window { Width = 600, Height = 400, Content = combo };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(AvaloniaFormControlStyles.ComboPopupFor(combo)!.Width > 80);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongLabelAfterTheTwoHundredthItemStillWidensThePopupAndAReplacedItemIsSeen()
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<string>(Enumerable.Range(0, 300).Select(i => $"Item {i}"));
        var combo = new ComboBox { Width = 80, ItemsSource = items };
        var window = new Window { Width = 600, Height = 400, Content = combo };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double Open() { combo.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs(); combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs(); return AvaloniaFormControlStyles.ComboPopupFor(combo)!.Width; }
            var narrow = Open();
            items[299] = new string('W', 30);   // same count: a replace
            Assert.True(Open() > narrow);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePreferencesCategoryFilterListsOnlyThePagesAndKeepsItsWidthWhenScrolled()
    {
        var bar = new PreferencesSearchBar();
        Assert.Equal(new[] { PreferencesSearchRules.AllCategoriesText }.Concat(TabForge.Services.SettingsCatalog.Categories), bar.Category.Items.Cast<string>());
        var window = new Window { Width = 600, Height = 400 };
        try { OpenScrollAndMeasure(bar.Category, window, TabForge.Services.SettingsCatalog.Categories[^1], bar); }
        finally { window.Close(); }
    }
}
