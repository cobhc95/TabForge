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
        if (Selected is { } slot) OpenActiveOnFor(new object[] { slot }, _activeOnButton.Button, PlacementMode.Bottom);
    }

    /// <summary>The container header's button: the popup for that container, under the button.</summary>
    internal void OpenActiveOnForContainer(ChainContainer container, Control anchor) =>
        OpenActiveOnFor(new object[] { container }, anchor, PlacementMode.Bottom);

    private void OpenActiveOnFor(IReadOnlyList<object> targets, Control anchor, PlacementMode placement)
    {
        _activeOnPopup?.Close();
        var subject = targets[0] switch { PluginSlot p => p.Name, ChainContainer c => c.Name, _ => null };
        _activeOnPopup = new FxActiveOnPopup(anchor, RuleOf(targets[0]), subject,
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
        _activeOnButton.Update(slot?.ActiveOn, slot is not null);
    }

    private void ApplyActiveOn(IReadOnlyList<object> targets, ActiveOnRule? rule) =>
        EditTree(_ =>
        {
            foreach (var target in targets)
                if (target is PluginSlot plugin) plugin.ActiveOn = ActiveOnRule.Normalize(rule);
                else if (target is ChainContainer container) container.ActiveOn = ActiveOnRule.Normalize(rule);
        }, refresh: true);

    private static ActiveOnRule? RuleOf(object target) => target switch
    {
        PluginSlot plugin => plugin.ActiveOn,
        ChainContainer container => container.ActiveOn,
        _ => null,
    };

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
