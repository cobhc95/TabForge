using System.Text.Json;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the capture steps "clip-edit" (fades and a split on the example audio clip), "delete-prompt" (the bar-range Delete prompt as a tool window), "marker-size" (fretboard note marker size), "band-playing" (the Band view shown as playing), "learn-playing" (the Keyboard mode pane shown as playing; a third value "keys" or "keys-wait" shows a keyboard track with made-up play-along state, see WindowProbes.CaptureKeyboardModeKeys.cs), "learn-midi-menu" (its MIDI input list), "effect" (a note-effect editor dialog, see WindowProbes.CaptureEffects.cs),
//   "timeline-height" (the timeline pane squeezed to a height, as when the user collapses it), "section-tip" (the section lane's hover hint),
//   "restore-saved" (the layout the profile started with, as after a restart) "rec-state" (the title-bar Record video button's red dot and the status bar's REC time, shown without recording) and "tab-strip" (a title-bar tab strip with idle, playing, unsaved and long-titled tabs, as the tool window "TabStrip").
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
                case "effect": EffectShot(value.GetString() ?? "bend"); break;
                case "band-hide-instrument": _w.Window.Band.ToggleInstrumentOf(value.GetInt32()); break;
                case "band-playing": BandPlaying(value[0].GetInt32() - 1, value[1].GetDouble()); break;
                case "learn-playing":   // a third value ("keys" or "keys-wait") turns the selected track into a two-hand riff with made-up play-along state
                    UseExampleMidi();
                    if (value.GetArrayLength() > 2) KeyboardModeKeysPlaying(value[0].GetInt32() - 1, value[1].GetDouble(), value[2].GetString() == "keys-wait");
                    else KeyboardModePlaying(value[0].GetInt32() - 1, value[1].GetDouble());
                    break;
                case "learn-midi-menu": KeyboardModeMidiMenu(); break;   // the Keyboard mode MIDI input list (then target "menu")
                case "timeline-height":
                    _w._settings.Timeline.TrackListHeight = value.GetDouble();   // a user-collapsed pane: the auto-fit keeps it short
                    // Diagnostics only: the window's fit controller is private and the probe bridge stays as it is.
                    (typeof(MainWindow).GetField("_trackListFit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_w.Window)
                        as Controllers.TrackListFitController ?? throw new InvalidOperationException("no track list fit")).FitToTracks(); break;
                case "section-tip": SectionTip(value.GetInt32()); break;
                case "restore-saved": _w._settings.LastLayout = _savedLayoutName; _w._dockWorkspace?.ApplyLayout(_savedWorkspace ?? new()); break;   // as after a restart
                case "tab-strip": TabStrip(value.GetDouble()); break;
                case "rec-state": _w.Window.RecIndicator.Text = "REC 01:15"; _w.Window.RecIndicator.Visibility = _w.Window.VideoRecDot.Visibility = value.GetBoolean() ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; break;   // the Record video REC state, no recording runs
                default: throw new InvalidOperationException($"unknown step '{verb}'");
            }
            await _w.Settle(500);
        }

        /// <summary>Renders the section lane's hover hint for section <paramref name="index"/> as a ToolTip root (then <c>target: "menu"</c>).</summary>
        private void SectionTip(int index)
        {
            var tip = new System.Windows.Controls.ToolTip { Content = new SectionTipController(new TipHost(_w._project), _w.Arrangement).TextFor(index) };
            _menu = "section-tip";
            TryRootDpi(tip);
            tip.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            tip.Arrange(new System.Windows.Rect(tip.DesiredSize));
            tip.UpdateLayout();
            _menuBitmap = Render(tip, tip.ActualWidth, tip.ActualHeight);
            _lastMenu = tip;
        }

        /// <summary>A tab strip <paramref name="width"/> wide on the title-bar colour: idle, playing (inactive and active), unsaved, long-titled and playing + unsaved tabs.</summary>
        private void TabStrip(double width)
        {
            var docs = new Documents.DocumentManager();
            Documents.DocumentSession Doc(string title, bool dirty)
            {
                var d = Documents.DocumentSession.Blank();
                d.Project.Title = title;
                if (dirty) Documents.DocumentEdits.Run(d, p => { p.Title = title + " "; return true; });
                return docs.Add(d, activate: false);
            }
            var idle = Doc("Idle song", false);
            var elsewhere = Doc("Playing elsewhere", false);
            var here = Doc("Playing here", false);
            Doc("Unsaved song", true);
            Doc("A very long song title that keeps going and going", false);
            var both = Doc("Playing and unsaved", true);
            docs.Activate(here);
            var bar = new BrowserTabBar();
            bar.Bind(docs);
            bar.Refresh();
            bar.SetPlayingDocuments(new[] { elsewhere, here, both });
            var host = new System.Windows.Controls.Border { Child = bar, Width = width, Height = 44, Padding = new System.Windows.Thickness(0, 4, 0, 4) };
            host.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChromeStripBrush");
            _toolName = "TabStrip";
            Adopt(new System.Windows.Window { Content = host, SizeToContent = System.Windows.SizeToContent.WidthAndHeight, WindowStyle = System.Windows.WindowStyle.None, ResizeMode = System.Windows.ResizeMode.NoResize });
        }

        private sealed record TipHost(Models.SongProject? Project) : ISectionTipHost
        {
            public bool IsMouseOver => true;
            public bool SectionGestureActive => false;
            public int HoverSectionIndex => 0;
        }

        /// <summary>Shows the Band view as playing at a bar and a fraction of it, at that moment of the song's timeline.</summary>
        private void BandPlaying(int bar, double fraction)
        {
            var timeline = MidiTimelineBuilder.Build(_w._project, new PlaybackOptions());
            var span = timeline.Bars[Math.Clamp(bar, 0, timeline.Bars.Count - 1)];
            _w.Window.Band.ProbePlay = (bar, fraction, span.StartMs + fraction * (span.EndMs - span.StartMs), timeline);
        }

        /// <summary>Shows the Keyboard mode pane as playing the selected track (whatever its kind) at a bar and a fraction of it, at that moment of the song's timeline (select the <c>learn</c> panel first).</summary>
        private void KeyboardModePlaying(int bar, double fraction)
        {
            var timeline = MidiTimelineBuilder.Build(_w._project, new PlaybackOptions());
            var span = timeline.Bars[Math.Clamp(bar, 0, timeline.Bars.Count - 1)];
            _w.Window.Learn.ProbePlay = (span.StartMs + fraction * (span.EndMs - span.StartMs), timeline);
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
