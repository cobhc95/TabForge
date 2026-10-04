namespace TabForge;

// Owns: the curated release set (--areas release).
// Does not own: the tests themselves (tests/full-suite) or the packaging scripts that require this run.
// Tests: TestOnlyOption, TestEveryTestHasAnArea.
public static partial class SelfTest
{
    private static HashSet<string>? _releaseTests;   // --areas release: these named tests run in addition to the basic set

    /// <summary>
    /// The release gate: the basic set plus saving, atomic writes and recovery, import and malformed-input containment, playback and editing
    /// interaction, document context, and plug-in trust and input limits. It needs a build made with -p:TabForgeFullSuite=true.
    /// </summary>
    private static readonly string[] ReleaseTestNames =
    {
        "TestSaveTransactions", "TestAsyncSaveSequencing", "TestPairSaveRecovery", "TestPairSaveEveryStage", "TestAutosaveRecovery", "TestEmergencyRecoveryNames",
        "TestDirectTforgeOpenRecovers", "TestRecoveryCopyOverTforgeLimit", "TestDataIntegrityLeftovers", "TestTforgeCompression", "TestPersistenceSchema",
        "TestEmbeddedProjectLimit", "TestAudioDataSizeLimit", "TestMediaPathPolicy",
        "TestGuitarProFiles", "TestGuitarProImportContainment", "TestGuitarProImportWorker", "TestSyntheticGuitarProFixture", "TestSyntheticFixtures",
        "TestImportPlausibility", "TestMalformedInputFuzz", "TestScoreClipRejectsUntrustedInput",
        "TestPlaybackOrderSpec", "TestNoHangingNotes", "TestSeekWhilePlayingSoundsFirstNote", "TestEditCommands", "TestEditorCopyPaste", "TestUndoController", "TestDocuments",
        "TestDocumentOperations", "TestDocumentContext", "TestStartupFileOpen", "TestSelectionClipboardMatrix",
        "TestSecurityInputBoundaries", "TestNightPluginApproval", "TestPairMarkerIsUntrusted", "TestQuarantineAllowAgain", "TestPluginStateCollection", "TestPluginRightsNotice",
    };

    private static readonly HashSet<string> _releaseRan = new(StringComparer.Ordinal);
    /// <summary>The gate needs at least this many checks: a build without the full suite would pass the basic set alone.</summary>
    private const int ReleaseMinimumChecks = 1500;

    /// <summary>--areas release: every curated test group must have run and the run must reach the minimum check count; no silent skips.</summary>
    private static void ReportReleaseGate()
    {
        if (_releaseTests is null || _only is not null) return;
        var missing = ReleaseTestNames.Where(n => !_releaseRan.Contains(n)).ToList();
        Check($"release gate: all {ReleaseTestNames.Length} curated release tests ran", missing.Count == 0, "did not run (not compiled in? build needs -p:TabForgeFullSuite=true): " + string.Join(", ", missing));
        Check($"release gate: at least {ReleaseMinimumChecks} checks ran", _pass >= ReleaseMinimumChecks, $"only {_pass} passed so far");
    }
}
