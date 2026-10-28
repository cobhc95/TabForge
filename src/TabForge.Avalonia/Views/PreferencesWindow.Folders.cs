#pragma warning disable CS0618 // Avalonia 11.3 marks the IDataObject drag API obsolete; it still delivers the dropped folder list.
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the folder list of the Settings rows (SettingKind.FolderList: Plug-in folders, Common plug-in folders) with Add folder…, Remove and
//       Reset to defaults. The stored value stays one "; "-joined string per row.
// Does not own: the rows (SettingsCatalog.Audio) or the folder picker (AvaloniaFileDialogs).
// Tests: TabForge.Avalonia.Tests.PreferencesFolderListTests.
public sealed partial class PreferencesWindow
{
    private const string CommonFoldersKey = "vst.commonfolders";

    /// <summary>The folders of a folder-list row as a list with Add folder…, Remove (or the Delete key) and, for the common folders, Reset to defaults. Folders can also be dropped onto it.</summary>
    private Control FolderListEditor(SettingDescriptor row)
    {
        var folders = new ObservableCollection<string>(FolderListText.Parse(row.Get()?.ToString()));
        var list = new ListBox { ItemsSource = folders, MinWidth = ChromeMetrics.FolderListMinWidth, MaxHeight = ChromeMetrics.FolderListMaxHeight, Name = "FolderList" };
        AutomationProperties.SetName(list, row.Title);
        var add = new Button { Content = "Add folder…", Name = "AddFolderButton", Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
        var remove = new Button { Content = "Remove", Name = "RemoveFolderButton", IsEnabled = false, Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { add, remove } };

        void Commit() => Changed(row, FolderListText.Join(folders));
        void AddAll(string[]? chosen)
        {
            if (FolderListText.Add(folders, chosen ?? Array.Empty<string>(), out var refused)) Commit();
            if (refused.Length > 0) _status.Text = refused;
        }
        void RemoveSelected()
        {
            if (list.SelectedItem is not string chosen) return;
            folders.Remove(chosen);
            Commit();
        }

        add.Click += (_, _) => AddAll(PickFolders(row.Title));
        remove.Click += (_, _) => RemoveSelected();
        list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedItem is not null;
        list.KeyDown += (_, e) => { if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; } };
        DragDrop.SetAllowDrop(list, true);
        list.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = MainWindowFileDrop.LocalFolders(e.Data).Any() ? DragDropEffects.Copy : DragDropEffects.None);
        list.AddHandler(DragDrop.DropEvent, (_, e) => AddAll(MainWindowFileDrop.LocalFolders(e.Data).ToArray()));
        if (row.Key == CommonFoldersKey)
        {
            var reset = new Button { Content = "Reset to defaults", Name = "ResetFoldersButton", Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
            reset.Click += (_, _) =>
            {
                row.Set("");
                folders.Clear();
                foreach (var path in FolderListText.Parse(row.Get()?.ToString())) folders.Add(path);
                MarkChanged();
            };
            buttons.Children.Add(reset);
        }
        return new StackPanel { Spacing = 6, Children = { list, buttons } };
    }

    /// <summary>The folders the user picks in the system folder dialog (the seam <see cref="PreferencesRowActions.PickFolders"/> replaces it); null when cancelled.</summary>
    private string[]? PickFolders(string title) =>
        _actions?.PickFolders is { } pick ? pick(this)
            : _dialogs.PickFolders(new TabForge.Platform.FileDialogOptions { Key = TabForge.Services.DialogKeys.SettingsFolder, Title = "Add folders: " + title, Multiselect = true }, this);
}
#pragma warning restore CS0618
