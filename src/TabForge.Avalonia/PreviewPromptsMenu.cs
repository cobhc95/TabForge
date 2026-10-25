using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>
/// The preview's Tools > Track prompts submenu: one item for each small track and confirm prompt. Each opens its prompt over the preview
/// window with sample wording; the Undo sentences name the catalogue's default Undo key (the preview keeps no bindings).
/// Owns: the submenu's items and their sample prompts. Does not own: the prompts (PromptWindow, PromptTextsPresenter) or the Tools menu (PreviewToolsMenu).
/// Tests: PromptWindowTests.
/// </summary>
public static class PreviewPromptsMenu
{
    private static readonly string SampleUndoKey = HotkeyCatalog.DisplayAll(new HotkeySettings(), "Edit.Undo");

    public static MenuItem Build(Window owner)
    {
        var menu = new MenuItem { Header = "Track prompts" };
        menu.Items.Add(Item("Add track…", owner, PromptTextsPresenter.AddTrack()));
        menu.Items.Add(Item("Delete track…", owner, PromptTextsPresenter.DeleteTrack("Guitar", SampleUndoKey)));
        menu.Items.Add(Item("Convert to audio track…", owner, PromptTextsPresenter.ConvertTrackToAudio("Guitar", SampleUndoKey)));
        menu.Items.Add(Item("Convert to instrument track, MIDI clips…", owner, PromptTextsPresenter.ConvertTrackToInstrumentClips("Guitar", hasMidi: true)));
        menu.Items.Add(Item("Convert to instrument track, audio clips…", owner, PromptTextsPresenter.ConvertTrackToInstrumentClips("Guitar", hasMidi: false)));
        menu.Items.Add(Item("Change instrument…", owner, PromptTextsPresenter.ChangeInstrument("Nylon Guitar", "Kick Drum")));
        menu.Items.Add(Item("Confirm…", owner, PromptTextsPresenter.Confirm("TabForge", "Smoke check prompt", "Yes", true, SampleUndoKey)));
        return menu;
    }

    private static MenuItem Item(string header, Window owner, PromptSpec spec)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await PromptWindow.AskAsync(owner, spec);
        return item;
    }
}
