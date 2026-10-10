using System.IO;
using System.Security.Cryptography;
using System.Text;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

/// <summary>Data-integrity leftovers (audit WP-8): atomic presets / chains, temp sweep, chain-state hash check and GC, raw recovery copies.</summary>
public static partial class SelfTest
{
    private static void TestDataIntegrityLeftovers()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-integrity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            AtomicPresetsKeepOldFileOnFailure(Path.Combine(scratch, "presets"));
            StaleLeftoversAreSwept(Path.Combine(scratch, "sweep"));
            ChainStatesAreVerifiedAndCollected(Path.Combine(scratch, "states"));
            InvalidProjectStillGetsRecoveryCopy(Path.Combine(scratch, "recovery"));
            AutosaveTimerAndRoundTrip(Path.Combine(scratch, "autosave"));
        }
        finally
        {
            FilePathPolicy.FaultInjection = null;
            PluginLibrary.RootOverride = null;
            ChainStateStore.FolderOverride = null;
            try { Directory.Delete(scratch, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // P-01: presets and chains are written via FilePathPolicy.WriteAtomically; a failed write leaves the previous file intact.
    private static void AtomicPresetsKeepOldFileOnFailure(string root)
    {
        PluginLibrary.RootOverride = root;
        var slot = new PluginSlot { Name = "SelfTest Synth", Path = @"C:\selftest\synth.dll" };
        PluginLibrary.SavePreset(slot, "Lead", "AAAA");
        var failures = 0;
        foreach (var stage in new[] { "staged:", "commit:" })
        {
            FilePathPolicy.FaultInjection = s => { if (s.StartsWith(stage, StringComparison.Ordinal)) throw new IOException("injected " + stage); };
            try { PluginLibrary.SavePreset(slot, "Lead", "BBBB"); }
            catch (IOException) { failures++; }
            finally { FilePathPolicy.FaultInjection = null; }
        }
        var loaded = PluginLibrary.LoadPreset(slot, "Lead");
        var presetFolder = Path.GetDirectoryName(Directory.GetFiles(root, "*.tfpreset", SearchOption.AllDirectories).Single())!;
        Check("P-01: a preset write failing at stage or commit throws and leaves the previous preset intact (no temp file left)",
            failures == 2 && loaded?.State == "AAAA" && Directory.GetFiles(presetFolder, ".tabforge-*").Length == 0,
            $"failures {failures}, state {loaded?.State ?? "null"}");

        var chainPath = Path.Combine(root, "chain.tfchain");
        PluginLibrary.SaveChain(chainPath, new[] { slot });
        var before = File.ReadAllBytes(chainPath);
        FilePathPolicy.FaultInjection = s => { if (s.StartsWith("staged:", StringComparison.Ordinal)) throw new IOException("injected"); };
        var threw = false;
        try { PluginLibrary.SaveChain(chainPath, new[] { slot, new PluginSlot { Name = "Second" } }); }
        catch (IOException) { threw = true; }
        finally { FilePathPolicy.FaultInjection = null; }
        Check("P-01: a failing FX chain save leaves the previous chain file byte-identical",
            threw && File.ReadAllBytes(chainPath).AsSpan().SequenceEqual(before));
    }

    // P-02: our own stale staging / backup files older than 24 h are removed on the next save in that folder; nothing else is touched.
    private static void StaleLeftoversAreSwept(string dir)
    {
        Directory.CreateDirectory(dir);
        string Make(string name, TimeSpan age)
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }
        var staleTmp = Make($".tabforge-{Guid.NewGuid():N}.tmp", TimeSpan.FromHours(48));
        var staleBak = Make($".tabforge-{Guid.NewGuid():N}.bak", TimeSpan.FromHours(30));
        var freshTmp = Make($".tabforge-{Guid.NewGuid():N}.tmp", TimeSpan.FromHours(1));
        var foreign = Make(".tabforge-notours.tmp", TimeSpan.FromHours(48));
        var userBak = Make("song.bak", TimeSpan.FromHours(48));
        FilePathPolicy.WriteAtomically(Path.Combine(dir, "song.tforge"), s => s.WriteByte(1));
        Check("P-02: a save removes our own .tmp/.bak leftovers older than 24 h in that folder",
            !File.Exists(staleTmp) && !File.Exists(staleBak));
        Check("P-02: young leftovers, other .tabforge-* names and the user's own .bak files are kept",
            File.Exists(freshTmp) && File.Exists(foreign) && File.Exists(userBak));
    }

    // P-03: a state file whose content does not hash to its name is ignored; GC keeps referenced and young files.
    private static void ChainStatesAreVerifiedAndCollected(string dir)
    {
        ChainStateStore.FolderOverride = dir;
        string State(int seed) => Convert.ToBase64String(Enumerable.Range(0, 3000).Select(i => (byte)(i * seed)).ToArray());
        string FileOf(PluginSlot p) => Path.Combine(dir, p.State![ChainStateStore.Prefix.Length..] + ".state");

        var kept = new PluginSlot { Name = "Kept", State = State(3) };
        var dropped = new PluginSlot { Name = "Dropped", State = State(5) };
        var young = new PluginSlot { Name = "Young", State = State(7) };
        var tampered = new PluginSlot { Name = "Tampered", State = State(11) };
        var originalTampered = tampered.State;
        ChainStateStore.Externalise(new[] { kept, dropped, young, tampered });

        // Tamper: same length, different content, name unchanged.
        var tamperedFile = FileOf(tampered);
        var bytes = File.ReadAllBytes(tamperedFile);
        bytes[10] = (byte)(bytes[10] == (byte)'A' ? 'B' : 'A');
        File.WriteAllBytes(tamperedFile, bytes);
        var probe = new PluginSlot { State = tampered.State };
        ChainStateStore.Resolve(new[] { probe });
        Check("P-03: a tampered chain-state file (content does not match its SHA-256 name) is treated as missing", probe.State == "");
        var again = new PluginSlot { State = originalTampered };
        ChainStateStore.Externalise(new[] { again });
        var restored = new PluginSlot { State = again.State };
        ChainStateStore.Resolve(new[] { restored });
        Check("P-03: storing the same state again replaces the damaged file, and it resolves to the original",
            restored.State == originalTampered && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tamperedFile))).Equals(Path.GetFileNameWithoutExtension(tamperedFile), StringComparison.OrdinalIgnoreCase));

        var settings = new PluginSettings();
        settings.AutoChains.Add(new AutoChain { Key = "guitar", Plugins = { kept } });
        settings.StartupTracks.Add(new StartupTrack { Plugins = { tampered } });
        var old = DateTime.UtcNow - TimeSpan.FromHours(3);
        foreach (var p in new[] { kept, dropped, tampered }) File.SetLastWriteTimeUtc(FileOf(p), old);
        var foreign = Path.Combine(dir, "notes.state");
        File.WriteAllText(foreign, "not a hash name");
        File.SetLastWriteTimeUtc(foreign, old);
        var deleted = ChainStateStore.CollectGarbage(settings);
        Check("P-03: GC deletes only unreferenced hash-named files older than 1 h; referenced, young and foreign files stay",
            deleted == 1 && !File.Exists(FileOf(dropped)) && File.Exists(FileOf(kept)) && File.Exists(FileOf(tampered)) && File.Exists(FileOf(young)) && File.Exists(foreign),
            $"deleted {deleted}");
        var resolvedKept = new PluginSlot { State = kept.State };
        ChainStateStore.Resolve(new[] { resolvedKept });
        Check("P-03: a referenced state still resolves after GC", resolvedKept.State == State(3));
    }

    // Autosave timer logic, file naming, crash-orphan detection, and the snapshot -> recovery copy -> load round trip.
    private static void AutosaveTimerAndRoundTrip(string dir)
    {
        var t0 = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var planner = new AutosavePlanner(t0);
        Check("F-04: autosave is not due before the interval", !planner.Due(t0.AddSeconds(119), 2, playing: false));
        Check("F-04: autosave is due at the interval", planner.Due(t0.AddSeconds(120), 2, playing: false));
        Check("F-04: while playing the interval is doubled (a cap, not a stop): not due at 3 min, due at 4 min of a 2-min interval",
            !planner.Due(t0.AddMinutes(3), 2, playing: true) && planner.Due(t0.AddMinutes(4), 2, playing: true) && planner.Due(t0.AddMinutes(3), 2, playing: false));
        Check("F-04: after a failed pass the retry comes after 1 minute (doubled while playing)",
            !planner.Due(t0.AddSeconds(59), 10, playing: false, retrying: true) && planner.Due(t0.AddSeconds(60), 10, playing: false, retrying: true)
            && !planner.Due(t0.AddSeconds(119), 10, playing: true, retrying: true) && planner.Due(t0.AddSeconds(120), 10, playing: true, retrying: true));
        Check("F-04: autosave off (0) never runs", !planner.Due(t0.AddHours(5), 0, playing: false));
        planner.Ran(t0.AddMinutes(5));
        Check("F-04: the interval restarts after a pass", !planner.Due(t0.AddMinutes(6), 2, playing: false) && planner.Due(t0.AddMinutes(7), 2, playing: false));
        Check("F-04: interval setting is bounded and defaults to 2",
            AutosaveService.NormalizeMinutes(-1) == 2 && AutosaveService.NormalizeMinutes(999) == 2 && AutosaveService.NormalizeMinutes(0) == 0 && AutosaveService.NormalizeMinutes(10) == 10
            && new GeneralSettings().AutosaveMinutes == 2 && AutosaveChoices.ToMinutes(AutosaveChoices.ToLabel(5)) == 5);

        var project = TabForge.Presets.TemplateFactory.Blank();
        project.Title = "Autosave probe";
        project.IsDirty = true;
        var snapshot = ProjectService.SnapshotBytes(project);
        var mine = AutosaveService.FileFor(dir, Environment.ProcessId, Guid.NewGuid(), "Probe: song?");
        var dead = AutosaveService.FileFor(dir, 999_999, Guid.NewGuid(), "Crashed");
        App.WriteRecoveryCopy(ProjectService.RestoreBytes(snapshot), mine);
        App.WriteRecoveryCopy(ProjectService.RestoreBytes(snapshot), dead);
        var loaded = ProjectService.Load(dead);
        Check("F-04: an autosave copy loads back with the same song", loaded.Title == "Autosave probe" && loaded.Tracks.Count == project.Tracks.Count);
        Check("F-04: the snapshot on the UI side leaves the song unsaved", project.IsDirty);
        var orphans = AutosaveService.FindOrphans(dir, pid => pid == Environment.ProcessId);
        Check("F-04: only a dead process's copy is offered for recovery", orphans.Count == 1 && orphans[0] == dead);
        AutosaveService.DeleteOwn(dir, Environment.ProcessId);
        Check("F-04: a normal exit removes this process's copies only", !File.Exists(mine) && File.Exists(dead));
        AutosaveService.Delete(dead);
        Check("F-04: a saved song's copy is deleted", !File.Exists(dead));
    }

    // S-04: recovery copies are written without validation (errors listed beside them), and do not clear the unsaved flag.
    private static void InvalidProjectStillGetsRecoveryCopy(string dir)
    {
        var project = TabForge.Presets.TemplateFactory.Blank();
        project.Tempo = 1;   // outside 20..400: fails ProjectValidator
        project.IsDirty = true;
        var path = Path.Combine(dir, "Broken-20260929.tforge");
        var errors = App.WriteRecoveryCopy(project, path);
        var sidecar = Path.ChangeExtension(path, ".errors.txt");
        var tempoWritten = false;
        if (File.Exists(path))
        {
            using var json = System.Text.Json.JsonDocument.Parse(ProjectService.ReadJsonText(path));
            tempoWritten = json.RootElement.TryGetProperty(nameof(project.Tempo), out var tempo) && tempo.GetDouble() == 1;
        }
        Check("S-04: an invalid project still gets its raw recovery copy, with the validation error in a .errors.txt beside it",
            errors is not null && tempoWritten && File.Exists(sidecar) && File.ReadAllText(sidecar, Encoding.UTF8).Contains("tempo", StringComparison.OrdinalIgnoreCase),
            errors ?? "no validation error reported");
        Check("S-04: writing a recovery copy leaves the song marked unsaved", project.IsDirty);

        var valid = TabForge.Presets.TemplateFactory.Blank();
        var validPath = Path.Combine(dir, "Fine-20260929.tforge");
        var validErrors = App.WriteRecoveryCopy(valid, validPath);
        Check("S-04: a valid project's recovery copy loads back and has no error list",
            validErrors is null && !File.Exists(Path.ChangeExtension(validPath, ".errors.txt")) && ProjectService.Load(validPath).Tracks.Count == valid.Tracks.Count,
            validErrors);
    }
}
