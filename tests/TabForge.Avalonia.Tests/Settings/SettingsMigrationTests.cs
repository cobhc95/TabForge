using System;
using System.IO;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the "move to the next note after writing" settings migration (old AutoAdvance files take the new default once).
// Does not own: the settings values (AppSettings, EditingSettings) or the settings file I/O (SettingsFileService, AppSettingsStore).
// Tests: this class.
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-settings-migration-{Guid.NewGuid():N}");

    public SettingsMigrationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string json)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, json);
        return path;
    }

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void OldJsonMissingTheVersionMarker_MigratesAutoAdvanceToFalseWithVersionOne()
    {
        // EditingSettings.cs:50-53: "AutoAdvance" is the stored name of MoveToNextNoteAfterWriting.
        // SettingsMigration.cs:134-138: a file without the MoveToNextNoteVersion marker takes the new default once.
        var loaded = SettingsFileService.Load(Write("old.json", "{\"Editing\":{\"AutoAdvance\":true}}"));

        Assert.False(loaded.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(1, loaded.Editing.MoveToNextNoteVersion);
    }

    [Fact]
    public void MigrationIsIdempotent_MigratingTwiceGivesTheSameResult()
    {
        // SettingsMigration.cs:122-123: each versioned step runs once (the marker is saved, so a second run is a no-op).
        var first = SettingsFileService.Load(Write("old.json", "{\"Editing\":{\"AutoAdvance\":true}}"));
        var bytes = SettingsFileService.SerializeToBytes(first); // SettingsFileService.cs:64-68
        var second = SettingsFileService.Load(WriteBytes("migrated.json", bytes));

        Assert.False(second.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(1, second.Editing.MoveToNextNoteVersion);
        Assert.Equal(bytes, SettingsFileService.SerializeToBytes(second));
    }

    [Fact]
    public void UnknownJsonKeys_AreIgnoredNotFatal()
    {
        // SettingsFileService.cs:14-18: the reader is case-insensitive with default ignore-unknown behaviour.
        var loaded = SettingsFileService.Load(Write("future.json",
            "{\"SomeFutureSetting\":42,\"Editing\":{\"MoveToNextNoteVersion\":1,\"AutoAdvance\":true,\"SomeFutureKey\":\"x\"}}"));

        Assert.True(loaded.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(1, loaded.Editing.MoveToNextNoteVersion);
    }

    [Fact]
    public void ValueSetAfterMigration_IsNeverChangedAgain()
    {
        // SettingsMigration.cs:134: with the marker already at 1 the step is skipped, so a later opt-in is kept.
        var loaded = SettingsFileService.Load(Write("optin.json",
            "{\"Editing\":{\"MoveToNextNoteVersion\":1,\"AutoAdvance\":true}}"));

        Assert.True(loaded.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(1, loaded.Editing.MoveToNextNoteVersion);
    }

    [Fact]
    public void ExplicitZeroMarker_IsMigratedOnce()
    {
        // SettingsMigration.cs:134: a stored version below 1 migrates the same way as a missing marker.
        var loaded = SettingsFileService.Load(Write("zeromarker.json",
            "{\"Editing\":{\"MoveToNextNoteVersion\":0,\"AutoAdvance\":true}}"));

        Assert.False(loaded.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(1, loaded.Editing.MoveToNextNoteVersion);
    }

    [Fact]
    public void MissingEditingSection_MigratesOnce()
    {
        var loaded = SettingsFileService.Load(Write("noediting.json", "{}"));

        Assert.False(loaded.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(1, loaded.Editing.MoveToNextNoteVersion);
    }

    [Fact]
    public void MalformedJson_FallsBackToDefaultsWithoutThrowing()
    {
        // SettingsFileService.cs:44-46 throws on malformed JSON, but AppSettingsStore.cs:111-123 catches any
        // load failure and falls back to defaults (LoadFailed), never throwing out of Open.
        using var store = AppSettingsStore.Open(Write("broken.json", "{ not valid json"), TimeSpan.Zero);

        Assert.True(store.LoadFailed);
        var defaults = new AppSettings();
        Assert.Equal(defaults.Editing.MoveToNextNoteAfterWriting, store.Settings.Editing.MoveToNextNoteAfterWriting);
        Assert.Equal(defaults.Editing.MoveToNextNoteVersion, store.Settings.Editing.MoveToNextNoteVersion);
    }
}
