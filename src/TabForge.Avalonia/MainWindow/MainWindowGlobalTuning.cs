using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Tools > Global tuning handler on the shown song: it opens the Global tuning window on the song's current per-string tuning; Apply retunes
//   every pitched track by the difference (one undo step) and shows the status line; Cancel changes nothing.
// Does not own: the tuning window (GlobalTuningWindow), the retune rule and status text (GlobalRetune in TabForge.Core), the menu row (MainMenuTable),
//   the transport bar's tuning button, the commands table (MainWindowCommands).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every menu row is handled or disabled).
/// <summary>The Global tuning command, bound to the shown song.</summary>
public static class MainWindowGlobalTuning
{
    private const string GlobalTuningId = "Tools.GlobalTuning";

    /// <summary>The Tools menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { GlobalTuningId };

    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="owner">The window the Global tuning window opens over.</param>
    /// <param name="notice">Shows the status line after a retune.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner, Action<string> notice) =>
        commands.Add(GlobalTuningId, () => _ = ChooseAndRetuneAsync(songs.Active, owner, notice));

    /// <summary>Makes the transport bar's Global tuning button run the same window and retune as the menu command.</summary>
    public static void BindTransport(TransportBarView transport, MainWindowTabs songs, Window owner, Action<string> notice) =>
        transport.GlobalTuningRequested += () => _ = ChooseAndRetuneAsync(songs.Active, owner, notice);

    /// <summary>Opens the window on the song's current tuning and applies the change it returns.</summary>
    private static async Task ChooseAndRetuneAsync(IEditableDocument document, Window owner, Action<string> notice)
    {
        var current = GlobalTuningPresenter.StandardE.Select((pitch, s) => pitch + document.TuningShift[s]).ToArray();
        var chosen = await DialogLauncher.Ask<GlobalTuningWindow, int[]?>(owner, () => new GlobalTuningWindow(current, TuningPresets.SixStringPresets));
        if (chosen is null) return;
        var status = GlobalRetune.Apply(document, chosen.Select((pitch, s) => pitch - current[s]).ToArray());
        if (status is not null) notice(status);
    }
}
