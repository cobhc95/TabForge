using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Export preflight (Save or Export as .gp): shown before a clean Guitar Pro file is written, when the song uses something that file cannot hold.
/// Three choices: keep a full TabForge copy (the default button), write only the compatible file, or cancel (the cancel button). The chosen value goes to
/// the <paramref name="chosen"/> callback; closing the window any other way reports nothing.
/// Owns: the wiring of the texts, the list, the automation ids and the three buttons. Does not own: the wording and buttons
/// (GpExportPreflightPresenter), the loss list (GpPreflightReport), what is written (GpExportPreflight), the colours (palette resources in the XAML).
/// The preview has no song to analyse: the sample report is built by SampleReport.
/// Tests: TabForge.Avalonia.Tests.GpExportPreflightWindowTests.
/// </summary>
public partial class GpExportPreflightWindow : Window
{
    public const string KeepId = "GpPreflight.KeepCopy", CompatibleId = "GpPreflight.Compatible", CancelId = "GpPreflight.Cancel";

    /// <summary>For the XAML loader and designer only: the app passes the real report; the preview passes SampleReport.</summary>
    public GpExportPreflightWindow() : this(SampleReport(), GpExportKind.Export, "Demo song.gp", _ => { }) { }

    public GpExportPreflightWindow(GpPreflightReport report, GpExportKind kind, string fileName, Action<GpExportChoice> chosen)
    {
        InitializeComponent();
        Title = GpExportPreflightPresenter.Title(kind);
        HeadlineText.Text = GpExportPreflightPresenter.Headline(fileName);
        IntroText.Text = GpExportPreflightPresenter.Intro;
        LossList.Text = GpExportPreflightPresenter.ListText(report);
        AutomationProperties.SetName(LossList, GpExportPreflightPresenter.ListName);
        AdviceText.Text = GpExportPreflightPresenter.Advice(kind);
        foreach (var entry in GpExportPreflightPresenter.Buttons(kind))
            ButtonsPanel.Children.Add(MakeButton(entry, chosen));
    }

    /// <summary>A report with two sample losses for the preview, which has no song to analyse.</summary>
    public static GpPreflightReport SampleReport()
    {
        var report = new GpPreflightReport();
        report.Losses.Add(new GpLoss("Vibrato", "Kept as a text mark", 3, "bars 1 to 4"));
        report.Losses.Add(new GpLoss("Plug-in effects", "Left out", 1, "track 2"));
        return report;
    }

    private Button MakeButton(GpExportPreflightButton entry, Action<GpExportChoice> chosen)
    {
        var button = new Button
        {
            Content = entry.Label, IsDefault = entry.IsDefault, IsCancel = entry.IsCancel,
            Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), MinWidth = DialogMetrics.DialogButtonMinWidth,
        };
        button.Bind(TemplatedControl.ForegroundProperty, button.GetResourceObservable("TextPrimary"));
        AutomationProperties.SetAutomationId(button, IdOf(entry.Choice));
        AutomationProperties.SetName(button, entry.Label);
        if (entry.IsDefault)
        {
            button.BorderThickness = new Thickness(2);
            button.Bind(TemplatedControl.BorderBrushProperty, button.GetResourceObservable("AccentBrush"));
        }
        button.Click += (_, _) =>
        {
            chosen(entry.Choice);
            Close();
        };
        return button;
    }

    private static string IdOf(GpExportChoice choice) => choice switch
    {
        GpExportChoice.KeepNativeCopy => KeepId,
        GpExportChoice.ExportCompatible => CompatibleId,
        _ => CancelId,
    };
}
