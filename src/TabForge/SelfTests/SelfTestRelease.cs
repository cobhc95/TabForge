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
        "TestDocumentOperations", "TestDocumentContext",
        "TestSecurityInputBoundaries", "TestNightPluginApproval", "TestPairMarkerIsUntrusted", "TestQuarantineAllowAgain", "TestPluginStateCollection", "TestPluginRightsNotice",
    };
}
