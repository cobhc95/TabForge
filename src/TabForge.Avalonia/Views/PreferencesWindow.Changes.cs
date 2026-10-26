using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

// Owns: applying the Settings window's edits: the pending-changes list, discard question and the page, all and theme resets (partial of PreferencesWindow).
// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.

public sealed partial class PreferencesWindow
{
    /// <summary>Takes the edits as the saved settings and raises <see cref="Applied"/> once (the preview notice is left out: the owner's save tells its observers).</summary>
    public void Apply()
    {
        WriteLive(Working, preview: false);
        _baseline = PreferencesCatalog.Copy(Working);
        _savedRows = null;
        _workspaceWritten = false;
        _dirty = false;
        RefreshChanges();
        Applied?.Invoke();
    }

    /// <summary>The rows of the saved snapshot: the values every change is compared with.</summary>
    private List<SettingDescriptor> SavedRows() => _savedRows ??= PreferencesCatalog.Build(PreferencesCatalog.Copy(_baseline));

    private List<SettingDescriptor>? _savedRows;

    /// <summary>"Page › Setting: old → new" for every row that differs from the saved settings.</summary>
    public List<string> PendingChanges() =>
        PreferencesPagePresenter.PendingChanges(SavedRows(), Rows, shortcutsChanged: false);

    IReadOnlyList<PreferencesPagePresenter.PreferenceChange> IPreferencesChangesHost.Changes() =>
        PreferencesPagePresenter.Changes(SavedRows(), Rows);

    void IPreferencesChangesHost.UndoChange(string key)
    {
        var saved = SavedRows().Single(r => r.Key == key).Get();
        Rows.Single(r => r.Key == key).Set(saved);
        MarkChanged();
        Rebuild();
    }

    void IPreferencesChangesHost.JumpToChange(string key)
    {
        var row = Rows.Single(r => r.Key == key);
        // A row behind "More options" opens its group first, so the page shows it.
        var card = PreferencesPagePresenter.Cards(Rows.Where(r => r.Category == row.Category).ToList(), searching: false)
            .FirstOrDefault(c => c.More.Any(more => more.Key == key));
        if (card is not null) _openMore.Add(card.Category() + "|" + card.Group);
        ShowPage(row.Category);
        if (_shown.Any(s => s.Row.Key == key))
        {
            EditorOf(key).Focus();
            EditorOf(key).BringIntoView();
        }
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty || !Saved.General.ConfirmDiscardSettingsChanges) return true;
        var summary = DiscardPreferencesPresenter.Summarize(PreferencesPagePresenter.Changes(SavedRows(), Rows), ThemePresetRows());
        var dialog = new PreferencesDiscardWindow(summary);
        return await _host.ShowModal<MessageBoxAnswer?>(dialog, this) is MessageBoxAnswer.Yes;
    }

    /// <summary>The saved rows with only the chosen theme picked: the values the theme preset itself sets.</summary>
    private List<SettingDescriptor> ThemePresetRows()
    {
        var rows = PreferencesCatalog.Build(PreferencesCatalog.Copy(_baseline));   // its own rows: the cached saved rows keep the real saved values for Changes()
        rows.Single(r => r.Key == DiscardPreferencesPresenter.ThemeKey).Set(Rows.Single(r => r.Key == DiscardPreferencesPresenter.ThemeKey).Get());
        return rows;
    }

    public async Task ResetPageAsync()
    {
        var defaults = DefaultsByKey();
        var page = Rows.Where(r => r.Category == _page && r.Kind != SettingKind.Button).ToList();
        var changed = page.Count(r => PreferencesPagePresenter.ShowValue(r.Get()) != PreferencesPagePresenter.ShowValue(defaults[r.Key].Get()));
        if (changed == 0) { _status.Text = PreferencesPagePresenter.NoChangedSettings; return; }
        var prompt = PreferencesPagePresenter.ResetPage(_page, changed);
        if (!await Confirm(prompt)) return;
        // The theme goes first: its preset rewrites the colours the rows after it then restore.
        foreach (var row in page.OrderBy(r => r.Key == "appearance.thememode" ? 0 : 1)) row.Set(defaults[row.Key].Get());
        MarkChanged();
        Rebuild();
    }

    public async Task ResetAllAsync()
    {
        // The preview has no shortcuts, so the question leaves out the presenter's shortcut count.
        var prompt = new ResetPrompt(PreferencesPagePresenter.ResetAll(0).Title,
            "Restore every setting to its default? Cancel still undoes it until you press OK or Apply.");
        if (!await Confirm(prompt)) return;
        Working = PreferencesCatalog.ResetAllDefaults();
        DropRows();
        _workspaceReset = true;
        MarkChanged();
        Rebuild();
    }

    /// <summary>The Theme card's reset: after a confirmation, the built-in theme keys go back to their defaults (the same keys).</summary>
    public async Task ResetThemeAsync()
    {
        var prompt = new ResetPrompt("Reset theme", "Restore the built-in theme: palette, sizing, typography, motion and the score page look?");
        if (!await Confirm(prompt)) return;
        var defaults = DefaultsByKey();
        foreach (var key in PreferencesPagePresenter.ThemeResetKeys)
        {
            var row = Rows.FirstOrDefault(r => r.Key == key);
            if (row is not null) row.Set(defaults[key].Get());
        }
        MarkChanged();
        Rebuild();
    }

    private async Task<bool> Confirm(ResetPrompt prompt) =>
        await AvaloniaMessageBox.Show(_host, this, prompt.Message, prompt.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes;
}
