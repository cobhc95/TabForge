namespace TabForge.Audio.Metering;

// Owns: what every track-row meter keeps in the app settings: the master meter choices, the apply-to-all flag and the pre- or post-fader tap.
// Does not own: loading and saving (AppSettingsStore), the meter itself (MasterMeterModel, Views/Meters/TrackMeterColumn), the popup rows (MasterMeterPopup).
// Tests: TabForge.Avalonia.Tests.TrackMeterTests.
/// <summary>The track-row meters' saved choices. App-wide: one setting drives every track meter.</summary>
public sealed class TrackMeterSettings : MasterMeterSettings
{
    public const string PreFader = "Pre-fader", PostFader = "Post-fader";
    public static IReadOnlyList<string> Taps { get; } = Array.AsReadOnly(new[] { PostFader, PreFader });

    /// <summary>When on (the default), every track meter shows the same choices; the flag is stored so the menu says so.</summary>
    public bool ApplyToAll { get; set; } = true;

    /// <summary>Where the bars read the signal: after the fader and pan (the default) or before them.</summary>
    public string Tap { get; set; } = PostFader;

    /// <summary>The menu wording of a tap.</summary>
    public static string TapLabel(string tap) => ParseTap(tap) == PreFader ? PreFader : PostFader;

    /// <summary>The tap a label (or a stored name) names; <see cref="PostFader"/> for anything else.</summary>
    public static string ParseTap(string? text) => text == PreFader || text == TapLabel(PreFader) ? PreFader : PostFader;

    /// <summary>The shared meter reading for this tap.</summary>
    public bool IsPreFader => ParseTap(Tap) == PreFader;

    public override void Normalize()
    {
        base.Normalize();
        Tap = ParseTap(Tap);
    }
}
