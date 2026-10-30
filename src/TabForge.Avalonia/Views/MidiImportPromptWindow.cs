using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Import MIDI" question: the file name, its track count, two radio cards (tab and notation tracks / MIDI tracks, the one that suits the file picked first),
/// "Remember my choice", Cancel and Import. Enter imports, Esc cancels. <see cref="Answer"/> stays null on cancel.
/// Owns: the window's controls and reading them back. Does not own: the words (MidiImportPromptPresenter), the conversion or the stored setting.
/// Tests: TabForge.Avalonia.Tests.MidiImportChoiceTests.
/// </summary>
public sealed class MidiImportPromptWindow : Window
{
    private readonly RadioCard _tab;
    private readonly RadioCard _midi;
    private readonly CheckBox _remember;

    /// <summary>The answer the window closed with; null on Cancel / Esc / close.</summary>
    public MidiImportAnswer? Answer { get; private set; }

    public MidiImportPromptWindow(string path, MidiImportPreview preview)
    {
        PromptChrome.Setup(this, MidiImportPromptPresenter.Title, DialogMetrics.SectionAudioWidth);

        var body = PromptChrome.Column(MidiImportPromptPresenter.Title, PromptChrome.Muted("FileText", MidiImportPromptPresenter.FileLine(path)));
        body.Children.Add(PromptChrome.Muted("TracksText", MidiImportPromptPresenter.TracksLine(preview.TrackCount)));
        var group = new RadioCardGroup("MidiImportChoice");
        var tabFirst = MidiImportPromptPresenter.DefaultMode(preview) == MidiImportMode.Tab;
        _tab = new RadioCard(group, "Tab", MidiImportPromptPresenter.TabTitle, MidiImportPromptPresenter.TabHint, selected: tabFirst);
        _midi = new RadioCard(group, "Midi", MidiImportPromptPresenter.MidiTitle, MidiImportPromptPresenter.MidiHint, selected: !tabFirst);
        body.Children.Add(_tab);
        body.Children.Add(_midi);

        _remember = new CheckBox { Name = "RememberBox", Content = MidiImportPromptPresenter.RememberText };
        var hint = PromptChrome.Muted("RememberHint", MidiImportPromptPresenter.RememberHint);
        var cancel = PromptChrome.Action("CancelButton", MidiImportPromptPresenter.CancelText, primary: false, isCancel: true);
        cancel.Click += (_, _) => Close();
        var import = PromptChrome.Action("ImportButton", MidiImportPromptPresenter.ImportText, primary: true);
        import.Click += (_, _) => Import();
        body.Children.Add(PromptChrome.Footer(_remember, hint, cancel, import));
        Content = body;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Import();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => (tabFirst ? _tab : _midi).Focus();
    }

    /// <summary>Shows the question over <paramref name="owner"/>; null when the person cancelled the import.</summary>
    public static async Task<MidiImportAnswer?> AskAsync(Window? owner, string path, MidiImportPreview preview) =>
        await DialogLauncher.AskFrom<MidiImportPromptWindow, MidiImportAnswer?>(owner, () => new MidiImportPromptWindow(path, preview), window => window.Answer);

    private void Import()
    {
        Answer = new MidiImportAnswer(_tab.IsSelected ? MidiImportMode.Tab : MidiImportMode.Midi, _remember.IsChecked == true);
        Close();
    }
}
