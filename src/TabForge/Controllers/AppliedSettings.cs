using System.Text.Json;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Controllers;

// Owns: what a window last applied from the settings (the visual settings, the default notation) and whether a refresh after
//   another window's save is already queued, so a settings change only redoes the parts that differ.
// Does not own: applying the settings (the window's SyncFromSettings), the settings store.
// Tests: TestNoOpOptionChangesDoNotRestartPlayback, TestViewMenuWording.
/// <summary>The settings a window has applied, compared with each new sync.</summary>
internal sealed class AppliedSettings
{
    private string? _visualKey;

    /// <summary>The settings object the last sync applied; another window replacing the shared object (Settings window preview, apply or cancel) makes it differ.</summary>
    public AppSettings? Settings { get; set; }

    /// <summary>True when the last <see cref="TakeVisual"/> found the visual settings changed (re-theme and re-lay out the score).</summary>
    public bool VisualChanged { get; private set; } = true;

    /// <summary>The default notation last applied; null before the first sync.</summary>
    public NotationMode? Notation { get; set; }

    /// <summary>The page layout (continuous or page) and scrolling (horizontal or vertical) last applied; null before the first sync.</summary>
    public bool? ContinuousView { get; set; }
    public bool? HorizontalView { get; set; }

    /// <summary>A refresh after another window's settings save is queued (many saves in a row cost one refresh).</summary>
    public bool SharedRefreshQueued { get; set; }

    private string? _instrumentKey;

    /// <summary>True when an Editing setting (every fretboard, keyboard and look-ahead option lives there) or the fretboard style differs from the last applied ones.</summary>
    public bool TakeInstrument(AppSettings settings)
    {
        var key = JsonSerializer.Serialize(settings.Editing) + settings.Audio.FretboardStyle;
        var changed = _instrumentKey is not null && !string.Equals(key, _instrumentKey, StringComparison.Ordinal);
        _instrumentKey = key;
        return changed;
    }

    private (bool AutoFit, bool AddLane)? _fitKey;

    /// <summary>True when the track list auto-fit or the Add-track lane differs from the last applied values (the rows must be re-fitted).</summary>
    public bool TakeTrackListFit(TimelineSettings timeline)
    {
        var key = (timeline.AutoFitTrackList, timeline.ShowAddTrackLane);
        var changed = _fitKey is not null && _fitKey != key;
        _fitKey = key;
        return changed;
    }

    /// <summary>Compares the visual settings (appearance, follow, timeline) with the last applied ones and remembers them.</summary>
    public void TakeVisual(AppSettings settings)
    {
        var key = JsonSerializer.Serialize(settings.Appearance) + JsonSerializer.Serialize(settings.Follow) +
                  JsonSerializer.Serialize(settings.Timeline);
        VisualChanged = !string.Equals(key, _visualKey, StringComparison.Ordinal);
        _visualKey = key;
    }
}
