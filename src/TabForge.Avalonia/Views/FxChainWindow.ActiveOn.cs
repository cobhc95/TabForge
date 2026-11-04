using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the "Active on..." entry of the FX chain right-click menu and its popup; the rule is written as one undo step and the chain
//   is matched again by the engine (the window never bypasses anything itself).
// Does not own: the rule and matcher (ActiveOnRules), the popup's controls (FxActiveOnPopup), the device list (FxActiveOnDevices).
// Tests: FxActiveOnTests.
public sealed partial class FxChainWindow
{
    private FxActiveOnPopup? _activeOnPopup;

    /// <summary>The open popup (tests read its lists).</summary>
    internal FxActiveOnPopup? ActiveOnPopup => _activeOnPopup;

    /// <summary>The output the rules are matched against now: the engine's when it knows one, else the saved settings.</summary>
    private ActiveOnOutput ActiveOnOutputNow
    {
        get
        {
            var output = _host.Engine?.ActiveOutput ?? default;
            return output.IsKnown ? output : new ActiveOnOutput(_host.PluginSettings.Driver, _host.PluginSettings.Device);
        }
    }

    /// <summary>The toolbar button (right after Wiring...): the selected plug-in's rule.</summary>
    private readonly FxActiveOnButton _activeOnButton = new("ActiveOnButton");

    /// <summary>The plug-in toolbar's Active on button (tests read it).</summary>
    internal Button ActiveOnButton => _activeOnButton.Button;

    /// <summary>Opens the popup for the selected plug-ins and containers (a right-click selects the row, as the menu does).</summary>
    private void OpenActiveOn()
    {
        var targets = _list.SelectedTags.Where(tag => tag is PluginSlot or ChainContainer).ToList();
        if (targets.Count == 0) return;
        OpenActiveOnFor(targets, _list, PlacementMode.Pointer);
    }

    /// <summary>The toolbar button: the popup for the selected plug-in, under the button.</summary>
    private void OpenActiveOnFromButton()
    {
        if (Selected is { } slot) OpenActiveOnFor(new object[] { LivePlugin(slot.Id) ?? slot }, _activeOnButton.Button, PlacementMode.Bottom);
    }

    /// <summary>The container header's button: the popup for that container, under the button.</summary>
    internal void OpenActiveOnForContainer(ChainContainer container, Control anchor) =>
        OpenActiveOnFor(new object[] { LiveContainer(container.Id) ?? container }, anchor, PlacementMode.Bottom);

    private void OpenActiveOnFor(IReadOnlyList<object> targets, Control anchor, PlacementMode placement)
    {
        _activeOnPopup?.Close();
        var live = ResolveLive(targets[0]);
        var subject = live switch { PluginSlot p => p.Name, ChainContainer c => c.Name, _ => null };
        _activeOnPopup = new FxActiveOnPopup(anchor, RuleOf(live), subject,
            kind => FxActiveOnDevices.Choices(kind, _host.PluginSettings, RememberedDeviceNames()),
            () => ActiveOnOutputNow,
            FxActiveOnDevices.Settled,
            rule => ApplyActiveOn(targets, rule), placement);
        _activeOnPopup.Open();
    }

    /// <summary>Shows the selected plug-in's rule on the toolbar button (called with the bar).</summary>
    private void UpdateActiveOnButton()
    {
        var slot = Selected;
        _activeOnButton.Update(slot?.ActiveOn, slot is not null, ActiveOnRules.Matches(slot?.ActiveOn, ActiveOnOutputNow));
    }

    private void ApplyActiveOn(IReadOnlyList<object> targets, ActiveOnRule? rule) =>
        EditTree(tree =>
        {
            var normalized = ActiveOnRule.Normalize(rule);
            foreach (var target in targets)
            {
                var id = target switch { PluginSlot p => p.Id, ChainContainer c => c.Id, _ => null };
                if (id is null) continue;
                if (target is PluginSlot && FindLivePlugin(tree, id) is { } plugin) plugin.ActiveOn = normalized;
                else if (target is ChainContainer && FindLiveContainer(tree, id) is { } container) container.ActiveOn = normalized;
            }
        }, refresh: true);

    private static ActiveOnRule? RuleOf(object target) => target switch
    {
        PluginSlot plugin => plugin.ActiveOn,
        ChainContainer container => container.ActiveOn,
        _ => null,
    };

    /// <summary>The live plug-in or container for a cached target: the rig may have replaced its objects (undo, settings reload) while the pane kept the old one.</summary>
    private object ResolveLive(object target) => target switch
    {
        PluginSlot p => LivePlugin(p.Id) ?? p,
        ChainContainer c => LiveContainer(c.Id) ?? c,
        _ => target,
    };

    private PluginSlot? LivePlugin(string id) => _track.Rig.Plugins.FirstOrDefault(p => p.Id == id);

    private ChainContainer? LiveContainer(string id) => _track.Rig.Containers?.FirstOrDefault(c => c.Id == id);

    private static PluginSlot? FindLivePlugin(List<ChainItem> tree, string id)
    {
        foreach (var item in tree)
        {
            if (item.Plugin is { } plugin && plugin.Id == id) return plugin;
            if (FindLivePlugin(item.Children, id) is { } found) return found;
        }
        return null;
    }

    private static ChainContainer? FindLiveContainer(List<ChainItem> tree, string id)
    {
        foreach (var item in tree)
        {
            if (item.Container is { } container && container.Id == id) return container;
            if (FindLiveContainer(item.Children, id) is { } found) return found;
        }
        return null;
    }

    // Every device a rule in this chain names stays in the popup (greyed when unplugged) instead of being forgotten.
    private IEnumerable<string> RememberedDeviceNames() =>
        _track.Rig.Plugins.Select(p => p.ActiveOn)
            .Concat((_track.Rig.Containers ?? new List<ChainContainer>()).Select(c => c.ActiveOn))
            .Where(ActiveOnRules.NamesDevice)
            .Select(rule => rule!.DeviceName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void OnOutputChanged()
    {
        if (!_closed) RefreshFromTrack();   // the signature carries the output, so the rows and hints follow a device switch
    }
}
