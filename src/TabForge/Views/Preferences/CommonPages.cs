using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Services;
using WpfPath = System.Windows.Shapes.Path;
using static TabForge.Views.PreferencesCards;

namespace TabForge.Views;

/// <summary>The Common settings page (shortcuts to the same rows) and the Advanced page (version, files, reset).</summary>
internal sealed class CommonPages
{
    private readonly IPreferencesHost _host;
    private readonly Window _window;

    internal CommonPages(IPreferencesHost host)
    {
        _host = host;
        _window = host.Dialog;
    }

    private Button CreateButton(string text, string styleKey, string? tooltip = null) => PreferencesCards.CreateButton(_window, text, styleKey, tooltip);

    internal void BuildHomePage()
    {
        void Group(string title, string[] keys, params (string Label, Action Click)[] links)
        {
            var card = GroupCard(title);
            foreach (var key in keys)
                if (_host.DescriptorsByKey.TryGetValue(key, out var descriptor)) AddCardContent(card, _host.SettingRow(descriptor, false));
            foreach (var (label, click) in links)
            {
                var button = CreateButton(label, "SecondaryActionButton");
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 10, 0, 0);
                button.Click += (_, _) => click();
                AddCardContent(card, button);
            }
            _host.AddTopCard(card, 1);
        }
        Group("Look", new[] { "appearance.thememode", "appearance.uiscale", "appearance.fretboard", "general.toolbar" },
            ("All appearance settings", () => _host.NavigateToCategory(SettingsCatalog.Appearance)));
        Group("Sound", new[] { "vst.driver", "vst.device", "audio.metronome", "audio.metrovolume", "audio.countin", "audio.speed" },
            ("More audio settings", () => _host.NavigateToCategory(SettingsCatalog.AudioVst)),
            ("Playback and practice settings", () => _host.NavigateToCategory(SettingsCatalog.Playback)));
        Group("Score", new[] { "score.defaultnotation", "follow.mode" },
            ("All score settings", () => _host.NavigateToCategory(SettingsCatalog.Score)));
        Group("Files", new[] { "general.autosave", "general.saveformat" },
            ("Files and backups", () => _host.NavigateToCategory(SettingsCatalog.Files)));
        var shortcuts = GroupCard("Shortcuts");
        var open = CreateButton("Open all shortcuts", "SecondaryActionButton");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 6, 0, 0);
        open.Click += (_, _) => _host.NavigateToCategory(SettingsCatalog.Hotkeys);
        AddCardContent(shortcuts, Note("Pick a preset layout (TabForge or another program's) and change any key on the Shortcuts page.", 12, new Thickness(0, 2, 0, 0)));
        AddCardContent(shortcuts, open);
        _host.AddTopCard(shortcuts, 1);
        _host.AddTopCard(InformationCard("Looking for _window song's settings?",
            "Preferences are for the program. A song's own settings are in other windows: Project settings (the File menu) and Track properties (right-click a track)."), 1);
    }

    internal void BuildAdvancedPage()
    {
        _host.AddTopCard(InformationCard("Version", $"TabForge {AppInfo.DisplayVersion}"), 1);
        _host.AddTopCard(InformationCard("Settings file",
            UserPaths.SettingsFile), 1);
        _host.AddTopCard(InformationCard("Compatibility",
            "Older flat settings files are migrated into the current settings model when loaded or imported. All fields recognized by the settings model remain available to export; unrecognized JSON properties are ignored."), 1);
        _host.AddTopCard(InformationCard("Arrangement playback",
            "Section edits and reordering always refresh the future playback route at a safe bar boundary. This safety behavior is intentionally not optional."), 1);
        var reset = GroupCard("Reset");
        var resetAll = CreateButton("Reset all settings…", "SecondaryActionButton", "Restore every setting and every shortcut to its default (asks first).");
        resetAll.HorizontalAlignment = HorizontalAlignment.Left;
        resetAll.Margin = new Thickness(0, 10, 0, 0);
        resetAll.Click += (_, _) => _host.ResetAll();
        AddCardContent(reset, Note("Restores every setting and all keyboard shortcuts to their defaults. You can still press Cancel to undo it.", 12, new Thickness(0, 2, 0, 0)));
        AddCardContent(reset, resetAll);
        _host.AddTopCard(reset, 1);
    }
}
