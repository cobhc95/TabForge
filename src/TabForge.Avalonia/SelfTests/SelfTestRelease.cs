using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge;

// Owns: the curated release set (--areas release).
// Does not own: the tests themselves (the Release folder beside this file) or the packaging scripts that require this run.
// Tests: TestOnlyOption, TestEveryTestHasAnArea.
public static partial class SelfTest
{
    private static HashSet<string>? _releaseTests;   // --areas release: these named tests run in addition to the basic set

    /// <summary>
    /// The release gate: the basic set plus saving, atomic writes and recovery, import and malformed-input containment, playback and recording
    /// interaction, clipboard and settings safety. Every name is a test that needs neither a window nor the Avalonia application.
    /// </summary>
    private static readonly string[] ReleaseTestNames =
    {
        "TestEngineMessagesGolden", "TestEngineMessagesRoundTrip", "TestMalformedInputFuzz", "TestGp5OwnFilesImport", "TestSyntheticGuitarProFixture", "TestGpRoundTripCounts",
        "TestTforgeCompression", "TestImportPlausibility", "TestGuitarProImportWorker", "TestSyntheticFixtures", "TestPresetFolderStoreNoOverwrite", "TestNumberedSiblingCeiling",
        "TestSaveClaimAtomic", "TestAutosaveCopies", "TestScoreClipRejectsUntrustedInput", "TestClipboardCopyPasteBytes", "TestDirectTforgeOpenRecovers", "TestPairMarkerIsUntrusted",
        "TestPairSaveEveryStage", "TestMixPointsSurviveSeek", "TestNoHangingNotes", "TestPlaybackOrderSpec", "TestLiveAudioFidelity",
        "TestLiveRecordingLoudness", "TestSongExtent", "TestVideoEncoderGolden", "TestSettingsChangeDuringSaveStaysPending", "TestSettingsBadWorkspaceKeepsOtherSettings",
        "TestPluginRightsNotice", "TestRenderOutputFolderRefused",
    };

    private static readonly HashSet<string> _releaseRan = new(StringComparer.Ordinal);
    /// <summary>The gate needs at least this many checks: a run that skipped the release tests would pass the basic set alone.</summary>
    private const int ReleaseMinimumChecks = 800;

    /// <summary>--areas release: every curated test group must have run and the run must reach the minimum check count; no silent skips.</summary>
    private static void ReportReleaseGate()
    {
        if (_releaseTests is null || _only is not null) return;
        var missing = ReleaseTestNames.Where(n => !_releaseRan.Contains(n)).ToList();
        Check($"release gate: all {ReleaseTestNames.Length} curated release tests ran", missing.Count == 0, "did not run: " + string.Join(", ", missing));
        Check($"release gate: at least {ReleaseMinimumChecks} checks ran", _pass >= ReleaseMinimumChecks, $"only {_pass} passed so far");
    }
}
