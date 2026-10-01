# Changelog

All notable changes to TabForge. Downloads are on the [Releases page](https://github.com/cobhc95/TabForge/releases).

## 0.5 — 2026-10-01

The first official release, and the first without a beta label.

### 🚀 Why 0.5 and not 0.4

This is a major release. The jump from 0.3 to 0.5 reflects the size of the change: a new built-in tutorial, drag-and-drop audio and MIDI, a rebuilt keyboard and menu system, reorganised settings, a much more faithful .gp save and reopen, a safer audio engine, and engraving that is close to publication quality.

It is also the first version without a beta label. Every area of the program was exercised end to end on real hardware before release, as described in "Under the hood" below.

---

### ✨ Highlights

##### 🎓 Built-in tutorial: Basic and Detailed Guides
- **Help > Tutorial…** opens a guide window with a contents list, instant search across every chapter (Ctrl+F), Back and Forward, and previous and next chapter.
- **Basic Guide:** seven short chapters that get you playing, practising, writing tab and saving in minutes.
- **Detailed Guide:** sixteen chapters covering every feature, from reading tab to drums, plug-ins, the timeline, files and every keyboard shortcut.
- **Export PDF…** saves either guide as a polished A4 PDF with a cover, a contents page with page numbers, chapter banners and selectable text.

##### 🎚️ Drag audio and MIDI straight onto the timeline
- Drag WAV, MP3, FLAC, OGG, AIFF, M4A, WMA or MIDI files from Windows, or a groove straight out of a plug-in's editor, onto any track.
- **Live landing preview:** a translucent block in the track's colour shows exactly where the file will land and how long it is, snapped to the grid. Hold Alt to place it freely.
- **Overlaps:** dropping over existing material automatically opens a **new lane**. Dropping below the last track creates a fitting **new track** (drum MIDI makes a drum track).
- **MIDI timing:** a dropped MIDI file lands on the song's tempo map beat for beat, so a drum groove sits exactly on the bar grid. It plays through the track's instrument and effects.
- **Plug-in temp files are kept:** files a plug-in drags out of its temporary folder are copied into the song's media folder, so the clip keeps working after the plug-in deletes its own copy.
- **Song files** (.tforge, .gp, .gp3, .gp4, .gp5, .gpx) dropped anywhere on the window open in new tabs.
- **Clips now move anywhere.** Drag a clip to another lane, another track, or below the last track, with the same preview. Ctrl+drag copies it, and Esc cancels.
- **Tidy lanes:** lanes left empty are removed automatically. You can turn this off in Preferences.

##### 🎛️ Type exact values into every knob
- Double-click, right-click, or press F2 or Enter on any round knob to type a value: master volume, track volume and pan, FX chain volume, MIDI processor settings and Track properties.
- Units are understood: "-6 dB", "75%", "2:1", "100 ms", "L30", and comma decimals too.
- Dragging works as before. Ctrl+click (or Home) resets to the default, as a single undo step.

##### 🎸 A sharper fretboard and keyboard
- **New default look:** a shaded blue scale highlight, white position dots and large fret numbers. Your saved choices are kept.
- **New "Scale highlight strength" (10–150%)** makes the scale highlight dimmer or brighter in every style, and the root note always stays strongest.
- **Smaller panel:** the empty band above the fretboard is gone, so the panel needs less height. The colour legend now sits neatly at the bottom.
- **No overlapping labels:** technique tags (P.M., TAP, …) move beside a note when there is no room above it.

##### ⏱️ Timeline polish
- **Hover shade:** the bar cell under the mouse gets a soft shade.
- **New "Playback position marker" setting:** choose a **Line** (the default), a **Bar marker** (a small square in the current bar), or **Both**.

##### 🔊 Sound that never goes silent, never clips
- **No more silent tracks:** if a track's VST instrument stops playing, the track falls back to the built-in General MIDI sound automatically. That covers FX switched off, an instrument bypassed or removed, a crashed or missing plug-in, or a plug-in that fails to load. Your own GM on/off choice is always respected.
- **Clip-free renders:** rendered audio (WAV and MP3) passes through a transparent **safety limiter** (-0.3 dBFS ceiling), so loud passages no longer clip the file. Stems are never limited. An optional live-playback limiter is available too, off by default.
- **Crashed plug-ins can come back:** a plug-in that crashed and was switched off can be **allowed again** from its FX chain window or from Preferences. This never bypasses plug-in approval.
- **Finding plug-ins on a fresh install:** an empty plug-in list now offers **Scan the standard VST folders** in one click.

##### ⌨️ Keyboard and menus that tell the truth
- **Live shortcuts in menus:** every menu item shows **your current shortcut**, and it follows any rebinding or preset change instantly. Wrong or invented labels are gone.
- **Shortcuts that never worked now do:** Previous and Next section, Insert and Delete bar (Ctrl+Insert / Ctrl+Delete), Previous and Next track (Ctrl+Shift+Up / Down), playback speed (Ctrl+Alt+Up / Down) and the Alt-arrow note steps.
- **Typing comes first:** text boxes always receive your typing first.
- **Tooltips:** long tooltips wrap onto a few lines, and every button that runs a command shows its shortcut in brackets.
- **Keyboard-only menus:** Shift+F10 and the Menu key open the right-click menu at the selection in the score, timeline and fretboard.
- **New bindable commands:** Metronome, Count-in, Export MIDI, Export ASCII tab, Project settings, Transpose, Insert beat and Delete beats, and more.

##### 🎼 Engraving that reads like the real thing
- **Clefs:** every system shows the track's real clef (treble, bass, alto, tenor, percussion, guitar 8vb), and a smaller clef where it changes mid-system.
- **Harmonics** are written at the fretted pitch with a diamond notehead.
- **Notation details:** full eighth-note flags, slide strokes in the staff, and hammer-on and pull-off slurs on the staff as well as in the tab.
- **Clearances:** ties and slurs stop short of accidentals and ghost-note brackets, and slide and bend lines clear bracketed frets.
- **Tab view:**
  - strum arrows point the standard way;
  - grace-note slides are drawn as a small arc;
  - a tied-to fret is not printed twice;
  - the gap between staff and tab is tighter.
- **Repeat-bar (simile) signs** are drawn as a proper slash with two dots (two slashes for "repeat two bars"), centred in the bar.
- **Volta (alternate ending) brackets** sit above the staff at one height across an ending, open when the ending continues.
- **7- and 8-string tracks** no longer spill out of their system, and 4- and 5-string basses get shorter systems.
- **Tab-only view** has no empty staff area above the tablature: systems are about 100 px shorter, and marks stack above the tab without colliding.
- **Fermatas** are drawn for each voice and on rests.
- **Ledger lines** now match the staff lines exactly. A single setting controls both.
- **Thousands fewer collisions:** across a 300-song sample, overlapping symbols dropped by about two thirds compared with 0.3. Every marking above or below the staff gets its own row, and systems grow to fit lyrics, fingering and harmonic marks.

##### ✍️ Editing upgrades
- **Duplicate bar** copies the bar on **every track**, so tracks stay lined up. With bars selected, it duplicates the whole selection.
- **Time and key signatures carry forward** to the next change, on every track. Use **Only this bar** for a one-bar change, or apply a change to exactly the bars you selected.
- **Move to string above / below** moves the selected notes to the neighbouring string at the same pitch (Alt+Shift+Up / Down).
- **Shift+click** extends the selection to the clicked beat.
- **Effect keys** on an empty beat use the current note value.
- **Tools > Transpose** now:
  - moves both voices;
  - skips drum tracks;
  - can work on just the selected bars;
  - moves notes that would leave the neck to a free string.
- **Capo changes** keep the written frets and move the sounding pitch. Playback, notation and fretboard all agree.

##### 🥁 Drums and tracks
- **Every drum pad writes.** Typing any drum number works, and pads follow the track's drum notation preset.
- **New tracks match the song's length.** Quick-add drum tracks start with the full drum line set.
- **Choosing an instrument for an empty track** gives it that instrument's own strings, for example a 4-string bass tuned E A D G.
- **Right-clicks on track-row controls** (knobs, sliders, buttons) reach the control itself.

##### 📁 Files you can trust
- **Saving and reopening .gp files** keeps far more of your song: arpeggios, pickup bars, clefs, harmonics, tempo ramps, unusual drum sounds and transposed tracks. In our test set, songs that changed after a save-and-reopen dropped from 26 of 82 to 4.
- **MIDI export follows playback to the millisecond:** tempo changes, ramps and held fermatas are all included.
- **Long Guitar Pro 3–5 files open:** songs with more than 1,000 bars, up to 20,000 bars per track.
- **Long text no longer blocks opening:** songs with long notes or notices open.
- **Imported drum ties** keep sustaining instead of re-striking.

##### 🧠 Recording, memory and long songs
- **Recording survives a busy disk:** the input-to-disk buffer holds about 175 s instead of about 5 s.
- **Plug-in memory:** closing a tab unloads its plug-ins at once, so opening and closing songs with a large sampler no longer piles up memory.
- **Long songs open:** Guitar Pro 3–5 files with more than 1,000 bars (up to 20,000 per track) and songs with very long notices.
- **Fermatas hold** in playback, the playhead, MIDI export and audio render.

##### ✅ Consistent, careful details
- **Esc closes dialogs** however they were opened.
- **Menu items that open a dialog** all end in the same "…" character.
- **Typing a tempo** in the toolbar applies on Enter and is heard at once while playing.
- **Tooltips** wrap and show the command's shortcut; every round knob accepts a typed value.
- **Demo song:** "Ashen Meridian" no longer leaves its pickup bar alone on the first line, and its drum swell and outro piano can now be heard.

##### ⚙️ Preferences, reorganised
- **14 clear pages in four groups,** with a new **Common settings** page first.
- **Simpler layout:** everyday settings are visible, with the rest behind "More options".
- **Search** matches any word in any order.
- **Safer resets:** Reset buttons ask before acting.
- **Lean right-click menus** keep only frequent actions and end in one "… settings…" link to the right page.

![Common settings](https://raw.githubusercontent.com/cobhc95/TabForge/main/docs/screenshots/preferences-common-settings.png)

---

### 🛠️ Under the hood: stability, engine and performance

- **Real-hardware playback testing:**
  - no clicks, dropouts, clipping or stuck notes in recorded output;
  - zero audio-engine deadline misses;
  - stopping reaches digital silence in under 0.1 s.
- **Low-latency audio** was exercised on **ASIO (64 and 128 samples)** and **WASAPI exclusive**. The engine log now reports the block size the device actually delivers.
- **Recording survives a busy disk.** The input-to-disk buffer grew from about 5 s to about 175 s at 48 kHz. Under heavy disk load we lost 0 s of a 3-minute, 2-track take, against 33 s before. If the disk ever stalls for longer, the lost stretch is kept as silence so the take stays in time, and TabForge tells you how much was lost.
- **Memory:**
  - closing a tab now unloads its plug-ins immediately;
  - a 30-minute open-and-close session with a large sampler peaked at about 0.9 GB, down from about 3.9 GB;
  - switching between open tabs still keeps the other tab's plug-ins ready.
- **No more hanging notes:** notes that kept sounding after they should stop were found in 16 of about 1,500 songs. They are fixed (slide-ins, before-the-beat grace notes and tremolo cut-offs on the first beat).
- **Fermatas hold during playback** in every track, the playhead, MIDI export and audio render.
- **Drawing errors are contained:** a drawing error in any control shows a small outline instead of repeating an error dialog, and the score keeps drawing the rest of the page.
- **Your layout is left alone:** starting TabForge no longer rewrites your saved dock layout.
- **Typing a tempo** applies on Enter and is heard immediately while playing.
- **Lighter drawing:** the timeline hover, the drop preview, the knob editor and the tooltip shortcuts do no work per frame.
- **Audio engine details:**
  - **Render limiter:** a transparent safety limiter on rendered audio (ceiling -0.3 dBFS, 1.5 ms lookahead, about 80 ms release). The demo song's mix used to peak at +3.1 dBFS with 1,261 clipped samples; it now peaks at -0.3 dBFS with none.
  - **Live limiter:** an optional limiter on live playback (same ceiling, about 1.5 ms extra delay), off by default.
  - **Block size:** the engine log reports the block size the device really delivers. WASAPI exclusive, for example, may give 192 frames when 64 are requested.
- **Timing:**
  - MIDI export follows playback to the millisecond. Bar starts used to drift by up to 17 ms, and the file ended 172 ms early; they now agree within 0.002 ms.
  - Tempo ramps, mid-bar tempo changes and fermata holds are all exported.
- **Plug-in hosting:**
  - closing a tab unloads its plug-ins at once;
  - a crashed plug-in is switched off safely and can be allowed again;
  - a track whose instrument can't play falls back to the General MIDI sound.
- **Crash and glitch fixes:**
  - **Second-voice slurs:** a hammer-on or pull-off slur in a second voice with more beats than the first could crash the score drawing. Fixed.
  - **Contained drawing errors:** an error while drawing one line of the score, or any self-drawn control, no longer repeats an error dialog on every repaint.
  - **Long notices:** songs with notices of more than 4,096 characters failed to open; they now open.
  - **Long Guitar Pro 3–5 files:** songs with more than 1,000 bars now open.
- **Responsiveness:** large drags of in-memory files are read on drop, not while you hover. Timeline hover, drop previews, knob editing and tooltip shortcuts do no per-frame work.
- **Over 3,600 automated checks** run on every build, covering file fidelity, playback timing, engraving, editing, keyboard routing, settings upgrades, plug-in safety and recording.

---

### 🎵 Demo song

- The demo song "Ashen Meridian" is included. The opening layout, the drum swell and the outro piano have been improved so every part can be heard.

---

### 📋 Full list of changes

Every new feature, fix and change since 0.3, item by item.

###### New in this release

- Timeline: audio and MIDI clips can be dragged to another lane or another track, or below the last track to make a new one. A ghost shows where the clip will land (it snaps like a dropped file; hold Alt for free placement), a missing lane is created, Ctrl+drag copies, Esc cancels, and the whole move is one undo step. Lanes left empty are removed and the lanes below close up (Preferences > Timeline & Tracks > Remove empty clip lanes automatically, on by default; a track armed for recording keeps its lanes).
- Tutorial: a Basic guide / Detailed guide switch at the top of the window (each with its own contents, search and reader; the last one is remembered), Export PDF saves the guide shown as "TabForge Basic Guide.pdf" or "TabForge Detailed Guide.pdf", and a new unbound command Help > Detailed guide… opens the Detailed Guide directly.
- Drums: every pad of the drum map (snare, hi-hat, crash, ride, toms and the rest, notes 27-87) now writes its note, and typing a drum number above 36 on a drum line (for example 38) writes that number instead of 36.
- Drums: clicking a pad writes on the TAB line given by the track's drum notation preset or custom map, not a fixed line.
- Tracks: a track added from the Track menu now has as many bars as the song instead of at least 32.
- Tab view: strum (brush) and arpeggio arrows now point the standard way: a downstroke (bass string first) points up to the top string, an upstroke points down. Playback was already right.

- Tab view: a grace note's slide or hammer-on / pull-off is now drawn as a small arc between the grace fret and its main note (a bent grace note as a rising arrow) instead of a slide stroke after the main note.

- Score layout: the gap between the notation staff and the tab staff is about 12 px smaller, so a system is shorter and the tab sits nearer the notes above it.

- Tab view: a tied-to note no longer prints its fret number again (the tie already shows in the notation); the beat under the cursor still shows it so it can be edited. In tab-only view (no notation to show the tie) the number stays.

- Notation: each system now starts with the track's real clef (treble, bass, alto, tenor, percussion, with a small 8 under an 8vb treble clef) instead of always a treble clef, and a bar that changes clef shows a smaller clef at its start.
- Timeline: drag audio files (.wav, .mp3, .flac, .ogg, .aif/.aiff, .m4a, .wma) or MIDI files (.mid, .midi) from Windows or out of a plug-in's editor onto a track. While you drag, a translucent block in the track's colour shows where the files will land: it starts at the snapped position (the snap settings; hold Alt to place freely), is exactly as long as the files (several files lie end to end, with a divider between them) and shows the name and length. Where that stretch of the lane is taken, the block moves to a new lane, shown as a lane opening under the track; below the last track it opens a new track (drum MIDI makes a drum track, other MIDI a keys track, audio an audio track). Files that cannot go somewhere show the reason on the block and the no-drop cursor. As with recorded takes, a drop over existing clips of the same kind plays on its new lane and greys out the older lane. One drop is one undo step.
- A dropped MIDI file becomes a MIDI clip that plays through the track's instrument and effects like a recorded MIDI take. It is placed on the song's own tempo map, beat for beat, so a groove dragged from a drum plug-in lands exactly on the bar grid at the song's tempo (the file's own tempo is not used; a file timed in SMPTE frames keeps its seconds). All tracks of a multi-track file are merged into one clip; a drum track takes the file's drum channel (channel 10) when the file also has other channels, a melodic track the other channels.
- Files a plug-in drags out of its temp folder, and in-memory files some plug-ins and archives drag, are copied into the song's media folder ("<song> Media" beside the saved song, or Music\TabForge Recordings for an unsaved song), so the clip keeps working after the plug-in deletes its file. Files dragged from your own folders are used where they are, as before. "Add audio file…" on a lane places files the same way.
- Song files (.tforge, .gp, .gp3, .gp4, .gp5, .gpx) dropped anywhere on the window, the timeline and the tab strip included, open in new tabs.

- Tracks with a VST instrument no longer go silent when the instrument stops playing: switching the track's FX off, bypassing or removing the instrument (even the last plug-in), a plug-in that crashed, is untrusted, is missing or fails to load now all bring in the track's General MIDI sound automatically (setting "Auto-switch to GM sound when no VST instrument plays"), and it steps back out when the instrument plays again. A GM tick or untick you made yourself is still respected.
- Sections: the two "delete" actions are now named for what they remove. The Sections panel's button is "Remove" (only the marker; the bars and notes stay) and the timeline's section menu says "Delete section and its bars…" (it asks first, and Undo restores everything; the status bar says what was removed). View > "Zoom 100% (fit width)" is now "Zoom 100%" (it sets 100%; the zoom box's "Fit width" fits the width). F3 and F8 are described as what they do: "Show track list" (brings the track list into view) and "Show / hide arrangement overview" (View > Arrangement overview now shows its F8 key); their keys are unchanged.

- Menus: the key shown beside every main-menu item is now read from your current shortcuts (and follows a rebind, a preset change or "Reverse + / - duration keys" at once); an unbound command shows no key. This fixes labels that were wrong or invented: Insert beat / Delete beats ("Ctrl +" / "Ctrl -" zoom), Double dotting (Ctrl+. is Stop), Triplet feel (Ctrl+/) and Stylesheet (F7) had keys that do nothing; the Sections menu showed Alt+Left / Right instead of Alt+Shift+Left / Right. Keys such as the semicolon, slash and bracket keys and the number keys are now written as printed (";" instead of "Oem1") in menus, tooltips and the Shortcuts page, and the note right-click menu's pitch / string items follow your bindings too.

- Note menu: "Increase duration +" / "Decrease duration -" are now "Longer note value" and "Shorter note value", matching what they do (the + key makes a note shorter, the - key longer, unless "Reverse + / - duration keys" is on). They are bindable commands (`Note.Longer` / `Note.Shorter`); the keys shown in the menu follow your bindings and the reverse setting.

- Note > "Move to higher string" / "Move to lower string" (and the same two items in the note's right-click menu) now move the selected note(s) to the next string keeping the pitch; the fret is recalculated from the tuning. If the pitch cannot be played there (fret below 0 or past the last fret) or the string already has a note in that beat, nothing changes and the status bar says why. One undo step. New bindable commands "Move note to higher / lower string" (Alt+Shift+Up / Down); the plain Up / Down arrows still move only the cursor.

- Keyboard fixes: shortcuts written with Alt and Shift together (Previous / Next section, Alt+Shift+Left / Right) now work (the key map spelled the modifiers in an order the window never produced); Alt chords reach the editor again (Alt+Left / Right step to the next / previous entered note); Ctrl+Insert, Ctrl+Delete, Shift+Insert, Ctrl+Shift+Insert / Delete and Ctrl+Shift+Up / Down no longer get swallowed as beat edits or line moves, so Insert bar, Delete bar, the section editor, Add / Delete track and Next / Previous track run from the keyboard. A bound Ctrl or Alt shortcut now always wins over the editor's own keys.
- Insert beat (Insert), Delete beats (no key), Shift pitch up / down (Shift+Up / Down) are now catalogued commands: rebindable in Preferences > Shortcuts and listed in the command palette. The editor's Ctrl+Plus / Ctrl+Minus beat insert and delete (which always zoomed instead) and Alt+Up / Down cursor moves are gone.

- New bindable commands (no default key; Preferences > Shortcuts, command palette): Export MIDI, Export ASCII tab, Project settings, Metronome on / off and Count-in on / off.

- Tracks: a drum track added with the quick-add button now starts with the default drum map's TAB lines (6) instead of 5, the same as one made in the + Track window, so no rebuild through Track properties is needed.
- Notation: a hammer-on / pull-off added with the H toggle now also draws its slur in the standard notation staff, matching the arc in the tab.
- Score: Shift+click extends the selection from the cursor to the clicked beat (like Shift+arrows); a plain click still clears it and moves the cursor.
- Score: an effect key (a technique, dead note or ghost note) pressed on an empty beat now gives the new beat the current note value instead of leaving the default.
- Plug-ins: on a fresh profile the empty Add plug-in window now has a Scan the standard VST folders button (its tooltip lists the folders). One click turns on "Also scan the standard VST folders" and scans; the default is unchanged (nothing is scanned until you choose).
- Plug-ins: a plug-in that crashed and was switched off can now be allowed again. Select it in its FX chain window and press Allow again, or use Preferences > Audio & Plug-ins > More options > Plug-ins switched off after a crash, which lists them all. It loads again on the next playback and the faulted icon on the track clears; allowing again does not approve an untrusted plug-in.
- Tooltips: long tooltips now wrap onto a few lines instead of running across the whole screen (short ones are unchanged), and every button, tab and toggle that runs a command shows its current shortcut in brackets at the end ("Play / pause (Space)"). The bracket follows your rebinding and shortcut presets at once and disappears when a command has no shortcut.
- Track list: a right-click on a control in a track row now reaches that control (knob type-in, pan menu, sliders, buttons, the number's colour menu). Only a right-click on the row's own background opens Track properties.
- Instruments: choosing a bass, keys or guitar sound for a new track (+ Track) or on an empty track (the row's Instrument button, or Track properties) gives it that instrument's own strings (a bass gets 4 strings, E A D G; keys get their key rows) instead of keeping the guitar's six. A track that already has notes keeps its strings and kind, so no note moves; Track properties says so when you pick a different kind of instrument for it.
- Capo: changing a track's capo keeps the written fret numbers and changes the sounding pitch (open string + capo + fret) of the notes already written, so playback, the staff and the fretboard agree with the new capo.
- Tools > Transpose now moves both voices, skips drum tracks (percussion numbers are instruments), moves only the selected bars when some are selected, and moves slide and trill targets with the notes. A note that would go below fret 0 or past the last fret is moved to a free string of its beat where it fits; the status bar counts any note that fits on no free string. It is a bindable command ("Transpose", unbound by default). One undo step.

- Duplicate bar now copies the bar on every track, not only the selected one: bars are shared by all tracks, so the copy lands right after the source in each track and the tracks stay lined up, like Insert bar and Delete bar. With bars selected (in the score or on the timeline) it duplicates the whole selection after its last bar and selects the copy. One undo step. A section label is not repeated on the copy.
- Time signature and key signature now carry forward: setting one on a bar applies from that bar up to the next change (or to the end of the song), on every track, and only the bar where it changes shows it. The score, the bar check (red bars), beaming, accidentals, playback and exports all use the same bars. Both dialogs have an "Only this bar" check box for a one-bar change; with several bars selected, the change applies to exactly those bars. A bar without a key of its own is exported in the song's key, as the score shows it. The toolbar now shows the signatures of the bar you are on. Changing bar 1 stops at the next change, and later bars keep what they had. One undo step.
- Notation: ledger lines now always match the staff lines (same colour, opacity and thickness, so they are as light as the staff). The separate "Ledger-line opacity" setting is gone; "Staff and ledger line opacity" under Preferences > Score & Notation sets both together, and your saved staff-line opacity is kept.
- Fretboard: new look by default for fresh settings (shaded blue scale highlight, white fret dots at original brightness, large fret numbers, natural string spacing); saved choices are kept. New "Scale highlight strength" (10-150%, 100% is the standard look) in Preferences > Fretboard > Appearance dims or brightens the scale highlight on the fretboard and keyboard in every style, with the root note always stronger; bindable as "Scale highlight brighter" / "Scale highlight dimmer" (10% steps, unbound by default).
- Fretboard layout: the empty band above the top string is gone, so the panel needs less height; the colour legend and Scales button now line up with the bottom of the fretboard, and a technique tag on the top string sits beside its marker instead of above it. New `--render-fretboard` diagnostic draws the fretboard or keyboard to a PNG.
- Timeline: the bar cell under the mouse pointer gets a soft shade (a lighter grey on empty cells, a slight lighten on filled ones), on any track row; it follows scrolling and zooming and never gets in the way of clicks, drags or selections.
- Timeline: new setting Preferences > Timeline & Tracks > Playback position marker. Line (default) is the white vertical line; Bar marker shows a small dark square in the current bar of the selected track instead (it moves bar by bar); Both shows the two together. Bindable as "Cycle playback position marker" (`View.CyclePlayheadStyle`, no default key).
- Notation staff: artificial, tapped, pinch and semi harmonics are written at the fretted pitch with a diamond head (they were drawn several ledger lines too high), eighth-note flags are full flags, and slides (slide in / out, shift and legato) now draw their short slanted stroke in the staff as well as in the tab.
- New **Help > Tutorial…**: the Beginner's Guide opens in its own window with a contents list, instant search across every chapter (matches highlighted in the snippets), Back / Forward (Alt+Left / Alt+Right), previous and next chapter, pictures at their natural size, tip / note / warning boxes and tables, in both themes. Ctrl+F jumps to the search box and Esc clears it; the last chapter and the window size are remembered. **Export PDF…** saves the whole guide as an A4 PDF (cover, contents with page numbers, chapter banners, running header and footer, selectable text) while the window stays usable. The command `Help.Tutorial` can be bound to a key in Preferences > Shortcuts (F1 stays with the shortcut reference).
- Clean .gp export round trips more faithfully: arpeggio strokes stay arpeggios (not brush strokes); a pickup or short last bar of an imported song stays short instead of being padded to a full bar (empty parts of it too), and an empty bar in 3/4 or another metre stays a full bar; every bar's clef is written (and the guitar clef is read back as the guitar clef); a tempo ramp cut short by the bar end or the next tempo point keeps the tempo it reaches; drum sounds outside the usual General MIDI drum range keep their number; a note whose sound differs from its string and fret (a track whose tuning the source file did not give) is written where it sounds.
- Guitar Pro import: a string played mostly in harmonics or tied notes no longer comes in tuned an octave too high; a drum hit with no sound of its own in a .gp3/.gp4/.gp5 file gets the sound playback gives it, and a long drum tie keeps sustaining its first hit instead of starting new hits partway through.

- Notation staff: ties and hammer-on / pull-off slurs now stop short of an accidental that sits beside the tied note in the same chord, and start after / end before ghost-note brackets; the opening ghost bracket sits a little closer to its notes so it clears accidentals and the previous beat's stem.
- Tab view: slide lines and bend lines now start and stop at the edge of a bracketed (ghost) fret number instead of running through its brackets.
- Every rotary knob (master volume, track volume and pan, FX chain Volume, MIDI processor numbers, Track Properties) lets you type an exact value: double-click, right-click, or F2 / Enter on a focused knob opens a small editor on the knob. Units are understood ("-6 dB", "75%", "2:1", "100 ms", comma decimals); Enter or clicking away applies like a drag, Esc cancels, an invalid entry shows a red outline. Reset to default moved from double-click to Ctrl+click (and Home). Knobs with a right-click menu get "Type value…" as its first item. FX chain Volume now moves in 0.1 dB steps.

- Tab view: a rest moves up clear of the other voice's fret at the same beat, and dynamics above the tab keep a little more distance from the fret numbers.

- Engraving: a system now grows to hold the finger rings, right-hand letters and harmonic values under its TAB (they were cut off at the bottom, worst with two voices).
- Shift+F10 and the Menu key now also open the timeline's menu (the selection menu when bars are selected, otherwise the bar menu for the playing or current bar) and the fretboard / keyboard panel's menu, each with its first item focused; the instrument panel can be reached with Tab and shows an outline when focused (clicking it still leaves the focus on the score).

- Engraving: tight gaps cleared: a grace fret keeps 2.4 px from a two-digit main fret, a trill's second fret and the lyric rows (14.5 px apart) are spaced out, and the 8va caption's space includes its full height.
- Engraving: a simile bar no longer extends a P.M. line through it or gets tie stubs from its neighbours (the line ends at the bar line before it).
- Shift+F10 and the Menu key now open the score's right-click menu (the note menu on a note or selection, the score menu elsewhere) at the caret, with the first item focused, so the menu can be reached and used with the keyboard alone.

- Every menu item that opens a dialog now ends in the single ellipsis character "…" (the main menus used three typed dots), so all menus, buttons and hints match.
- Engraving: lyric rows under the TAB (up to three, below any fingering) no longer leave their system; a system with lyrics grows by the room they need, and the PDF export reads the grown height.
- Engraving: a beat text longer than its bar no longer runs under the next bar's number, tempo or title (they stack above it); the arrow of an arpeggio or brush stroke counts as ink, so a bar number clears its arrowhead; a repeated identical bend or whammy amount over a crowded run is printed once instead of as a pile.
- Long check-box labels now wrap instead of being cut off (the "Tint the track's row and lane" option in Track properties), and the Render window's progress bar uses the theme colours instead of a bright white strip.
- Guitar Pro 3-5 songs (.gp3/.gp4/.gp5) with more than 1,000 bars now open: the Guitar Pro reader's fixed 1,000-bar limit is replaced by TabForge's own limit of 20,000 bars per track.

###### Sound

- Audible change: rendered audio (File > Render, the WAV and MP3 master mix) now goes through a transparent safety limiter (ceiling -0.3 dBFS, 1.5 ms lookahead, about 80 ms release) after the master chain, so loud passages no longer clip the file (the demo song's mix peaked at +3.1 dBFS with 1,261 clipped samples; it now peaks at -0.3 dBFS). Audio already below the ceiling is unchanged, stems are never limited, and Monitor FX still stay out of renders. On by default; turn it off in Preferences > Audio & Plug-ins > Output device (Safety limiter on rendered audio).
- New optional safety limiter for live playback through the audio engine (same -0.3 dBFS ceiling, after the master chain and Monitor FX, adds about 1.5 ms of delay). Off by default; Preferences > Audio & Plug-ins > Output device, More options (Safety limiter on live playback).
- Playback: fixed notes that could keep sounding after they should stop (found on 16 of about 1,500 test songs): a slide-in or a before-the-beat grace note on the very first beat of a song, and a tremolo-picked note cut short by a following slide-in, left a note-off before its note-on. Tremolo attacks that fall after such a cut are now left out. A before-the-beat grace note on the first beat now plays on the beat, taking a slice off the main note, since nothing can sound before the song starts. The playback check now lists each hanging note with its neighbouring events.
- Fermatas now hold during playback (twice the note's length) in every track, the playhead, MIDI export and audio render.
- The engine log's 10-second audio line now also shows the block size the device really delivers (for example WASAPI exclusive gave 192 frames when 64 was requested).

###### Notation

- Score engraving: every marking above or below the staff (accents, fermatas, tuplet brackets and numbers, trills, wah and tap marks, 8va lines, vibrato, chord names, beat text, dynamics, let ring, harmonic captions, tempo, swing mark, bar numbers, section titles, directions) now claims its own row and the next one stacks outside it, so no two texts or marks overlap; voice 2's marks go below the staff. The space above and between the staves grows to fit the song's own content (low ledger notes, stacked marks).
- Score and tab: stacked marks keep at least 2.6 px apart (dynamics, Wah, F.B., Harm., let ring and chord names no longer touch); with two voices on the staff each voice keeps its fermata (upright above, inverted below), and the tab-only view draws a fermata both voices share once; a fade wedge clears harmonic captions.
- Score and tab: tempo changes inside a bar are now printed over the beat where they start; fermatas, chord names and beat text on rests are drawn (and beat text is kept inside its bar).
- Score and tab: the repeat-bar (simile) sign is now drawn as a proper slash with two dots (two slashes for "repeat two bars") instead of the coda sign. A repeat-bar bar shows only its sign, centred, with no notes beside it (its notes appear while the edit cursor or selection is inside it, so they can still be edited).
- Score and tab: the height of each system follows the track's string count, so 7- and 8-string tracks no longer spill below their system and 4- and 5-string basses get shorter systems.
- Score: an empty bar shows a whole-bar rest; very deep whammy dives are drawn compressed so they stay inside their system (the labels keep the true amounts).
- Score: grace notes now read left to right in time; tuplet numbers and other marks stack clear of short rests (a triplet number no longer runs through a 32nd or 64th rest).
- Score: fixes from a sweep over many real songs - a chord of ghost notes now gets one pair of brackets instead of overlapping ones, accidentals no longer sit on top of a displaced chord head or a ghost bracket, grace notes sit clear of the main note's accidental and get room in the bar, and the first note after a key or time signature no longer touches it.
- Alternate-ending (volta) brackets are drawn above the staff with the other bar texts, at one height across the bars of an ending: label and hook at the start, hook at the end, open when the ending continues.
- Ledger lines are drawn at full staff-line contrast (they were faint and read as stray marks), and a little longer in the default Minimal style.
- Ghost-note brackets clear the ends of ledger lines; a pre-bend arrow stands beside the higher strings' numbers instead of through them; the second voice's fingering and harmonic marks sit below the first voice's.
- Tab-only view: the empty staff area above the tablature is gone (systems are about 100 px shorter and the TAB sits right under its marks); the playback line in tab-only view now spans the tablature instead of the hidden staff.
- Tab-only view: dynamics that would print below the system (under fingering or lyric rows) now stack above the TAB instead of being cut off; wah, trill, rasgueado marks and whammy curves take part in the stacking; in notation-only view a fade in/out wedge sits under the dynamics row instead of through it.
- Tab-only view: accents, staccato and tenuto marks, let ring spans and technique labels now stack above the palm-mute lane instead of printing over it, so double P.M. and accent marks and "let ring" no longer collide.

###### Editing and UI

- Preferences are reorganised: 14 pages in four captioned groups (new first page "Common settings", then General, Appearance, Score & Notation, Fretboard & Keyboard, Timeline & Tracks, Editing, Playback & Practice, Audio & Plug-ins, Recording, Tabs & Windows, Shortcuts, Files & Backups, Advanced), one centred column per page with the everyday settings shown and the rest behind "More options", each feature's colours in its own "Appearance" group, all "ask before" questions together on General, search that matches every word in any order (with everyday synonyms and the old menu names), Esc closing the window, and resets that ask first ("Reset page…" at the top of each page, "Reset all settings…" on Advanced). No setting was removed or changed in the settings file; "Follow the playhead" is now "Scroll the score while playing" and "Density" is "Spacing".
- Settings rows for every menu option that had none: Lock fretboard size, Score page layout (Page / Continuous), Score scrolling (Vertical / Horizontal), Text & fonts, the four timeline appearance options (now also remembered between sessions), Show tracks in groups in new songs, and one colour per track group. "Display" groups are called "Appearance".
- Lean right-click menus: the fretboard, score, timeline, section and track-list menus keep only frequent actions and end in ONE "... settings..." entry (Fretboard settings, Score settings, Timeline settings, Track list settings) that opens Settings on the right page and highlights the right row. Styling moved to Settings: fretboard appearance, sizes, colours and look-ahead; dark/light page, ledger lines, page turns, playback colours and Text & fonts; the timeline's four appearance toggles. Menus are at most two levels deep and never hold a same-named submenu ("Appearance and layout > Appearance" is gone). "Show all tracks as" is a button in Settings > Fretboard; "+ Track" offers only Add track and Mixer (colour items are under Colours in the track list menu; the colour of each group is in Settings > Appearance & colours).
- One menu separator style everywhere (the gap with no line in the note and timeline menus is fixed), choices of one value (Page / Continuous, durations, dynamics, pan knob / slider) are drawn as a dot instead of a tick, unticked note tools can be ticked, and "original position" reads "default position". The Effects "Chord…" / "Text…" and Sections "Go to…" menu items match the other menus, and a stale tooltip over the fretboard menu's Scale row is gone.
- Dialogs now close on Esc however they were opened (the "Repeat close" prompt ignored it when opened with its shortcut). Track properties shows the drum-channel hint only for drum tracks or channel 9, hides the tuning for keyboard tracks, and wraps the "Tint the track's row" text.
- View > Mixer / VST opens the Mixer window (before, it only selected the docked Practice / Mixer tab).
- The keyboard view's legend is no longer about three times the fretboard legend and leaves a strip clear of the keys; the Render window keeps its Render and Close buttons in a fixed footer (no scrolling at 1920x1080); the import Cancel in the status bar looks and acts like a button.
- Typing a tempo in the toolbar box now applies on Enter (before, only clicking away applied it) and keeps the cursor in the box; changing the tempo while playing recompiles from the current position so the new tempo is heard at once.
- Score information and Song properties: the notice, instructions and lyrics boxes scroll inside a bounded box, so a long notice no longer stretches the dialog.
- Long check-box labels now wrap instead of being cut off (the "Tint the track's row and lane" option in Track properties), and the Render window's progress bar uses the theme colours instead of a bright white strip.

###### Files and import

- Guitar Pro import: songs with a long notice (more than 4,096 characters, for example a tabber's note of a few hundred lines) failed to open with "invalid or overlong notice"; the notice may now be up to 65,536 characters (control characters and larger texts are still refused).
- MIDI export now follows playback to the millisecond: the tempo track is written as the exact time playback takes for each stretch (ramps, mid-bar tempo changes and fermata holds included), and notes that ring on past the last bar keep their full length. Before, bar starts drifted up to 17 ms in the demo song and the file ended 172 ms early.
- Tempo ramps now survive a clean .gp export: each ramp is written as Guitar Pro's linear (progressive) tempo point, and a progressive tempo point in a Guitar Pro 7/8 file is read back as a ramp of the right length. Before, a ramp came back as an instant tempo step.
- A double bar on the last bar now survives a clean .gp export and reopen (the Guitar Pro reader TabForge uses drops it; the importer now reads it from the file itself).
- A track that plays transposed (for example the demo song's Sub Drop, written an octave up and sounding an octave down) now keeps its sound in a clean .gp export: the transposition is written into the track's tuning (every fret stays as written), because Guitar Pro's own transpose setting only changes how the notes are displayed. A keys track, or a shift that would take a string out of range, has its notes moved to the sounding string and fret instead. Before, the dive-bomb notes came back an octave too high after export and reopen. `--exportgp` takes an optional `clean` argument to write and re-read a clean file.
- Clean .gp export and import keep simile marks, tremolo-picking speeds, triplet feel, dead-slapped notes, a strum's own spread, a whammy curve's peak and dip, bend graces and slides into harmonics; 7-, 9- and 13-tuplet runs keep their exact positions on import.

###### Recording and engine

- Recording: the buffer between the audio input and the disk grew from about 5 s to about 175 s at 48 kHz (67 MB while recording, shared by all armed tracks; at 192 kHz it is capped at 256 MB), so a busy disk (a big copy, a scan, a backup) no longer loses seconds of a take. If the disk stalls for longer than that, the lost stretch is still silence in place, so the take keeps its timing. When recording stops after a loss, a dialog says how many gaps and how many seconds were lost; the engine log also records each gap and the total. Writes to disk are 4 times larger.
- Closing a tab now unloads its plug-ins at once. Before, a closed song's plug-ins stayed loaded (silent) for 5 minutes like a background tab's, so opening and closing songs with a large sampler kept several copies in memory (up to about 4 GB) and added audio-thread load. Switching between open tabs still keeps the other tab's plug-ins loaded for instant switching back.

###### Stability

- Starting TabForge no longer rewrites your saved dock layout: the track-list auto-fit adjusts the split for the current window only, and the layout file keeps the size you chose until you drag a splitter or change the layout yourself.
- A drawing error in any self-drawn control (timeline, fretboard and keyboard, knobs, meters, icons, tuner gauge) is now contained like the score's: the control shows a small dashed outline instead of repeating an error dialog on every repaint, and the cause is written once to the diagnostics log.
- Score and tab: an error while drawing one line of the score no longer brings up an error dialog on every repaint: that line shows a short note, the error is logged once, and the rest of the score draws normally.
- Score and tab: a hammer-on or pull-off slur in the second voice looked for its neighbours in the first voice's notes, which crashed the drawing (index out of range) on songs whose second voice has more beats than the first; the slur now follows its own voice.

###### Demo song

- The first forced line break no longer leaves the one-beat pickup bar alone on line 1 (it now shares the line with the following bars).
- The drum crash that faded in over bar 10 swelled the whole kit from silence (a fade-in is a channel-wide volume ramp), so the kick, snare and the start of the snare roll were inaudible. The reverse-cymbal swell now sits alone on beats 3-4 of bar 3, rising into the kick of bar 4; bar 10 has no fade-in. The sample file was regenerated.
- The Outro piano ("music box", two octaves up) was written pp and could not be heard (about 40 dB under the mix). It now plays mf with the piano fader lifted for bars 135-140, about 20 dB under the mix, audible under the guitars; the sample file was regenerated.

## Earlier versions

The 0.1, 0.2 and 0.3 betas are described on the [Releases page](https://github.com/cobhc95/TabForge/releases).
