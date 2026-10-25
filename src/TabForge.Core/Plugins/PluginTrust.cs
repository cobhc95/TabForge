using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: the trust boundary for native plug-in code (approvals, fingerprints, publisher-update policy, location trust).
// Does not own: the load-gate wiring (AudioEngineClient/PluginTrustLoadGate) or the approval UI (MediaAccess, approval windows).
// Tests: ProjectPluginAndClipLimitTests, ApprovalReviewWindowsTests, AudioSmokeTests.

/// <summary>
/// Result of a trust check; <see cref="Reason"/> says why a plug-in is not loaded (empty when trusted). <see cref="LocationTrusted"/> is
/// set when the trust comes from the folder alone (Program Files / Common Files): no per-file hash is checked at load.
/// </summary>
public readonly record struct TrustVerdict(bool Trusted, string Reason, bool LocationTrusted = false)
{
    /// <summary>Trusted by an approval or a fingerprint: the engine checks the hash at each load.</summary>
    public static readonly TrustVerdict Yes = new(true, "");
    /// <summary>Trusted by location, or nothing to load: no per-file hash.</summary>
    public static readonly TrustVerdict Location = new(true, "", true);
}

/// <summary>
/// Trust boundary for native plug-in code: a song (.tforge, embedded .gp, sidecar, rig preset, auto-load chain) names plug-in
/// paths, but nothing is loaded from a path the user has not trusted.
/// <para><b>What an approval covers.</b> Outside Program Files / Common Files an explicit approval covers <b>this binary</b>: its
/// SHA-256 (with size, last-write time and the signer, recorded in <see cref="PluginSettings.TrustRecords"/>), at that path. A scan
/// only records the fingerprint; the plug-in is listed as needing approval until the user approves it once. An approval is never a
/// blanket approval of the path, the folder or the vendor. An approved path always keeps its record (the record cap drops only scan-only records). A binary with different content is
/// "changed since you approved it" and is not loaded until the user approves it again, with one exception, the publisher-update
/// policy: the new file is accepted (and becomes the new baseline) when its Authenticode signature is valid (WinVerifyTrust,
/// no UI, no revocation lookup) and made with the same signing key as the approved one (SHA-256 of the certificate's public
/// key, not the subject text, which anybody can copy into a self-made certificate). A renewed certificate with a new key
/// needs a new approval. A failed check never rewrites the baseline; a rescan refreshes it only for unchanged content or the
/// same signing key.</para>
/// <para><b>Location trust</b> applies only under Program Files / Common Files on a local fixed drive (the default ACLs there
/// need administrator rights to write; a machine with loosened ACLs is not covered). It is decided on the file's final path
/// (junctions and symbolic links are resolved through the open handle), so a link inside Program Files that points to a
/// user-writable folder gets no location trust. Such files are not hashed.</para>
/// <para><b>Network (UNC) and removable/optical locations</b> always need an explicit approval, which records the hash when
/// the user approves. They are not read during routine checks (an unreachable share must not stall the UI); the content is
/// checked at each real load instead.</para>
/// <para><b>When content is verified.</b> Routine checks (trust bar, Sync) compare size and last-write time with the record
/// and hash only when either differs (cached per file). A replacement that keeps size and time is caught at load: the trust
/// gate sends the approved hash in <see cref="PluginSpec.ExpectedSha256"/> and the engine hashes the exact file before every
/// real load through a handle that denies writers and stays open until the plug-in is created
/// (<see cref="PluginIdentity.Hold"/>; never on an audio callback, never for an already-loaded instance). A mismatch there
/// blocks the plug-in ("blocked, changed since you approved it"), marks the path changed here, and is fixed only by a new
/// approval. Residual gap: a writer that already had the file open with write access before the load, or a driver-level
/// change, is not excluded; and once mapped, a plug-in is not re-checked until it is loaded again.</para>
/// This is a load gate against swapped or unexpected files, not an OS sandbox: a plug-in the user approves runs with the
/// user's rights, in isolated mode too.
/// </summary>
public sealed class PluginTrust
{
    public const string ReasonChanged = "changed since you approved it";
    public const string ReasonRemote = "network or removable location";
    public const string ReasonUnknown = "not from your scan or an approved location";
    public const string ReasonNeedsApproval = "found by a scan outside Program Files: needs your approval once (Review plug-ins)";
    public const string ReasonUnreadable = "could not be read to check it (in use or access denied): close what is using it, then Review again";
    public const string ReasonNoBaseline = "approved before file checks existed: approve it again to record it";
    private const int MaxRecords = 1024;

    private readonly ConcurrentDictionary<string, bool> RemoteRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ProtectedBases = new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
        }
        .Select(Environment.GetFolderPath).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => PluginPath.Normalize(p).TrimEnd('\\') + "\\")
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private readonly object Gate = new();
    private Index? _index;
    /// <summary>Binary path -> fingerprint at a (size, last-write): a file is hashed again only when either changes.</summary>
    private readonly ConcurrentDictionary<string, PluginFingerprint> Fingerprints = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>This session's scan results (plug-in path -> fingerprint), merged into each settings' records once per scan.</summary>
    private readonly ConcurrentDictionary<string, PluginFingerprint> ScanRecords = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<PluginSettings, StrongBox<int>> MergedScan = new();
    private int _scanGeneration;
    private int _hashCount;
    /// <summary>Paths whose file was found different from the approval when the engine went to load it (until approved again).</summary>
    private readonly ConcurrentDictionary<string, bool> ChangedAtLoad = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many files were hashed so far (self-test: the cache must avoid re-hashing).</summary>
    internal int HashCount => Volatile.Read(ref _hashCount);

    /// <summary>UNC / device paths and network, removable or optical drives: never trusted without approval.</summary>
    public bool IsRemote(string normalized)
    {
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root)) return true;
        return RemoteRoots.GetOrAdd(root, r =>
        {
            try { return new DriveInfo(r).DriveType is not DriveType.Fixed; }
            catch (Exception) { return true; } // Not logged: drive type probe, cached per root: remote is the safe default
        });
    }

    /// <summary>Under Program Files / Common Files (x64 or x86) on a local fixed drive: only administrators can write there.</summary>
    public bool IsProtectedLocation(string normalized) =>
        !IsRemote(normalized) && ProtectedBases.Any(b => normalized.StartsWith(b, StringComparison.OrdinalIgnoreCase));

    public bool IsTrusted(string path, PluginSettings settings, IPluginFileTrust files) => Check(path, settings, files).Trusted;

    public TrustVerdict Check(string path, PluginSettings settings, IPluginFileTrust files)
    {
        if (string.IsNullOrWhiteSpace(path)) return TrustVerdict.Location;   // nothing to load
        var full = PluginPath.Normalize(path);
        lock (Gate)
        {
            var index = IndexFor(settings);
            var remote = IsRemote(full);
            var isProtected = !remote && IsProtectedLocation(full) && ResolvesInsideProtected(full, files);
            if (!isProtected && ChangedAtLoad.ContainsKey(full)) return new(false, ReasonChanged);
            if (isProtected && settings.AllowInstalledPlugins) return TrustVerdict.Location;   // installed where only an administrator can write: no approval step
            if (index.Approved.Contains(full))
            {
                if (isProtected) return TrustVerdict.Location;
                // A network path is not read here (an unreachable share must not stall the UI): its approval's record is enough.
                if (remote) return index.Records.ContainsKey(full) ? TrustVerdict.Yes : new(false, ReasonNoBaseline);
                return Verify(full, index, settings, files);
            }
            if (remote) return new(false, ReasonRemote);
            if (isProtected)
                return index.Scanned.Contains(full) || index.ProtectedRoots.Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                    ? TrustVerdict.Location : new(false, ReasonUnknown);
            // A scan only records the baseline: the plug-in is trusted by the user's approval alone.
            return index.Scanned.Contains(full) ? new(false, ReasonNeedsApproval) : new(false, ReasonUnknown);
        }
    }

    /// <summary>The engine found the file at <paramref name="path"/> different from the approval when loading it: untrusted until <see cref="Approve"/>.</summary>
    public void MarkChanged(string path)
    {
        var full = PluginPath.Normalize(path);
        if (full.Length > 0 && full.Length <= InputLimits.MaxPathLength) ChangedAtLoad[full] = true;
    }

    /// <summary>The hash the engine must find in the file at load time; "" when the location is trusted without a per-file record.</summary>
    private string ExpectedHashFor(string path, PluginSettings settings, IPluginFileTrust files)
    {
        var full = PluginPath.Normalize(path);
        if (full.Length == 0) return "";
        lock (Gate)
        {
            if (!IsRemote(full) && IsProtectedLocation(full) && ResolvesInsideProtected(full, files)) return "";
            if (!IndexFor(settings).Records.TryGetValue(full, out var rec)) return "";
            return rec.Sha256.Length > 0 ? rec.Sha256 : PluginIdentity.ExpectMissing;
        }
    }

    /// <summary>
    /// The SHA-256 the identify probe must find (it executes the plug-in, so it is only ever run for a trusted one): the approved hash,
    /// or, for a plug-in trusted by location (Program Files, which has no per-file record), the file's hash now. "" when there is
    /// none (missing or unreadable): the probe then refuses to load. Reads the file, so call it off the UI thread.
    /// </summary>
    public string ProbeHash(string path, PluginSettings settings, IPluginFileTrust files)
    {
        var expected = ExpectedHashFor(path, settings, files);
        if (expected.Length > 0) return expected;
        var bin = BinaryOf(PluginPath.Normalize(path));
        if (bin is null) return "";
        try
        {
            using var stream = new FileStream(bin, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            return PluginIdentity.Sha256Hex(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Engine, "plug-in hash: " + ex.Message); return ""; }
    }

    /// <summary>Approves a path and records the fingerprint of the file as it is now: the new baseline, taken only on this explicit action.</summary>
    public void Approve(PluginSettings settings, string path, IPluginFileTrust files)
    {
        var full = PluginPath.Normalize(path);
        if (full.Length == 0 || full.Length > InputLimits.MaxPathLength) return;
        lock (Gate)
        {
            settings.ApprovedPluginPaths ??= new();
            if (!settings.ApprovedPluginPaths.Any(a => string.Equals(PluginPath.Normalize(a), full, StringComparison.OrdinalIgnoreCase)))
                settings.ApprovedPluginPaths.Add(full);
            ChangedAtLoad.TryRemove(full, out _);
            if (!IsRemote(full) && IsProtectedLocation(full) && ResolvesInsideProtected(full, files)) return;   // location trust: no per-file record
            var bin = BinaryOf(full);
            var now = bin is null ? null : FingerprintOf(bin, files, fresh: true);   // remote/removable: read once, here, on the user's action
            var index = IndexFor(settings);
            if (!index.Records.TryGetValue(full, out var rec))
            {
                rec = new PluginFingerprint { Path = full };
                AddRecord(settings, index, rec);
            }
            if (now is null) { rec.Sha256 = ""; rec.Signer = ""; rec.Size = 0; rec.LastWriteUtcTicks = 0; }   // missing: whatever appears there later counts as changed
            else CopyFingerprint(now, rec);
        }
    }

    /// <summary>Forgets the in-memory scan fingerprints (a test that scans scratch folders must not leak them into later tests).</summary>
    public void ClearScanRecords() { ScanRecords.Clear(); Interlocked.Increment(ref _scanGeneration); }

    /// <summary>Called by the scanner: fingerprints the plug-ins it found in user-writable local folders (the scan's baseline).</summary>
    public void RecordScan(IReadOnlyList<VstPluginInfo> found, CancellationToken cancellationToken, IProgress<(int Found, string Folder)>? progress, IPluginFileTrust files)
    {
        foreach (var plugin in found)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = PluginPath.Normalize(plugin.Path);
            if (full.Length == 0 || IsRemote(full) || (IsProtectedLocation(full) && ResolvesInsideProtected(full, files))) continue;
            var bin = BinaryOf(full);
            if (bin is null) continue;
            progress?.Report((found.Count, full));
            var fp = FingerprintOf(bin, files, fresh: true);
            if (fp is null) continue;
            var rec = new PluginFingerprint { Path = full, Scanned = true };
            CopyFingerprint(fp, rec);
            ScanRecords[full] = rec;
        }
        Interlocked.Increment(ref _scanGeneration);
    }

    /// <summary>The spec list Sync sends: quarantined and untrusted plug-ins are marked Skip so the engine never loads them.</summary>
    public List<PluginSpec> BuildSpecs(IEnumerable<PluginSlot> plugins, ICollection<string> quarantined, PluginSettings settings, IPluginFileTrust files)
    {
        var specs = new List<PluginSpec>();
        foreach (var p in plugins)
        {
            // The engine gets only the normalised spelling (the one the trust checks compared); an unparseable path becomes "" and loads nothing.
            var path = PluginPath.Normalize(p.Path);
            var verdict = Check(p.Path, settings, files);   // once per slot
            var untrusted = !verdict.Trusted;
            // The engine hashes the file it loads against ExpectedSha256, unless the verdict says the folder alone is the trust.
            var expected = untrusted || verdict.LocationTrusted ? "" : ExpectedHashFor(p.Path, settings, files);
            specs.Add(new PluginSpec(path, p.Format, p.Type == PluginSlotType.Instrument, p.Plays, p.Wet, p.State, p.Name, p.Pins, p.OutputDb, p.Id,
                untrusted || quarantined.Contains(path, StringComparer.OrdinalIgnoreCase), Untrusted: untrusted,
                ExpectedSha256: expected, LocationTrusted: verdict.LocationTrusted));
        }
        return specs;
    }

    /// <summary>
    /// What every place that asks the user to trust a plug-in says (the Review plug-ins dialog, the Settings option for running plug-ins in their own process).
    /// A self-test keeps the key facts in it.
    /// </summary>
    public const string RightsNotice =
        "A plug-in runs with your Windows permissions and can read and write your files like any program. " +
        "Running plug-ins in their own process (Settings > Audio & Plug-ins, off by default) protects TabForge from a plug-in crash, not your files from the plug-in. " +
        "Only use plug-ins from sources you trust.";

    /// <summary>Distinct untrusted plug-in paths of every rig of the song (see <see cref="SongRigs.All"/>: tracks, every group bus, master and the song's own monitor chain) with the reason for each.</summary>
    public List<(string Path, string Reason)> UntrustedDetails(SongProject project, PluginSettings settings, IPluginFileTrust files)
    {
        var slots = SongRigs.All(project).SelectMany(r => r.Rig.Plugins);
        var list = new List<(string Path, string Reason)>();
        foreach (var path in slots.Select(p => p.Path).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var verdict = Check(path, settings, files);
            if (!verdict.Trusted) list.Add((path, verdict.Reason));
        }
        return list;
    }

    /// <summary>Distinct untrusted plug-in paths of every rig of the song (see <see cref="UntrustedDetails"/>).</summary>
    public List<string> Untrusted(SongProject project, PluginSettings settings, IPluginFileTrust files) =>
        UntrustedDetails(project, settings, files).Select(u => u.Path).ToList();

    // ---- trusted sets, rebuilt once per settings revision ----

    private sealed class Index
    {
        public required Revision Revision;
        public required HashSet<string> Approved;
        public required HashSet<string> Scanned;
        public required string[] ProtectedRoots;
        public required Dictionary<string, PluginFingerprint> Records;
    }

    private readonly record struct Revision(PluginSettings Settings, List<string>? Approved, int ApprovedCount, List<KnownPlugin>? ScanCache, int ScanCount,
        IReadOnlyList<VstPluginInfo> LastScan, List<PluginFingerprint>? Records, int FolderHash, bool Standard, int ScanGeneration);

    private Revision RevisionOf(PluginSettings s)
    {
        var folders = new HashCode();
        foreach (var f in s.Folders ?? new()) folders.Add(f ?? "", StringComparer.OrdinalIgnoreCase);
        folders.Add(-1);
        foreach (var f in s.CommonFolders ?? new()) folders.Add(f ?? "", StringComparer.OrdinalIgnoreCase);
        return new Revision(s, s.ApprovedPluginPaths, s.ApprovedPluginPaths?.Count ?? 0, s.ScanCache, s.ScanCache?.Count ?? 0,
            s.LastScan, s.TrustRecords, folders.ToHashCode(), s.ScanStandardFolders, Volatile.Read(ref _scanGeneration));
    }

    private Index IndexFor(PluginSettings settings)
    {
        MergeScanRecords(settings);
        var revision = RevisionOf(settings);
        if (_index is { } cached && cached.Revision == revision) return cached;
        var cmp = StringComparer.OrdinalIgnoreCase;
        var records = new Dictionary<string, PluginFingerprint>(cmp);
        foreach (var r in settings.TrustRecords ?? new())
            if (r is { Path.Length: > 0 and <= InputLimits.MaxPathLength })
            {
                r.Sha256 ??= ""; r.Signer ??= "";   // hand-edited settings
                records[PluginPath.Normalize(r.Path)] = r;
            }
        var scanned = new HashSet<string>((settings.ScanCache ?? new()).Where(k => k is not null).Select(k => PluginPath.Normalize(k.Path))
            .Concat(settings.LastScan.Select(p => PluginPath.Normalize(p.Path)))
            .Concat(records.Where(r => r.Value.Scanned).Select(r => r.Key)).Where(p => p.Length > 0), cmp);
        var roots = VstScannerService.RootsFor(settings).Select(PluginPath.Normalize).Where(r => r.Length > 0 && IsProtectedLocation(r))
            .Select(r => r.TrimEnd('\\') + "\\").Distinct(cmp).ToArray();
        return _index = new Index
        {
            Revision = revision, Records = records, Scanned = scanned, ProtectedRoots = roots,
            Approved = new HashSet<string>((settings.ApprovedPluginPaths ?? new()).Select(PluginPath.Normalize).Where(p => p.Length > 0), cmp),
        };
    }

    /// <summary>The latest scans' fingerprints replace this settings' records for those paths (a rescan is a new baseline).</summary>
    private void MergeScanRecords(PluginSettings settings)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        var merged = MergedScan.GetOrCreateValue(settings);
        if (merged.Value == generation) return;
        merged.Value = generation;
        settings.TrustRecords ??= new();
        var byPath = new Dictionary<string, PluginFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in settings.TrustRecords) if (r is not null) byPath[PluginPath.Normalize(r.Path)] = r;
        var approved = new HashSet<string>((settings.ApprovedPluginPaths ?? new()).Select(PluginPath.Normalize), StringComparer.OrdinalIgnoreCase);
        foreach (var (path, scan) in ScanRecords)
        {
            if (byPath.TryGetValue(path, out var rec))
            {
                // A rescan is a new baseline only for the same content or the same signing key; a different, unsigned file stays
                // "changed since you approved it" until the user approves it (a failed check never rewrites the baseline).
                var sameContent = string.Equals(scan.Sha256, rec.Sha256, StringComparison.OrdinalIgnoreCase);
                var sameKey = SignerKey(rec.Signer).Length > 0 && SignerKey(rec.Signer) == SignerKey(scan.Signer);
                if (sameContent || sameKey) { CopyFingerprint(scan, rec); rec.Scanned = true; }
            }
            else if (!approved.Contains(path))   // an approval with no fingerprint is never given one by a scan (see Verify)
            {
                rec = new PluginFingerprint { Path = path, Scanned = true };
                CopyFingerprint(scan, rec);
                settings.TrustRecords.Add(rec);
                byPath[path] = rec;
            }
        }
        if (settings.TrustRecords.Count > MaxRecords) TrimRecords(settings);
        _index = null;
    }

    private void AddRecord(PluginSettings settings, Index index, PluginFingerprint rec)
    {
        settings.TrustRecords ??= new();
        settings.TrustRecords.Add(rec);
        if (settings.TrustRecords.Count > MaxRecords)
        {
            TrimRecords(settings);
            _index = null;   // dropped records must leave the index too
        }
        else index.Records[rec.Path] = rec;
    }

    /// <summary>
    /// Brings the fingerprint cache back to <see cref="MaxRecords"/> by dropping the oldest scan-only records. A record of an approved path
    /// is never dropped: approvals are uncapped, so a full cache can never turn an approval into "approve again" or re-baseline it.
    /// </summary>
    private void TrimRecords(PluginSettings settings)
    {
        var excess = settings.TrustRecords.Count - MaxRecords;
        if (excess <= 0) return;
        var approved = new HashSet<string>((settings.ApprovedPluginPaths ?? new()).Select(PluginPath.Normalize), StringComparer.OrdinalIgnoreCase);
        var dropped = new HashSet<PluginFingerprint>(ReferenceEqualityComparer.Instance);
        foreach (var record in settings.TrustRecords)
        {
            if (dropped.Count == excess) break;
            if (record is not null && !approved.Contains(PluginPath.Normalize(record.Path))) dropped.Add(record);
        }
        settings.TrustRecords.RemoveAll(dropped.Contains);
    }

    // ---- fingerprints ----

    /// <summary>
    /// An approved path in a user-writable location: trusted while unchanged, or changed with the same valid signer. An approval with no
    /// fingerprint is never re-baselined from the file as it is now (that would trust whatever sits there); it needs approving again.
    /// </summary>
    private TrustVerdict Verify(string full, Index index, PluginSettings settings, IPluginFileTrust files)
    {
        if (!index.Records.TryGetValue(full, out var rec)) return new(false, ReasonNoBaseline);
        var bin = BinaryOf(full);
        if (bin is null || Stat(bin) is not { } stat) return TrustVerdict.Yes;   // nothing on disk to load: the engine reports it missing
        if (rec.Sha256.Length > 0 && rec.Size == stat.Size && rec.LastWriteUtcTicks == stat.Ticks) return TrustVerdict.Yes;
        var now = FingerprintOf(bin, files);
        if (now is null) return new(false, ReasonUnreadable);
        var sameContent = rec.Sha256.Length > 0 && string.Equals(now.Sha256, rec.Sha256, StringComparison.OrdinalIgnoreCase);
        var sameSigner = SignerKey(rec.Signer).Length > 0 && string.Equals(SignerKey(now.Signer), SignerKey(rec.Signer), StringComparison.Ordinal);   // publisher-update policy: same signing key
        if (!sameContent && !sameSigner) return new(false, ReasonChanged);   // the record is left as it was
        CopyFingerprint(now, rec);   // touched, or a vendor update signed with the same key: the new file is the baseline
        return TrustVerdict.Yes;
    }

    private void CopyFingerprint(PluginFingerprint from, PluginFingerprint to)
    {
        to.Size = from.Size; to.LastWriteUtcTicks = from.LastWriteUtcTicks; to.Sha256 = from.Sha256; to.Signer = from.Signer;
    }

    private string? BinaryOf(string full) => PluginIdentity.BinaryOf(full);

    /// <summary>
    /// True when the plug-in's binary really lives under a protected base once every link and junction is resolved (the final path
    /// Windows reports for the open file). A link inside Program Files that leads to a user-writable folder is not protected.
    /// A missing binary counts as protected (nothing to load); an unresolvable one does not.
    /// </summary>

    internal bool ResolvesInsideProtected(string full, IPluginFileTrust? files)
    {
        var bin = BinaryOf(full);
        if (bin is null) return true;
        if (files is null) return false; // No file-trust port composed: deny
        try
        {
            var final = files.FinalPath(bin);
            if (final is null) return false;
            if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return false;
            if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
            var norm = PluginPath.Normalize(final);
            return !IsRemote(norm) && ProtectedBases.Any(b => norm.StartsWith(b, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Services.Trace.Error(Services.Trace.Engine, "folder trust check: " + ex.Message); return false; } // Any adapter failure denies: fail closed
    }

    private (long Size, long Ticks)? Stat(string bin)
    {
        try
        {
            var info = new FileInfo(bin);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target) info = target;
            return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Services.Trace.Error(Services.Trace.Engine, "plug-in file info: " + ex.Message); return null; }
    }

    /// <summary>SHA-256 and valid Authenticode signer of a binary, cached by (path, size, last-write); null when unreadable. <paramref name="fresh"/> (approve and scan, the moments a baseline is taken) ignores the cache: a file replaced with the same size and time must not inherit an older hash.</summary>
    private PluginFingerprint? FingerprintOf(string bin, IPluginFileTrust files, bool fresh = false)
    {
        if (Stat(bin) is not { } stat) return null;
        if (!fresh && Fingerprints.TryGetValue(bin, out var cached) && cached.Size == stat.Size && cached.LastWriteUtcTicks == stat.Ticks) return cached;
        try
        {
            string sha;
            using (var stream = new FileStream(bin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
                sha = Convert.ToHexString(SHA256.HashData(stream));
            Interlocked.Increment(ref _hashCount);
            var fp = new PluginFingerprint { Path = bin, Size = stat.Size, LastWriteUtcTicks = stat.Ticks, Sha256 = sha, Signer = SignerOf(bin, files) };
            Fingerprints[bin] = fp;
            return fp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Engine, "plug-in fingerprint: " + ex.Message); return null; }
    }

    private const string KeyPrefix = "spki-sha256:";

    /// <summary>
    /// Signer identity of a binary whose embedded Authenticode signature WinVerifyTrust accepts (no UI, no revocation lookup):
    /// "spki-sha256:&lt;hex SHA-256 of the signing certificate's public key&gt;; &lt;subject, display only&gt;", or "" when
    /// unsigned or invalid. Only the key part is compared (<see cref="SignerKey"/>); subject text is not an identity.
    /// </summary>

    internal string SignerOf(string bin, IPluginFileTrust? files)
    {
        if (files is null) return ""; // No file-trust port composed: unsigned
        try { if (!files.HasValidEmbeddedSignature(bin)) return ""; }
        catch (Exception ex) { Services.Trace.Error(Services.Trace.Engine, "plug-in signature check: " + ex.Message); return ""; } // Any adapter failure reads as unsigned: fail closed
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(bin));
            var key = Convert.ToHexString(SHA256.HashData(cert.PublicKey.EncodedKeyValue.RawData));
            var subject = (cert.Subject ?? "").Replace(';', ',');
            var text = KeyPrefix + key + "; " + subject;
            return text.Length > 512 ? text[..512] : text;
        }
        catch (CryptographicException ex) { Services.Trace.Error(Services.Trace.Engine, "plug-in signer: " + ex.Message); return ""; }
    }

    /// <summary>The comparable part of a recorded signer ("" for none, unsigned, or the older subject-only format, which never matches).</summary>
    private string SignerKey(string signer)
    {
        if (!signer.StartsWith(KeyPrefix, StringComparison.Ordinal)) return "";
        var end = signer.IndexOf(';');
        return end < 0 ? signer : signer[..end];
    }
}
