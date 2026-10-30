using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

/// <summary>
/// The "Load an FX preset" question: the title, how many plug-ins the chain already has, Replace (default) and Add cards with their
/// explanations, "Remember my choice", and Cancel / Load. Enter loads, Esc cancels; the close box cancels. Null is Cancel.
/// Owns: the window's controls and reading them back. Does not own: the words and answer mapping (FxPresetLoadPresenter), the merge
/// (FxChainMerge) or showing it from a load path (FxPresetLoadQuiz).
/// Tests: FxPresetLoadTests.
/// </summary>
public sealed class FxPresetLoadPromptWindow : Window
{
    private readonly RadioCard _replace;
    private readonly RadioCard _add;
    private readonly CheckBox _remember;

    /// <summary>The answer the window closed with; null on Cancel / Esc / close.</summary>
    public FxPresetLoadAnswer? Answer { get; private set; }

    public FxPresetLoadPromptWindow(string name, int count, bool twoInstruments, FxPresetLoadMode preferred = FxPresetLoadMode.Replace)
    {
        PromptChrome.Setup(this, FxPresetLoadPresenter.Title(name), DialogMetrics.SectionAudioWidth);

        var body = PromptChrome.Column(FxPresetLoadPresenter.Title(name), PromptChrome.Muted("PresetLoadQuestion", FxPresetLoadPresenter.Question(count)));
        var group = new RadioCardGroup("FxPresetLoadChoice");
        _replace = new RadioCard(group, "Replace", FxPresetLoadPresenter.ReplaceTitle, FxPresetLoadPresenter.ReplaceHint(count), selected: preferred == FxPresetLoadMode.Replace);
        _add = new RadioCard(group, "Add", FxPresetLoadPresenter.AddTitle, FxPresetLoadPresenter.AddHint(count), selected: preferred == FxPresetLoadMode.Add);
        body.Children.Add(_replace);
        body.Children.Add(_add);
        if (twoInstruments) body.Children.Add(PromptChrome.Muted("TwoInstrumentsLine", FxPresetLoadPresenter.TwoInstrumentsLine));

        _remember = new CheckBox { Name = "RememberBox", Content = FxPresetLoadPresenter.RememberText };
        AutomationProperties.SetName(_remember, FxPresetLoadPresenter.RememberText);

        var cancel = PromptChrome.Action("CancelButton", FxPresetLoadPresenter.CancelText, primary: false, isCancel: true);
        AutomationProperties.SetName(cancel, FxPresetLoadPresenter.CancelText);
        cancel.Click += (_, _) => Close();
        var load = PromptChrome.Action("LoadButton", FxPresetLoadPresenter.LoadText, primary: true);
        AutomationProperties.SetName(load, FxPresetLoadPresenter.LoadText);
        load.Click += (_, _) => Load();
        body.Children.Add(PromptChrome.Footer(_remember, null, cancel, load));
        Content = body;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Load();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => (preferred == FxPresetLoadMode.Add ? _add : _replace).Focus();
    }

    /// <summary>Shows the question over <paramref name="owner"/>; null when the user cancelled.</summary>
    public static async Task<FxPresetLoadAnswer?> AskAsync(Window? owner, string name, int count, bool twoInstruments,
        FxPresetLoadMode preferred = FxPresetLoadMode.Replace) =>
        await DialogLauncher.AskFrom<FxPresetLoadPromptWindow, FxPresetLoadAnswer?>(owner,
            () => new FxPresetLoadPromptWindow(name, count, twoInstruments, preferred), window => window.Answer);

    private void Load()
    {
        Answer = new FxPresetLoadAnswer(_add.IsSelected ? FxPresetLoadMode.Add : FxPresetLoadMode.Replace, _remember.IsChecked == true);
        Close();
    }
}
