using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the tuning preset catalog rules: the built-in verified data (TuningPresets), the grouped
/// searchable menu (TuningPresetMenu) and the minimal user-preset rules (TuningUserRules).
/// Does not own: the window (TrackPropertiesWindowTests) or applying a tuning (TrackTuningDraft).
/// Tests: TuningPresets, TuningPresetMenu, TuningUserRules.
/// </summary>
public class TuningPresetCatalogTests
{
    private static readonly (string Group, string Name, int[] Midi)[] Existing =
    {
        ("Guitar", "Standard E", new[] { 64, 59, 55, 50, 45, 40 }),
        ("Guitar", "Eb standard (half step down)", new[] { 63, 58, 54, 49, 44, 39 }),
        ("Guitar", "D standard", new[] { 62, 57, 53, 48, 43, 38 }),
        ("Guitar", "C# standard", new[] { 61, 56, 52, 47, 42, 37 }),
        ("Guitar", "C standard", new[] { 60, 55, 51, 46, 41, 36 }),
        ("Guitar", "Drop D", new[] { 64, 59, 55, 50, 45, 38 }),
        ("Guitar", "Drop C#", new[] { 63, 58, 54, 49, 44, 37 }),
        ("Guitar", "Drop C", new[] { 62, 57, 53, 48, 43, 36 }),
        ("Guitar", "Drop B", new[] { 61, 56, 52, 47, 42, 35 }),
        ("Guitar", "Drop A", new[] { 59, 54, 50, 45, 40, 33 }),
        ("Guitar", "Open G (D G D G B D)", new[] { 62, 59, 55, 50, 43, 38 }),
        ("Guitar", "Open D (D A D F# A D)", new[] { 62, 57, 54, 50, 45, 38 }),
        ("Guitar", "DADGAD", new[] { 62, 57, 55, 50, 45, 38 }),
        ("Guitar", "Standard B", new[] { 64, 59, 55, 50, 45, 40, 35 }),
        ("Guitar", "Drop A", new[] { 64, 59, 55, 50, 45, 40, 33 }),
        ("Guitar", "A standard", new[] { 62, 57, 53, 48, 43, 38, 33 }),
        ("Guitar", "Standard F#", new[] { 64, 59, 55, 50, 45, 40, 35, 30 }),
        ("Guitar", "Drop E", new[] { 64, 59, 55, 50, 45, 40, 35, 28 }),
        ("Bass", "Standard E", new[] { 43, 38, 33, 28 }),
        ("Bass", "Eb standard", new[] { 42, 37, 32, 27 }),
        ("Bass", "D standard", new[] { 41, 36, 31, 26 }),
        ("Bass", "Drop D", new[] { 43, 38, 33, 26 }),
        ("Bass", "Drop C", new[] { 41, 36, 31, 24 }),
        ("Bass", "Standard B", new[] { 43, 38, 33, 28, 23 }),
        ("Bass", "Standard E, high C", new[] { 48, 43, 38, 33, 28 }),
        ("Bass", "Drop A", new[] { 43, 38, 33, 28, 21 }),
        ("Bass", "A standard", new[] { 41, 36, 31, 26, 21 }),
        ("Bass", "Standard B (high C)", new[] { 48, 43, 38, 33, 28, 23 }),
        ("Bass", "Drop A", new[] { 48, 43, 38, 33, 28, 21 }),
        ("Bass", "Standard F# (high F)", new[] { 53, 48, 43, 38, 33, 28, 23 }),
        ("Guitar", "Standard C#", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25 }),
    };

    [Fact]
    public void ExistingPresetsArePreservedVerbatim()
    {
        var catalog = TuningPresets.Catalog.ToList();
        foreach (var (group, name, midi) in Existing)
        {
            var hits = catalog.Where(e => e.Group == group && e.Preset.Name == name && e.Preset.HighToLow.Length == midi.Length).ToList();
            Assert.Equal(midi, Assert.Single(hits).Preset.HighToLow);
        }
        var six = TuningPresets.SixStringPresets.Select(p => p.Name).ToList();
        Assert.Equal(Existing.Where(e => e.Group == "Guitar" && e.Midi.Length == 6).Select(e => e.Name).ToList(), six.Take(13).ToList());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void EveryCountFromThreeToTenHasPresets(int strings)
    {
        Assert.Contains(TuningPresets.Catalog, e => e.Preset.HighToLow.Length == strings);
        Assert.NotEmpty(TuningPresets.For(strings, false));
    }

    [Fact]
    public void MenuGroupsByInstrumentWithFamilyFirst()
    {
        var six = TuningPresetMenu.Groups("", 6, allCounts: false, bass: false);
        Assert.Equal(TuningPresetMenu.GuitarGroup, six[0].Name);
        Assert.Equal(TuningPresetMenu.BassGroup, six[1].Name);
        Assert.Contains(six, g => g.Name == TuningPresetMenu.LapSteelGroup);
        Assert.All(six.SelectMany(g => g.Entries), e => Assert.Equal(6, e.Strings));
        Assert.Equal(TuningPresetMenu.BassGroup, TuningPresetMenu.Groups("", 6, false, bass: true)[0].Name);
    }

    [Fact]
    public void MenuSearchFindsInstrumentsAndTunings()
    {
        var uke = TuningPresetMenu.Groups("uku", 4, false, false);
        Assert.Contains(uke, g => g.Name == TuningPresetMenu.UkuleleGroup);
        Assert.All(uke.SelectMany(g => g.Entries), e => Assert.Equal(4, e.Strings));
        Assert.NotEmpty(TuningPresetMenu.Groups("dulcimer", 3, false, false));
        Assert.NotEmpty(TuningPresetMenu.Groups("lap", 6, false, false));
        Assert.NotEmpty(TuningPresetMenu.Groups("banjo", 5, false, false));
        Assert.NotEmpty(TuningPresetMenu.Groups("nashville", 6, false, false));
        Assert.NotEmpty(TuningPresetMenu.Groups("double drop", 6, false, false));
        var all = TuningPresetMenu.Groups("", 6, allCounts: true, bass: false).SelectMany(g => g.Entries);
        Assert.Contains(all, e => e.Strings == 3);
        Assert.Contains(all, e => e.Strings == 10);
        Assert.Empty(TuningPresetMenu.Groups("zzzz", 6, true, false));
        Assert.Equal("Drop A (7 strings)", TuningPresetMenu.Groups("drop a", 6, true, false).SelectMany(g => g.Entries).First(e => e.Strings == 7).Text(true));
    }

    [Fact]
    public void MatchPrefersFamilyThenCatalogOrder()
    {
        Assert.Equal("Standard E", TuningPresetMenu.Label(new[] { 64, 59, 55, 50, 45, 40 }, bass: false));
        Assert.Equal("Drop D", TuningPresetMenu.Label(new[] { 64, 59, 55, 50, 45, 38 }, bass: false));
        var edited = new[] { 64, 59, 55, 50, 45, 41 };
        Assert.Equal(TuningPresetMenu.Custom, TuningPresetMenu.Label(edited, bass: false));
        // Baritone DGBE and Chicago DGBE share MIDI; Ukulele sorts before Banjo.
        var both = TuningPresetMenu.Match(new[] { 64, 59, 55, 50 }, bass: false);
        Assert.NotNull(both);
        Assert.Equal(TuningPresetMenu.UkuleleGroup, both.Group);
    }

    [Fact]
    public void ApplyResizesAcrossNewCounts()
    {
        var standard = new[] { 64, 59, 55, 50, 45, 40 };
        var draft = new TrackTuningDraft(standard);
        draft.Apply(new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25, 18 });
        Assert.Equal(10, draft.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, -1, -1, -1, -1 }, draft.Origin);
        draft.Apply(new[] { 62, 57, 50 });
        Assert.Equal(new[] { 0, 1, 2 }, draft.Origin);
        Assert.Equal(new[] { 62, 57, 50 }, draft.Tunings);
    }

    [Fact]
    public void UserRulesSaveRenameAndDelete()
    {
        var store = new List<TuningUserPreset>();
        Assert.True(TuningUserRules.TrySave(store, "My drop", new[] { 64, 59, 55, 50, 45, 38 }, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { "My drop" }, TuningUserRules.Names(store));
        Assert.True(TuningUserRules.TrySave(store, "my DROP", new[] { 62, 57, 53, 48, 43, 38 }, out _));
        Assert.Single(store);
        Assert.False(TuningUserRules.TrySave(store, "", new[] { 64, 59, 55, 50, 45, 40 }, out error));
        Assert.NotNull(error);
        Assert.False(TuningUserRules.TrySave(store, "Standard E", new[] { 64, 59, 55, 50, 45, 40 }, out error));
        Assert.False(TuningUserRules.TrySave(store, "Too few", new[] { 64, 59 }, out _));
        Assert.False(TuningUserRules.TrySave(store, "Too low", new[] { 64, 59, 55, 50, 45, 5 }, out _));
        Assert.True(TuningUserRules.TryRename(store, "my drop", "My open", out error));
        Assert.Null(error);
        Assert.Equal(new[] { "My open" }, TuningUserRules.Names(store));
        Assert.False(TuningUserRules.TryRename(store, "My open", "DADGAD", out _));
        Assert.False(TuningUserRules.TryRename(store, "Missing", "Other", out _));
        var entries = TuningPresetMenu.UserEntries(store);
        Assert.Equal(TuningPresetMenu.MyTuningsGroup, Assert.Single(entries).Group);
        var groups = TuningPresetMenu.Groups("my open", 6, false, false, entries);
        Assert.Contains(groups, g => g.Name == TuningPresetMenu.MyTuningsGroup);
        Assert.True(TuningUserRules.Delete(store, "MY OPEN"));
        Assert.Empty(store);
        Assert.False(TuningUserRules.Delete(store, "MY OPEN"));
    }

    [Fact]
    public void UserStoreRefusesAFullListButReplaces()
    {
        var store = new List<TuningUserPreset>();
        for (var i = 0; i < TuningUserRules.MaxPresets; i++)
            Assert.True(TuningUserRules.TrySave(store, $"Mine {i}", new[] { 64, 59, 55, 50, 45, 40 + (i % 2) }, out _));
        Assert.False(TuningUserRules.TrySave(store, "One more", new[] { 64, 59, 55, 50, 45, 40 }, out var error));
        Assert.NotNull(error);
        Assert.True(TuningUserRules.TrySave(store, "Mine 0", new[] { 62, 57, 53, 48, 43, 38 }, out _));
        Assert.Equal(TuningUserRules.MaxPresets, store.Count);
    }

    [Fact]
    public void DubiousResearchEntriesAreExcluded()
    {
        var catalog = TuningPresets.Catalog.ToList();
        Assert.DoesNotContain(catalog, e => e.Preset.Name.Contains("Ostrich"));
        Assert.DoesNotContain(catalog, e => e.Preset.HighToLow.Length == 12);
        Assert.DoesNotContain(catalog, e => e.Preset.Name == "B11" || e.Preset.Name.StartsWith("E6 "));
        Assert.DoesNotContain(catalog, e => e.Preset.Name == "Drop Db" || e.Preset.Name == "Drop Bb");
        Assert.DoesNotContain(catalog, e => e.Preset.Name.Contains("GDAD") || e.Preset.Name.Contains("FCGD"));
        Assert.DoesNotContain(catalog, e => e.Preset.Name.Contains("Drop Ab"));
        Assert.Contains(catalog, e => e.Preset.Name == "E7 (low-E form)");
        Assert.Contains(catalog, e => e.Group == TuningPresets.DulcimerGroup && e.Preset.HighToLow.Length == 3);
    }

    [Fact]
    public void AllBuiltInMidisAreInRange()
    {
        foreach (var (_, preset) in TuningPresets.Catalog)
        {
            Assert.InRange(preset.HighToLow.Length, TrackPropertiesPresenter.FewestStrings, TrackPropertiesPresenter.MostStrings);
            Assert.All(preset.HighToLow, m => Assert.InRange(m, TrackPropertiesPresenter.LowestNote, TrackPropertiesPresenter.HighestNote));
        }
    }

    [Fact]
    public void UserSaveAppearsLabelAppliesAndRoundTrips()
    {
        var settings = new AppSettings();
        var saved = 0;
        var store = new TuningUserSettingsStore(settings, () => saved++);
        Assert.True(store.TrySave("My DADGAD", new[] { 62, 57, 55, 50, 45, 40 }, out var error));
        Assert.Null(error);
        Assert.Equal(1, saved);
        var users = TuningUserSettings.LoadUserEntries(settings);
        var entries = TuningPresetMenu.UserEntries(users);
        Assert.Equal(TuningPresetMenu.MyTuningsGroup, Assert.Single(entries).Group);
        var groups = TuningPresetMenu.Groups("", 6, allCounts: false, bass: false, entries);
        Assert.Contains(groups, g => g.Name == TuningPresetMenu.MyTuningsGroup);
        Assert.Equal("My DADGAD", TuningPresetMenu.Label(new[] { 62, 57, 55, 50, 45, 40 }, bass: false, entries));
        var draft = new TrackTuningDraft(new[] { 64, 59, 55, 50, 45, 40 });
        draft.Apply(entries[0].HighToLow);
        Assert.Equal(new[] { 62, 57, 55, 50, 45, 40 }, draft.Tunings);
        var bytes = SettingsFileService.SerializeToBytes(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(bytes)!;
        back = SettingsValidator.Normalize(back);
        var again = TuningPresetMenu.UserEntries(TuningUserSettings.LoadUserEntries(back));
        Assert.Equal("My DADGAD", TuningPresetMenu.Label(new[] { 62, 57, 55, 50, 45, 40 }, bass: false, again));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tforge-tuning-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsFileService.SaveAtomic(path, settings);
            var loaded = SettingsFileService.Load(path);
            Assert.Equal("My DADGAD", TuningPresetMenu.Label(new[] { 62, 57, 55, 50, 45, 40 }, bass: false, TuningPresetMenu.UserEntries(TuningUserSettings.LoadUserEntries(loaded))));
            Assert.Equal(settings.EffectPresets.Count, loaded.EffectPresets.Count);
        }
        finally { try { System.IO.File.Delete(path); } catch { } }
    }

    [Fact]
    public void UserThreeStringHiddenUnlessAllCountsAndApplyResizes()
    {
        var settings = new AppSettings();
        var store = new TuningUserSettingsStore(settings);
        Assert.True(store.TrySave("My dulcimer", new[] { 62, 57, 50 }, out _));
        var entries = TuningPresetMenu.UserEntries(TuningUserSettings.LoadUserEntries(settings));
        Assert.DoesNotContain(TuningPresetMenu.Groups("", 6, allCounts: false, bass: false, entries), g => g.Name == TuningPresetMenu.MyTuningsGroup);
        var all = TuningPresetMenu.Groups("", 6, allCounts: true, bass: false, entries);
        var mine = Assert.Single(all, g => g.Name == TuningPresetMenu.MyTuningsGroup).Entries;
        Assert.Equal("My dulcimer (3 strings)", Assert.Single(mine).Text(true));
        var draft = new TrackTuningDraft(new[] { 64, 59, 55, 50, 45, 40 });
        draft.Apply(mine[0].HighToLow);
        Assert.Equal(new[] { 62, 57, 50 }, draft.Tunings);
        Assert.Equal(new[] { 0, 1, 2 }, draft.Origin);
    }

    [Fact]
    public void UserRenameDeleteValidationCallsSaveOncePerMutation()
    {
        var settings = new AppSettings();
        var saved = 0;
        var store = new TuningUserSettingsStore(settings, () => saved++);
        Assert.True(store.TrySave("My DADGAD", new[] { 62, 57, 55, 50, 45, 40 }, out _));
        Assert.True(store.TrySave("Other", new[] { 64, 59, 55, 50, 45, 40 }, out _));
        Assert.Equal(2, saved);
        Assert.False(store.TryRename("My DADGAD", "Standard E", out var builtIn));
        Assert.NotNull(builtIn);
        Assert.False(store.TryRename("My DADGAD", "Other", out var clash));
        Assert.NotNull(clash);
        Assert.False(store.TryRename("My DADGAD", "", out _));
        Assert.False(store.TryRename("My DADGAD", new string('x', 65), out _));
        Assert.False(store.TrySave("Two strings", new[] { 64, 59 }, out _));
        Assert.False(store.TrySave("Too low", new[] { 64, 59, 55, 50, 45, 11 }, out _));
        Assert.False(store.TrySave("Too high", new[] { 64, 59, 55, 50, 45, 101 }, out _));
        Assert.Equal(2, saved);
        Assert.True(store.TryRename("My DADGAD", "Kept", out _));
        Assert.Equal(3, saved);
        Assert.True(store.Delete("Kept"));
        Assert.Equal(4, saved);
        Assert.Equal(new[] { "Other" }, store.Names());
        Assert.False(store.Delete("Kept"));
        Assert.Equal(4, saved);
    }

    [Fact]
    public void ValidatorDropsCorruptCapsAtMaxAndKeepsOtherFields()
    {
        var settings = new AppSettings { EffectPresets = new List<EffectPresetEntry> { new() { Kind = "Bend", Name = "Kept" } } };
        settings.TuningUserPresets.Add(new TuningUserPresetEntry { Name = null, HighToLow = new List<int> { 64, 59, 55 } });
        settings.TuningUserPresets.Add(new TuningUserPresetEntry { Name = "Twelve", HighToLow = new List<int> { 64, 59, 55, 50, 45, 40, 35, 30, 25, 20, 19, 18 } });
        settings.TuningUserPresets.Add(new TuningUserPresetEntry { Name = "High note", HighToLow = new List<int> { 200, 59, 55 } });
        settings.TuningUserPresets.Add(new TuningUserPresetEntry { Name = "Drop D", HighToLow = new List<int> { 64, 59, 55 } });
        settings.TuningUserPresets.Add(new TuningUserPresetEntry { Name = "", HighToLow = new List<int> { 64, 59, 55 } });
        for (var i = 0; i < 30; i++) settings.TuningUserPresets.Add(new TuningUserPresetEntry { Name = $"Mine {i}", HighToLow = new List<int> { 64, 59, 55 } });
        var kept = SettingsValidator.Normalize(settings);
        Assert.Equal(TuningUserRules.MaxPresets, kept.TuningUserPresets.Count);
        Assert.Equal("Mine 0", kept.TuningUserPresets[0].Name);
        Assert.Equal("Mine 23", kept.TuningUserPresets[23].Name);
        Assert.Single(kept.EffectPresets);
        var old = new AppSettings();
        var normalised = SettingsValidator.Normalize(old);
        Assert.NotNull(normalised.TuningUserPresets);
        Assert.Empty(normalised.TuningUserPresets);
    }
}
