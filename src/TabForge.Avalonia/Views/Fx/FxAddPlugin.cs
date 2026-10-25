using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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

    /// <summary>Approves the picked plug-in, identifies it when needed and returns the slot (not yet on any chain).</summary>
    public async Task<PluginSlot> SlotFor(VstPluginInfo chosen)
    {
        var settings = _host.PluginSettings;
        _host.Trust.Approve(settings, chosen.Path, _host.PluginFiles);   // picked from the user's own scan / browse: trusted
        _host.SaveSettings();
        var role = chosen.Role;
        var vendor = chosen.Vendor;
        if (role.Length == 0)
        {
            // Find out what the plug-in is by asking it (in a throwaway process, crash-safe).
            _status($"Checking {chosen.Name}…");
            var described = await Task.Run(() => PluginCatalog.Describe(chosen.Path, settings, _host.Trust, _host.PluginFiles));
            _host.SaveSettings();
            role = described.Role;
            if (vendor.Length == 0) vendor = described.Vendor;
            _status("");
        }
        return new PluginSlot
        {
            Name = chosen.Name, Path = chosen.Path, Format = chosen.Format, Vendor = vendor, Enabled = true,
            Type = role == "Instrument" ? PluginSlotType.Instrument : PluginSlotType.Effect, RoleMode = PluginRoles.Auto,
        };
    }
}
