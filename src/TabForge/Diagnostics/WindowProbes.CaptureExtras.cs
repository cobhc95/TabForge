using System.Text.Json;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the capture steps "clip-edit" (fades and a split on the example audio clip), "delete-prompt" (the bar-range Delete prompt as a tool window) and "marker-size" (fretboard note marker size).
// Does not own: step dispatch (WindowProbes.Capture.cs) or the shots themselves (WindowProbes.CaptureShots.cs).
// Tests: none (diagnostics only; run through --capture).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private async Task ExtraStepAsync(string verb, JsonElement value)
        {
            switch (verb)
            {
                case "clip-edit": ClipEdit(); break;
                case "delete-prompt": DeletePrompt(); break;
                case "marker-size": _w._settings.Editing.FretMarkerSizePercent = value.GetInt32(); _w.RefreshInstrument(); break;
            }
            await _w.Settle(500);
        }

        /// <summary>Gives the example clip a fade-in and fade-out, splits it near the middle and selects the first piece.</summary>
        private void ClipEdit()
        {
            var track = _w._project.Tracks[_track];
            var clip = track.AudioClips[0];
            ClipSplitGlue.SetFades(clip, 1.2, 1.6);
            ClipSplitGlue.Split(track, clip, clip.StartSec + clip.SourceLengthSec * 0.55);
            _w.RefreshArrangement();
            _w.Arrangement.SelectedClip = clip;
        }

        /// <summary>Opens the bar-range Delete prompt over the current bar selection; the dialog is kept as the tool window "DeletePrompt".</summary>
        private void DeletePrompt()
        {
            if (_selection is not { Length: > 5 } sel) throw new InvalidOperationException("select bars first");
            _toolName = "DeletePrompt";
            DialogHost.Capture = dialog => Adopt(dialog);
            try
            {
                var text = $"Bars {sel[5..]} are selected. What should Delete do?";
                BarRangePrompt.Ask(_w.Window, text, BarRangeAction.Clear, true, id => HotkeyCatalog.DisplayAll(_w._settings.Hotkeys, id));
            }
            finally { DialogHost.Capture = null; }
        }
    }
}
