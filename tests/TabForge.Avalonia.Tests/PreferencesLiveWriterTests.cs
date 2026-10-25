using System.Linq;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the guards of the Settings window's live writer: only the edited rows reach the live settings, in edit order (the last edit of a row wins over a
/// theme pick between), and a full copy of the baseline undoes partial writes.
/// Tests: PreferencesLiveWriter.
/// </summary>
public class PreferencesLiveWriterTests
{
    private static SettingDescriptor Row(System.Collections.Generic.List<SettingDescriptor> rows, string key) => rows.Single(r => r.Key == key);

    [Fact]
    public void AReEditedRowMovesToTheEndSoTheLastEditWins()
    {
        var saved = new AppSettings();
        var working = PreferencesCatalog.Copy(saved);
        var rows = PreferencesCatalog.Build(working);
        var writer = new PreferencesLiveWriter(saved);

        Row(rows, "appearance.accent").Set("#123456");
        writer.Note(Row(rows, "appearance.accent"));
        Row(rows, "appearance.thememode").Set("Light");   // the preset rewrites the palette, accent included
        writer.Note(Row(rows, "appearance.thememode"));
        Row(rows, "appearance.accent").Set("#654321");
        writer.Note(Row(rows, "appearance.accent"));
        writer.WritePending(working, rows, includeWorkspace: false);

        Assert.Equal("Light", saved.Appearance.ThemeMode);
        Assert.Equal(working.Appearance.Accent, saved.Appearance.Accent);
        Assert.Equal("#654321", saved.Appearance.Accent);
        Assert.False(writer.Pending);
    }

    [Fact]
    public void ABaselineCopyUndoesWhatPartialWritesDid()
    {
        var saved = new AppSettings();
        var baseline = PreferencesCatalog.Copy(saved);
        var working = PreferencesCatalog.Copy(saved);
        var rows = PreferencesCatalog.Build(working);
        var writer = new PreferencesLiveWriter(saved);
        var was = saved.Appearance.ShowDynamics;

        Row(rows, "score.dynamics").Set(!was);
        writer.Note(Row(rows, "score.dynamics"));
        writer.WritePending(working, rows, includeWorkspace: false);
        Assert.Equal(!was, saved.Appearance.ShowDynamics);

        writer.WriteAll(baseline, includeWorkspace: false);
        Assert.Equal(was, saved.Appearance.ShowDynamics);
    }
}
