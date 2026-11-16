// Owns: which icon the MIDI processing window draws for each processor type, catalog group and parameter section, and drawing one
//   in a theme brush (the shared stroked icon of MenuRowToggles, recoloured).
// Does not own: the path data (IconGeometry), the groups (MidiProcessorCatalog.Groups) or where the icons sit (the window).
// Tests: MidiProcessingWindowTests (every processor and group has its own icon).
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Midi;

internal static class MidiIcons
{
    private static string? TypeIcon(string key) => key switch
    {
        "transpose" => IconGeometry.MidiTranspose,
        "noteRange" => IconGeometry.MidiNoteRange,
        "scaleSnap" => IconGeometry.MidiScaleSnap,
        "chord" => IconGeometry.MidiChord,
        "velocity" => IconGeometry.ToolVelocity,
        "velVariation" => IconGeometry.MidiVelVariation,
        "humanize" => IconGeometry.MidiHumanize,
        "delay" => IconGeometry.MidiDelay,
        "noteHold" => IconGeometry.MidiNoteHold,
        "repeater" => IconGeometry.MidiRepeater,
        "noteMap" => IconGeometry.MidiNoteMap,
        "choke" => IconGeometry.MidiChoke,
        "chokeGroup" => IconGeometry.MidiChokeGroup,
        "audioDrum" => IconGeometry.MidiAudioDrum,
        "arp" => IconGeometry.MidiArp,
        "stepSeq" => IconGeometry.MidiStepSeq,
        "lfo" => IconGeometry.MidiLfo,
        "noteRandom" => IconGeometry.MidiNoteRandom,
        "modalRandom" => IconGeometry.MidiModalRandom,
        "scaleVariation" => IconGeometry.MidiScaleVariation,
        "channelMap" => IconGeometry.MidiChannelMap,
        "channelRoute" => IconGeometry.MidiChannelRoute,
        "programBank" => IconGeometry.MidiProgramBank,
        "ccSender" => IconGeometry.MidiCcSender,
        "ccMapper" => IconGeometry.MidiCcMapper,
        "audioDucker" => IconGeometry.MidiAudioDucker,
        "loopSampler" => IconGeometry.MidiLoopSampler,
        "looper" => IconGeometry.MidiLooper,
        "sanitizer" => IconGeometry.MidiSanitizer,
        "panic" => IconGeometry.MidiPanic,
        "log" => IconGeometry.MidiLog,
        _ => null,
    };

    private static string? GroupIcon(string key) => key switch
    {
        "notes" => IconGeometry.MidiGroupNotes,
        "velocity" => IconGeometry.MidiGroupVelocity,
        "timing" => IconGeometry.MidiGroupTiming,
        "drums" => IconGeometry.MidiGroupDrums,
        "generate" => IconGeometry.MidiGroupGenerate,
        "channels" => IconGeometry.MidiGroupChannels,
        "audio" => IconGeometry.MidiGroupAudio,
        "utilities" => IconGeometry.MidiGroupUtilities,
        _ => null,
    };

    /// <summary>The icon data of a processor type; the warning triangle for a type this version does not know.</summary>
    public static string ForType(string type) => TypeIcon(type) ?? IconGeometry.Warning;

    /// <summary>The icon data of a catalog group.</summary>
    public static string ForGroup(string key) => GroupIcon(key) ?? IconGeometry.MidiGroupUtilities;

    /// <summary>The icon data of a parameter section heading.</summary>
    public static string ForSection(string section) => section switch
    {
        "Applies to" => IconGeometry.Target,
        "Only note numbers" or "Keep the result between" => IconGeometry.MidiNoteRange,
        "Only notes with velocity" => IconGeometry.ToolVelocity,
        "Finishing" => IconGeometry.CheckMark,
        _ => IconGeometry.Prefs,
    };

    /// <summary>True when the type has its own icon (tests check every catalog type does).</summary>
    public static bool HasOwnIcon(string type) => TypeIcon(type) is not null;

    /// <summary>An icon of <paramref name="size"/> DIP stroked in the theme brush <paramref name="brushKey"/>.</summary>
    public static Control Icon(string data, double size, string brushKey = "TextPrimary")
    {
        var icon = MenuRowToggles.Icon(data, size);
        if (icon is Viewbox { Child: Path path }) path[!Shape.StrokeProperty] = new DynamicResourceExtension(brushKey);
        icon.IsHitTestVisible = false;
        return icon;
    }
}
