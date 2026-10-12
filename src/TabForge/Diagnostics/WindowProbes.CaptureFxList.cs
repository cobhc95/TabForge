using TabForge.Plugins;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the capture step "fx-list" on the open FX chain window (FxChain-example): "five" fills the chain to five example plug-ins,
//   "select3" selects three of them, "drag" lifts the selected rows as a drag in progress, "containers" builds an expanded and a
//   collapsed container, "container" focuses the first container (its pane shows), "drop-into" drags a plug-in onto a container,
//   "drop-before" to a container's top edge, "drop-between" between two plug-ins of a container, "drop-end" to the end inside it.
// Does not own: step dispatch (WindowProbes.CaptureExtras.cs), the list (Views/Fx/FxChainList.cs) or the shots.
// Tests: none (diagnostics only; run through --capture).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private void FxList(string what)
        {
            var fx = _tools.GetValueOrDefault(_toolName ?? "") as FxChainWindow ?? throw new InvalidOperationException("open the FxChain-example window first");
            var list = fx.ChainList;
            // Diagnostics only: the window's list refresh is private and stays so.
            void Reload() => typeof(FxChainWindow).GetMethod("RefreshList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(fx, new object[] { 0 });
            switch (what)
            {
                case "five":
                    foreach (var name in new[] { "Example equaliser", "Example compressor", "Example reverb" })
                    {
                        if (fx.Track.Rig.Plugins.Count >= 5) break;
                        var slot = new PluginSlot { Name = name, Path = fx.Track.Rig.Plugins[^1].Path, Format = "VST2", Vendor = "Example maker" };
                        fx.Track.Rig.Plugins.Add(slot);
                        _cleanup.Add(() => fx.Track.Rig.Plugins.Remove(slot));
                    }
                    Reload();
                    break;
                case "select3": list.Click(1); list.Click(2, ctrl: true); list.Click(4, ctrl: true); break;
                case "drag": list.SimulateDragStart(list.SelectedRows[^1]); list.SimulateDragMove(list.RowCentre(list.Count - 1) + 40); break;
                case "containers":
                    if (fx.Track.Rig.Containers is null)
                    {
                        var first = ChainTree.Group(fx.Track.Rig, 1, 2, "Pre-amp"); var second = ChainTree.Group(fx.Track.Rig, 3, 2, "Cabinet and room");
                        if (first is not null) first.Routing = "CONTAINER_CFG 4 2 2 0\n";
                        if (second is not null) second.Collapsed = true;
                        _cleanup.Add(() => fx.Track.Rig.Containers = null);
                    }
                    Reload();
                    break;
                case "container": list.Click(1); break;
                case "drop-into": list.HandleEscape(); list.Click(2); list.SimulateDragStart(2); list.SimulateDragMove(list.RowCentre(list.Count - 1)); break;
                case "drop-before": list.HandleEscape(); list.Click(0); list.SimulateDragStart(0); list.SimulateDragMove(list.RowTop(1) + 2); break;
                case "drop-between": list.HandleEscape(); list.Click(0); list.SimulateDragStart(0); list.SimulateDragMove(list.RowTop(3) + 1, 200); break;
                case "drop-end": list.HandleEscape(); list.Click(0); list.SimulateDragStart(0); list.SimulateDragMove(list.RowBottom(3) - 2, 200); break;
                default: throw new InvalidOperationException($"unknown fx-list '{what}' (five, select3, drag, containers, container, drop-into, drop-before, drop-between, drop-end)");
            }
        }
    }
}
