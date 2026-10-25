using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the compact undo-state headers of a song and a track.
// Does not own: the rest of the class (see the main file).
// Tests: no dedicated test class.
public static partial class ProjectService
{

    /// <summary>
    /// Undo state headers (see <c>ProjectState</c>): the compact in-memory JSON of the song without its
    /// track list, and of a track without its bars. The bars are kept as separate shared chunks, so an edit re-stores only what changed.
    /// </summary>
    private static readonly JsonSerializerOptions UndoHeaderOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { OmitInitialValues, OmitScoreBody, OmitViewState, OmitArmState } }
    };

    /// <summary>
    /// Input monitoring and the chosen audio input are monitoring state, not song content: undo never stores or replays them (they stay
    /// live). Record arm is different since 2026-10-21: it is song state with its own undo step ("Arm track"/"Disarm track"), so it is stored.
    /// The disk form still writes all three, and the restore keeps the live input values.
    /// </summary>
    private static void OmitArmState(System.Text.Json.Serialization.Metadata.JsonTypeInfo info)
    {
        if (info.Type != typeof(TrackModel)) return;
        RemoveProperty(info, nameof(TrackModel.AudioInput));
        RemoveProperty(info, nameof(TrackModel.MonitorInput));
    }

    /// <summary>
    /// View state (the Band layout and the collapsed mixer groups) is neither part of the unsaved-changes hash nor of an undo state:
    /// undo keeps the live values. The disk form still writes both.
    /// </summary>
    private static void OmitViewState(System.Text.Json.Serialization.Metadata.JsonTypeInfo info)
    {
        if (info.Type == typeof(SongProject)) RemoveProperty(info, nameof(SongProject.BandLayout));
        else if (info.Type == typeof(MixerSettings)) RemoveProperty(info, nameof(MixerSettings.CollapsedGroups));
    }

    private static void RemoveProperty(System.Text.Json.Serialization.Metadata.JsonTypeInfo info, string name)
    {
        for (var index = info.Properties.Count - 1; index >= 0; index--)
            if (info.Properties[index].Name == name) info.Properties.RemoveAt(index);
    }

    private static void OmitScoreBody(System.Text.Json.Serialization.Metadata.JsonTypeInfo info)
    {
        var body = info.Type == typeof(SongProject) ? nameof(SongProject.Tracks)
            : info.Type == typeof(TrackModel) ? nameof(TrackModel.Measures) : null;
        if (body is null) return;
        for (var index = info.Properties.Count - 1; index >= 0; index--)
            if (info.Properties[index].Name == body) info.Properties.RemoveAt(index);
    }

    internal static byte[] UndoSongHeader(SongProject project) => JsonSerializer.SerializeToUtf8Bytes(project, UndoHeaderOptions);
    internal static byte[] UndoTrackHeader(TrackModel track) => JsonSerializer.SerializeToUtf8Bytes(track, UndoHeaderOptions);

    internal static SongProject RestoreUndoSongHeader(byte[] json) =>
        JsonSerializer.Deserialize<SongProject>(json, UndoHeaderOptions) ?? throw new InvalidDataException("The undo state is damaged.");

    internal static TrackModel RestoreUndoTrackHeader(byte[] json) =>
        JsonSerializer.Deserialize<TrackModel>(json, UndoHeaderOptions) ?? throw new InvalidDataException("The undo state is damaged.");

    /// <summary>Compact in-memory JSON of any model object (self-tests compare the undo bar codec against it).</summary>
    internal static byte[] CompactJson<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CompactOptions);

    /// <summary>The JSON property names the in-memory form writes for <paramref name="type"/> (the undo bar codec must cover every one).</summary>
    internal static IReadOnlyList<string> CompactPropertyNames(Type type) =>
        CompactOptions.GetTypeInfo(type).Properties
            .Where(property => property.AttributeProvider?.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true)
                .OfType<System.Text.Json.Serialization.JsonIgnoreAttribute>()
                .Any(ignore => ignore.Condition == System.Text.Json.Serialization.JsonIgnoreCondition.Always) != true)
            .Select(property => property.Name).ToList();
}
