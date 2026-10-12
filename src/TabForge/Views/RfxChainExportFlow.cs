using System.IO;
using System.Text;
using System.Windows;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

// Owns: the "save this chain as an FX chain file for other hosts" step: reading the plug-ins' current settings and their own
//       description from the engine, the file dialog, the atomic write and the one message with what was left out.
// Does not own: the file's layout (RfxChainExporter), the engine request (AudioEngineClient.GetPluginInfoAsync), the menu item
//       that calls it (the FX chain window).
// Tests: TestRfxChainExport (the exporter and the engine answer; the dialog itself is not driven by a test).
public static class RfxChainExportFlow
{
    private const string Caption = "Export FX chain";

    /// <summary>True when a file was written. Shows one message when something was left out or the file could not be written.</summary>
    public static async Task<bool> SaveAsync(Window owner, TrackModel track, AudioEngineClient engine)
    {
        await engine.CollectStatesAsync(new[] { track }, 2000);
        // One snapshot of the chain: the info is asked for, and attached to, exactly these plug-ins even if the chain changes meanwhile.
        var slots = track.Rig.Plugins.ToList();
        var info = await engine.GetPluginInfoAsync(track, slots);
        if (!owner.IsLoaded) return false;   // the window closed while the engine was asked
        var result = RfxChainExporter.Export(RfxChainExporter.FromTree(ChainTree.Tree(slots, track.Rig.Containers), slots, info), MixerGroups.GmSounds(track, engine.Mixer));
        var notes = string.Join("\n\n", result.Notes);
        if (result.Written == 0)
        {
            ThemedMessageBox.Show(owner, "No plug-in of this chain can be exported.\n\n" + notes, Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Caption, Filter = "FX chain for other hosts (*.RfxChain)|*.RfxChain", DefaultExt = ".RfxChain", AddExtension = true,
            FileName = TabForge.Audio.Contracts.SafeFileNames.SafeFileName(track.Name, "FX chain"),
        };
        if (dialog.ShowDialog(owner) != true) return false;
        try
        {
            var path = FilePathPolicy.OutputFile(dialog.FileName, "FX chain file", ".rfxchain");
            var bytes = new UTF8Encoding(false).GetBytes(result.Text);
            FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Services.Trace.Error(Services.Trace.Ui, "FX chain export: " + ex.Message);
            ThemedMessageBox.Show(owner, "The FX chain could not be exported: " + ex.Message, Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (notes.Length > 0) ThemedMessageBox.Show(owner, notes, Caption, MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }
}
