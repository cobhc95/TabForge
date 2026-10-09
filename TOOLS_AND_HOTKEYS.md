# TabForge icons, commands and hotkeys

Generated from the source (`MainWindow.xaml.cs` palette tables and `Services/HotkeyCatalog.cs`). Every item here can be bound to a key in **Settings → Shortcuts**; palette icons are registered automatically as `Tool.<id>` commands, so new icons become mappable without editing this list.

## Tools palette

| Group | Icon | Function | Command id | Hotkey command |
|---|---|---|---|---|
| Edit | `Edit/select_pointer` | Selection cursor | `edit:pointer` | Tool.edit:pointer |
| Edit | `Edit/erase_note` | Erase note | `edit:erase_note` | Tool.edit:erase_note |
| Edit | `Edit/change_accidental` | Change accidental (not yet supported) | `edit:change_accidental` | — (not bindable until supported) |
| Composition | `Composition/time_signature` | Time signature | `composition:time_signature` | existing command (see below) |
| Composition | `Composition/tempo` | Tempo change | `composition:tempo` | Tool.composition:tempo |
| Composition | `Composition/repeat_open` | Repeat start | `composition:repeat_open` | existing command (see below) |
| Composition | `Composition/repeat_close` | Repeat end / count | `composition:repeat_close` | existing command (see below) |
| Composition | `Composition/alternate_ending` | Alternate ending | `composition:alternate_ending` | Tool.composition:alternate_ending |
| Duration | `Duration/whole_note` | Whole note | `duration:whole` | Tool.duration:whole |
| Duration | `Duration/half_note` | Half note | `duration:half` | Tool.duration:half |
| Duration | `Duration/quarter_note` | Quarter note | `duration:quarter` | Tool.duration:quarter |
| Duration | `Duration/eighth_note` | Eighth note | `duration:eighth` | Tool.duration:eighth |
| Duration | `Duration/sixteenth_note` | 16th note | `duration:sixteenth` | Tool.duration:sixteenth |
| Duration | `Duration/thirty_second_note` | 32nd note | `duration:thirtysecond` | Tool.duration:thirtysecond |
| Duration | `Duration/sixty_fourth_note` | 64th note | `duration:sixtyfourth` | Tool.duration:sixtyfourth |
| Duration | `Duration/dotted_note` | Dotted duration | `duration:dotted` | existing command (see below) |
| Duration | `Duration/double_dotted_note` | Double-dotted duration | `duration:double-dotted` | existing command (see below) |
| Duration | `Duration/tied_note` | Tie | `duration:tie` | existing command (see below) |
| Duration | `Duration/tuplet` | Triplet | `duration:tuplet` | existing command (see below) |
| Duration | `Duration/tuplet_menu` | Choose tuplet ratio | `duration:tuplet-menu` | Tool.duration:tuplet-menu |
| Dynamic | `Dynamic/ppp` | ppp | `dynamic:ppp` | Tool.dynamic:ppp |
| Dynamic | `Dynamic/pp` | pp | `dynamic:pp` | Tool.dynamic:pp |
| Dynamic | `Dynamic/p` | p | `dynamic:p` | Tool.dynamic:p |
| Dynamic | `Dynamic/mp` | mp | `dynamic:mp` | Tool.dynamic:mp |
| Dynamic | `Dynamic/mf` | mf | `dynamic:mf` | Tool.dynamic:mf |
| Dynamic | `Dynamic/f` | f | `dynamic:f` | Tool.dynamic:f |
| Dynamic | `Dynamic/ff` | ff | `dynamic:ff` | Tool.dynamic:ff |
| Dynamic | `Dynamic/fff` | fff | `dynamic:fff` | Tool.dynamic:fff |
| Effects | `Effects/vibrato` | Vibrato | `effect:vibrato` | existing command (see below) |
| Effects | `Effects/bend` | Bend (opens the bend editor) | `effect:bend` | existing command (see below) |
| Effects | `Effects/tremolo_bar` | Tremolo bar (opens the tremolo bar editor) | `effect:tremolo_bar` | existing command (see below) |
| Effects | `Effects/slides` | Slide | `effect:slides` | existing command (see below) |
| Effects | `Effects/dead_note` | Dead note | `effect:dead_note` | existing command (see below) |
| Effects | `Effects/hammer_on_pull_off` | Hammer-on / pull-off | `effect:hammer_on_pull_off` | existing command (see below) |
| Effects | `Effects/ghost_note` | Ghost note | `effect:ghost_note` | existing command (see below) |
| Effects | `Effects/accent` | Accent | `effect:accent` | Tool.effect:accent |
| Effects | `Effects/heavy_accent` | Heavy accent | `effect:heavy_accent` | Tool.effect:heavy_accent |
| Effects | `Effects/let_ring` | Let ring | `effect:let_ring` | existing command (see below) |
| Effects | `Effects/natural_harmonic` | Natural harmonic | `effect:natural_harmonic` | existing command (see below) |
| Effects | `Effects/grace_note` | Grace note | `effect:grace_note` | existing command (see below) |
| Effects | `Effects/trill` | Trill | `effect:trill` | existing command (see below) |
| Effects | `Effects/tremolo_picking` | Tremolo picking | `effect:tremolo_picking` | Tool.effect:tremolo_picking |
| Effects | `Effects/palm_mute` | Palm mute | `effect:palm_mute` | existing command (see below) |
| Effects | `Effects/staccato` | Staccato | `effect:staccato` | existing command (see below) |
| Effects | `Effects/tapping` | Tapping | `effect:tapping` | Tool.effect:tapping |
| Effects | `Effects/slapping` | Slapping | `effect:slapping` | Tool.effect:slapping |
| Effects | `Effects/popping` | Popping | `effect:popping` | Tool.effect:popping |
| Effects | `Effects/fade_in` | Fade in | `effect:fade_in` | existing command (see below) |
| Beat | `Beat/chord` | Chord | `effect:chord` | existing command (see below) |
| Beat | `Beat/chord_menu` | Choose chord | `effect:chord_menu` | Tool.effect:chord_menu |
| Beat | `Beat/text` | Text annotation | `effect:text` | existing command (see below) |
| Beat | `Beat/stroke_down` | Brush down | `effect:stroke_down` | Tool.effect:stroke_down |
| Beat | `Beat/stroke_up` | Brush up | `effect:stroke_up` | Tool.effect:stroke_up |
| Beat | `Beat/pickstroke_down` | Pickstroke down | `effect:pickstroke_down` | Tool.effect:pickstroke_down |
| Beat | `Beat/pickstroke_up` | Pickstroke up | `effect:pickstroke_up` | Tool.effect:pickstroke_up |

## Structure palette

| Group | Icon | Function | Command id | Hotkey command |
|---|---|---|---|---|
| Bar editing | `More/insert_bar` | Insert bar before the cursor | `gp:insert_bar` | existing command (see below) |
| Bar editing | `More/append_bar` | Add bar at the end | `gp:append_bar` | Tool.gp:append_bar |
| Bar editing | `More/duplicate_bar` | Duplicate bar | `gp:duplicate_bar` | Tool.gp:duplicate_bar |
| Bar editing | `More/delete_bar` | Delete bar | `gp:delete_bar` | existing command (see below) |
| Bar editing | `More/check_bar` | Check bar durations | `gp:check_bars` | existing command (see below) |
| Step through | `More/step_back` | Step back one beat | `gp:step_back` | Tool.gp:step_back |
| Step through | `More/step_forward` | Step forward one beat | `gp:step_forward` | Tool.gp:step_forward |
| Bars | `Composition/time_signature` | Time signature | `composition:time_signature` | existing command (see below) |
| Bars | `Composition/tempo` | Tempo change | `composition:tempo` | Tool.composition:tempo |
| Bars | `Composition/repeat_open` | Repeat start | `composition:repeat_open` | existing command (see below) |
| Bars | `Composition/repeat_close` | Repeat end / count | `composition:repeat_close` | existing command (see below) |
| Key and bars | `More/key_signature` | Key signature | `gp:key_signature` | existing command (see below) |
| Key and bars | `More/triplet_feel` | Triplet feel | `gp:triplet_feel` | Tool.gp:triplet_feel |
| Key and bars | `More/free_time` | Free time | `gp:free_time` | Tool.gp:free_time |
| Key and bars | `More/double_barline` | Double barline | `gp:double_barline` | Tool.gp:double_barline |
| Repeats and directions | `More/repeat_one_bar` | One-bar repeat | `gp:repeat_one_bar` | Tool.gp:repeat_one_bar |
| Repeats and directions | `More/repeat_two_bars` | Two-bar repeat | `gp:repeat_two_bars` | Tool.gp:repeat_two_bars |
| Repeats and directions | `More/directions` | Score directions | `gp:directions` | existing command (see below) |
| Markers | `More/add_marker` | Add marker | `gp:add_marker` | Tool.gp:add_marker |
| Markers | `More/marker_list` | Marker list | `gp:marker_list` | Tool.gp:marker_list |
| Markers | `More/previous_marker` | Previous marker | `gp:previous_marker` | existing command (see below) |
| Markers | `More/next_marker` | Next marker | `gp:next_marker` | existing command (see below) |

## Rhythm palette

| Group | Icon | Function | Command id | Hotkey command |
|---|---|---|---|---|
| Tuplets and ties | `More/custom_ntuplet` | N-tuplet | `gp:custom_ntuplet` | Tool.gp:custom_ntuplet |
| Tuplets and ties | `More/tie_note` | Tie note | `gp:tie_note` | Tool.gp:tie_note |
| Tuplets and ties | `More/tie_beat` | Tie beat / chord | `gp:tie_beat` | Tool.gp:tie_beat |
| Sounding pitch and duration | `More/sound_duration` | Sound duration | `gp:sound_duration` | Tool.gp:sound_duration |
| Sounding pitch and duration | `More/octave_8va` | 8va — octave above | `gp:octave_8va` | Tool.gp:octave_8va |
| Sounding pitch and duration | `More/octave_8vb` | 8vb — octave below | `gp:octave_8vb` | Tool.gp:octave_8vb |
| Sounding pitch and duration | `More/octave_15ma` | 15ma — two octaves above | `gp:octave_15ma` | Tool.gp:octave_15ma |
| Sounding pitch and duration | `More/octave_15mb` | 15mb — two octaves below | `gp:octave_15mb` | Tool.gp:octave_15mb |

## Layout palette

| Group | Icon | Function | Command id | Hotkey command |
|---|---|---|---|---|
| Voices | `More/voice_lead` | Voice 1 | `gp:voice_1` | Tool.gp:voice_1 |
| Voices | `More/voice_bass` | Voice 2 | `gp:voice_2` | Tool.gp:voice_2 |
| Voices | `More/inactive_voice_gray` | Gray inactive voice | `gp:inactive_voice_gray` | Tool.gp:inactive_voice_gray |
| System layout | `More/force_line_break` | Force line break | `gp:force_line_break` | Tool.gp:force_line_break |
| System layout | `More/prevent_line_break` | Prevent line break | `gp:prevent_line_break` | Tool.gp:prevent_line_break |
| Beaming | `More/beam_auto` | Automatic beaming | `gp:beam_auto` | Tool.gp:beam_auto |
| Beaming | `More/beam_force` | Force beam group | `gp:beam_force` | Tool.gp:beam_force |
| Beaming | `More/beam_break` | Break primary beam | `gp:beam_break` | Tool.gp:beam_break |
| Beaming | `More/beam_break_secondary` | Break secondary beam | `gp:beam_break_secondary` | Tool.gp:beam_break_secondary |
| Stems | `More/stem_auto` | Automatic stem direction | `gp:stem_auto` | Tool.gp:stem_auto |
| Stems | `More/stem_invert` | Invert stem direction | `gp:stem_invert` | Tool.gp:stem_invert |

## Commands (Settings → Shortcuts)

Default keys are the **TabForge** preset, currently identical to the **Classic** preset. The **Alternative** column shows where that preset differs (blank = same as TabForge, "—" = unbound).

| Category | Command | What it does | Id | TabForge / Classic | Alternative |
|---|---|---|---|---|---|
| File and tabs | New score | Create a new score in a new tab. | `File.New` | Ctrl+N |  |
| File and tabs | New from template | Create a new score from a built-in or saved template (File > New from template). | `File.NewFromTemplate` | — | Also File > New from template. |
| File and tabs | Save as template | Save a copy of the active score as a template in the templates folder. | `File.SaveAsTemplate` | — | Templates are stored in %APPDATA%\TabForge\Templates. |
| File and tabs | Open score | Open a .tforge or score file in the current tab. | `File.Open` | Ctrl+O |  |
| File and tabs | Open score in new tab | Open a .tforge or score file in a new tab. | `File.OpenInNewTab` | Ctrl+Shift+O |  |
| File and tabs | Save | Save the active score. | `File.Save` | Ctrl+S |  |
| File and tabs | Save as | Save the active score under a new name. | `File.SaveAs` | Ctrl+Shift+S | F12 |
| File and tabs | Cancel import | Cancel the score import(s) running in the background (same as the status-bar Cancel button). | `File.CancelImport` | — | Unbound by default; Esc is used by other commands. |
| File and tabs | Export PDF | Export the engraved score (notation and tab) as a PDF file. | `File.ExportPdf` | — | Also File > Export PDF. |
| File and tabs | Export MusicXML | Export the song as uncompressed MusicXML (.musicxml): a part per track with notation and a tab staff. | `File.ExportMusicXml` | — | Also File > Export MusicXML. |
| File and tabs | Export MIDI | Export the song as a standard MIDI file (File > Export MIDI). | `File.ExportMidi` | — | Also File > Export MIDI. |
| File and tabs | Export ASCII tab | Export the tablature as a plain-text ASCII tab file (File > Export ASCII tab). | `File.ExportAscii` | — | Also File > Export ASCII tab. |
| File and tabs | Export compatible .gp file | Write a compatible .gp copy for other programs. If the song uses something that file cannot hold, you choose first: keep a full TabForge copy, export the compatible file only, or cancel. | `File.ExportGuitarPro` | — | Also File > Export compatible .gp file. |
| File and tabs | Project settings | Open the project settings: song info, credits, tempo and key (the toolbar's Project settings button). | `File.ProjectSettings` | — | Also the toolbar's Project settings button. |
| File and tabs | Command palette | Search every command by name, see its key and run it. | `App.CommandPalette` | Ctrl+Shift+A | Also File > Command palette. |
| File and tabs | Render to audio file | Render the song to WAV / MP3 (master mix and / or stems) faster than realtime. | `File.Render` | Ctrl+Alt+R |  |
| File and tabs | Print | Print the active score. | `File.Print` | Ctrl+P |  |
| File and tabs | Print preview | Preview the printed page. | `File.PrintPreview` | Ctrl+Shift+P |  |
| File and tabs | New tab | Open another score in a new tab. | `Tab.New` | Ctrl+T |  |
| File and tabs | Close tab | Close the active tab. | `Tab.Close` | Ctrl+W |  |
| File and tabs | Duplicate tab | Duplicate the active tab as an unsaved copy. | `Tab.Duplicate` | Ctrl+Shift+D | — |
| File and tabs | Preferences | Open this settings window. | `App.Preferences` | F12 | F7 |
| File and tabs | Keyboard shortcuts | Show the shortcut reference. | `App.Shortcuts` | F1 |  |
| Editing | Undo | Undo the last edit. | `Edit.Undo` | Ctrl+Z |  |
| Editing | Redo | Redo the last undone edit. | `Edit.Redo` | Ctrl+Y |  |
| Editing | Copy | Copy the selected beats or bars, or the beat at the cursor. | `Edit.Copy` | Ctrl+C |  |
| Editing | Cut | Cut the selected beats or bars (later beats move up; whole bars are removed, or emptied on one track of several). | `Edit.Cut` | Ctrl+X |  |
| Editing | Paste | Paste at the cursor; asks only what the paste needs. | `Edit.Paste` | Ctrl+V |  |
| Editing | Paste special | Paste with a repeat count, replace or insert, octave shift, keep string and fret, and bar settings. | `Edit.PasteSpecial` | Ctrl+Shift+V | — |
| Editing | Select whole track | Select every bar of the track. | `Edit.SelectAll` | Ctrl+A |  |
| Editing | Insert beat | Insert an empty beat at the cursor and push the rest of the bar right (the bar keeps its length). | `Edit.InsertBeat` | Insert |  |
| Editing | Delete beats (shift left) | Remove the beat at the cursor and pull the rest of the bar left. Remove the beat at the cursor and pull the rest of the bar left. The Delete key clears the notes first, and removes an empty beat the same way. | `Edit.DeleteBeats` | — | Also Edit > Delete beats. |
| Editing | Repeat selection | Repeat the selected bars. | `Edit.RepeatSelection` | Ctrl+Shift+R | Was Ctrl+R; Ctrl+R now records. |
| Transport | Play / pause | Start or pause playback. | `Transport.PlayPause` | Space |  |
| Transport | Play from the start | Restart playback from the beginning. | `Transport.PlayFromStart` | Shift+Space |  |
| Transport | Stop | Stop playback and return to the edit cursor. | `Transport.Stop` | Ctrl+OemPeriod |  |
| Transport | Loop | Play the loop range repeatedly. | `Transport.Loop` | F9 | F9 |
| Transport | Metronome on / off | Turn the metronome click on or off (the same as the transport's Metronome button). | `Transport.Metronome` | — | Also Tools > Metronome. |
| Transport | Count-in on / off | Turn the count-in before playback on or off (the same as the transport's Count-in button). | `Transport.CountIn` | — | Also Tools > Count-in. |
| Bars and sections | Insert bar | Insert an empty bar before the cursor. | `Bar.Insert` | Ctrl+Insert |  |
| Bars and sections | Delete bar | Delete the bar at the cursor. | `Bar.Delete` | Ctrl+Delete |  |
| Bars and sections | Mix table | Change instrument, volume, pan, effects or tempo from the selected beat. | `Beat.MixTable` | F10 |  |
| Bars and sections | Add section | Add a new section (marker) at the cursor bar. | `Section.Add` | M |  |
| Bars and sections | Time signature | Change the time signature. | `Bar.TimeSignature` | Ctrl+Shift+T |  |
| Bars and sections | Key signature | Change the key signature. | `Bar.KeySignature` | Ctrl+K |  |
| Bars and sections | Clef | Change the clef of the track. | `Bar.Clef` | K |  |
| Bars and sections | Directions | Edit repeat/DC/DS directions. | `Bar.Directions` | D |  |
| Bars and sections | Go to bar | Jump to a bar number. | `Bar.GoTo` | Ctrl+G |  |
| Bars and sections | Read current bar | Announce every beat of the current bar (for screen readers; also shown in the status bar). | `Reader.ReadBar` | Ctrl+Alt+B |  |
| Bars and sections | Read position | Announce the track, bar, beat, string, section, time signature, tempo and time of the cursor (for screen readers; also shown in the status bar). | `Reader.ReadPosition` | Ctrl+Alt+P |  |
| Bars and sections | First bar | Jump to the first bar. | `Bar.First` | Ctrl+Home | Ctrl+Shift+Left |
| Bars and sections | Last bar | Jump to the last written beat of the last bar. | `Bar.Last` | Ctrl+End | Ctrl+Shift+Right |
| Bars and sections | Check bars | Report bars that do not fill their time signature. | `Bar.Check` | F4 |  |
| Bars and sections | Score information | Edit title, artist and other score information. | `Bar.ScoreInfo` | F5 |  |
| Bars and sections | Section editor | Add or edit a section marker. | `Section.Edit` | Shift+Insert |  |
| Bars and sections | Previous section | Jump to the previous section. | `Section.Previous` | Alt+Shift+Left | Alt+Left |
| Bars and sections | Next section | Jump to the next section. | `Section.Next` | Alt+Shift+Right | Alt+Right |
| Notes and tracks | Chord name | Attach a chord name to the beat. | `Note.Chord` | A |  |
| Notes and tracks | Beat text | Attach text to the beat. | `Note.Text` | T |  |
| Notes and tracks | Add track | Add a new track. | `Track.Add` | Ctrl+Alt+T (Hotkey 1), Ctrl+Shift+Insert (Hotkey 2) | The Classic preset swaps them: Ctrl+Shift+Insert is Hotkey 1. Menus and tooltips show whichever keys are set. |
| Notes and tracks | Delete track | Delete the selected track. | `Track.Delete` | Ctrl+Shift+Delete |  |
| Notes and tracks | Track properties | Edit the selected track's properties. | `Track.Properties` | F6 |  |
| Transport | Speed up | Playback speed to the next preset (50, 75, 100, 125, 150, 200 %). | `Playback.SpeedUp` | Ctrl+Alt+Up |  |
| Transport | Slow down | Playback speed to the previous preset (50, 75, 100, 125, 150, 200 %). | `Playback.SpeedDown` | Ctrl+Alt+Down |  |
| Transport | Reset speed to 100 % | Play at the song's own tempo (100 %). | `Playback.SpeedReset` | Ctrl+Alt+D0 |  |
| Notes and tracks | Move track up | Move the selected track up one place in the track list. In the Mixer window it moves the selected mixer row (a track, or a whole group) up; a track passing the top of its group joins the group above. | `Track.MoveUp` | Alt+Shift+Up |  |
| Notes and tracks | Move track down | Move the selected track down one place in the track list. In the Mixer window it moves the selected mixer row (a track, or a whole group) down; a track passing the end of its group joins the group below. | `Track.MoveDown` | Alt+Shift+Down |  |
| Notes and tracks | Transpose | Transpose the notes of the selected track (or only the selected bars) by a number of semitones, in every voice. Drum tracks are skipped. One undo step. | `Tools.Transpose` | — |  |
| Notes and tracks | Convert track to audio track | Turn the selected instrument track into an audio track; its notation becomes a MIDI clip. | `Track.ConvertToAudio` | — | No default key. |
| Notes and tracks | Next track | Select the next track. | `Track.Next` | Ctrl+Shift+Down |  |
| Notes and tracks | Previous track | Select the previous track. | `Track.Previous` | Ctrl+Shift+Up |  |
| View | Show track list | Scroll the track list (the mixer table with every track) into view; click a track's colour block there to jump to it. | `View.Multitrack` | F3 |  |
| View | Show / hide arrangement overview | Show or hide the Arrangement (timeline) panel, the same as View > Arrangement overview. | `View.Global` | F8 |  |
| View | Fullscreen | Toggle fullscreen. | `View.Fullscreen` | F11 |  |
| View | Zoom in | Increase the score zoom. | `View.ZoomIn` | Ctrl+OemPlus |  |
| View | Zoom out | Decrease the score zoom. | `View.ZoomOut` | Ctrl+OemMinus |  |
| Notes and tracks | Repeat previous beat | Copy the previous beat onto the cursor. | `Note.RepeatBeat` | C | — |
| Durations | Longer note value | Make the note value one step longer (16th to 8th to quarter...). Numpad - does the same. Preferences > Editing > "Reverse + / - duration keys" swaps the two default keys. | `Note.Longer` | OemMinus (the - key) |  |
| Durations | Shorter note value | Make the note value one step shorter (quarter to 8th to 16th...). Numpad + and Shift+= do the same. Preferences > Editing > "Reverse + / - duration keys" swaps the two default keys. | `Note.Shorter` | OemPlus (the + / = key) |  |
| Notes and tracks | Shift pitch up (semitone) | Raise the selected note by a semitone. | `Note.PitchUp` | Shift+Up |  |
| Notes and tracks | Shift pitch down (semitone) | Lower the selected note by a semitone. | `Note.PitchDown` | Shift+Down |  |
| Notes and tracks | Move note to higher string | Move the selected note(s) to the next higher string, keeping the pitch (the fret is recalculated). Nothing changes when the pitch cannot be played there or the string is taken in that beat. On an empty spot the cursor moves to the higher string. The plain Up arrow moves only the cursor. | `Note.MoveStringUp` | Alt+Up |  |
| Notes and tracks | Move note to lower string | Move the selected note(s) to the next lower string, keeping the pitch (the fret is recalculated). Nothing changes when the pitch cannot be played there or the string is taken in that beat. On an empty spot the cursor moves to the lower string. The plain Down arrow moves only the cursor. | `Note.MoveStringDown` | Alt+Down |  |
| Notes and tracks | Rest | Turn the beat into a rest (or back). | `Note.Rest` | R |  |
| Notes and tracks | Tie note | Tie the note to the previous one. | `Note.Tie` | L |  |
| Notes and tracks | Fermata | Hold the beat. | `Note.Fermata` | F | — |
| Note effects | Accent | Toggle an accent on the beat or selection (same as the Accent tool). | `Note.Accent` | Oem1 |  |
| Note effects | Staccato | Play the note short. | `Note.Staccato` | Shift+D1 |  |
| Note effects | Tenuto | Hold the note for its full value. | `Note.Tenuto` | Shift+OemMinus |  |
| Note effects | Bend | Open the bend editor (same as the Bend tool). | `Note.Bend` | B |  |
| Note effects | Bend editor | Open the bend editor: draw the bend curve or pick a preset. | `Note.BendEditor` |  |  |
| Note effects | Hammer-on / pull-off | Legato to the next note. | `Note.HammerPull` | H |  |
| Note effects | Vibrato | Left-hand vibrato. | `Note.Vibrato` | V |  |
| Note effects | Legato slide | Slide into the next note. | `Note.Slide` | S |  |
| Note effects | Let ring | Let the note ring over. | `Note.LetRing` | I |  |
| Note effects | Dead note | Muted, percussive note. | `Note.Dead` | X |  |
| Note effects | Ghost note | Very soft note in brackets. | `Note.Ghost` | O |  |
| Note effects | Natural harmonic | Open the harmonic editor (same as the Harmonic tool); on a note that has a harmonic, remove it. | `Note.Harmonic` | Y |  |
| Note effects | Trill | Open the trill editor (same as the Trill tool). | `Note.Trill` | N |  |
| Note effects | Tremolo bar | Open the tremolo bar editor (same as the Tremolo bar tool). | `Note.TremoloBar` | W |  |
| Note effects | Tremolo bar editor | Open the tremolo bar editor: draw the whammy curve or pick a preset. | `Note.TremoloBarEditor` |  |  |
| Note effects | Grace note | Open the grace note editor (same as the Grace tool). | `Note.Grace` | G |  |
| Note effects | Trill editor | Open the trill editor: set the trill fret and speed or pick a preset. | `Note.TrillEditor` |  |  |
| Note effects | Grace note editor | Open the grace note editor: fret, position, duration, dynamic and transition. | `Note.GraceEditor` |  |  |
| Note effects | Harmonic editor | Open the harmonic editor: pick the harmonic type and its fret. | `Note.HarmonicEditor` |  |  |
| Note effects | Palm mute | Palm-muted note. | `Note.PalmMute` | P |  |
| Note effects | Fade in | Volume swell in. | `Note.FadeIn` | Shift+OemComma | F |
| Note effects | Fade out | Volume swell out. | `Note.FadeOut` | Shift+OemPeriod |  |
| Durations | Dotted | Add or remove a dot. | `Note.Dot` | OemPeriod | Multiply |
| Durations | Double dotted | Make the beat double-dotted. | `Note.DoubleDot` | — |  |
| Durations | Triplet | Toggle triplet (numpad / also works). | `Note.Triplet` | OemQuestion |  |
| Bars and sections | Repeat start | Start a repeated passage. | `Bar.RepeatOpen` | OemOpenBrackets |  |
| Bars and sections | Repeat end | End a repeated passage. | `Bar.RepeatClose` | OemCloseBrackets |  |
| View | Switch instrument view | Cycle the instrument panel between fretboard, keyboard and drum pads (this session; the default is in Settings > Fretboard & Keyboard). | `View.InstrumentView` | — |  |
| View | Show / hide side panel | Hide the side panel (tools, sections, practice) for more score space, or bring it back. | `View.SidePanel` | — |  |
| View | Show / hide fretboard / keyboard | Hide the instrument panel (fretboard, keyboard or drum map) for more score space, or bring it back (same as View > Instrument view). | `View.InstrumentPanel` | — | Also the fretboard button next to the side-panel button in the toolbar. |
| View | Show / hide Band view | Switch to the Band layout (every track's instrument and tab in rows, with the arrangement below), or back to the layout you left (same as View > Band view). | `View.BandView` | — |  |
| View | Band view: show / hide the selected track's row | Add the selected track's row to the Band view, or take it out (the same as clicking its pill at the top of the Band view). | `Band.ToggleTrackRow` | — |  |
| View | Band view: more rows per screen | Show one more Band view row on the screen at once (1 to 5, 3 by default); further rows scroll. | `Band.RowsMore` | — |  |
| View | Band view: fewer rows per screen | Show one fewer Band view row on the screen at once (1 to 5, 3 by default), so each row is taller. | `Band.RowsFewer` | — |  |
| View | Band view: lane content (Tab / Notation / Both) | Switch what the Band view lanes show: the tab, the notation, or both. | `Band.CycleLaneContent` | — |  |
| View | Band view: lane layout (Vertical / Horizontal) | Switch the Band view lanes between lines stacked down the lane (vertical) and one line that slides sideways (horizontal). | `Band.CycleLaneLayout` | — |  |
| View | Band view: instrument size | Switch the Band view instruments between the full neck (or keyboard), the first 12 frets, and a small keyboard. | `Band.CycleInstrumentSize` | — |  |
| View | Band view: smooth / page follow | Switch the Band view lanes between following like the score (its Follow settings) and the Band view own smooth or page-by-page follow. | `Band.ToggleSmoothFollow` | — |  |
| View | Band view: reset row heights | Give every Band view row the shared height again (undoes rows resized one by one). | `Band.ResetRowHeights` | — |  |
| View | Fretboard position: toggle top / bottom | Move the fretboard / keyboard pane between above the score (default) and below it, above the timeline (same as the fretboard's right-click menu > Position). | `View.FretboardPosition` | — | Also in Settings > Fretboard & Keyboard. |
| View | Lock fretboard size | Lock or unlock the fretboard / keyboard pane's height. Unlocked, dragging its edge resizes it and the drawing scales to fit; locked, it keeps its size. | `View.LockInstrumentSize` | — | Also in the fretboard's right-click menu; unlocked by default. |
| View | Layout: Compose | Switch to the Compose workspace layout (score and tab editor large, fretboard and tools, small arrangement). | `View.LayoutCompose` | Ctrl+D1 |  |
| View | Layout: Practice | Switch to the Practice workspace layout (score and fretboard large, sections panel). | `View.LayoutPractice` | Ctrl+D2 |  |
| View | Layout: Mix | Switch to the Mix workspace layout (arrangement and mixer large, small score). | `View.LayoutMix` | Ctrl+D3 |  |
| Transport | Record | Record every armed track while the song plays; press again to stop. | `Transport.Record` | Ctrl+R |  |
| View | Snap clips on / off | Turn snapping of audio and MIDI clips on or off (right-click the snap button for its settings). | `Timeline.Snap` | Alt+S | Common default. |
| Transport | Arm track for recording | Monitor the input (audio or MIDI) through the selected track and record it with Record. | `Track.Arm` | — | No default key for this either. |

## Dropping files on the timeline

- Drag audio (.wav, .mp3, .flac, .ogg, .aif/.aiff, .m4a, .wma) or MIDI (.mid, .midi) files from Windows or from a plug-in editor onto a track. A translucent block shows where they will land: at the snapped position (hold Alt while dragging to place freely), as long as the files, on the lane under the pointer when it is free there, otherwise on the first free lane, or on a new lane (shown opening under the track). Below the last track a new track opens. One drop is one undo step.
- A MIDI file becomes a MIDI clip that plays through the track's instrument and chain, placed beat for beat on the song's tempo. Plug-in temp files and in-memory files are copied into the song's media folder.
- Song files (.tforge, .gp, .gp3, .gp4, .gp5, .gpx) dropped anywhere on the window open in new tabs.
- No command or key: dropping is mouse-only (*Add audio file…* on a lane's right-click menu places files the same way).

## Audio and MIDI clips (only while a clip or clip lane is selected)

These share keys with score commands on purpose: they act only while a clip is selected (click a clip) or a clip lane was clicked. Clicking the score gives the keys back to note editing.

| Function | Description | Command id | Default |
|---|---|---|---|
| Delete clip | Delete the selected clip. | `Clip.Delete` | Delete |
| Deselect clip | Deselect the clip. | `Clip.Deselect` | Escape |
| Move clip left / right | One beat earlier / later. | `Clip.NudgeLeft` / `Clip.NudgeRight` | Left / Right |
| Move clip left / right (fine) | 10 ms earlier / later. | `Clip.NudgeLeftFine` / `Clip.NudgeRightFine` | Shift+Left / Shift+Right |
| Drag a clip | Drag to another lane, another track or below the last track (new track); Ctrl+drag copies, Alt places freely, Esc cancels. | mouse | |
| Lane above / below | Move the clip to the lane above / below (a new lane under the last). | `Clip.LaneUp` / `Clip.LaneDown` | Up / Down |
| Copy / cut / paste clip | Paste goes to the lane position last clicked. | `Clip.Copy` / `Clip.Cut` / `Clip.Paste` | Ctrl+C / Ctrl+X / Ctrl+V |
| Duplicate clip | Right after itself. | `Clip.Duplicate` | Ctrl+D |
| Copy / cut / paste track | While a track row of the track list has the focus (not the score or timeline); Paste goes after the focused track under a unique name. Also in the track row's right-click menu. | `TrackRow.Copy` / `TrackRow.Cut` / `TrackRow.Paste` | Ctrl+C / Ctrl+X / Ctrl+V |
| Duplicate track | Right after itself, while a track row has the focus. | `TrackRow.Duplicate` | Ctrl+D |
| Delete track (track row) | Asks first (Enter keeps the track); undo restores it. | `TrackRow.Delete` | Delete |
| Delete selected bars | While bars are selected on the timeline and the timeline has the focus (a drag across bars gives it the focus), or "Delete…" in the range menu: opens a prompt (clear, remove and close the gap, insert a gap before or after; All tracks (default) or This track; arrows, Enter, Esc). "Remember my answer" makes Delete do that directly (Settings > Editing > Safety turns the prompt back on). | `Range.Delete` | Delete |
| Delete selected bars (leave a gap) | Empties the bars of every track and leaves a gap (the bar count, sections and clips stay). | `Range.Clear` | |
| Delete selected bars (close the gap) | Removes the bars and closes the gap: later bars, sections, markers and clips move earlier, clips inside go, clips across an edge are cut. One track: that track shifts left and empty bars fill its end. | `Range.Remove` | Ctrl+Delete |
| Insert a gap before the selection | Empty bars as long as the selection in front of it; the selection and later clips move later. | `Range.InsertBefore` | Ctrl+Shift+Space |
| Insert a gap after the selection | Empty bars as long as the selection right after it. | `Range.InsertAfter` | |
| Split clip | At the edit cursor (the spot last clicked on the lane), or under the playhead; both parts keep offset, gain, pitch and speed. | `Clip.Split` | S |
| Glue clips | Joins the clip with the clips that continue it on its lane (same file, speed, pitch and level). | `Clip.Glue` | Ctrl+Shift+G |
| Reset clip fades | Removes the fade-in and fade-out (drag the handles at the clip's top corners to set them). | `Clip.FadeReset` | |
| Mute clip | Mute or unmute. | `Clip.Mute` | Ctrl+M |
| Clip properties | Name, volume, pitch, speed. | `Clip.Properties` | F2 |
| View | Mixer | Open the mixer: track and group levels, pan, pitch, sound source and FX chains. | `View.Mixer` | — |  |
| View | Horizontal score scrolling | Switch the score between wrapped lines (scroll down) and one line (scroll right). | `View.HorizontalScroll` | — |  |
| View | Smooth page-turn follow | Switch playback follow between instant and smooth (glided) page turns. | `View.SmoothFollow` | — |  |
| View | Highlight playing bar | Shade the whole bar that is playing (staff and tab) with a translucent band behind the notes; off by default. Colour, opacity and "Also show the cursor's bar when stopped" are in Preferences > Playback > Appearance (the "when stopped" option under More options). | `View.PlayingBar` | — |  |
| View | Show tracks in groups | Show or hide a header per mixer group in the track list (same setting as the Mixer's "Groups in track list" box and the track list's right-click menu; off by default). | `View.ShowTrackGroups` | — |  |
| View | Track FX chain | Open the selected track's FX (plug-in) chain. | `Track.FxChain` | — |  |
| View | Auto-resize track list to fit | Turn on / off growing and shrinking the track list to fit all tracks and group rows. | `View.AutoFitTrackList` | — |  |
| View | Reset track row height | Put the track rows back to their default height (stretched by dragging the track list's splitter) and fit the track list to them. Double-click the splitter does the same. | `View.ResetTrackRowHeight` | — |  |
| View | Plug-in wiring | Open the wiring window (audio pins, sidechain 3/4, MIDI input, channel filter and MIDI output forwarding) of the selected track's selected plug-in. | `Track.Wiring` | — |  |
| View | Manage linked audio approvals | Open the Linked audio window: folders on network locations or removable drives this song is waiting on, and the approvals you gave earlier (revoke them there). Also in Preferences > Files & Backups. | `Media.ManageApprovals` | — |  |
| View | Master FX chain | Open the master effects chain (applied to the whole mix after the group buses). | `Mixer.MasterFx` | — |  |
| View | Mixer: Monitor FX | Open the monitoring effects chain (e.g. speaker calibration). It plays after the master, live only: never included in renders or exports. | `Mixer.MonitorFx` | — |  |
| View | Mixer: collapse all groups | Collapse every mixer group to its group row (the track list's group boxes follow). | `Mixer.CollapseAllGroups` | — |  |
| View | Mixer: expand all groups | Expand every mixer group to show its tracks. | `Mixer.ExpandAllGroups` | — |  |
| View | Mixer: group rules | Open the group rules editor: which instruments go in which mixer group. | `Mixer.GroupRules` | — |  |
| View | Group bus FX chain | Open the effects bus chain of the selected track's mixer group (its tracks sum into it before the master). | `Mixer.GroupFx` | — |  |
| View | Plug-in MIDI processing | Open the MIDI processing window (filter, transpose, drum map, velocity, humanize, delay, program / CC, log) of the selected track's selected plug-in. | `Track.MidiProcessing` | — |  |
| View | Clear scale highlight | Remove the highlighted scale from the fretboard and keyboard. | `View.ClearScale` | — |  |
| View | Scale highlight brighter | Make the scale highlight on the fretboard and keyboard 10% stronger (up to 150%). The same setting as Preferences > Fretboard > Appearance > Scale highlight strength. | `View.ScaleHighlightBrighter` | — |  |
| View | Scale highlight dimmer | Make the scale highlight on the fretboard and keyboard 10% weaker (down to 10%). The same setting as Preferences > Fretboard > Appearance > Scale highlight strength. | `View.ScaleHighlightDimmer` | — |  |
| View | Show or hide the Add-track lane | Show or hide the Add track strip under the last track. The same setting as Preferences > Timeline & Tracks > Show the Add-track lane. | `View.ToggleAddTrackLane` | — |  |
| View | Toggle lines between tracks | Show or hide the thin lines between track rows in the timeline and the track list. The same setting as Preferences > Timeline & Tracks > Show lines between tracks. | `View.ToggleTrackLines` | — |  |
| View | Cycle playback position marker | Switch the timeline's playback position marker between Line (default), Bar marker and Both. The same setting as Preferences > Timeline & Tracks > Playback position marker. | `View.CyclePlayheadStyle` | — |  |
| View | Cycle fretboard string spacing | Switch the fretboard string spacing between Compact, Natural (default) and Wide (at most 1.5x natural). The same setting as right-click the fretboard > Appearance > String spacing. | `View.CycleStringSpacing` | — |  |
| View | Scale finder | Find which scales the selected notes (or the whole song) fit, or pick any scale, and highlight it on the fretboard. | `Tools.ScaleFinder` | — |  |
| View | Chord finder | Show the notes of a chord for a root and type, and insert its name on the beat under the score cursor. | `Tools.ChordFinder` | — |  |
| View | Song stats | Show the number of tracks, bars, notes and sections in the song. | `Tools.SongStats` | — |  |
| View | Tuner | Open the chromatic tuner: the note and cents of the sound on the armed input, with the selected track's string tunings. | `Tools.Tuner` | — |  |
| View | Tutorial | Open the Beginner's Guide: searchable chapters with pictures, and a PDF export (Help > Tutorial). | `Help.Tutorial` | — | Also Help > Tutorial…; F1 stays with Keyboard shortcuts. Inside the window: Ctrl+F search, Esc clears it, Alt+Left / Alt+Right back / forward; the switch at the top changes between the Basic and the Detailed guide. |
| View | Detailed guide | Open the Tutorial window on the Detailed Guide, the full reference to every feature (Help > Detailed guide). | `Help.TutorialDetailed` | — | Also Help > Detailed guide…; unbound by default. |
| View | Check for updates | Ask GitHub whether a newer TabForge release exists (one anonymous HTTPS request). | `Help.CheckForUpdates` | — |  |

## Hotkey 1 and Hotkey 2

Every command has two shortcut slots in **Settings → Shortcuts**: Hotkey 1 (the key shown in the tables above and in menus and tooltips) and Hotkey 2, an optional extra key for the same command. Each slot has its own button and its own **Clear**; **Reset** returns both to their defaults. A key can belong to only one slot of one command per context: pressing a key that is already used offers **Reassign**, which takes it from the other slot only. If a Hotkey 1 and a Hotkey 2 ever name the same key, the Hotkey 1 wins. Settings files saved before Hotkey 2 existed load as Hotkey 1; a command whose Hotkey 1 was changed or cleared there never gets a default Hotkey 2 added behind your back.

## Keeping presets current

Presets are stored as *differences* from the defaults (`HotkeyPresets` in `Services/HotkeyCatalog.cs`). A new command or palette icon automatically appears in every preset with its default key; only add a preset override when that application binds it differently. Keys on the numeric keypad for fret entry, arrows, Home/End, Insert/Delete beat and +/- durations stay fixed editor keys.

## Global tuning button (arrangement header)
- Left-click fork icon: global tuning window (presets + per-string, all instruments). Setting: Timeline > Developer > 'Tuning icon opens the global tuning window' (default on).
- Left-click number: type a semitone shift. Double-click: edit the number inline (Enter / click away saves, Esc cancels).
- Right-click: quick menu (+1 / -1 / Tune by / window / back to original).

## Track volume / pan
- .gp5-style 0-16 volume and -8..+8 pan sliders with the value on the handle; double-click resets (volume 13, pan 0). Settings > Timeline & Tracks > Track list: slider or knob.

## Rotary knobs (control behaviour, not bindable commands)
- Drag (circular or up/right), mouse wheel, arrow keys: change the value. Shift = fine.
- Double-click, right-click, or F2 / Enter on a focused knob: type an exact value ("2.1", "-6 dB", "75%", "2:1", "100 ms"; comma decimals). Enter or clicking away applies, Esc cancels, a red outline marks an invalid entry. A knob with its own right-click menu has "Type value…" as the first item.
- Ctrl+click (or Home): reset to the default. This replaces the old double-click reset on knobs.
- Not in the hotkey presets: these are control gestures, not commands.


## Loop (transport)
- Left-click / F9 toggles; right-click opens loop settings: number of loops (empty/∞ = forever; loops-left badge beside the button), count-in before every loop, simple loop or speed trainer (from % to %, +% per loop).
- Section right-click > Loop section (ticked while that section loops; click again to stop).

## Metronome
- Boosted (layered) click on by default; count-in volume slider (default 70%).


## Selected area (timeline)
- Drag across bars (lanes or ruler) to select an area; it stays highlighted even with loop off. Play plays normally; with Loop on it loops the area.
- Right-click inside the area: Copy / Cut / Paste before / Move (area pulses, click the new position; Esc or right-click cancels) / Delete / Loop / Skip during playback / Clear selection.

## Count-in
- Right-click the count-in button: volume (default 70%), length 1-4 bars, click sound.


## Score appearance
- Score right-click > Playback appearance > Text & fonts…: per area (fret numbers, techniques, chords, text/lyrics, bar info, header, other) font, size, bold, italic, colour, outline colour + thickness. Live preview; Cancel restores.
- Playback line / duration glow colour: colour chooser with preset dropdown, swatches, hue/saturation/brightness and hex (live preview).


## Instruments
- Track instrument button (timeline): six quick picks, then every GM family as a hover submenu with badges (all 128 GM programs + GM drum kits), plus VST instruments.
- Track properties > Instrument > Change...: searchable catalogue with pictures (type to filter, Enter picks the first match, double-click selects). Drum kits switch the track to channel 10; the badge background follows the track colour.


## Add track
- The arrangement's **+ Track** button opens the Add track window: the same window as Track properties (instrument catalogue via *Change…*, MIDI program/channel, mixer, tuning, frets, capo, name, colour, notes) plus **Position**: last (default), first, after the selected track, or as track number N. *Add track* adds it; Cancel adds nothing. Track > Add Guitar/Bass/Drums/Keys still add a default track instantly.
- In Sections, **Add at cursor…** shows the starting bar before adding a marker. Click a section or select it and press Enter to navigate. Edit opens its displayed colour; with similar-section matching enabled, a colour change applies to that name group.

## Instrument panel and scale finder
- The instrument panel matches the track out of the box: a fretboard with the track's own strings for stringed instruments (guitar, bass, oud, cello, violin, ukulele, mandolin, banjo… by name or GM program), drum pads for drums, and a keyboard (88 keys) for piano, winds, brass, synths and everything else. Settings > Fretboard & Keyboard > *Default instrument view* can instead always show a fretboard, keyboard or drums; *Keyboard size* picks 88/76/61/49/37/25 keys (smaller keyboards follow the notes).
- Right-click the instrument panel: *Show this track as* (this track only) or *Show all tracks as* (every track, this session); *Keyboard size* when a keyboard is shown. Bindable: *Switch instrument view* (cycles this track; no default key).
- Right-click > *Scale* > *Select scale* (quick list) or *Find scale…*; also Tools > Scale finder… and the **Scales** button under the colour legend beside the fretboard (top-right in keyboard/drums view). Bindable: *Scale finder* (no default key).
- Scale finder window: *Likely scales* analyses the notes (weighted by length; drums and dead notes ignored) of the **selection** (score or timeline area) or the **entire song** — selection by default when there is one — for the selected track or all tracks, and lists the scales and keys they fit, best first (several usually fit). *All scales* picks any key + scale without searching. *Show on fretboard* highlights it; *Clear highlight* removes it.
- Right-click > *Scale* > *Clear selection* removes the highlighted scale. Bindable: *Clear scale highlight* (no default key).
- The keyboard view highlights the scale's keys like the fretboard (the root more strongly), with the same right-click scale tools.
- Right-click > *Appearance* (saved for every song and window; also Settings > Fretboard & Keyboard): *Key colours* on the keyboard (match the theme: grey in dark, white in light; or always grey / white); *Scale highlight style* (Shaded, Circles, or Rings) and *Scale highlight colour*; on the fretboard, *Fret marker colour* and *Fret marker brightness* for the position dots (default: slightly brighter than before; *Original* restores the old look). The scale finder window has the same highlight style and colour choices.
- Songs double-clicked in Explorer open as a new tab in the running TabForge (Settings > Files & Backups > *Open songs from Explorer in*: a new tab, the default, or a new window).
- Note > Shift pitch down on an open string moves the note to the same pitch on the next lower free string.

## Mixer and FX chains
- **Mixer** button (fader icon, beside the tuning fork) or the *Mixer* hotkey: one strip per track, grouped **by instrument** (Guitars, Basses, Keys — piano, organ, synth —, Drums, Other), **compact** (Guitars, Basses, Drums, all other instruments) or **no groups**. The group strip in front of each group sets level (%), pan and pitch (semitones) for all its tracks, with group mute / solo. Right-click a track strip to move it to another group. Changes are live and undoable, and saved with the song.
- **FX button** on each track row (before Mute) and mixer strip, in the usual style: *FX* opens the track's chain; the power symbol switches the track between its plug-in chain (on, green) and Windows MIDI (off).
- **FX chain window** : drag to reorder, tick to enable / bypass, Delete to remove, double-click to float a plug-in's window. The selected plug-in's controls are docked in the window (or floating, optionally on top), with a bar for presets (**+** saves / deletes your own), channel wiring, role (Auto / Instrument / Effect), wet and bypass. FX menu: save / load / append FX chains (`.tfchain`), clear. *Add…* lists plug-ins with vendor, format and role, filters as you type, shows scan progress, identifies unknown plug-ins in the background, adds folders or a single .vst3 / .dll file (64-bit VST2 and VST3); *Folder settings…* opens Settings > Audio & Plug-ins (remembered plug-in list is opt-in).
- **MIDI sound** (tick in the chain window and the mixer's MIDI column): with the chain on, the track's General MIDI sound also plays. With effects and no VST instrument, that sound is rendered by the audio engine (Windows' own `gm.dls` bank) so the effects shape it; this built-in synth runs only in that case. Off: only a VST instrument sounds.
- **Audio engine**: plug-ins run in a separate process (TabForge.exe, audio-engine mode). With *Play the whole song through the audio engine* on (the default) every song's tracks play on the engine; with it off the engine runs only for tracks with plug-ins, audio clips or armed audio input, and it is kept warm for a few minutes after the last such track stops. Windows MIDI tracks (when that option is off) are delayed by the engine's latency so everything stays in time. A plug-in that crashes or freezes is named in the status bar, switched off, and the engine restarts without it. It stays off until you allow it again: select it in its FX chain window and press *Allow again*, or use Settings > Audio & Plug-ins > More options > *Plug-ins switched off after a crash*, which lists every switched-off plug-in with an *Allow again* button. Allowing again only takes it off that list (it loads on the next playback); it does not approve an untrusted file, which stays blocked until you approve it.
- **Settings > Audio & Plug-ins** (and **Recording** for the input): audio driver (WASAPI shared / exclusive, ASIO, DirectSound), output device, sample rate, buffer size; **plug-in folders** (Browse to add; only these are scanned unless *Also scan the standard VST folders* is on); *Run each plug-in in its own process* (off by default: plug-ins share one engine process, separate from TabForge). *Recording offset (ms)* (Audio input; a numeric setting, not a hotkey command): shifts new takes on top of the input latency the device reports; positive moves them earlier (use it when recordings sound late).
- **Saving as .gp** a song that uses plug-ins, FX or mixer groups asks once per song: *.gp with the audio settings inside* (recommended), *clean .gp + .tfaudio file* (most compatible with other programs; TabForge re-applies the .tfaudio when it opens the .gp), or *.tforge*. Songs without these settings save as .gp exactly as before. Exporting to .gp folds group levels and pan into each track.

## Mix Table
- F10: Mix Table for the selected beat (instrument, volume, pan, chorus, reverb, phaser, tremolo, tempo; transition; all tracks). Red dot above the beat in the score and in the timeline lane. Imported from .gp5 automations.

## Context menus from the keyboard
- **Shift+F10** or the **Menu** key opens the right-click menu of the part that has the focus, with the first item focused (arrow keys move, Enter chooses, Esc closes).
- **Score**: the note menu on a note or selection, the score menu elsewhere, opened at the caret.
- **Timeline**: the selection menu when bars are selected, otherwise the bar menu for the playing or current bar.
- **Fretboard / keyboard panel**: its menu. The panel is a Tab stop and shows an outline while it has the focus (clicking it still leaves the focus on the score).

## Misc
- Ctrl+O opens in the current tab, Ctrl+Shift+O in a new tab.
- Esc clears a selected timeline area.
- Save prompt: Y = save, N = don't save, Esc = cancel.
- Track colour: right-click the swatch for named colours (left click does nothing).
- Loop settings: 'Loop button loops what is playing' (off by default: Loop with no area loops the whole song).
- Count-in settings: only from bar 1, before every section, before every loop repeat.


- **Resize a section**: drag the left/right edge of a section block in the timeline (cursor turns to a resize arrow). Shrinking leaves empty bars; growing into a neighbour pushes it along. Undoable.

- **+ / - (duration)**: .gp5 style � + makes the note shorter (8th to 16th), - makes it longer. Settings > Editing > Note entry > 'Reverse + / - duration keys' swaps them.
- **Free rhythm entry** (default): durations, dots and tuplets can always be changed; bars that don't add up turn red. Settings > Editing > Note entry > 'Prevent rhythms that overfill a bar' blocks such changes instead.

- **Add section (M)**: adds a section at the cursor bar (also the Sections panel's Add button, and right-click on the timeline's section lane: *Add section at bar N*). If a section already starts there, it opens that section's title.
- **Moving sections**: drag a section block to move the section together with its bars (other sections make room); **Ctrl+drag** moves only its marker into free bars (the bars stay put). Drag an edge to resize. The section tooltip explains this.

- **Score scrolling** (right-click the score > Score scrolling): *Vertical* wraps the bars into lines and scrolls down; *Horizontal* puts the whole score on one line that scrolls (and follows playback) to the right, with a horizontal scrollbar. Independent of *Score layout* (page / continuous). Bindable as *Horizontal score scrolling* (no default key).
- **Follow playback** (right-click the score): *Page turn* (default) jumps half a page, or to the next line, when the playhead nears the edge; *Smooth page turn* makes the same turns but glides them (~0.2 s at the display refresh rate, idle in between). Bindable as *Smooth page-turn follow* (no default key).
