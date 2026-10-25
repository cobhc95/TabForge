using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Transport menu's Metronome and Count-in rows and the transport bar buttons that press them: flips the saved audio choice,
//   pushes both choices to the playback sessions, sets the menu checks and shows the status line; and the right-click settings popups of the
//   metronome, count-in and loop buttons (opened above the button by a right click; each popup's On/Off switch sets the same
//   choice as a left click on its button, which toggles it), whose changes reach the sessions at once and are saved.
// Does not own: the rows (MainMenuTable in Core), the commands table (MainWindowCommands), the playback options (PlaybackTransport and
//   PlaybackEngine), the settings file (MainWindowSettings), the bar's buttons (TransportBarView), the popups' rows (Views/TransportPopups),
//   the loop area (SelectionLoopLink).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every menu row is handled or disabled), TabForge.Avalonia.Tests.TransportSettingsPopupTests.
public static class MainWindowTransportToggles
{
    /// <summary>The command id of the Metronome row and the transport bar's metronome button.</summary>
    public const string MetronomeId = "Transport.Metronome";

    /// <summary>The command id of the Count-in row and the transport bar's count-in button.</summary>
    public const string CountInId = "Transport.CountIn";

    /// <summary>The command id the metronome button's right-click runs: the metronome settings popup.</summary>
    public const string MetronomeSettingsId = "Transport.MetronomeSettings";

    /// <summary>The command id the count-in button's right-click runs: the count-in settings popup.</summary>
    public const string CountInSettingsId = "Transport.CountInSettings";

    /// <summary>The command id the loop button's right-click runs: the loop settings popup.</summary>
    public const string LoopSettingsId = "Transport.LoopSettings";

    private const string MetronomeOnText = "Metronome on";
    private const string MetronomeOffText = "Metronome off";
    private const string CountInOnText = "Count-in on";
    private const string CountInOffText = "Count-in off";

    /// <summary>The transport button names each popup opens above (TransportBarView.Items).</summary>
    public const string MetronomeButtonName = "Metronome";
    public const string CountInButtonName = "Click and beat count-in";
    public const string LoopButtonName = "Loop enable";

    /// <summary>The Transport menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { MetronomeId, CountInId };

    /// <param name="commands">The commands table the toggles and popups run from.</param>
    /// <param name="playback">The playback sessions the choices are pushed to; null (no engine host) keeps the choices in the settings only.</param>
    /// <param name="owner">The window whose Transport menu rows show the checks and whose transport bar the popups open over.</param>
    /// <param name="settings">The shared settings the choices are read from and saved to; null keeps them for this window only.</param>
    /// <param name="notice">Shows the status line text in the status bar after a toggle.</param>
    /// <param name="loop">The window's loop area (the loop popup's area line and Clear loop area); null leaves the loop popup without an area.</param>
    public static void Register(MainWindowCommands commands, AvaloniaPlaybackSessions? playback, Window owner, MainWindowSettings? settings, Action<string> notice,
        SelectionLoopLink? loop = null)
    {
        // Without a settings file the choices live in this window's own audio settings, so the toggles still turn on and off.
        var audio = settings?.Settings.Audio ?? new AudioSettings();
        // The checks the menu shows now (null: never painted yet); a slider drag never flips a toggle, so repaints skip the tree walk.
        (bool Metronome, bool CountIn)? paintedChecks = null;
        void RefreshChecks()
        {
            if (paintedChecks == (audio.Metronome, audio.CountIn)) return;
            paintedChecks = (audio.Metronome, audio.CountIn);
            SetChecks(owner, audio);
        }
        // A change made in a popup reaches the sessions, the Transport menu's checks and the saved settings, as a toggle does.
        void Changed()
        {
            playback?.SetClickOptions(audio);
            playback?.SetToggles(audio.Metronome, audio.CountIn);
            RefreshChecks();
            settings?.SaveSettingsQuietly();
        }
        commands.Add(MetronomeId, () => Toggle(audio, metronome: true, playback, owner, settings, notice));
        commands.Add(CountInId, () => Toggle(audio, metronome: false, playback, owner, settings, notice));
        commands.Add(MetronomeSettingsId, () => Open(ClickSettingsPopups.Metronome(audio, Changed), owner, MetronomeButtonName));
        commands.Add(CountInSettingsId, () => Open(ClickSettingsPopups.CountIn(audio, Changed), owner, CountInButtonName));
        commands.Add(LoopSettingsId, () => Open(LoopSettingsPopup.Create(audio, loop?.AreaText ?? TabForge.Playback.LoopSettingsRules.AreaText(false, false, 0, 0),
            () => loop?.LoopOn == true, _ => loop?.ToggleLoop(), Changed, () => loop?.ClearLoopArea()), owner, LoopButtonName));
        owner.Opened += (_, _) => RefreshChecks();
        playback?.SetToggles(audio.Metronome, audio.CountIn);
        playback?.SetClickOptions(audio);
    }

    /// <summary>Opens a settings popup above the named transport button, or over the window's top-left when the bar is not shown.</summary>
    private static void Open(TransportPopupFrame popup, Window owner, string buttonName)
    {
        var anchor = owner.GetVisualDescendants().OfType<TransportBarView.TransportButton>().FirstOrDefault(button => button.Item.Name == buttonName && button.IsEffectivelyVisible);
        popup.OpenAbove(anchor ?? (Control)owner);
    }

    /// <summary>Transport > Metronome or Count-in: flips the choice, saves it, pushes both choices to the sessions and shows the status line.</summary>
    private static void Toggle(AudioSettings audio, bool metronome, AvaloniaPlaybackSessions? playback, Window owner, MainWindowSettings? settings, Action<string> notice)
    {
        bool on;
        if (metronome) on = audio.Metronome = !audio.Metronome;
        else on = audio.CountIn = !audio.CountIn;
        SetChecks(owner, audio);
        settings?.SaveSettingsDeferred(notifyObservers: false);
        playback?.SetToggles(audio.Metronome, audio.CountIn);
        notice(metronome ? (on ? MetronomeOnText : MetronomeOffText) : (on ? CountInOnText : CountInOffText));
    }

    private static void SetChecks(Window owner, AudioSettings audio)
    {
        SetCheck(owner, MetronomeId, audio.Metronome);
        SetCheck(owner, CountInId, audio.CountIn);
    }

    /// <summary>Sets the check of every Transport menu row whose tag is <paramref name="id"/>.</summary>
    private static void SetCheck(Window owner, string id, bool on)
    {
        foreach (var item in owner.GetLogicalDescendants().OfType<MenuItem>().Where(item => Equals(item.Tag, id)))
            item.IsChecked = on;
    }
}
