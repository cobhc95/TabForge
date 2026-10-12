using TabForge.Services;

namespace TabForge.Services.Features;

// Owns: the ids of the FX chain window's commands (the FxChain.* ids the window's keys, menus and catalogue rows share).
// Does not own: the commands' work (FxChainWindow.RunFxCommand, FxChainContainers.Run) or their routing (Views/Fx/FxChainKeys.cs).
// Tests: TestFxChainCommandScope, TestEveryHotkeyIdHasHandler.
public static class FxChainIds
{
    public const string AddPlugin = "FxChain.AddPlugin", RemovePlugin = "FxChain.RemovePlugin", Save = "FxChain.Save", Load = "FxChain.Load",
        AddToEnd = "FxChain.AddToEnd", Clear = "FxChain.Clear", SaveForHosts = "FxChain.SaveForHosts", NewContainer = "FxChain.NewContainer",
        GroupSelected = "FxChain.GroupSelected", Dissolve = "FxChain.Dissolve", Rename = "FxChain.Rename", DeleteContainer = "FxChain.DeleteContainer";
}

// Owns: the FX chain window's catalogue rows (its own key scope, HotkeyCatalog.IsFxChainAction). The defaults are the keys the window
//     already had (Ctrl+S, Ctrl+O, Ctrl+G, Ctrl+Shift+G, F2); the other commands have none.
// Does not own: the ids (FxChainIds) or the window's routing (Views/Fx/FxChainKeys.cs).
// Tests: TestFxChainCommandScope, TestFeatureModuleContributions.
public sealed class FxChainFeatureModule : IFeatureModule
{
    private const string Cat = HotkeyCatalog.CategoryFxChain;

    public string Name => "FX chain window";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new(FxChainIds.AddPlugin, Cat, "Add plug-in", "", "Add a plug-in to the end of this FX chain."), "View.Fullscreen"),
        new(new(FxChainIds.RemovePlugin, Cat, "Remove plug-in", "", "Remove the selected plug-ins from this FX chain."), "View.Fullscreen"),
        new(new(FxChainIds.Save, Cat, "Save FX chain", "Ctrl+S", "Save this FX chain as a chain file in the chains folder."), "View.Fullscreen"),
        new(new(FxChainIds.Load, Cat, "Load FX chain", "Ctrl+O", "Load a saved FX chain, replacing this chain."), "View.Fullscreen"),
        new(new(FxChainIds.AddToEnd, Cat, "Add FX chain to the end", "", "Load a saved FX chain and add its plug-ins after the ones in this chain."), "View.Fullscreen"),
        new(new(FxChainIds.Clear, Cat, "Clear FX chain", "", "Remove every plug-in and container from this FX chain."), "View.Fullscreen"),
        new(new(FxChainIds.SaveForHosts, Cat, "Save FX chain for other hosts", "", "Write this chain as a .RfxChain file other hosts can load."), "View.Fullscreen"),
        new(new(FxChainIds.NewContainer, Cat, "New container", "", "Add an empty container behind the focused row in this FX chain."), "View.Fullscreen"),
        new(new(FxChainIds.GroupSelected, Cat, "Put selected in a new container", "Ctrl+G", "Move the selected rows into a new container."), "View.Fullscreen"),
        new(new(FxChainIds.Dissolve, Cat, "Dissolve container", "Ctrl+Shift+G", "Remove the focused container; its plug-ins stay in the chain."), "View.Fullscreen"),
        new(new(FxChainIds.Rename, Cat, "Rename container", "F2", "Type a new name for the focused container."), "View.Fullscreen"),
        new(new(FxChainIds.DeleteContainer, Cat, "Delete container with its plug-ins", "", "Remove the selected containers and every plug-in inside them."), "View.Fullscreen"),
    };
}
