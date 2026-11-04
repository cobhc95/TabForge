using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.AudioEngine.Isolation;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the path from the Add button to a new slot: the plug-in browser on the real scan, the approval of what the user picked (trusted, and written to the
//   settings now so it holds after a restart), the role and vendor from the throwaway identify process when the scan did not tell, and the slot made from it.
// Does not own: putting the slot on the chain (FxChainWindow), the browser (PluginBrowserWindow, PluginBrowserSession), trust and identify rules (PluginTrust, PluginCatalog).
// Tests: TabForge.Avalonia.Tests.FxAddPluginTests.
public sealed class FxAddPlugin
{
    private const string UnknownRole = "";
    private readonly IFxChainHost _host;
    private readonly FxChainDialogs _dialogs;
    private readonly Action<string> _status;

    public FxAddPlugin(IFxChainHost host, FxChainDialogs dialogs, Action<string> status)
    {
        (_host, _dialogs, _status) = (host, dialogs, status);
    }

    /// <summary>Asks which plug-in to add and makes its slot; null when the user cancels.</summary>
    public async Task<PluginSlot?> ChooseAsync(Window owner)
    {
        var chosen = await _dialogs.ChoosePlugin(owner, _host);
        return chosen is null ? null : await SlotFor(chosen);
    }

    /// <summary>Approves the picked plug-in, identifies it when needed and returns the slot (not yet on any chain). Bridged plug-ins on Linux prepare Wine once off the UI thread with a preparing status and use the bridged probe ceiling; a refusal stays on the status line.</summary>
    public async Task<PluginSlot> SlotFor(VstPluginInfo chosen)
    {
        var settings = _host.PluginSettings;
        if (!TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(chosen.Path))   // TabForge's own effect: no file to approve
        {
            _host.Trust.Approve(settings, chosen.Path, _host.PluginFiles);   // picked from the user's own scan / browse: trusted
            _host.SaveSettings();
        }
        var role = chosen.Role;
        var vendor = chosen.Vendor;
        if (role.Length == 0)
        {
            // Find out what the plug-in is by asking it (in a throwaway process, crash-safe, never on the UI thread).
            string? wineFailure = null;
            if (PluginCatalog.NeedsBridgedProbe(chosen.Path, chosen.Format) && OperatingSystem.IsLinux())
            {
                _status(PluginBrowserListPresenter.WinePreparingText);
                try { await Task.Run(() => { WineRuntime.SweepStaleBlocks(); WineRuntime.Locate()?.EnsureReady(); }); }
                catch (Exception ex) { wineFailure = $"{ProbeRefusal.BrokenPrefixText} ({ex.GetBaseException().Message})"; }
            }
            if (wineFailure is null) _status($"Checking {chosen.Name}…");
            var detailed = wineFailure is null
                ? await Task.Run(() => PluginCatalog.DescribeDetailed(chosen.Path, settings, _host.Trust, _host.PluginFiles, PluginCatalog.DefaultProbeTimeoutMs, chosen.Format))
                : (Role: "", Vendor: "", Name: "", Refusal: wineFailure);
            _host.SaveSettings();
            role = detailed.Role;
            if (vendor.Length == 0) vendor = detailed.Vendor;
            _status(detailed.Refusal ?? "");
        }
        return new PluginSlot
        {
            Name = chosen.Name, Path = chosen.Path, Format = chosen.Format, Vendor = vendor, Enabled = true,
            Type = role == "Instrument" ? PluginSlotType.Instrument : PluginSlotType.Effect, RoleMode = PluginRoles.Auto,
        };
    }
}
