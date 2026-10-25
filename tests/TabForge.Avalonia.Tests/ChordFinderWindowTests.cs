using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Chord finder window checks (structure, presenter output, dark and light renders, text colours).
/// Does not own: the window, the presenter or the palette; the expected values come from ChordFinderPresenter and Palette.
/// Tests: this class.
/// </summary>
public class ChordFinderWindowTests
{
    // The WPF window's controls in tree order, written from the WPF app's ChordFinderWindow.cs:
    // role, control name (the WPF variable name, PascalCased) and text. The "Root" and "Type" labels have no name.
    private static readonly (string Role, string Name, string Text)[] WpfControls =
    {
        ("TextBlock", "", "Root"),
        ("ComboBox", "RootBox", ""),
        ("TextBlock", "", "Type"),
        ("ComboBox", "TypeBox", ""),
        ("TextBlock", "StatusText", ""),
        ("Button", "ShowButton", "Show"),
        ("Button", "InsertButton", "Insert name"),
        ("Button", "CloseButton", "Close"),
        ("ListBox", "NotesList", ""),
    };

    [AvaloniaFact]
    public void ControlsMatchWpfWindowRolesNamesAndTextsInOrder()
    {
        var window = new ChordFinderWindow(_ => false);
        window.Show();

        Assert.Equal("Chord finder", window.Title);
        Assert.Equal((380d, 330d, 320d, 260d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        var found = window.GetLogicalDescendants()
            .Where(c => c is TextBlock or ComboBox or Button or ListBox)
            .Where(c => !string.IsNullOrEmpty(((Control)c).Name) || c is TextBlock { Text: "Root" or "Type" })
            .Select(c => (Role: c.GetType().Name, Name: ((Control)c).Name ?? "", Text: TextOf(c)))
            .ToList();
        Assert.Equal(WpfControls.Select(c => (c.Role, c.Name, c.Text)), found);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("C", "Maj")]
    [InlineData("A", "min")]
    [InlineData("F#", "min7")]
    public void ShowAndInsertUsePresenterNoteLinesAndChordName(string root, string type)
    {
        string? insertedName = null;
        var window = new ChordFinderWindow(name => { insertedName = name; return true; });
        window.Show();
        window.FindControl<ComboBox>("RootBox")!.SelectedItem = root;
        window.FindControl<ComboBox>("TypeBox")!.SelectedItem = type;

        Click(window, "ShowButton");
        Assert.Equal(ChordFinderPresenter.NoteLines(root, type), ((IEnumerable<string>)window.FindControl<ListBox>("NotesList")!.ItemsSource!).ToList());

        Click(window, "InsertButton");
        Assert.Equal(ChordFinderPresenter.ChordName(root, type), insertedName);
        Assert.Equal(ChordFinderPresenter.InsertStatus(true, insertedName!), window.FindControl<TextBlock>("StatusText")!.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void InsertWithoutBeatUnderCursorReportsPresenterStatus()
    {
        var window = new ChordFinderWindow(_ => false);
        window.Show();
        Click(window, "InsertButton");
        Assert.Equal(ChordFinderPresenter.InsertStatus(false, "CMaj"), window.FindControl<TextBlock>("StatusText")!.Text);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    public void RendersPngInEachTheme(bool light, string themeName)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var window = new ChordFinderWindow(_ => false);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
        var path = DialogTestKit.RenderPath(nameof(ChordFinderWindowTests), $"chord-finder-{themeName}.png");
        frame.Save(path);
        Assert.True(new FileInfo(path).Length > 0);

        window.Close();
        app.RequestedThemeVariant = null;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextAndBackgroundColoursResolveToCorePaletteValues(bool light)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var window = new ChordFinderWindow(_ => false);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("WindowBackground", light)), Assert.IsType<SolidColorBrush>(window.Background).Color);
        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("TextPrimary", light)), Assert.IsType<SolidColorBrush>(window.Foreground).Color);
        Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("TextMuted", light)), Assert.IsType<SolidColorBrush>(window.FindControl<TextBlock>("StatusText")!.Foreground).Color);
        foreach (var buttonName in new[] { "ShowButton", "InsertButton", "CloseButton" })
            Assert.Equal(AvaloniaPalette.ColorOf(AvaloniaPalette.Get("TextPrimary", light)), Assert.IsType<SolidColorBrush>(window.FindControl<Button>(buttonName)!.Foreground).Color);

        window.Close();
        app.RequestedThemeVariant = null;
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensChordFinder()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Chord finder…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.OfType<ChordFinderWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        var background = AvaloniaPalette.Get("WindowBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), background) >= 4.5, $"{text} on WindowBackground (light: {light})");
    }

    private static void Click(Window window, string buttonName) =>
        window.FindControl<Button>(buttonName)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string TextOf(object control) => control switch
    {
        Button button => button.Content as string ?? "",
        TextBlock text => text.Text ?? "",
        _ => "",
    };
}
