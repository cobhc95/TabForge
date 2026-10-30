using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the one place every FX preset load asks the Replace / Add question, honours the stored setting and stores a remembered answer.
// Does not own: the question window (FxPresetLoadPromptWindow), the words (FxPresetLoadPresenter) or the merge (FxChainMerge).
// Tests: FxPresetLoadTests.
/// <summary>Asks the FX preset load question, unless the chain is empty or Settings already says what to do.</summary>
public static class FxPresetLoadQuiz
{
    /// <summary>Null means Cancel: nothing changes. An empty chain returns Replace without asking.</summary>
    public static async Task<FxPresetLoadAnswer?> AskAsync(Window owner, FxChainDialogs dialogs, PluginSettings settings, Action save,
        string name, int count, bool twoInstruments, FxPresetLoadMode preferred = FxPresetLoadMode.Replace)
    {
        if (FxPresetLoadPresenter.WithoutAsking(count, settings.FxPresetLoad) is { } known)
            return new FxPresetLoadAnswer(known, Remember: false);
        var answer = await dialogs.AskLoadPreset(owner, name, count, twoInstruments, preferred);
        if (answer is { Remember: true } remembered)
        {
            settings.FxPresetLoad = FxPresetLoadPresenter.Store(remembered.Mode);
            save();
        }
        return answer;
    }
}
