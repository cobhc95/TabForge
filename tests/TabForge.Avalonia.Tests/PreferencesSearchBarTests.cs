using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Settings search bar (a field-height box and category filter), the category rule, the Apply button enable rule and the empty FX chain bar.
/// Tests: this class.
/// </summary>
public class PreferencesSearchBarTests
{
    [AvaloniaFact]
    public void BarHasTheWpfSizesAndFiltersByCategory()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.Show();
        var bar = window.GetVisualDescendants().OfType<PreferencesSearchBar>().Single();
        Assert.Equal(AvaloniaFormControlStyles.PrefsFieldHeight, bar.Box.Height);
        Assert.Equal(PreferencesSearchRules.AllCategoriesText, bar.Category.Items.Cast<string>().First());
        Assert.True(PreferencesSearchRules.PassesCategory(null, "Audio"));
        Assert.False(PreferencesSearchRules.PassesCategory("Score", "Audio"));
    }

    [AvaloniaFact]
    public void TypingWaitsForThePauseEnterAppliesAtOnceAndSameQueryRaisesNothing()
    {
        var bar = new PreferencesSearchBar();
        var raised = 0;
        bar.Changed += () => raised++;
        bar.Box.Text = "theme";
        Assert.Equal(0, raised);
        bar.Box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter, Source = bar.Box });
        Assert.Equal(1, raised);
        bar.Box.Text = " Theme ";
        bar.Box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter, Source = bar.Box });
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void ApplyIsDisabledUntilAChangeAndTheEmptyFxBarIsDisabled()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.Show();
        var apply = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Apply");
        Assert.False(apply.IsEnabled);
        var fx = FxTestKit.Window(FxTestKit.Track(null, "Track"));
        fx.Show();
        Assert.False(fx.GetVisualDescendants().OfType<Button>().First(b => b.Name == "AddPresetButton").IsEffectivelyEnabled);
    }
}
