using TabForge.Services.Features;
using TabForge.Views.Fx;

namespace TabForge.Views;

// Owns: what each FX chain command does (the FxChain.* ids): one route for a key press and a menu click alike.
// Does not own: the key map and the menu gesture text (Fx/FxChainKeys), the containers' work (Fx/FxChainContainers.Run).
// Tests: TestFxChainCommandScope, TestFxChainWindow.
public sealed partial class FxChainWindow
{
    /// <summary>Runs a command of this window by its id. True when it was used (a rename with no container focused is not).</summary>
    private bool RunFxCommand(string id)
    {
        switch (id)
        {
            case FxChainIds.AddPlugin: AddPlugin(); return true;
            case FxChainIds.RemovePlugin: RemoveSelected(); return true;
            case FxChainIds.Save: SaveChain(); return true;
            case FxChainIds.Load: LoadChain(replace: true); return true;
            case FxChainIds.AddToEnd: LoadChain(replace: false); return true;
            case FxChainIds.SaveForHosts: SaveRfxChain(); return true;
            case FxChainIds.Clear: Edit(() => { CloseDocked(); _track.Rig.Plugins.Clear(); }, refresh: true); return true;
            default: return _containers.Run(id);
        }
    }

    private async void SaveRfxChain() => await RfxChainExportFlow.SaveAsync(this, _track, _host.Engine);
}
