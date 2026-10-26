using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the export preflight window checks (structure, presenter texts and buttons, the choice each button reports, dark and light PNGs,
/// contrast, the preview's menu item).
/// Does not own: the window, the presenter, the loss analysis (GpExportPreflight) or the palette. The sample losses come from SampleReport.
/// Tests: this class.
/// </summary>
public class GpExportPreflightWindowTests
{
    private const string FileName = "Demo song.gp";

    private static GpExportPreflightWindow Make(List<GpExportChoice> chosen, GpExportKind kind = GpExportKind.Export) =>
        new(GpExportPreflightWindow.SampleReport(), kind, FileName, choice => chosen.Add(choice));

    private static List<Button> Buttons(GpExportPreflightWindow window) =>
        window.FindControl<WrapPanel>("ButtonsPanel")!.Children.OfType<Button>().ToList();

    [AvaloniaFact]
    public void ControlsMatchWpfDialogRolesTextsAndIdsInOrder()
    {
        var window = Make(new List<GpExportChoice>());
        window.Show();

        // The dialog's controls in tree order, written from the app's GpExportPreflightDialog.cs: the headline, the intro,
        // the loss list (a read-only text box with an automation name), the advice, then the three buttons with their automation ids.
        var expected = new[]
        {
            ("TextBlock", GpExportPreflightPresenter.Headline(FileName), ""),
            ("TextBlock", GpExportPreflightPresenter.Intro, ""),
            ("TextBox", GpExportPreflightPresenter.ListText(GpExportPreflightWindow.SampleReport()), GpExportPreflightPresenter.ListName),
            ("TextBlock", GpExportPreflightPresenter.Advice(GpExportKind.Export), ""),
            ("Button", GpExportPreflightPresenter.KeepLabel, GpExportPreflightWindow.KeepId),
            ("Button", "Export compatible copy", GpExportPreflightWindow.CompatibleId),
            ("Button", GpExportPreflightPresenter.CancelLabel, GpExportPreflightWindow.CancelId),
        };
        var found = DialogTestKit.LayoutControls(window)
            .Where(c => c is Button or TextBlock or TextBox)
            .Select(c => c switch
            {
                Button b => ("Button", (string)b.Content!, AutomationProperties.GetAutomationId(b) ?? ""),
                TextBox t => ("TextBox", t.Text ?? "", AutomationProperties.GetName(t) ?? ""),
                TextBlock t => ("TextBlock", t.Text ?? "", ""),
                _ => ("", "", ""),
            })
            .ToList();
        Assert.Equal(expected, found);
        Assert.Equal(GpExportPreflightPresenter.Title(GpExportKind.Export), window.Title);
        Assert.Equal(3, Buttons(window).Count);
        Assert.True(Buttons(window)[0].IsDefault);
        Assert.True(Buttons(window)[2].IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public void ButtonsReportTheirChoiceAndCloseTheWindow()
    {
        var chosen = new List<GpExportChoice>();
        var window = Make(chosen);
        window.Show();
        DialogTestKit.Click(Buttons(window)[1]);
        Assert.Equal(new[] { GpExportChoice.ExportCompatible }, chosen);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void SaveKindUsesSaveWordingForTitleAndCompatibleButton()
    {
        var window = Make(new List<GpExportChoice>(), GpExportKind.Save);
        window.Show();
        Assert.Equal(GpExportPreflightPresenter.Title(GpExportKind.Save), window.Title);
        Assert.Equal("Save compatible .gp", (string)Buttons(window)[1].Content!);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "gp-export-preflight-dark.png")]
    [InlineData(true, "gp-export-preflight-light.png")]
    public void RendersPngInEachTheme(bool light, string fileName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Make(new List<GpExportChoice>());
        var path = DialogTestKit.SavePng(window, nameof(GpExportPreflightWindowTests), fileName);
        Assert.True(new System.IO.FileInfo(path).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnItsSurface(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Make(new List<GpExportChoice>());
        window.Show();
        var list = window.FindControl<TextBox>("LossList")!;
        Assert.True(DialogTestKit.Contrast(window.FindControl<TextBlock>("HeadlineText")!.Foreground, window.Background, "Headline") >= 4.5);
        Assert.True(DialogTestKit.Contrast(window.FindControl<TextBlock>("IntroText")!.Foreground, window.Background, "Intro") >= 4.5);
        Assert.True(DialogTestKit.Contrast(list.Foreground, list.Background, "Loss list") >= 4.5);
        Assert.True(DialogTestKit.Contrast(window.FindControl<TextBlock>("AdviceText")!.Foreground, window.Background, "Advice") >= 4.5);
        foreach (var button in Buttons(window))
            Assert.True(DialogTestKit.Contrast(button.Foreground, window.Background, "Button") >= 4.5);
        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensExportPreflight()
    {
        var main = new MainWindow();
        main.Show();
        // The File menu row runs the real export (MainWindowSaving.ExportGuitarPro); the preview entry still opens the sample preflight.
        PreviewToolsMenu.Entries(main).Single(p => p.Header == "Export .gp file…").Open();
        var opened = main.OwnedWindows.OfType<GpExportPreflightWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
