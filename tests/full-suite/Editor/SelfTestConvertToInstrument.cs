using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Convert to instrument track on an audio track: the picker opens with its search focused, the clip prompt branches, the remembered choice and the untouched track identity.</summary>
public static partial class SelfTest
{
    private static void TestConvertToInstrumentFlow() => RunInWindowFixture((a, context) =>
    {
        var settings = LtField<AppSettingsStore>(a, "_settingsStore")!.Settings;
        var previous = DialogHost.Capture;
        string? pick = null;
        bool? searchFocused = null;
        var prompts = new List<(string Text, int Radios)>();
        Action<ThemedConfirmDialog> answer = d => d.AnswerForTest(MessageBoxResult.Yes);
        DialogHost.Capture = w =>
        {
            if (w is ThemedConfirmDialog confirm)
            {
                prompts.Add((string.Join(" ", Logical<TextBlock>(w).Select(t => t.Text)), Logical<RadioButton>(w).Count()));
                answer(confirm);
                return true;
            }
            w.Show();
            SettleLifetimeDispatcher();
            var search = Logical<TextBox>(w).First();
            searchFocused = ReferenceEquals(FocusManager.GetFocusedElement(w), search);
            var tile = pick is null ? null : Logical<Border>(w).FirstOrDefault(b => b.ToolTip is string t && t.StartsWith(pick + " "));
            if (tile is not null)
                try { tile.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent }); } catch { }
            w.Close();
            return tile is not null;
        };
        try
        {
            settings.Editing.ConvertMidiClips = "Ask";
            var doc = DoOpen(a, DoSong(2));
            LtCall(a, "AddAudioTrack");
            var audio = doc.Project.Tracks[2];
            audio.Name = "Vox"; audio.ColorHex = "#35B954"; audio.Mute = true; audio.Volume = 61;
            audio.AudioClips.Add(new AudioClip { File = "a.wav", Name = "a", SourceLengthSec = 2, FileLengthSec = 2, Lane = 0 });
            doc.MarkClean();
            var hash = DoHash(doc); var undo0 = doc.Undo.UndoCount;
            void Undo() { DocumentEdits.Undo(doc); SettleLifetimeDispatcher(); }

            pick = null;
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: the picker opens with its search box focused", searchFocused == true, $"{searchFocused}");
            Check("convert flow: cancelling the picker changes nothing and asks nothing more", audio.IsAudio && prompts.Count == 0 && DoHash(doc) == hash);

            pick = "Drum Kit (Standard)";
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: a drum kit makes a drum track on channel 10", doc.Project.Tracks[2] == audio && audio.Kind == TrackKind.Drums && audio.MidiChannel == 9, $"{audio.Kind}");
            Check("convert flow: audio clips only: one info prompt without options, the clip stays on a second lane", prompts.Count == 1 && prompts[0].Radios == 0 && prompts[0].Text.Contains("audio clips")
                && audio.AudioClips.Count == 1 && audio.AudioClips[0].Lane >= 1, string.Join("|", prompts.Select(p => p.Text)));
            Check("convert flow: index, colour, name, mute and volume are unchanged and it is one undo step", doc.Project.Tracks.IndexOf(audio) == 2 && audio.Name == "Vox" && audio.ColorHex == "#35B954" && audio.Mute && audio.Volume == 61 && doc.Undo.UndoCount == undo0 + 1);
            Undo();
            Check("convert flow: undo restores the song exactly", DoHash(doc) == hash && doc.Project.Tracks[2].IsAudio);

            audio = doc.Project.Tracks[2];
            audio.AudioClips.Clear();
            audio.AudioClips.Add(new AudioClip { File = "", Name = "m", SourceLengthSec = 1, FileLengthSec = 1, Lane = 0, Notes = new() { new ClipNote(0, 0.4, 40, 90) } });
            doc.MarkClean();
            var midiHash = DoHash(doc);
            pick = "Electric Bass (Finger)"; prompts.Clear();
            answer = d => { d.PickForTest(0); d.AnswerForTest(MessageBoxResult.Yes); };
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: MIDI clips offer write or keep; Write turns them into notation", prompts.Count == 1 && prompts[0].Radios == 2 && audio.Kind == TrackKind.Bass
                && audio.AudioClips.All(c => !c.IsMidi) && audio.Measures.Any(m => m.Cells.Any(c => c.Notes.Count > 0)));
            Undo();
            Check("convert flow: undo after Write restores by hash", DoHash(doc) == midiHash);

            prompts.Clear();
            answer = d => { d.PickForTest(1); d.AnswerForTest(MessageBoxResult.Yes); };
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: Keep leaves the MIDI clip on a second lane and the bars empty", audio.Kind == TrackKind.Bass && audio.AudioClips.Count == 1 && audio.AudioClips[0].IsMidi
                && audio.AudioClips[0].Lane >= 1 && audio.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0)) && settings.Editing.ConvertMidiClips == "Ask");
            Undo();

            prompts.Clear();
            answer = d => d.AnswerForTest(MessageBoxResult.Cancel);
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: cancelling the prompt abandons the conversion", audio.IsAudio && DoHash(doc) == midiHash);

            answer = d => { d.PickForTest(0); d.RememberForTest(true); d.AnswerForTest(MessageBoxResult.Yes); };
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: Remember my choice stores the setting", settings.Editing.ConvertMidiClips == "Write as notation");
            Undo(); prompts.Clear();
            a.ConvertAudioTrack(2); audio = doc.Project.Tracks[2];
            Check("convert flow: a remembered choice skips the prompt and writes the notation", prompts.Count == 0 && audio.Kind == TrackKind.Bass && audio.AudioClips.All(c => !c.IsMidi));
            Undo();
        }
        finally { DialogHost.Capture = previous; settings.Editing.ConvertMidiClips = "Ask"; }
    });
}
