using System.Windows;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>The questions a save may ask, answered with dialogs owned by one window.</summary>
internal sealed class SaveDialogs : ISaveInteractions
{
    private readonly Window _owner;
    public SaveDialogs(Window owner) => _owner = owner;

    public AudioDataSaveChoice? AskAudioDataChoice(string fileName) => PluginSaveDialog.Ask(_owner, fileName) switch
    {
        PluginSaveDialog.TForge => AudioDataSaveChoice.TForgeFile,
        PluginSaveDialog.GpPlusDataFile => AudioDataSaveChoice.GpPlusDataFile,
        null => null,
        _ => AudioDataSaveChoice.GpWithEmbeddedProject,
    };

    public GpExportChoice AskGpPreflight(GpPreflightReport report, GpExportKind kind, string fileName) =>
        GpExportPreflightDialog.Ask(_owner, report, kind, fileName);

    public ReplaceFileChoice AskReplaceFullCopy(string fileName)
    {
        var dialog = new ThemedConfirmDialog("TabForge", string.Format(SaveFlowText.ReplaceFullCopy, fileName),
            yesToolTip: "Replace the existing file", noToolTip: "Keep the existing file and save this one under a numbered name",
            yesText: SaveFlowText.ReplaceButton, noText: SaveFlowText.KeepBothButton) { Owner = _owner };
        if (DialogHost.ShowModal(dialog) != true) return ReplaceFileChoice.Cancel;
        return dialog.Result switch { MessageBoxResult.Yes => ReplaceFileChoice.Replace, MessageBoxResult.No => ReplaceFileChoice.KeepBoth, _ => ReplaceFileChoice.Cancel };
    }
}
