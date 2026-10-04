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

    /// <summary>True when the last <see cref="TakeVisual"/> found the visual settings changed (re-theme and re-lay out the score).</summary>
    public bool VisualChanged { get; private set; } = true;

    /// <summary>The default notation last applied; null before the first sync.</summary>
    public NotationMode? Notation { get; set; }

    /// <summary>A refresh after another window's settings save is queued (many saves in a row cost one refresh).</summary>
    public bool SharedRefreshQueued { get; set; }

    /// <summary>Compares the visual settings (appearance, follow, timeline) with the last applied ones and remembers them.</summary>
    public void TakeVisual(AppSettings settings)
    {
        var key = JsonSerializer.Serialize(settings.Appearance) + JsonSerializer.Serialize(settings.Follow) +
                  JsonSerializer.Serialize(settings.Timeline);
        VisualChanged = !string.Equals(key, _visualKey, StringComparison.Ordinal);
        _visualKey = key;
    }
}
