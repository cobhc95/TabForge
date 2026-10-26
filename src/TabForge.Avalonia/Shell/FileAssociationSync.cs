using System;
using Avalonia.Controls;
using System.IO;
using System.Security;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Shell;

// Owns: making the Windows "Open with TabForge" registration match the Windows-integration setting (general.associate): at start-up and each time the
//     setting changes, only when the two differ; a refusal by Windows is reported to the person.
// Does not own: the registry writes (Platform/AvaloniaFileAssociations behind IFileAssociations), the extension names (Services/FileAssociations),
//     the setting itself (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.FileAssociationSyncTests.
/// <summary>Applies the Windows-integration setting to the file associations. UI thread only.</summary>
public sealed class FileAssociationSync
{
    private readonly IFileAssociations _associations;
    private readonly string _exePath;
    private readonly Action<string> _report;

    /// <param name="associations">The Windows registration (the adapter, or a test's fake).</param>
    /// <param name="exePath">This executable: the registration points at it.</param>
    /// <param name="report">Shows the outcome or the refusal to the person.</param>
    public FileAssociationSync(IFileAssociations associations, string exePath, Action<string> report)
    {
        _associations = associations;
        _exePath = exePath;
        _report = report;
    }

    /// <summary>Registers TabForge when <paramref name="wanted"/> and it is not registered, or removes the entries when not wanted and they exist; does nothing otherwise.</summary>
    public void Apply(bool wanted)
    {
        if (_exePath.Length == 0) return;
        try
        {
            if (_associations.IsRegistered(_exePath) == wanted) return;
            if (wanted) _associations.Register(_exePath); else _associations.Unregister();
            _report(wanted ? FileAssociationText.Registered : FileAssociationText.Removed);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // Reported, not swallowed: Windows refused the write, so the registry is as it was before this change.
            _report(FileAssociationText.Refused);
        }
    }

    /// <summary>Applies the setting now and again each time the settings change. When <paramref name="owner"/> is given, the change subscription ends with it.</summary>
    public static void Follow(MainWindowSettings? settings, IFileAssociations associations, Action<string> report, Window? owner = null)
    {
        if (settings is null) return;
        var sync = new FileAssociationSync(associations, Environment.ProcessPath ?? "", report);
        sync.Apply(settings.Settings.General.AssociateFiles);
        Action<AppSettings> onChanged = changed => sync.Apply(changed.General.AssociateFiles);
        settings.SettingsChanged += onChanged;
        if (owner is not null) owner.Closed += (_, _) => settings.SettingsChanged -= onChanged;
    }
}

/// <summary>The status texts of the file-association change (the words match the app's).</summary>
public static class FileAssociationText
{
    public const string Registered = "TabForge now opens .gp and .tforge files";
    public const string Removed = "File associations removed";
    public const string Refused = "Windows refused the file-association change";
}
