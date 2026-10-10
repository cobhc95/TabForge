using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabForge.Audio.Contracts;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestSecurityInputBoundaries()
    {
        CheckEmbeddedGpProjectBomb();
        CheckPluginTrustBoundary();
        CheckDetachedVisualCoordinates();
        var root = Path.Combine(Path.GetTempPath(), "TabForge-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var oversizedProject = Path.Combine(root, "oversized.tforge");
            using (var file = new FileStream(oversizedProject, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(InputLimits.MaxTforgeFileBytes + 1);
            Check("oversized .tforge input is rejected before JSON allocation",
                ThrowsInvalidData(() => ProjectService.Load(oversizedProject), out var projectError) &&
                projectError.Contains("128 MiB", StringComparison.Ordinal));

            var nestedProject = Path.Combine(root, "nested.tforge");
            var nestedJson = "{\"Title\":\"Nested\",\"Unknown\":" +
                             new string('[', InputLimits.MaxJsonDepth + 8) + "0" +
                             new string(']', InputLimits.MaxJsonDepth + 8) + "}";
            File.WriteAllText(nestedProject, nestedJson);
            Check("deeply nested project JSON is rejected", ThrowsInvalidData(() => ProjectService.Load(nestedProject), out _));

            var malformedProject = Path.Combine(root, "malformed.tforge");
            File.WriteAllText(malformedProject, "{\"Title\": \"unfinished\"");
            Check("malformed project JSON is rejected", ThrowsInvalidData(() => ProjectService.Load(malformedProject), out _));
            Check("malformed clipboard project JSON is rejected", ThrowsInvalidData(
                () => ProjectService.Restore("{\"Tracks\":[{"), out _));
            Check("clipboard project JSON passes the bounded shape preflight", ThrowsInvalidData(
                () => ProjectService.Restore(nestedJson), out _));

            var unicodeProject = Path.Combine(root, "曲-name-ñ.tforge");
            ProjectService.Save(unicodeProject, new SongProject { Title = "曲 ñ" });
            Check("validated file paths preserve Unicode score names", ProjectService.Load(unicodeProject).Title == "曲 ñ");
            Check("project save rejects a mismatched extension", ThrowsInvalidData(
                () => ProjectService.Save(Path.Combine(root, "not-a-project.mid"), new SongProject()), out _));
            var atomicTarget = Path.Combine(root, "atomic-output.txt");
            File.WriteAllText(atomicTarget, "previous complete contents");
            try
            {
                FilePathPolicy.WriteAtomically(atomicTarget, stream =>
                {
                    stream.Write("partial"u8);
                    throw new IOException("simulated interruption");
                });
            }
            catch (IOException) { }
            Check("interrupted atomic writes preserve the previous file", File.ReadAllText(atomicTarget) == "previous complete contents");
            Check("diagnostic output enforces its configured byte cap", ThrowsInvalidData(
                () => DiagnosticFileService.WriteText(Path.Combine(root, "small.log"), "12345", 4), out _));
            var cappedLog = Path.Combine(root, "capped-layout.log");
            DiagnosticFileService.AppendCappedLine(cappedLog, "first-line", 16);
            DiagnosticFileService.AppendCappedLine(cappedLog, "second-line", 16);
            Check("opt-in append logs remain capped by replacing old content", new FileInfo(cappedLog).Length <= 16 &&
                File.ReadAllText(cappedLog).Contains("second-line", StringComparison.Ordinal));

            var absurdProject = new SongProject
            {
                Tracks = Enumerable.Range(0, InputLimits.MaxTracks + 1).Select(_ => new TrackModel()).ToList()
            };
            Check("absurd project track counts are rejected", ThrowsInvalidData(() => ProjectValidator.Validate(absurdProject), out _));

            var invalidSignatureProject = new SongProject();
            var invalidSignatureTrack = new TrackModel();
            invalidSignatureTrack.Measures.Add(new MeasureModel { TimeSigDenom = 3 });
            invalidSignatureProject.Tracks.Add(invalidSignatureTrack);
            Check("unsupported project time signatures are rejected", ThrowsInvalidData(() => ProjectValidator.Validate(invalidSignatureProject), out _));

            var invalidIndexProject = new SongProject();
            var invalidIndexTrack = new TrackModel();
            invalidIndexTrack.Measures.Add(new MeasureModel());
            invalidIndexTrack.Measures[0].Cells[0].Notes.Add(new TabNote
            {
                StringIndex = invalidIndexTrack.StringTunings.Count,
                Fret = 1,
                MidiValue = 61
            });
            invalidIndexProject.Tracks.Add(invalidIndexTrack);
            Check("out-of-range note string indexes are rejected", ThrowsInvalidData(() => ProjectValidator.Validate(invalidIndexProject), out _));

            var invalidMarkerProject = new SongProject();
            var invalidMarkerTrack = new TrackModel();
            invalidMarkerTrack.Measures.Add(new MeasureModel());
            invalidMarkerProject.Tracks.Add(invalidMarkerTrack);
            invalidMarkerProject.Markers.Add(new MarkerModel { MeasureIndex = 1 });
            Check("section markers outside the project are rejected", ThrowsInvalidData(() => ProjectValidator.Validate(invalidMarkerProject), out _));

            var excessiveMeasures = Path.Combine(root, "excessive-measures.tforge");
            File.WriteAllText(excessiveMeasures, "{\"Tracks\":[{\"Measures\":[" +
                string.Join(',', Enumerable.Repeat("{}", InputLimits.MaxMeasuresPerTrack + 1)) + "]}]}");
            Check("excessive project measures are rejected by the bounded JSON preflight",
                ThrowsInvalidData(() => ProjectService.Load(excessiveMeasures), out var measureError) &&
                measureError.Contains("measures", StringComparison.OrdinalIgnoreCase));

            var excessiveCells = Path.Combine(root, "excessive-cells.tforge");
            File.WriteAllText(excessiveCells, "{\"Tracks\":[{\"Measures\":[{\"Cells\":[" +
                string.Join(',', Enumerable.Repeat("{}", InputLimits.MaxCellsPerMeasure + 1)) + "]}]}]}");
            Check("excessive beats in one measure are rejected by the bounded JSON preflight",
                ThrowsInvalidData(() => ProjectService.Load(excessiveCells), out var cellError) &&
                cellError.Contains("Cells", StringComparison.OrdinalIgnoreCase));

            var excessiveNotes = Path.Combine(root, "excessive-notes.tforge");
            File.WriteAllText(excessiveNotes, "{\"Tracks\":[{\"Measures\":[{\"Cells\":[{\"Notes\":[" +
                string.Join(',', Enumerable.Repeat("{}", InputLimits.MaxNotesPerCell + 1)) + "]}]}]}]}");
            Check("excessive notes in one beat are rejected by the bounded JSON preflight",
                ThrowsInvalidData(() => ProjectService.Load(excessiveNotes), out var noteError) &&
                noteError.Contains("Notes", StringComparison.OrdinalIgnoreCase));

            var truncatedGp = Path.Combine(root, "truncated.gp5");
            File.WriteAllBytes(truncatedGp, new byte[] { 0x47, 0x50, 0x35, 0x00 });
            var gpRejected = ThrowsInvalidData(() => GuitarProImporter.Import(truncatedGp), out var gpError);
            Check("truncated Guitar Pro input fails with a concise import error",
                gpRejected && gpError.Length <= 160 && !gpError.Contains("Stack", StringComparison.OrdinalIgnoreCase));

            var oversizedGp = Path.Combine(root, "oversized.gp5");
            using (var file = new FileStream(oversizedGp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(InputLimits.MaxGuitarProFileBytes + 1);
            Check("oversized Guitar Pro input is rejected before allocating the import buffer",
                ThrowsInvalidData(() => GuitarProImporter.Import(oversizedGp), out var gpSizeError) &&
                gpSizeError.Contains("128 MiB", StringComparison.Ordinal));

            var oversizedSettings = Path.Combine(root, "oversized-settings.json");
            using (var file = new FileStream(oversizedSettings, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(InputLimits.MaxSettingsJsonBytes + 1);
            Check("oversized settings import is rejected before deserialization",
                ThrowsInvalidData(() => SettingsFileService.Load(oversizedSettings), out var settingsError) &&
                settingsError.Contains("2 MiB", StringComparison.Ordinal));

            var extremeSettings = Path.Combine(root, "extreme-settings.json");
            File.WriteAllText(extremeSettings,
                "{\"WindowWidth\":999999,\"WindowHeight\":-10," +
                "\"Tabs\":{\"MaxTabWidth\":9999,\"Style\":\"unknown\"}," +
                "\"Follow\":{\"MaxFps\":9999,\"Mode\":\"unknown\",\"PlayheadThickness\":9999}," +
                "\"Audio\":{\"Speed\":999,\"PreviewLengthMs\":999999}," +
                "\"Hotkeys\":{\"Bindings\":{\"File.Save\":\"Ctrl+Q\",\"File.Open\":\"Ctrl+Q\",\"NotAnAction\":\"Ctrl+W\"}}}");
            var normalized = SettingsFileService.Load(extremeSettings);
            Check("imported settings clamp window, control, timer, and audio ranges; validate enums and hotkeys",
                normalized.WindowWidth == 7_680 && normalized.WindowHeight == 480 &&
                normalized.Tabs.MaxTabWidth == 600 && normalized.Follow.MaxFps == 240 &&
                normalized.Follow.Mode == FollowModes.Smooth && normalized.Follow.PlayheadThickness == 5 &&
                normalized.Audio.Speed == 2 && normalized.Audio.PreviewLengthMs == 1_200 &&
                normalized.Tabs.Style == TabStyles.Rounded && normalized.Hotkeys.Bindings.Count == 1 &&
                normalized.Hotkeys.Bindings.Keys.All(key => HotkeyCatalog.ById(key) is not null));

            var savedSettings = Path.Combine(root, "settings.json");
            SettingsFileService.SaveAtomic(savedSettings, new AppSettings { WindowWidth = 1_600 });
            var previousSettings = File.ReadAllBytes(savedSettings);
            var corruptImport = Path.Combine(root, "corrupt-settings.json");
            File.WriteAllText(corruptImport, "{not json");
            var corruptRejected = ThrowsInvalidData(() => SettingsFileService.Load(corruptImport), out _);
            Check("corrupt settings import leaves the existing valid settings file intact",
                corruptRejected && File.ReadAllBytes(savedSettings).SequenceEqual(previousSettings));

            var replacementBlocked = false;
            using (var blocker = new FileStream(savedSettings, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { SettingsFileService.SaveAtomic(savedSettings, new AppSettings { WindowWidth = 2_000 }); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { replacementBlocked = true; }
            }
            Check("failed atomic settings replacement leaves the previous valid file intact",
                replacementBlocked && File.ReadAllBytes(savedSettings).SequenceEqual(previousSettings));

            var cyclicRoot = new DockNodeState { Kind = "split", Orientation = "Horizontal", Ratio = 0.5 };
            cyclicRoot.First = cyclicRoot;
            cyclicRoot.Second = DockWorkspace.EditorNode();
            try
            {
                SettingsFileService.SaveAtomic(savedSettings, new AppSettings
                {
                    Workspace = new DockWorkspaceState { Root = cyclicRoot }
                });
            }
            catch (JsonException) { }
            catch (InvalidDataException) { }
            Check("failed atomic settings serialization preserves the previous file",
                File.ReadAllBytes(savedSettings).SequenceEqual(previousSettings));

            CheckVstReparseLoop(root);
        }
        catch (Exception ex)
        {
            Check("security input regression tests complete", false, ex.GetBaseException().Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Allow again: takes exactly that path off the quarantine list (any case); trust is untouched, so an untrusted path stays blocked.
    private static void TestQuarantineAllowAgain()
    {
        var settings = new PluginSettings();
        var untrusted = Path.Combine(Path.GetTempPath(), "tabforge-quar-" + Guid.NewGuid().ToString("N"), "crashy.dll");
        var trusted = Path.Combine(Path.GetTempPath(), "tabforge-quar-" + Guid.NewGuid().ToString("N"), "okay.dll");
        PluginTrust.Approve(settings, trusted);
        var slots = new List<PluginSlot> { new() { Name = "u", Path = untrusted, Format = "VST2" }, new() { Name = "t", Path = trusted, Format = "VST2" } };
        settings.Quarantined = new List<string> { untrusted, trusted, @"C:\other\keep.dll" };
        var specs = PluginTrust.BuildSpecs(slots, settings.Quarantined, settings);
        Check("quarantined plug-ins are sent as Skip", specs[0].Skip && specs[1].Skip && !specs[1].Untrusted);
        Check("the quarantine list reports a path in any case", PluginQuarantine.Contains(settings.Quarantined, trusted.ToUpperInvariant()));
        var removed = PluginQuarantine.AllowAgain(settings.Quarantined, trusted.ToUpperInvariant());
        Check("Allow again removes exactly that path (any case)", removed == 1 && settings.Quarantined.Count == 2
            && settings.Quarantined.Contains(untrusted) && settings.Quarantined.Contains(@"C:\other\keep.dll"));
        specs = PluginTrust.BuildSpecs(slots, settings.Quarantined, settings);
        Check("a trusted plug-in is no longer Skip after Allow again", !specs[1].Skip);
        PluginQuarantine.AllowAgain(settings.Quarantined, untrusted);
        specs = PluginTrust.BuildSpecs(slots, settings.Quarantined, settings);
        Check("an untrusted plug-in stays Skip/Untrusted after Allow again (trust is not granted)", specs[0] is { Skip: true, Untrusted: true });
        // Preferences applies a snapshot: the live crash list wins (a crash while Preferences was open is kept; Allow again sticks).
        var live = new PluginSettings { Quarantined = new List<string> { "x.dll" } };
        var snapshot = new PluginSettings { Quarantined = new List<string> { "x.dll" } };
        live.Quarantined.Add("y.dll");   // the crash, while Preferences is open
        PluginQuarantine.KeepLive(snapshot, live);
        Check("applying a Preferences snapshot keeps a crash that happened meanwhile ([x] + y = [x, y])",
            snapshot.Quarantined.SequenceEqual(new[] { "x.dll", "y.dll" }));
        PluginQuarantine.AllowAgain(live.Quarantined, "x.dll");   // Allow again through the hook edits the live list
        var snapshot2 = new PluginSettings { Quarantined = new List<string> { "x.dll" } };   // the stale copy still holds x
        PluginQuarantine.KeepLive(snapshot2, live);
        Check("Allow again sticks after a stale snapshot is applied ([y])", snapshot2.Quarantined.SequenceEqual(new[] { "y.dll" }));
        Check("Allow again on a path not in the list changes nothing", PluginQuarantine.AllowAgain(settings.Quarantined, "x.dll") == 0 && settings.Quarantined.Count == 1);
    }

    // Project-named plug-ins outside trusted locations must reach the engine as Skip until approved; UNC is never auto-trusted.
    private static void CheckPluginTrustBoundary()
    {
        var settings = new PluginSettings();
        var outside = Path.Combine(Path.GetTempPath(), "tabforge-untrusted-" + Guid.NewGuid().ToString("N"), "evil.dll");
        var unc = @"\\server\share\vst\evil.dll";
        var slots = new List<PluginSlot> { new() { Name = "evil", Path = outside, Format = "VST2" }, new() { Name = "net", Path = unc, Format = "VST2" }, new() { Name = "none" } };
        var none = new List<string>();
        var specs = PluginTrust.BuildSpecs(slots, none, settings);
        Check("a project plug-in outside trusted locations is sent as Skip", specs[0].Skip && specs[1].Skip && !specs[2].Skip);
        PluginTrust.Approve(settings, outside);
        specs = PluginTrust.BuildSpecs(slots, none, settings);
        Check("an approved plug-in path is no longer skipped (case-insensitive)", !specs[0].Skip && PluginTrust.IsTrusted(outside.ToUpperInvariant(), settings) && specs[1].Skip);
        var scanned = new PluginSettings { ScanCache = new() { new KnownPlugin { Path = unc } } };
        Check("a scanned UNC path is not auto-trusted", !PluginTrust.IsTrusted(unc, scanned));
        PluginTrust.Approve(scanned, unc);
        Check("a UNC path is trusted once explicitly approved", PluginTrust.IsTrusted(unc, scanned));
        CheckPluginFingerprintTrust();
        CheckPluginBinaryIdentity();
        CheckPluginIdentifyTrustGate();
    }

    // Item 4 (source review): approval covers the exact binary; a same-size, same-time replacement is caught at load; a changed
    // remote binary does not inherit its path's approval; failures say what to do.
    private static void CheckPluginBinaryIdentity()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tabforge-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dll = Path.Combine(dir, "trusted.dll");
            var original = Enumerable.Range(0, 4096).Select(i => (byte)(i * 3)).ToArray();
            File.WriteAllBytes(dll, original);
            var settings = new PluginSettings();
            PluginTrust.Approve(settings, dll);
            var slots = new List<PluginSlot> { new() { Name = "t", Path = dll, Format = "VST2" } };
            var spec = PluginTrust.BuildSpecs(slots, new List<string>(), settings)[0];
            Check("a trusted plug-in's spec carries the approved SHA-256 for the engine to check at load", !spec.Skip && spec.ExpectedSha256.Length == 64);
            using (var hold = PluginIdentity.Hold(spec))
            {
                Check("an unchanged binary passes the load-time check and is held open (writers denied)", hold is not null
                    && ThrowsIo(() => { using var w = new FileStream(dll, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }));
            }
            Check("the hold is released after the load (the file can be written again)", !ThrowsIo(() => { using var w = new FileStream(dll, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); }));

            // Same length, same last-write time, different content: the routine (size/time) check cannot see it, the load check must.
            var time = File.GetLastWriteTimeUtc(dll);
            var swapped = (byte[])original.Clone(); swapped[2000] ^= 0xFF;
            File.WriteAllBytes(dll, swapped);
            File.SetLastWriteTimeUtc(dll, time);
            var hashesBefore = PluginTrust.HashCount;
            Check("routine checks stay cheap: a same-size, same-time swap is not re-hashed by the UI check", PluginTrust.IsTrusted(dll, settings) && PluginTrust.HashCount == hashesBefore);
            var problem = "";
            try { PluginIdentity.Hold(spec)?.Dispose(); } catch (PluginChangedException ex) { problem = ex.Message; }
            Check("replacing a trusted DLL while keeping its length and timestamp is DETECTED at load", problem.Contains("changed since you approved it"), problem);
            Check("the failure message is actionable (says it was not loaded and where to approve)", problem.Contains("was not loaded") && problem.Contains("Review plug-ins"), problem);
            PluginTrust.MarkChanged(dll);
            var verdict = PluginTrust.Check(dll, settings);
            Check("a plug-in blocked at load is untrusted (changed) and is sent as Skip", verdict is { Trusted: false, Reason: PluginTrust.ReasonChanged }
                && PluginTrust.BuildSpecs(slots, new List<string>(), settings)[0] is { Skip: true, Untrusted: true });
            Check("a failed check does not reset the approval baseline", settings.TrustRecords.First().Sha256 == spec.ExpectedSha256);
            PluginTrust.Approve(settings, dll);
            Check("approving again takes the new file as the baseline and lifts the block", PluginTrust.IsTrusted(dll, settings)
                && PluginTrust.BuildSpecs(slots, new List<string>(), settings)[0].ExpectedSha256 != spec.ExpectedSha256);

            // A rescan does not silently re-baseline a changed, unsigned file.
            var rescanned = new PluginSettings { ScanCache = new() { new KnownPlugin { Path = dll } } };
            PluginTrust.Approve(rescanned, dll);
            var approvedHash = rescanned.TrustRecords.First(r => string.Equals(r.Path, PluginTrust.Normalize(dll), StringComparison.OrdinalIgnoreCase)).Sha256;
            File.WriteAllBytes(dll, original);
            PluginTrust.RecordScan(new List<VstPluginInfo> { new("t", dll, "VST2") }, CancellationToken.None, null);
            var afterScan = PluginTrust.Check(dll, rescanned);
            Check("a rescan leaves a changed, unsigned file's baseline alone (it stays untrusted until approved)",
                rescanned.TrustRecords.First(r => string.Equals(r.Path, PluginTrust.Normalize(dll), StringComparison.OrdinalIgnoreCase)).Sha256 == approvedHash
                && afterScan.Reason == PluginTrust.ReasonChanged, afterScan.Reason);

            // Missing at approval: whatever appears there later counts as changed at load too.
            var missing = Path.Combine(dir, "later.dll");
            var lateSettings = new PluginSettings();
            PluginTrust.Approve(lateSettings, missing);
            var lateSpec = PluginTrust.BuildSpecs(new List<PluginSlot> { new() { Name = "m", Path = missing, Format = "VST2" } }, new List<string>(), lateSettings)[0];
            File.WriteAllBytes(missing, new byte[] { 1, 2, 3 });
            var lateProblem = "";
            try { PluginIdentity.Hold(lateSpec)?.Dispose(); } catch (PluginChangedException ex) { lateProblem = ex.Message; }
            Check("a file that appears where none existed at approval is blocked at load", lateProblem.Contains("did not exist when you approved"), lateProblem);

            // Unreadable (open for writing elsewhere): a clear, actionable refusal, never a silent pass.
            using (var locked = new FileStream(dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var lockedProblem = "";
                try { PluginIdentity.Hold(spec with { ExpectedSha256 = approvedHash })?.Dispose(); } catch (PluginChangedException ex) { lockedProblem = ex.Message; }
                Check("an unreadable binary is refused with an actionable message", lockedProblem.Contains("could not be opened") && lockedProblem.Contains("not loaded"), lockedProblem);
            }

            // The OS loader can still map a file we hold open (writers denied): load a copy of a system DLL while it is held.
            var copy = Path.Combine(dir, "loadable.dll");
            File.Copy(Path.Combine(Environment.SystemDirectory, "version.dll"), copy);
            var copySettings = new PluginSettings();
            PluginTrust.Approve(copySettings, copy);
            var copySpec = PluginTrust.BuildSpecs(new List<PluginSlot> { new() { Name = "c", Path = copy, Format = "VST2" } }, new List<string>(), copySettings)[0];
            using (var hold = PluginIdentity.Hold(copySpec))
            {
                var loaded = System.Runtime.InteropServices.NativeLibrary.TryLoad(copy, out var handle);
                if (loaded) System.Runtime.InteropServices.NativeLibrary.Free(handle);
                Check("a DLL held for verification can still be loaded by the OS loader", hold is not null && loaded);
            }

            // Network / removable locations: approval records the hash and the engine checks it, so a changed file does not inherit the path's approval.
            var admin = @"\\localhost\" + dir[0] + "$" + dir[2..];
            var remote = Path.Combine(admin, "remote.dll");
            var remoteSettings = new PluginSettings();
            if (!Directory.Exists(admin))
                Skip("network plug-in changed-binary test", "the administrative share " + admin + " is not reachable here");
            else
            {
                File.WriteAllBytes(Path.Combine(dir, "remote.dll"), original);
                Check("a network path is remote and needs approval", PluginTrust.IsRemote(PluginTrust.Normalize(remote)) && !PluginTrust.IsTrusted(remote, remoteSettings));
                PluginTrust.Approve(remoteSettings, remote);
                var rslots = new List<PluginSlot> { new() { Name = "r", Path = remote, Format = "VST2" } };
                var rspec = PluginTrust.BuildSpecs(rslots, new List<string>(), remoteSettings)[0];
                Check("an approved network plug-in carries the hash recorded at approval", !rspec.Skip && rspec.ExpectedSha256.Length == 64);
                File.WriteAllBytes(Path.Combine(dir, "remote.dll"), swapped);
                var remoteProblem = "";
                try { PluginIdentity.Hold(rspec)?.Dispose(); } catch (PluginChangedException ex) { remoteProblem = ex.Message; }
                Check("a changed network binary does not inherit its path's approval", remoteProblem.Contains("changed since you approved it"), remoteProblem);
            }
        }
        catch (Exception ex) { Check("plug-in binary identity tests complete", false, ex.GetBaseException().Message); }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Identifying a plug-in (role / vendor) executes it, so an unapproved or changed file is never run to find out; an
    // approved file is probed with its approved hash, which the probe checks (fail closed) before loading.
    private static void CheckPluginIdentifyTrustGate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tabforge-identify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var launches = 0;
        PluginCatalog.ProbeOverride = (_, hash, _) => { launches++; return hash.Length == 64 ? ("Instrument", "Vendor", "Name") : null; };
        try
        {
            var dll = Path.Combine(dir, "unknown.dll");
            var original = Enumerable.Range(0, 4096).Select(i => (byte)(i * 7)).ToArray();
            File.WriteAllBytes(dll, original);
            var settings = new PluginSettings();
            var described = PluginCatalog.Describe(dll, settings);
            Check("identify never executes an unapproved plug-in (shown as unknown, nothing remembered)",
                launches == 0 && described.Role.Length == 0 && settings.Probed.Count == 0);

            var creates = 0;
            Func<string, TabForge.AudioEngine.Plugins.IPluginInstance> seam = _ => { creates++; throw new InvalidOperationException("seam reached"); };
            Check("the probe refuses a file with no approved hash and loads nothing (fail closed)",
                TabForge.AudioEngine.Isolation.PluginInfoProbe.Run(new[] { "--plugin-info", dll }, seam) == 5 && creates == 0);

            PluginTrust.Approve(settings, dll);
            var approved = PluginTrust.ProbeHash(dll, settings);
            var changed = (byte[])original.Clone(); changed[100] ^= 0xFF;
            File.WriteAllBytes(dll, changed);
            var rcChanged = TabForge.AudioEngine.Isolation.PluginInfoProbe.Run(new[] { "--plugin-info", dll, approved }, seam);
            Check("a file changed after approval is refused by the probe and never loaded", rcChanged == 5 && creates == 0);
            described = PluginCatalog.Describe(dll, settings);
            Check("identify does not run a changed-after-approval file either", launches == 0 && described.Role.Length == 0 && settings.Probed.Count == 0);

            File.WriteAllBytes(dll, original);
            var rcTrusted = TabForge.AudioEngine.Isolation.PluginInfoProbe.Run(new[] { "--plugin-info", dll, approved }, seam);
            Check("the probe gets past the identity gate for the approved file (load attempted exactly once)", rcTrusted == 1 && creates == 1);
            described = PluginCatalog.Describe(dll, settings);
            Check("a trusted plug-in still identifies, with the approved hash passed to the probe",
                launches == 1 && described.Role == "Instrument" && settings.Probed.Count == 1);
        }
        catch (Exception ex) { Check("plug-in identify trust gate tests complete", false, ex.GetBaseException().Message); }
        finally
        {
            PluginCatalog.ProbeOverride = null;
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool ThrowsIo(Action action)
    {
        try { action(); return false; }
        catch (IOException) { return true; }
    }

    // S-01: approval records (size, last-write, SHA-256, signer); a changed file is untrusted again; user-writable folders are not
    // folder-trusted; checks are cached by (path, size, last-write).
    private static void CheckPluginFingerprintTrust()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tabforge-trust-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dll = Path.Combine(dir, "approved.dll");
            File.WriteAllBytes(dll, Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray());
            var settings = new PluginSettings();
            PluginTrust.Approve(settings, dll);
            var record = settings.TrustRecords.FirstOrDefault(r => string.Equals(r.Path, PluginTrust.Normalize(dll), StringComparison.OrdinalIgnoreCase));
            Check("approval records the plug-in's size, time and SHA-256", record is { Sha256.Length: 64, Size: 4096 } && record.LastWriteUtcTicks > 0);
            Check("an approved, untouched plug-in is trusted", PluginTrust.Check(dll, settings) is { Trusted: true, Reason: "" });

            var hashes = PluginTrust.HashCount;
            var slots = new List<PluginSlot> { new() { Name = "a", Path = dll, Format = "VST2" }, new() { Name = "b", Path = dll.ToUpperInvariant(), Format = "VST2" } };
            for (var i = 0; i < 5; i++) PluginTrust.BuildSpecs(slots, new List<string>(), settings);
            Check("repeated trust checks of an unchanged plug-in do not re-hash it", PluginTrust.HashCount == hashes);

            File.SetLastWriteTimeUtc(dll, File.GetLastWriteTimeUtc(dll).AddMinutes(1));
            Check("a touched plug-in with the same content stays trusted", PluginTrust.IsTrusted(dll, settings) && PluginTrust.HashCount == hashes + 1);

            File.WriteAllBytes(dll, Enumerable.Range(0, 4100).Select(i => (byte)(i * 7)).ToArray());
            var changed = PluginTrust.Check(dll, settings);
            Check("a modified DLL at an approved path is untrusted: \"changed since you approved it\"", !changed.Trusted && changed.Reason == PluginTrust.ReasonChanged, changed.Reason);
            hashes = PluginTrust.HashCount;
            var specs = PluginTrust.BuildSpecs(slots, new List<string>(), settings);
            Check("a changed plug-in is sent as Skip and the change is hashed once", specs.All(s => s.Skip && s.Untrusted) && PluginTrust.HashCount == hashes);
            PluginTrust.Approve(settings, dll);
            Check("approving the changed plug-in again trusts it", PluginTrust.IsTrusted(dll, settings));

            var dropped = Path.Combine(dir, "dropped.dll");
            File.WriteAllBytes(dropped, new byte[] { 1, 2, 3 });
            var folderUser = new PluginSettings { Folders = new() { dir } };
            var verdict = PluginTrust.Check(dropped, folderUser);
            Check("a DLL in a user-writable scanned folder is not folder-trusted", !verdict.Trusted && verdict.Reason == PluginTrust.ReasonUnknown, verdict.Reason);
            folderUser.ScanCache = new() { new KnownPlugin { Path = dropped } };
            Check("a DLL in a user-writable folder is trusted once it is in the scan record", PluginTrust.IsTrusted(dropped, folderUser));
            File.WriteAllBytes(dropped, new byte[] { 9, 9, 9, 9 });
            Check("a scanned DLL that changed after the scan is untrusted", PluginTrust.Check(dropped, folderUser).Reason == PluginTrust.ReasonChanged);

            var vst3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3");
            var inProtected = Path.Combine(vst3, "tabforge-selftest-" + Guid.NewGuid().ToString("N") + ".vst3");
            Check("a plug-in under Common Files\\VST3 is folder-trusted when that folder is scanned",
                PluginTrust.IsProtectedLocation(PluginTrust.Normalize(inProtected)) && PluginTrust.IsTrusted(inProtected, new PluginSettings { Folders = new() { vst3 } })
                && !PluginTrust.IsTrusted(inProtected, new PluginSettings()));
            Check("the temp folder is not a protected location", !PluginTrust.IsProtectedLocation(PluginTrust.Normalize(dropped)));
        }
        catch (Exception ex) { Check("plug-in fingerprint trust tests complete", false, ex.GetBaseException().Message); }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CheckVstReparseLoop(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "vst-root");
        var container = Path.Combine(root, "Container");
        var plugin = Path.Combine(root, "Example.vst3");
        var junction = Path.Combine(container, "back-to-root");
        Directory.CreateDirectory(container);
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "metadata.txt"), "not executable by discovery");

        var linked = TryCreateJunction(junction, root);
        if (!linked)
        {
            try { Directory.CreateSymbolicLink(junction, root); linked = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
        }

        if (!linked)
        {
            Check("VST scan reparse-loop fixture can be created", false, "junction creation was unavailable");
            return;
        }

        try
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var wasCancelled = false;
                try { _ = VstScannerService.Scan(new[] { root }, cancellation.Token); }
                catch (OperationCanceledException) { wasCancelled = true; }
                Check("VST traversal observes cancellation", wasCancelled);
            }
            var plugins = VstScannerService.Scan(new[] { root });
            Check("VST discovery skips reparse-point loops and deduplicates paths",
                plugins.Count == 1 && string.Equals(Path.GetFullPath(plugins[0].Path), Path.GetFullPath(plugin), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(junction); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            var commandProcessor = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = commandProcessor,
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return process is not null && process.WaitForExit(5_000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool ThrowsInvalidData(Action action, out string message)
    {
        try
        {
            action();
            message = "";
            return false;
        }
        catch (InvalidDataException exception)
        {
            message = exception.Message;
            return true;
        }
    }

    // A crafted .gp whose embedded TabForge entry inflates past the project limit must be ignored
    // (falling back to the normal Guitar Pro import) instead of inflating into memory.
    private static void CheckEmbeddedGpProjectBomb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tabforge-bomb-{Guid.NewGuid():N}.gp");
        try
        {
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            using (var entry = zip.CreateEntry(GuitarProExporter.EmbeddedProjectEntry, System.IO.Compression.CompressionLevel.Optimal).Open())
            {
                var zeros = new byte[1024 * 1024];
                for (var i = 0; i <= InputLimits.MaxTforgeFileBytes / zeros.Length; i++) entry.Write(zeros, 0, zeros.Length);
            }
            var before = GC.GetTotalMemory(true);
            var result = GuitarProExporter.TryReadEmbedded(path);
            Check("an oversized embedded project in a .gp is rejected without inflating it",
                result is null && new FileInfo(path).Length < InputLimits.MaxGuitarProFileBytes,
                $"file {new FileInfo(path).Length:N0} B, memory delta {GC.GetTotalMemory(false) - before:N0} B");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Drag-and-drop crash: a tab/panel detached mid-drag made PointToScreen throw. The shared helper must
    // answer "not on screen" instead of throwing, so drags skip such elements.
    private static void CheckRenderNaming()
    {
        var now = new DateTime(2026, 9, 29, 14, 5, 9);
        var name = TabForge.Services.RenderNaming.Expand("$project - $tracknumber $track $date $time $bpm $x", "Sample Song", "Lead/Gtr", 3, now, 119.6);
        Check("render file names expand wildcards and drop illegal characters", name == "Sample Song - 03 Lead_Gtr 2026-09-29 14-05-09 120 $x");
        CheckRenderOwnership();
        CheckChainReadiness();
    }

    private static void CheckRenderOwnership()
    {
        var tempName = TabForge.Rendering.RenderStaging.TempName("Song.mp3", "ab12");
        Check("render staging names are job-tagged, hidden-style and keep the extension",
            tempName == ".Song.tfrender-ab12.tmp.mp3" && TabForge.Rendering.RenderStaging.IsOwnedTemp(@"C:\x\" + tempName, "ab12")
            && !TabForge.Rendering.RenderStaging.IsOwnedTemp(@"C:\x\" + tempName, "cd34") && !TabForge.Rendering.RenderStaging.IsOwnedTemp(@"C:\x\Song.mp3", "ab12"));
        var dir = Path.Combine(Path.GetTempPath(), "tf-render-own-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var final = Path.Combine(dir, "a.wav");
            var temp = TabForge.Rendering.RenderStaging.TempPath(final, "j1");
            File.WriteAllText(temp, "mine");
            File.WriteAllText(final, "someone else's");   // the target appeared while the job ran
            var published = TabForge.Rendering.RenderStaging.Publish(temp, final);
            Check("publishing never overwrites a file that appeared meanwhile (next free name, temp consumed)",
                File.ReadAllText(final) == "someone else's" && Path.GetFileName(published) == "a (2).wav" && File.ReadAllText(published) == "mine" && !File.Exists(temp));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static void CheckChainReadiness()
    {
        var ready = new TabForge.Rendering.ChainReadiness(new[] { (0, 2), (1, 1) });
        ChainAck Ack(int slot, int gen, params PluginLoadStatus[] statuses)
            => new(slot, gen, statuses.Select((s, i) => new PluginLoadResult(i, s, @"C:\vst\Plug" + i + ".dll")).ToList());
        var empty = !ready.IsReady && ready.MissingSlots.SequenceEqual(new[] { 0, 1 });
        ready.Acknowledge(Ack(0, 1, PluginLoadStatus.Loaded));   // an older generation than required
        var stale = ready.MissingSlots.SequenceEqual(new[] { 0, 1 });
        ready.Acknowledge(Ack(1, 1, PluginLoadStatus.Loaded, PluginLoadStatus.Failed));
        var partial = ready.MissingSlots.SequenceEqual(new[] { 0 }) && !ready.IsReady && ready.Problems.Count == 1 && ready.Problems[0].Slot == 1;
        ready.Acknowledge(Ack(0, 3, PluginLoadStatus.SkippedQuarantined, PluginLoadStatus.BlockedUntrusted));   // newer satisfies older
        ready.Acknowledge(Ack(0, 2, PluginLoadStatus.Loaded));   // an out-of-order older ack must not win
        Check("render readiness: required slots need their generation; stale acks do not count; failed / skipped / blocked plug-ins are listed",
            empty && stale && partial && ready.IsReady && ready.MissingSlots.Count == 0 && ready.ReadySlots.Count == 2 && ready.Problems.Count == 3
            && ready.ProblemDescriptions.Count == 3, $"problems: {string.Join("; ", ready.ProblemDescriptions)}");
    }

    private static void CheckDetachedVisualCoordinates()
    {
        CheckRenderNaming();
        var detached = new System.Windows.Controls.Border { Width = 40, Height = 20 };
        var threw = false;
        bool converted = true, back = true;
        System.Windows.Rect rect = default;
        try
        {
            converted = TabForge.Shell.ScreenPoints.TryToScreen(detached, new System.Windows.Point(1, 1), out _);
            back = TabForge.Shell.ScreenPoints.TryFromScreen(detached, new System.Windows.Point(1, 1), out _);
            rect = TabForge.Shell.ScreenPoints.ScreenRect(detached);
        }
        catch (InvalidOperationException) { threw = true; }
        Check("coordinates of an element that is not on screen are refused, never thrown (drag crash)",
            !threw && !converted && !back && rect.IsEmpty);
    }
}
