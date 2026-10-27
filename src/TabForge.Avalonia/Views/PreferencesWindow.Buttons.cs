using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the buttons of the Settings rows (SettingKind.Button): each key's label, enabled state and action, and keeping the window's
//       copy and baseline in step after an action changed the live approvals, plug-in quarantine or inputs.
// Does not own: the actions themselves (PreferencesRowActions, the main window's commands), the Inputs shown dialog (InputsShownWindow),
//       the score text editor (ScoreTextStyleWindow) or the rows (PreferencesCatalog).
// Tests: TabForge.Avalonia.Tests.PreferencesSettingsTransferTests.
public sealed partial class PreferencesWindow
{
    private const string CheckForUpdatesCommand = "Help.CheckForUpdates";
    private const string GroupRulesCommand = "Mixer.GroupRules";

    /// <summary>The button of a button row: each key has its own action, and an unknown key fails loudly.</summary>
    private Button ButtonEditor(SettingDescriptor row) => row.Key switch
    {
        "general.checknow" => RowButton(row, "Check now", _actions is not null, () => _actions?.RunCommand(CheckForUpdatesCommand)),
        "appearance.resetcolours" => RowButton(row, "Reset colours", true, ResetColours),
        "mixer.grouprules" => RowButton(row, "Group rules…", _actions is not null, () => _actions?.RunCommand(GroupRulesCommand)),
        "vst.asio.configure" => ConfigureAsioButton(row),
        "vst.soundfont.download" => SoundFontButton(row),
        "vst.inputsshown" => RowButton(row, "Inputs shown…", true, () => _ = OpenInputsShownAsync()),
        "audio.linkedmedia" => RowButton(row, "Manage approved folders…", _actions is not null, () => _ = ManageLinkedAudioAsync()),
        "vst.quarantine" => RowButton(row, "Plug-ins switched off after a crash…", _actions is not null, () => _ = ManageQuarantineAsync()),
        // Apply to all tracks needs the fretboard's per-track views, which only the fretboard window holds: the row stays disabled until it is wired.
        "fretboard.showallas" => RowButton(row, "Apply to all tracks", false, () => { }),
        "score.textfonts" => RowButton(row, "Text & fonts…", true, () => _ = OpenScoreTextFontsAsync()),
        _ => throw new InvalidOperationException($"The settings row {row.Key} is a button without an action."),
    };

    /// <summary>Drops the colours the user set explicitly and writes the chosen preset again; the page is built again to show them.</summary>
    private void ResetColours()
    {
        ThemeColours.ResetToPreset(Working.Appearance, light: AvaloniaPalette.PaintsLight(Working.Appearance, ActualThemeVariant == ThemeVariant.Light));
        MarkChanged();
        Rebuild();
    }

    private Button RowButton(SettingDescriptor row, string label, bool enabled, Action click)
    {
        var button = new Button { Content = label, Classes = { AvaloniaFormControlStyles.PrefsFieldClass }, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, row.Title);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The linked-audio review saved its approvals to the live settings: the window's copy and its baseline take them, so Apply does not bring back a revoked folder.</summary>
    private async Task ManageLinkedAudioAsync()
    {
        if (_actions is null) return;
        await _actions.ManageLinkedAudio(this);
        Working.Audio.ApprovedMedia = CopyApprovals(Saved.Audio.ApprovedMedia);
        _baseline.Audio.ApprovedMedia = CopyApprovals(Saved.Audio.ApprovedMedia);
        _savedRows = null;
    }

    private static List<MediaApproval> CopyApprovals(IEnumerable<MediaApproval> approvals) =>
        approvals.Select(a => new MediaApproval { Project = a.Project, Folder = a.Folder }).ToList();

    /// <summary>The quarantine window may have allowed plug-ins again in the live settings: the window's copy and baseline take that list.</summary>
    private async Task ManageQuarantineAsync()
    {
        if (_actions is null) return;
        await _actions.ManageQuarantine(this);
        Working.Plugins.Quarantined = Saved.Plugins.Quarantined.ToList();
        _baseline.Plugins.Quarantined = Saved.Plugins.Quarantined.ToList();
        _savedRows = null;
    }

    private async Task OpenInputsShownAsync()
    {
        if (await InputsShownWindow.EditAsync(this, Working.Plugins, _baseline.Plugins)) WriteLive(Working);
    }

    /// <summary>Score text styles for each area, edited on the window's staged copy and previewed live; Cancel puts back the copy taken before.</summary>
    private async Task OpenScoreTextFontsAsync()
    {
        var areas = Working.Appearance.ScoreTextAreas;
        var before = JsonSerializer.Serialize(areas);
        var fonts = FontManager.Current.SystemFonts.Select(font => font.Name);
        var kept = await DialogLauncher.Ask<ScoreTextStyleWindow, bool?>(this, () => new ScoreTextStyleWindow(areas, fonts, MarkChanged, Working.Appearance.RecentColours));
        if (kept == true) return;
        Working.Appearance.ScoreTextAreas = JsonSerializer.Deserialize<Dictionary<string, ScoreTextAreaStyle>>(before) ?? new();
        MarkChanged();
    }
}

