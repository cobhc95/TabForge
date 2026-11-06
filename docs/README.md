# TabForge 0.9.1

**A keyboard-driven tablature and notation editor for Windows, with experimental Linux packages.** Opens .gp3, .gp4, .gp5, .gpx and .gp files, and saves .gp and its own .tforge projects. Write, import and play back guitar, bass, drum and keyboard parts, with a live fretboard or keyboard, a piano roll, an arrangement timeline, audio and MIDI recording, a mixer, built-in effects, VST2, VST3 and CLAP plug-ins (32-bit and 64-bit VST), and offline audio and video rendering.

[![TabForge with a drum plug-in](media/screenshots/0.9.0/github-main.jpg)](media/screenshots/0.9.0/github-main.jpg)

*The plug-in shown is a third-party example (Superior Drummer 3 by Toontrack): not included and not affiliated.*

## Download

> [!TIP]
> **[Setup installer](https://github.com/cobhc95/TabForge/releases/download/v0.9.1/TabForge-0.9.1-win-setup.exe)** · no administrator rights needed
>
> **[Portable ZIP](https://github.com/cobhc95/TabForge/releases/download/v0.9.1/TabForge-0.9.1-win-x64-portable.zip)** · extract and run `TabForge.exe`

Windows 10 or 11, 64-bit, nothing else to install. Not code-signed: see [Verify your download](#verify-your-download). Older versions: [Releases](https://github.com/cobhc95/TabForge/releases).

### Linux packages (experimental)

The main Linux download is the AppImage ([`TabForge-0.9.1-linux-x86_64.AppImage`](https://github.com/cobhc95/TabForge/releases/download/v0.9.1/TabForge-0.9.1-linux-x86_64.AppImage)). The archive ([`TabForge-0.9.1-linux-x64.tar.gz`](https://github.com/cobhc95/TabForge/releases/download/v0.9.1/TabForge-0.9.1-linux-x64.tar.gz)) is the fallback. Both include Wine 9.21 for Windows plug-ins, FFmpeg for video export and a General MIDI SoundFont, so sound works on first run.

```bash
chmod +x TabForge-*-linux-x86_64.AppImage
./TabForge-*-linux-x86_64.AppImage
```

- **If FUSE 2 is missing:** add `--appimage-extract-and-run`, or install `libfuse2` (`libfuse2t64` on Ubuntu 24.04).
- **What it needs:** 64-bit x86, a glibc as new as Ubuntu 22.04's or newer, and the usual desktop libraries (X11, ALSA, fontconfig, OpenGL). The full list is in the header of `tools/package-linux.sh`.
- **How far it has been tested:** Linux has run in a Hyper-V Ubuntu virtual machine and on automated test machines, not on real hardware. Checks on other distributions are incomplete, and rendering, microphone input and CLAP plug-ins are not yet confirmed. See [Plug-ins](#plug-ins) for Wine's limits, and please report what you find.

### See it in action

<table>
<tr>
<td width="33%" valign="top"><a href="media/animations/recording.gif"><img src="media/animations/recording.gif" alt="Recording a take" width="215"></a><br>Recording a take onto a track.</td>
<td width="33%" valign="top"><a href="media/animations/section-move.gif"><img src="media/animations/section-move.gif" alt="Moving a section" width="215"></a><br>Moving a section to a new position.</td>
<td width="33%" valign="top"><a href="media/animations/mixer.gif"><img src="media/animations/mixer.gif" alt="Adjusting the mixer" width="215"></a><br>Adjusting levels in the mixer.</td>
</tr>
</table>

**Contents:** [Download](#download) · [What's new in 0.9.1](#whats-new-in-091) · [What sets it apart](#what-sets-it-apart) · [Writing tab and notation](#writing-tab-and-notation) · [Instruments on screen](#instruments-on-screen) · [Arrangement and tracks](#arrangement-and-tracks) · [Recording and live input](#recording-and-live-input) · [Piano roll](#piano-roll) · [Playback and practice](#playback-and-practice) · [Band view, Keyboard mode and compact mode](#band-view-keyboard-mode-and-compact-mode) · [Sound](#sound) · [Plug-ins](#plug-ins) · [Export](#export) · [Customising](#customising) · [Files, safety and updates](#files-safety-and-updates) · [Full feature reference](#full-feature-reference) · [Verify your download](#verify-your-download) · [Building from source](#building-from-source) · [Licence](#licence)

## What's new in 0.9.1

0.9.1 is a stable release.

- **More shortcuts:** the piano roll's tools and editing commands, the metronome, count-in and loop windows, loop commands, Clear selection, master track and Fretboard settings can all be given keys in Settings > Shortcuts.
- **Fretboard note colours:** choose the Now, Next and Upcoming colours and a Fill or Ring dot style, with the same colour picker as Appearance.
- **Capo pointer:** the fretboard's capo shows the Windows hand on hover and while you drag it.
- **Drag selection scrolls:** dragging a selection past the edge of the score, timeline or piano roll keeps the view scrolling.
- **Fixes:** the first sound at start-up now passes through Monitor FX, a start-up crash on the fretboard is fixed, docked plug-in editors scroll when larger than their area, and rewinding repeatedly no longer stalls.

Full list: the [changelog](CHANGELOG.md#091).

<details><summary>Earlier in 0.9.0: wound strings, lighter playback, ASIO</summary>

[![The redesigned fretboard with drawn wound strings](media/screenshots/0.9.0/fretboard.png)](media/screenshots/0.9.0/fretboard.png)

- **Fretboard:** wound strings have fine coils that stay sharp at any size, with a new Bronze finish. A sounding string shows a soft blur that rings for the note's own length. The Fretboard settings window now matches main Settings, and left-handed mode keeps string 1 on top, matching the tab.
- **Lighter playback:** a note change repaints only the playing line of the score, and a ringing string only the strings. A song's auto-loaded plug-ins load when first needed, and the audio engine wakes about a third as often while idle.
- **ASIO:** drivers open again. A driver that really cannot be opened now says so in plain words.
- **Hotkeys:** play, stop and other navigation keys work while the FX chain, a plug-in window or another tool window has the focus.
- **Plug-in windows:** a floating plug-in window comes back where you left it when you switch tabs and back.
- **Safer:** crash recovery keeps track inputs, Band layout and audio clip links, closing a tab during playback no longer crashes, and several shutdown problems are fixed.
- **Video recording:** the fretboard now shows in full.

Full list: the [changelog](CHANGELOG.md#090).

</details>

<details><summary>Earlier in 0.8.8: fretboard, capos, tuning and plug-ins</summary>

<table>
<tr>
<td width="50%" valign="top"><a href="media/screenshots/0.8.75/fretboard-settings.jpg"><img src="media/screenshots/0.8.75/fretboard-settings.jpg" alt="Fretboard appearance settings, including Flame maple and brightness" width="340"></a><br><b>Fretboard settings</b><br>Five looks, layered wood, shaded binding, material inlays and a brightness control.</td>
<td width="50%" valign="top"><a href="media/screenshots/0.8.75/track-properties.jpg"><img src="media/screenshots/0.8.75/track-properties.jpg" alt="Track properties with seven-string tuning controls and capo options" width="340"></a><br><b>Track properties and tuning</b><br>Presets for 3 to 10 strings, saved custom tunings and a capo panel. Changes apply when you choose OK.</td>
</tr>
<tr>
<td valign="top"><a href="media/screenshots/0.8.75/partial-capo.jpg"><img src="media/screenshots/0.8.75/partial-capo.jpg" alt="A partial capo shown on the fretboard" width="340"></a><br><b>Partial capo</b><br>Cover some strings and not others. Up to four partial capos can sit on different strings.</td>
<td valign="top"></td>
</tr>
</table>

- **A more lifelike fretboard:** layered wood, true fret spacing, shaded binding, wound strings and material inlays. Tune brightness and scale highlighting in the fretboard settings.
- **Full and partial capos:** add and drag capos on the fretboard or in Track properties. Notes, playback and score labels follow the effective capo.
- **More flexible tuning and track setup:** presets for 3 to 10 strings, saved custom tunings, per-string audition and compact Details, Mixer and MIDI cards.
- **Plug-in workflow:** 32-bit VST2 plug-ins run in a helper process, and bridged VST3 plug-ins can open their own editor. Approve all or selected plug-ins with visible progress and Stop.
- **Linux packages (experimental):** the AppImage is primary and the `.tar.gz` is a fallback. Both include pinned Wine and an LGPL FFmpeg build for video export.
- **Fixes:** File > Open no longer waits in a nested message loop, playheads follow display frames, and projects with plug-in paths from another operating system open with a readable notice.

Full list: the [changelog](CHANGELOG.md#088).

</details>

<details><summary>Earlier in 0.8: Avalonia, selection and loops, piano roll</summary>

TabForge now runs on Avalonia, the step that later made the Linux packages possible.

- **Selection and loops:** drag a box on the timeline, or drag across the score, to select and set the loop; the ruler loop has handles; *Clear loop* and Esc twice remove it.
- **Section resize by edges:** drag a section's left or right edge to make it shorter or longer; it never stretches the neighbouring section. Dragging the middle still moves it.
- **Split marker:** clicking an audio or MIDI clip leaves a dashed line (following the magnet / snap); S splits the clip there.
- **Light mode** redesigned.
- **Monitor FX** button in the title bar.
- **Loop recording** keeps each pass as a take.
- **Playback highlight** is a custom colour (green by default), or follows the accent colour (Preferences > Appearance).
- **Spacing** setting for the score.
- **Tuner strip** in the FX windows.
- **Audio settings per device**, remembered for each output.
- **Section previous / next** moves the playhead.

**Piano roll (new in 0.8.1).** MIDI tracks and takes open in a piano roll: draw, move, resize, split, glue and quantize notes, with velocity, CC and pitch-bend lanes, a scale highlight, and live recording straight into it. Switch any track between Piano and Tab with the pills in the menu bar. Redesigned in 0.8.6: a Select tool, a two-row toolbar with Snap, Scale and Step input groups, a master scale panel, a lane picker for velocity and controller lanes, and outlined notes.

<a href="media/screenshots/piano-roll.png"><img src="media/screenshots/piano-roll.png" alt="The redesigned MIDI piano roll with the lane picker and outlined notes" width="640"></a>

Each plug-in or container can be set to play only for a chosen driver type (ASIO or WASAPI) and output device, in the Active on popup.

<a href="media/screenshots/active-on.png"><img src="media/screenshots/active-on.png" alt="The Active on popup with Driver type and Output device" width="360"></a>

</details>

## What sets it apart

- **One window:** tab editing, a piano roll, audio and MIDI recording, a mixer and plug-ins on the tracks of a tab.
- **A real-looking fretboard:** layered wood, true fret spacing, drawn wound strings, material inlays, full and partial capos, scale highlights and a left-handed mode.
- **Band view and Keyboard mode:** every track on its own instrument following playback, or falling notes with play-along scoring and *Wait for me*.
- **Live input:** one button plays a guitar or MIDI keyboard through an FX chain, with no track and no recording.
- **Video from the score:** export a self-playing MP4, or record the score with the sound you hear.
- **Effects included, plug-ins welcome:** six built-in effects, plus VST2, VST3 and CLAP. 32-bit VST2 and VST3 plug-ins work too, through a helper process.
- **MIDI processing before every plug-in:** arpeggiator, step sequencer, humaniser, drum trigger and more, with articulation maps for sample libraries.
- **Yours to arrange:** remappable shortcuts with a command palette, dockable panes, and songs as tabs that move between windows.
- **Plug-in safety:** plug-ins outside Program Files load only once approved, and all run outside the editor; one that crashes is switched off.
- **Windows first, Linux coming:** experimental Linux packages include Wine, so Windows plug-ins can load there too.

## Writing tab and notation

Type frets with the keyboard; standard notation and tab update together. Click a picture for the full size.

<table>
<tr>
<td width="33%" valign="top"><a href="media/animations/score-entry.gif"><img src="media/animations/score-entry.gif" alt="Entering notes in the score" width="215"></a><br><b>Keyboard entry</b><br>Type frets (two quick digits for 10 and above), move with the arrows, <code>+</code> / <code>-</code> change the length, <code>.</code> adds a dot.</td>
<td width="33%" valign="top"><a href="media/screenshots/notation-clefs-harmonics.png"><img src="media/screenshots/notation-clefs-harmonics.png" alt="Standard notation above tab" width="215"></a><br><b>Notation and tab</b><br>Standard notation, tab or both, with the real clef on every system, two voices per bar, tuplets, ties and chord names.</td>
<td width="33%" valign="top"><a href="media/screenshots/tool-palette.png"><img src="media/screenshots/tool-palette.png" alt="Tool palette" height="150"></a><br><b>Tool palette</b><br>Durations, dynamics, effects and bar structure in tabbed pages. Right-click a tool to pin it to the quick strip.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/effect-bend.png"><img src="media/screenshots/0.9.0/effect-bend.png" alt="Bend editor" width="215"></a><br><b>Bend editor</b><br>Draw the curve on a grid or pick a type such as Bend, Release or Prebend, and save your own presets. It plays back as drawn and applies as one undo step.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/effect-tremolobar.png"><img src="media/screenshots/0.9.0/effect-tremolobar.png" alt="Tremolo bar editor" width="215"></a><br><b>Tremolo bar editor</b><br>Dips, dives and returns drawn the same way, with presets and your own saved shapes.</td>
<td width="33%" valign="top"><a href="media/screenshots/effect-trill.png"><img src="media/screenshots/effect-trill.png" alt="Trill editor" width="215"></a><br><b>Trill editor</b><br>Sets the trill fret and its speed: 16th, 32nd or 64th.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/effect-grace.png"><img src="media/screenshots/effect-grace.png" alt="Grace note editor" width="215"></a><br><b>Grace note editor</b><br>Fret, position before or on the beat, duration, dynamic and the transition into the main note.</td>
<td width="33%" valign="top"><a href="media/screenshots/effect-harmonic.png"><img src="media/screenshots/effect-harmonic.png" alt="Harmonic editor" width="215"></a><br><b>Harmonic editor</b><br>Natural, artificial, tapped, pinch or semi harmonics, with a fret box where one is needed.</td>
<td width="33%" valign="top"><a href="media/screenshots/score-text-style.png"><img src="media/screenshots/score-text-style.png" alt="Score text style window" width="215"></a><br><b>Score text styles</b><br>Fonts and sizes for each kind of text on the score.</td>
</tr>
</table>

- **30+ techniques**: bends, slides, hammer-ons, harmonics, palm mute, vibrato, tapping, slap and more.
- **Free rhythm entry**: a note can run past the end of the bar and the bar turns red instead of refusing (a strict mode exists). **Check bars (F4)** lists every bar that does not add up.
- **Edits during playback** are heard from the next bar.
- **Tools menu**: Transpose, Chord finder, Song stats, Scale finder and the Tuner.
- **Screen-reader support**: the tab editor announces track, bar, beat, string, fret and note name as the cursor moves.

<details><summary>More details</summary>

- **Techniques**: bends with curves, slides, hammer-ons/pull-offs, palm mute, let ring, vibrato and wide vibrato, natural/artificial/pinch/tapped harmonics, dead and ghost notes, tremolo picking, trills, fades, accents, staccato, tenuto, grace notes, arpeggio/brush strokes, tapping, slap and pop.
- **Edits during playback** are heard from the next bar. Bars you edit stay complete (*Fill incomplete bars with rests*, on by default); typing on a rest writes your remembered duration; the cursor stays on the note just written (*Move to the next note after writing*, off by default, moves it on).
- **Tidy bars:** with "Add rests automatically to fill bars" on, changing a note's length moves the notes after it along, and the rests at the end of the bar take up the change.
- **Delete in the score:** a bar left without notes becomes one whole-bar rest.
- **32nd and 64th notes** typed back to back beam together, and a bar holds 32 or 64 of them.
- **Edit cursor:** a see-through yellow highlight on the tab and a grey box behind the selected note on the staff; pausing or stopping moves it to where playback stopped.
- **Drum staff:** the percussion clef sits inside the staff, every drum note has a stem, and notes in one voice share a stem and beam.
- Fermatas, text, **mix table points (F10)** for tempo, volume and pan changes, copy/cut/paste beats and bars, repeat selections, insert/delete beats that keep the bar length.
- Engraving uses the real clef on every system, with harmonics at the fretted pitch, slide strokes and slurs, strum arrows, repeat-bar signs and volta brackets.

</details>

## Instruments on screen

A fretboard, keyboard or drum map matches each track, lights up as the song plays, and writes a note when clicked.

### Fretboard

[![The default Rosewood fretboard](media/screenshots/0.9.0/fretboard.png)](media/screenshots/0.9.0/fretboard.png)

Strings in the track's own tuning. The sounding note glows, the next notes are outlined and a line shows where the hand moves. The neck is drawn to real proportions, with true fret spacing, layered wood, shaded binding, material inlays and drawn wound strings that stay sharp at any size.

<table>
<tr>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-scale-highlight.png"><img src="media/screenshots/0.9.0/fretboard-scale-highlight.png" alt="A Minor Pentatonic highlighted on the fretboard" width="400"></a><br><b>Scale highlight</b><br>Any scale shown across the neck as Dots, Rings, Cell tint, Note names or Degrees, with your own colours.</td>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-left-handed.png"><img src="media/screenshots/0.9.0/fretboard-left-handed.png" alt="The left-handed fretboard" width="400"></a><br><b>Left-handed</b><br>The neck is mirrored and string 1 stays on top, matching the tab.</td>
</tr>
<tr>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-pearl-fins.png"><img src="media/screenshots/0.9.0/fretboard-pearl-fins.png" alt="The Pearl fins look" width="400"></a><br><b>Pearl fins</b><br>Rosewood, white binding and pearl shark fins.</td>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-ebony-blocks.png"><img src="media/screenshots/0.9.0/fretboard-ebony-blocks.png" alt="The Ebony blocks look" width="400"></a><br><b>Ebony blocks</b><br>Ebony, cream binding and ivory blocks.</td>
</tr>
<tr>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-maple.png"><img src="media/screenshots/0.9.0/fretboard-maple.png" alt="The Maple look" width="400"></a><br><b>Maple</b><br>Birdseye maple with black pearl dots.</td>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-flame-maple.png"><img src="media/screenshots/0.9.0/fretboard-flame-maple.png" alt="The Flame maple look" width="400"></a><br><b>Flame maple</b><br>Flame maple, black binding and abalone green dots.</td>
</tr>
<tr>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-settings-look.png"><img src="media/screenshots/0.9.0/fretboard-settings-look.png" alt="Fretboard settings, Look page" width="400"></a><br><b>Build your own look</b><br>Five presets, or mix any of nine woods, ten bindings, five inlay shapes and ten inlay materials. Open it with <b>Modify</b> under the Scales button, or right-click &gt; <i>Fretboard settings</i>.</td>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/fretboard-settings-notes.png"><img src="media/screenshots/0.9.0/fretboard-settings-notes.png" alt="Fretboard settings, Notes and markers page" width="400"></a><br><b>Notes and markers</b><br>Look-ahead notes, a faint trail of previous notes, fret numbers or note names, and the brightness and size of the markers.</td>
</tr>
<tr>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/partial-capo.png"><img src="media/screenshots/0.9.0/partial-capo.png" alt="Track properties with a partial capo on strings 2 to 4" width="400"></a><br><b>Capos and tuning</b><br>A full capo plus up to four partial capos, added and dragged on the fretboard or in Track properties (F6). Notes, playback and score labels follow. Tune 3 to 10 strings, with All -1 and All +1 and your own saved tunings.</td>
<td width="50%" valign="top"><a href="media/screenshots/0.9.0/scale-panel.png"><img src="media/screenshots/0.9.0/scale-panel.png" alt="The scale panel" width="400"></a><br><b>Scale panel</b><br>Detects the scale of the section, song or selection with Auto or a manual pick, shared with the status bar, and shows it on the fretboard.</td>
</tr>
</table>

- **Strings:** Steel, Nickel, Gold, Black or Bronze finish. A sounding string's ring can be Off, Mild, Medium or Strong, at Slow, Normal or Fast speed, and string numbers can be shown beside the nut.
- **Board:** brightness for the neck and notes on their own, 12 or 24 frets, Compact, Natural or Wide string spacing, and a Black, Bronze or Steel capo. A tall pane centres the board and a narrow one scales the drawing instead of squeezing the frets.
- **Scale highlight:** emphasise the root, dim the notes outside the scale, or show it only on the frets in view.
- **Scale finder:** the **Scales** button, right-click > *Find scale…* or Tools > Scale finder. It offers detected scales, scale families, a searchable list of every scale in all 12 keys, and a pick that sticks to the section and track.
- Chords move as one shape, and hovering previews where a click would write. Right-click to show this track as a fretboard, keyboard or drum map; the default is in Settings > Fretboard & Keyboard.

### Keyboard, drums and instruments

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/instrument-keyboard.png"><img src="media/screenshots/0.9.0/instrument-keyboard.png" alt="Keyboard view" width="215"></a><br><b>Keyboard</b><br>An 88-key keyboard, or 76, 61, 49, 37 or 25 keys, for keyboard tracks.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/instrument-drums.png"><img src="media/screenshots/0.9.0/instrument-drums.png" alt="Drum view" width="215"></a><br><b>Drum map</b><br>A percussion map for drum tracks; the pieces light up as they are hit.</td>
<td width="33%" valign="top"><a href="media/screenshots/instrument-picker.png"><img src="media/screenshots/instrument-picker.png" alt="Instrument picker" height="150"></a><br><b>Instrument picker</b><br>The full General MIDI set with pictures, categories and search.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/track-properties-drums.png"><img src="media/screenshots/0.9.0/track-properties-drums.png" alt="Track properties, drums" height="150"></a><br><b>Drum notation</b><br>Presets plus a custom map: tab line, tab text, staff position and notehead for every drum sound.</td>
</tr>
</table>

## Arrangement and tracks

One block per bar and track, with coloured sections across the top. Sections, bars, clips and tracks all move by dragging.

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/arrangement-timeline.png"><img src="media/screenshots/0.9.0/arrangement-timeline.png" alt="Arrangement timeline" width="215"></a><br><b>Timeline</b><br>Every bar of every track at a glance. Click to seek, drag across bars to select.</td>
<td width="33%" valign="top"><a href="media/animations/section-move.gif"><img src="media/animations/section-move.gif" alt="Moving a section" width="215"></a><br><b>Sections</b><br>Intro, Verse, Chorus and so on share a colour per type. Drag a section to move it with its bars and clips; Ctrl+drag moves only its marker.</td>
<td width="33%" valign="top"><a href="media/screenshots/sections-panel.png"><img src="media/screenshots/sections-panel.png" alt="Sections panel" height="150"></a><br><b>Sections panel</b><br>Add, rename, colour, loop and jump to sections. Lyrics is a tab beside it.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/animations/arrangement-drag.gif"><img src="media/animations/arrangement-drag.gif" alt="Dragging bars on the arrangement timeline" width="215"></a><br><b>Bar selections</b><br>Copy, cut, paste, move, delete, loop or skip the selected bars during playback.</td>
<td width="33%" valign="top"><a href="media/screenshots/timeline-delete-prompt.png"><img src="media/screenshots/timeline-delete-prompt.png" alt="The Delete prompt over selected timeline bars" width="215"></a><br><b>Delete on bars</b><br>One prompt: clear, remove and close the gap, or insert a gap, for all tracks or this one.</td>
<td width="33%" valign="top"><a href="media/animations/track-reorder.gif"><img src="media/animations/track-reorder.gif" alt="Reordering tracks by dragging" width="215"></a><br><b>Reorder tracks</b><br>Drag a track row up or down, or use Alt+Up and Alt+Down.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/audio-track-row.png"><img src="media/screenshots/audio-track-row.png" alt="Audio/MIDI track row with a waveform clip and the Add-track lane" width="215"></a><br><b>Audio/MIDI tracks</b><br>A track that holds audio and MIDI clips and no notation. It can be converted to a tab track and back.</td>
<td width="33%" valign="top"><a href="media/screenshots/timeline-drop.png"><img src="media/screenshots/timeline-drop.png" alt="Dropping a file on the timeline" width="215"></a><br><b>Drop files</b><br>Drop MP3, WAV, FLAC, OGG, AIFF, M4A, WMA or MIDI files on a track; a preview shows where they land.</td>
<td width="33%" valign="top"><a href="media/screenshots/timeline-drop-new-lane.png"><img src="media/screenshots/timeline-drop-new-lane.png" alt="A new lane opens on overlap" width="215"></a><br><b>Lanes</b><br>Overlapping clips open a new lane on the same track.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/clip-edit-fades.png"><img src="media/screenshots/clip-edit-fades.png" alt="An audio clip split in two, with fade handles and shaded fades" width="215"></a><br><b>Split, glue and fade</b><br><b>S</b> splits at the edit cursor, Ctrl+Shift+G glues, and fade handles sit at the top corners of a clip.</td>
<td width="33%" valign="top"><a href="media/screenshots/timeline-move-clip.png"><img src="media/screenshots/timeline-move-clip.png" alt="Moving a clip" width="215"></a><br><b>Move and snap clips</b><br>Drag a clip to another lane or track (Ctrl+drag copies). Snap aligns to the beat grid, the playhead or clip edges.</td>
<td width="33%" valign="top"><a href="media/screenshots/audio-track-main.png"><img src="media/screenshots/audio-track-main.png" alt="Track list with an audio/MIDI track" width="215"></a><br><b>Track list</b><br>Arm, mute, solo, level, pan and FX on every row, with collapsible groups.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/add-track.png"><img src="media/screenshots/0.9.0/add-track.png" alt="Add track" height="150"></a><br><b>Add track</b><br>Instrument and tuning for a new track; it lands at the end of the song (then move it with Alt+Up/Down).</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/track-row-menu.png"><img src="media/screenshots/0.9.0/track-row-menu.png" alt="Track right-click menu" height="150"></a><br><b>Track menu</b><br>Cut, copy, paste, duplicate, rename and colour whole tracks with their notation, clips, mixer settings and FX chain.</td>
<td width="33%" valign="top"><a href="media/screenshots/track-groups-menu.png"><img src="media/screenshots/track-groups-menu.png" alt="Track list groups menu" width="215"></a><br><b>Groups</b><br>Show tracks grouped by instrument family, with a header row for each group.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/track-properties-guitar.png"><img src="media/screenshots/0.9.0/track-properties-guitar.png" alt="Track properties for a seven-string guitar" width="215"></a><br><b>Track properties</b><br>Name, instrument, strings, tuning and capo for a track (F6), with up to 4 partial capos and one undo step.</td>
<td width="33%" valign="top"><a href="media/screenshots/global-tuning.png"><img src="media/screenshots/global-tuning.png" alt="Global tuning" width="215"></a><br><b>Global tuning</b><br>Shifts every track at once.</td>
</tr>
</table>

- **Add a track** from the Track menu, Ctrl+T, the Add track lane under the last track, or *Add track…* in the track list's right-click menu.
- **Master track lane** (track list right-click > *Show master track*): an overview above the first track, pictured under [At a glance](#at-a-glance).
- **Selection scope:** a timeline selection covers every track; bars selected in the score cover just that track, and copy, cut, paste and loop follow it.

<details><summary>More details</summary>

- **Drag a section's edge to resize it**: shrinking leaves empty bars, growing pushes the next section along. `Esc` clears a selection.
- **Stretch a section** by dragging its end past the next section (add empty bars, repeat its last bars or add a repeat sign), or its start back past the previous one (add lead-in bars, repeat its opening or take bars from the previous section); *Remember my choice* skips the question.
- **Close a gap**: right-click an empty area of the section strip to remove the gap and move later sections back; it asks first when the bars aren't empty.
- **Moving a section that audio crosses** asks whether to split the audio and move it along, or leave it in place (Preferences > Editing > Clips).
- **Drag takes** to other lanes or onto other tracks (Ctrl copies; below the last track makes a new track). When an edit makes clips overlap, TabForge offers to move the existing clip to a free lane.
- Right-click menus for bars, tracks and sections; drag tracks to reorder them.
- **Delete on bars** opens one prompt: clear (leave a gap), remove (close the gap) or insert a gap before or after, for all tracks or this track; Ctrl+Delete closes the gap, Ctrl+Shift+Space inserts a gap.
- **Add-track lane**: a strip under the last track (click it, or drop files on it); hide it in Preferences > Timeline & Tracks.
- **Sections carry their clips**: moving, copying or duplicating a section takes the clips inside it; the song grows to fit clips that run past its end.
- **S** splits the selected clip at the edit cursor, **Ctrl+Shift+G** glues, fade handles sit at the top corners. **Snap** aligns clips to a beat grid, the playhead or other clip edges; MIDI clips can be written into the track's notation.
- A live preview shows where a dropped file lands; overlaps open a new lane, and a MIDI groove lands on the song's bar grid. Drag a clip to another lane or track (Ctrl+drag copies it).
- **Clip edges extend past the media**: dragging an end beyond the source repeats it inside the clip (a dashed line marks each pass), and the song grows to hold it. With the snap magnet on, edges jump to beats.
- **Empty end bars are removed** when clips shrink (Settings > Editing > Clips, on by default), so moving or deleting clips does not leave blank bars at the end.
- **Collapsible timeline**: drag it down to about three track rows; its tracks and lanes then scroll with the mouse wheel.

An **audio/MIDI track** holds audio and MIDI clips and has no notation. Right-click it > *Convert to tab track* keeps every clip. Recording stops at the take's start; waveforms and MIDI notes appear while recording. Click anywhere on a lane to seek to that bar; clicking a take also selects it. Clips can be moved, trimmed, muted, duplicated, or edited through clip properties.

- **Tuning editor**: per-string `−`/`+`, type a note such as `Eb3` in a string's box, click a string to hear it, and choose 3 to 10 strings from the drop-down. Presets cover guitar, bass, ukulele, banjo, mandolin, violin, viola, cello, double bass, lap steel and dulcimer, with search and your own saved tunings. Choose whether retuning keeps the fret numbers or the sounding pitches; removing a string with notes asks first.
- **Track menu**: right-click a track row for Cut, Copy, Paste, Duplicate, Delete, Rename, Colour and Properties; whole tracks (notation, clips, mixer settings, FX chain) copy and paste. A focused track row takes Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D and Delete.
- **Mixer groups** collapse to one row and follow group rules you set for one song or all songs; a track's colour chip opens its colour palette.
- **Instrument and tool icons** are original artwork.

</details>

## Recording and live input

Capture song ideas quickly and turn them into notation and tab. It is a sketchpad for songwriting, not a full recording studio.

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/recording-lanes.png"><img src="media/screenshots/recording-lanes.png" alt="MIDI take lanes" width="215"></a><br><b>Takes in lanes</b><br>Ctrl+R records armed tracks into audio or MIDI clips. Overlapping and loop-recorded takes get their own lanes; click a take to choose it, Ctrl+click to layer.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/live-input.png"><img src="media/screenshots/0.9.0/live-input.png" alt="The transport bar and track list, with the live input microphone button at the far left" width="215"></a><br><b>Live input</b><br>The microphone button plays an audio input, a MIDI keyboard or both through a live FX chain and the master, with no track and no recording.</td>
<td width="33%" valign="top"><a href="media/screenshots/tuner.png"><img src="media/screenshots/tuner.png" alt="Tuner" height="150"></a><br><b>Tuner</b><br>Detects the pitch of the armed input and shows the note, a cents needle and the track's string tunings.</td>
</tr>
</table>

- **Inputs:** each track has an *Audio input* and a *MIDI input* list (None, a device or All MIDI inputs), so it can take audio, MIDI or both; the meter shows the level or the MIDI note velocity, and the speaker half of the input pill toggles monitoring. Every input of a multi-input device can be used, as one channel or a stereo pair; "Inputs shown" in the audio settings chooses which are listed.
- **MIDI monitoring:** a MIDI keyboard sounds through the armed track's own instrument and FX, or through the selected track when no armed track takes it (Preferences > MIDI recording > *Monitor MIDI input*). Waveforms and MIDI notes appear while recording.
- MIDI clips can be written into the track's notation.
- **Pop protection:** changing the audio device or its settings fades the sound out and back in (on by default).

## Piano roll

MIDI tracks and takes open in a piano roll. Switch any track between Piano and Tab with the pills in the menu bar.

<a href="media/screenshots/piano-roll.png"><img src="media/screenshots/piano-roll.png" alt="The piano roll with the lane picker and outlined notes" width="640"></a>

- **Tools:** Select, draw, erase, split and glue, with box and lasso selection. Right-click a piano key to select every note on that pitch.
- **Snap and quantize:** grid snap on Alt+S, quantize options and step input.
- **Scales:** the shared scale panel, *Lock to scale* for drawn and moved notes, and moving notes up or down by half steps or by steps of the scale.
- **Lanes:** velocity, controllers and pitch bend, from a lane picker under the grid.
- **Recording:** live MIDI recording goes straight into it, and playback follows along.

## Playback and practice

Play along with a metronome and count-in, loop any section, and let the speed trainer raise the tempo each pass.

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/timeline-playhead-styles.png"><img src="media/screenshots/timeline-playhead-styles.png" alt="Playback marker: line and bar marker" width="215"></a><br><b>Playback marker</b><br>A line or a bar marker on the timeline. In the score, the green line and note glow show only while the song plays.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/score-horizontal.jpg"><img src="media/screenshots/0.9.0/score-horizontal.jpg" alt="Horizontal score scrolling" width="215"></a><br><b>Score views</b><br>Page or continuous layout, scrolling vertically or horizontally with the whole song on one line. Only bars near the view are drawn.</td>
<td width="33%" valign="top"><a href="media/screenshots/mix-table.png"><img src="media/screenshots/mix-table.png" alt="Mix table" height="150"></a><br><b>Mix table points (F10)</b><br>Tempo, volume and pan changes written into the song and played back.</td>
</tr>
</table>

- MIDI playback with per-track mute, solo, volume and pan, including bends, slides, let ring, palm mute and swing.
- **Metronome and count-in** with their own sounds, levels and subdivisions.
- **Looping** of the song, a section or a selection; a **speed trainer**; skip areas.
- **Zoom, speed and tempo** sit in the **View and tempo** pane at the bottom of the side panel (speed 25 to 200%).
- **Status bar** shows Playing, Paused, Stopped or Recording, the playhead's bar, beat and time while a song plays (the edit cursor otherwise), the tempo in force at that position, and the likely scale of the section (click it for alternatives and settings).

<details><summary>More details</summary>

- **Song structure played as written:** repeats (up to x99, several closes, multi-pass endings such as 1.2.3.), D.S. / D.C. / Coda / Fine, and tempo changes that last until the next one, including partway through a bar.
- **Score follow**: the view keeps the playing bar in sight and stops if you scroll by hand. Settings > Playback & Practice chooses whether it follows, and whether it jumps or scrolls smoothly (glided at the display refresh rate, idle in between).
- **Two score views:** right-click the score > *Page layout* for **page** or **continuous**, scrolling **vertically** or **horizontally** (the whole song on one line). Only bars near the view are drawn, so long songs stay light.

</details>

## Band view, Keyboard mode and compact mode

### At a glance

A short tour of the band view, compact mode and the newer controls. Click a picture for the full size.

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/band-above.jpg"><img src="media/screenshots/0.9.0/band-above.jpg" alt="Band view with the fretboard above the tab" width="215"></a><br><b>Band view</b><br>One row per track, each framed in its colour. The fretboard now sits above the tab and only the first track shows one; the others show a line of music each. Fretboard beside the tab is in the BAND VIEW menu.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/compact-mode.jpg"><img src="media/screenshots/0.9.0/compact-mode.jpg" alt="Compact mode" width="215"></a><br><b>Compact mode</b><br>Hides the track rows and the menu row so the score gets the room. A strip of track capsules switches track, and its menu holds the menus and tools.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/live-input.png"><img src="media/screenshots/0.9.0/live-input.png" alt="The transport bar and track list, with the live input microphone button at the far left" width="215"></a><br><b>Live input</b><br>The microphone button plays your guitar or a MIDI keyboard through a live FX chain at once: no track, no recording. One for the whole program.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/master-lane.png"><img src="media/screenshots/0.9.0/master-lane.png" alt="The master track lane above the tracks" width="215"></a><br><b>Master track lane</b><br>An overview above the tracks: one thin line per track, filled where that track plays, with the master FX, volume and pan beside it.</td>
<td width="33%" valign="top"><a href="media/screenshots/icon-row.png"><img src="media/screenshots/icon-row.png" alt="The grouped icons at the right of the top bar" width="215"></a><br><b>Top bar icons</b><br>Grouped by kind: side panel, fretboard and Keyboard mode, Band view and compact mode, project settings. Drag an icon to reorder; the order is remembered.</td>
<td width="33%" valign="top"><a href="media/screenshots/zoom-speed.png"><img src="media/screenshots/zoom-speed.png" alt="The View and tempo pane" width="215"></a><br><b>View and tempo</b><br>Zoom, speed, tempo and time / key are one row of four buttons in the side panel: click for presets, right-click to fine-tune.</td>
</tr>
</table>

### Band view

Every track gets its own row: an instrument view and a scrolling tab lane, each row as tall as the music it shows. Open it from the top bar or **View > Band view**.

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/band-above.jpg"><img src="media/screenshots/0.9.0/band-above.jpg" alt="Band view, fretboard above the tab" width="215"></a><br><b>Fretboard above the tab</b><br>The default: the tab uses the full row width. Only the first track shows its fretboard; the others start with a thin name strip.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/band-beside.jpg"><img src="media/screenshots/0.9.0/band-beside.jpg" alt="Band view, fretboard beside the tab" width="215"></a><br><b>Fretboard beside the tab</b><br>For wide screens: the instrument sits in a column to the left of the tab. Chosen from the BAND VIEW drop-down.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/band-view-playing.jpg"><img src="media/screenshots/0.9.0/band-view-playing.jpg" alt="Band view while playing" width="215"></a><br><b>During playback</b><br>Every row keeps its playing line of music in view, also when rows have different heights.</td>
</tr>
</table>

- The first track gets two lines of music and the others one, when that fits. The drop-down also turns the track colour frame on or off, and *Reset view* returns to the defaults.
- The layout and colour frame belong to the program and are the same for every song; a song keeps only which of its tracks are shown and their order.
- The right-click menu has a **Row sizes** submenu; the playhead line is set in Preferences.

### Keyboard mode (experimental)

Practise a part by playing it along with a MIDI keyboard. Turn it on from the top bar or **View > Keyboard mode** (Ctrl+Alt+L).

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/keyboard-mode.jpg"><img src="media/screenshots/0.9.0/keyboard-mode.jpg" alt="Keyboard mode with falling notes" width="215"></a><br><b>Falling notes</b><br>Any track as notes falling onto a piano keyboard in time with playback, in hand colours with note names and finger numbers.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/keyboard-mode-wait.jpg"><img src="media/screenshots/0.9.0/keyboard-mode-wait.jpg" alt="Wait for me" width="215"></a><br><b>Wait for me</b><br>Ctrl+Alt+W holds the song at each chord until its notes are pressed, in any order. Skip (Ctrl+Alt+Q) moves on.</td>
<td width="33%" valign="top"><a href="media/screenshots/keyboard-mode-popout.png"><img src="media/screenshots/keyboard-mode-popout.png" alt="Keyboard mode in its own window" width="215"></a><br><b>Pop out</b><br>Ctrl+Alt+O shows the falling notes in their own window; F11 makes it full screen.</td>
</tr>
</table>

- **Play along**: the selected track is silenced and your keys play its sound live; its mute is never changed. Correct notes turn green, wrong ones red, with an accuracy figure and streak.
- **Timing tolerance**: Strict, Normal or Relaxed (the default).
- **Control bar**: track selector, speed, loop, hands and zoom. The MIDI input defaults to any available device.

*Keyboard mode is experimental and has not been tried with every MIDI device. Please report anything odd.*

### Compact mode

Compact mode gives the score the room. It hides the track rows and the menu row, and shows the master track lane at twice its height. A strip of track capsules under the title bar selects the track (the selected one is lit); right-click the strip, or click its caption, for the menus and tools, and the track controls become a slim column of three rows of buttons. Turn it on from the toolbar button beside Keyboard mode or **View > Compact mode** (Ctrl+Alt+C); pressing it again brings the tracks back as they were. It is pictured under [At a glance](#at-a-glance).

## Sound

Every track has level, pan, mute, solo, a level meter and an FX chain, with six built-in effects, your own plug-ins and MIDI processing in front of every plug-in. For plug-in formats and Linux, see [Plug-ins](#plug-ins).

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/mixer.png"><img src="media/screenshots/mixer.png" alt="Mixer groups and track controls" width="215"></a><br><b>Mixer</b><br>A row per track, with the Master row at the top and a live input row under it. Mute and solo are instant: solo wins, muted tracks are drawn grey.</td>
<td width="33%" valign="top"><a href="media/animations/mixer.gif"><img src="media/animations/mixer.gif" alt="Adjusting levels in the mixer" width="215"></a><br><b>Levels and pan</b><br>Sliders on one fine scale. Double-click a knob and type "-6 dB", "75%" or "L30"; Ctrl+click resets.</td>
<td width="33%" valign="top"><a href="media/screenshots/mixer-groups-collapsed.png"><img src="media/screenshots/mixer-groups-collapsed.png" alt="Mixer with a collapsed group" width="215"></a><br><b>Groups</b><br>Group rows set level, pan and pitch for all their tracks, collapse to one row, and each has an effects bus.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/mixer-group-rules.png"><img src="media/screenshots/mixer-group-rules.png" alt="Mixer group rules" height="150"></a><br><b>Group rules</b><br>Rules decide which tracks fall into which group, for one song or all songs.</td>
<td width="33%" valign="top"><a href="media/screenshots/mixer-colour-menu.png"><img src="media/screenshots/mixer-colour-menu.png" alt="Mixer colour chip menu" height="150"></a><br><b>Track colours</b><br>A track's colour chip opens its colour palette.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/fx-chain.png"><img src="media/screenshots/0.9.0/fx-chain.png" alt="Track FX chain" width="215"></a><br><b>FX chains</b><br>One instrument, then effects in series, each with bypass, wet/dry and its own window. Group plug-ins in containers; chains save and load as files, including .RfxChain files for other hosts.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/plugin-wiring.png"><img src="media/screenshots/0.9.0/plugin-wiring.png" alt="Audio and MIDI pin routing" height="150"></a><br><b>Wiring</b><br>Audio channels, MIDI source and channel filter, sidechain, and MIDI forwarding to another track.</td>
<td width="33%" valign="top"><a href="media/screenshots/fx-allow-again.png"><img src="media/screenshots/fx-allow-again.png" alt="Allow again" width="215"></a><br><b>Plug-in approval</b><br>Plug-ins under Program Files load without asking by default; others need your approval. A plug-in that crashes or freezes is named and switched off until you allow it again.</td>
<td width="33%" valign="top"><a href="media/screenshots/prefs-audio.png"><img src="media/screenshots/prefs-audio.png" alt="Preferences, audio" height="150"></a><br><b>Audio output</b><br>On Windows: WASAPI (shared or exclusive), ASIO or DirectSound. On Linux: ALSA, PulseAudio, PipeWire or JACK. With device, sample rate and buffer size.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/midi-processing-overview.png"><img src="media/screenshots/0.9.0/midi-processing-overview.png" alt="MIDI processing chain" width="215"></a><br><b>MIDI processing</b><br>Processors run top to bottom before the plug-in, on the song, live keys and tracks routed to it.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/midi-processing.png"><img src="media/screenshots/0.9.0/midi-processing.png" alt="MIDI processor chain" width="215"></a><br><b>Processor chain</b><br>Transpose, humanise, arpeggiator, step sequencer and more. Each can be bypassed, reordered by dragging and limited to some notes.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/midi-processing-search.png"><img src="media/screenshots/0.9.0/midi-processing-search.png" alt="Searching processors and parameters" width="215"></a><br><b>Search every parameter</b><br>One box finds processors and single parameters; Enter adds the processor or jumps to the parameter.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/midi-processing-velocity.png"><img src="media/screenshots/0.9.0/midi-processing-velocity.png" alt="Velocity processor" width="215"></a><br><b>Velocity</b><br>Scale and offset, a fixed value, a compressor mode and variation accents.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/midi-routing.png"><img src="media/screenshots/0.9.0/midi-routing.png" alt="MIDI routed into an instrument plug-in" width="215"></a><br><b>MIDI routing</b><br>MIDI from another track or a live keyboard routed into an instrument plug-in.</td>
</tr>
</table>

- **Built-in effects:** IR Loader (cabinet, room or speaker correction, with no added latency), EQ, Compressor, Limiter, Delay and Noise Gate. They are listed first in the Add plug-in window and work on tracks, group buses, the master and Monitor FX, on Windows and Linux.
- **Master FX and Monitor FX:** the Master row has an FX chain, pan and volume over the whole mix. The headphones button in the title bar runs a Monitor FX chain, kept for every song, that is heard live only and is never rendered or exported. It lights only when a plug-in really plays on the current device.
- **Active on:** set a plug-in or container to play only for a chosen driver type and output device.
- **Fallback sound:** with effects but no VST instrument, the track's General MIDI sound plays through the effects; if a VST instrument stops, the track falls back to it.
- **Sound engine:** Windows GM (the default on Windows), a SoundFont (GeneralUser GS, FluidR3 GM or MuseScore General, each downloaded once when you ask), or None if every track uses your own instruments.
- **Articulations:** a loaded instrument's MIDI window can turn note techniques such as palm mute, harmonics and slides into the keyswitches its library expects, with maps you can save.
- **Meters and Panic:** every track row has a stereo meter, the transport bar has a master meter, and Tools > Panic releases every sounding note.
- **Saving:** `.gp` with the settings inside, a clean `.gp` plus `.tfaudio`, or `.tforge`.

**Current limits:** sidechain input works with VST2 plug-ins that have four or more inputs; offline rendering does not yet compensate plug-in delay on sidechain and bus routes; a compatible .gp export lists what it cannot keep before you save.

<details><summary>More details: plug-ins, mixer and the MIDI processors</summary>

- With effects but no VST instrument, the track's own General MIDI sound plays through the effects (rendered from Windows' own sound bank); a *MIDI sound* switch turns it off.
- **Audio output** device, sample rate and buffer size are in **Settings > Audio & Plug-ins**. Plug-in folders are added by browsing (no automatic scan unless you turn it on). Changing the audio device or its settings fades the sound out and back in, so speakers do not pop (Protect speakers from pops when audio settings change, on by default).
- **Wiring** selects audio input channels, the MIDI source/channel filter, sidechain input (VST2 with four or more inputs) and MIDI output forwarding to another track.
- **Serial MIDI chain:** MIDI flows through the plug-in list in order; instruments add their audio, effects process everything before them.
- **Per-plug-in MIDI processors** run right before their plug-in: note mapping, transposition, velocity, humanisation, delay, CC tools, scale/chord tools, arpeggiator, randomizers, LFOs, step sequencer, audio-to-MIDI drum trigger and loopers. Each has an "Applies to" note filter; whole-list presets (with built-ins) and a search that indexes every parameter; all saved with the song.
- **Group rows** set level, pan and pitch for all their tracks at once; tracks can be moved between groups. The Mixer is the fader button beside the tuning fork.
- Renders pass through a transparent safety limiter.

**MIDI processors** (the **MIDI…** button in the FX chain):

- **Velocity**: scale and offset, fixed value, compressor mode, variation accents.
- **Pitch**: transpose (±64), note map for drum maps (with an editor and a "Superior Drummer 3" map), snap to scale / key, chord (up to four voices).
- **Timing**: humanizer, MIDI delay (ms, beats or samples).
- **Filtering and routing**: channel filter / remap and router, note range filter, choke and choke group, duplicate-note sanitizer, note hold.
- **Controllers**: program / bank select, CC sender and mapper, LFO to any CC or pitch wheel.
- **Generators**: note repeater, arpeggiator, randomizers, step sequencer.
- **Audio-coupled**: audio-to-MIDI drum trigger, MIDI EQ ducker, loop sampler, synchronized looper.
- **Utilities**: All notes off / Panic and a live **MIDI log**.

**Every parameter is searchable**: one box finds processors and individual parameters ("Processor › Parameter"); Enter adds the processor or jumps to the parameter. Processors can be limited to some notes and to an input channel. A note-off always follows its note-on, so nothing hangs. Each processor can be bypassed, reordered by dragging, and set by typing into its knobs. Whole chains save as **presets**, built-in ones such as "Humanize light" and your own. Presets can be organised in their drop-downs and in a preset manager (**Tools > Preset manager…**).

</details>

## Plug-ins

Add instruments and effects from the Add plug-in window. Plug-in folders are added by browsing, and there is no automatic scan unless you turn it on.

| Plug-in | On Windows | On Linux (experimental) |
|---|---|---|
| Built-in effects | Included | Included |
| VST3, 64-bit | Loads natively | Linux builds load natively; Windows builds through Wine |
| VST3, 32-bit | Through a helper, shown as *32-bit (bridged)* | Windows builds through Wine |
| VST2, 64-bit | Loads natively | Windows builds through Wine, shown as *64-bit (bridged)* |
| VST2, 32-bit | Through a helper, shown as *32-bit (bridged)* | Windows builds through Wine |
| CLAP (opt-in) | Loads natively | Loads natively, not yet confirmed |

Turn CLAP on with *Also list CLAP plug-ins (new)* in Settings > Audio & Plug-ins.

- **32-bit plug-ins:** they load through a small helper process beside `TabForge.exe`, with audio, MIDI input, parameters, state and their own editor. If one crashes, TabForge writes a short report and switches it off. The rest of your session carries on.
- **Crash protection:** plug-ins run in an audio engine process that starts only when a track uses them. *Run each plug-in separately* in Settings isolates each one, at a little more CPU and memory. The exception is a faulty native 64-bit VST2 plug-in, which can still end the audio engine.
- **Approval:** plug-ins under Program Files load without asking by default, and others need your approval. *Approve all* and *Approve selected* show their progress and can be stopped.
- **Trust:** this is crash protection, not a security sandbox. A plug-in runs with your own permissions and can read and write your files, so load only plug-ins you trust.
- **No editor?** Every VST2, VST3 and CLAP plug-in also lists its parameters as searchable sliders.

### Windows plug-ins on Linux through Wine

The Linux packages include Wine 9.21 and TabForge's helpers, so Windows VST2 and VST3 plug-ins, 32-bit and 64-bit, can load with nothing else to install. TabForge uses its own private Wine setup and never touches `~/.wine`. The first use takes a moment to prepare it. Windows plug-ins open their editor in a separate floating window, from the **Editor** button in the FX window.

This is experimental. The audio route has been measured on automated test machines, not on real hardware. Known limits:

- An editor built with JUCE 8 may stay black, while audio, state and parameters still work.
- The lowest measured buffer is about 256 frames with one bridged plug-in and about 1024 with eight.
- A bridged plug-in's own preset menu has ended its helper, for a cause not yet known. Reload the plug-in if that happens.
- A plug-in that needs a copy-protection driver may refuse to load. TabForge says why rather than failing silently, and does the same for a missing Wine or a broken Wine setup.


## Export

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/render.png"><img src="media/screenshots/render.png" alt="Render dialog" height="150"></a><br><b>Render audio</b><br>Ctrl+Alt+R: the master mix, track stems or both, for the song, a selection or a custom range, as WAV 16/24/32-bit float or MP3.</td>
<td width="33%" valign="top"><a href="media/screenshots/video-export-dialog.png"><img src="media/screenshots/video-export-dialog.png" alt="Video export" height="150"></a><br><b>Export video</b><br>File > Export > Video (MP4): the song as a video that plays itself with the full mix, at 1080p or 4K and 30, 60 or 120 frames per second.</td>
<td width="33%" valign="top"><a href="media/screenshots/record-video-button.png"><img src="media/screenshots/record-video-button.png" alt="Record video button next to Settings" width="215"></a><br><b>Record video</b><br>The camera button (Ctrl+Alt+V) plays the song and records the chosen part of the screen and everything you hear to MP4.</td>
</tr>
</table>

| Format | Open | Save |
|---|---|---|
| `.gp3`, `.gp4`, `.gp5` | ✔ | export a copy |
| `.gpx` | ✔ | |
| `.gp` | ✔ | ✔ (default) |
| TabForge project (`.tforge`) | ✔ | ✔ |
| Standard MIDI (`.mid`) | ✔ | export |
| ASCII tab (`.txt`), PDF, MusicXML (`.musicxml`) | | export |
| Audio (WAV, MP3) and video (MP4) | | export |

- **Video layouts**: one track with its fretboard, keyboard or drums; score only; Band view; or score and Band view. Choose the range (whole song, selection, bars or a section), the tracks, and dark or light.
- **Recording** covers the score, Band view, the whole window, or score with instrument. A red REC time shows in the status bar; stopping saves the file in Videos\TabForge.
- Score-only export is about twice as fast, and the progress bar shows the phase and time left.
- **PDF** gives engraved notation and tab as A4 pages; **Print** and **Print preview** are in the File menu.
- **MIDI:** opening a MIDI file asks whether to make tab and notation tracks or MIDI tracks. File > Export > MIDI picks the tracks and range, plays the song through each track's MIDI processors, and can apply articulation maps.

<details><summary>More details</summary>

Drum parts import from every format, including `.gpx` extended articulations and `.gp` drum kits. Known limit: .gp3/.gp4/.gp5 files open with up to 20,000 bars per track. `.gtp` and `.tg` files are not supported.

A `.gp` saved by TabForge follows the `.gp` file format **and** carries the complete TabForge project inside it (an extra entry other readers can ignore), so reopening it in TabForge loses nothing: drum map, section sizes, score styles, mix points. Saved `.gp` files carry correct zip checksums and keep the album, copyright, tab author, music, words and instructions fields.

**File > Render…** exports the master mix, track stems or both, for the full song, a selection or a custom range, with tail length, file-name wildcards and WAV 16/24/32-bit float; MP3 bit rates are offered when Windows has an encoder. Rendering shows progress and can be cancelled. PDF export gives engraved notation and tab as A4 pages.

</details>

## Customising

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/main-window.jpg"><img src="media/screenshots/0.9.0/main-window.jpg" alt="Main window" width="215"></a><br><b>Dockable panes</b><br>Drag any pane by its tab to dock, tab, split or float it. Layouts are remembered; Compose, Practice and Mix are on Ctrl+1/2/3.</td>
<td width="33%" valign="top"><a href="media/screenshots/0.9.0/main-window-light.jpg"><img src="media/screenshots/0.9.0/main-window-light.jpg" alt="Light theme" width="215"></a><br><b>Themes</b><br>Dark, Light and System, with every colour adjustable. One click sets interface, accent, icons and score page together.</td>
<td width="33%" valign="top"><a href="media/screenshots/command-palette.png"><img src="media/screenshots/command-palette.png" alt="Command palette" height="150"></a><br><b>Command palette</b><br>Ctrl+Shift+A searches every command by name, shows its key and runs it with Enter.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/settings-hotkeys.png"><img src="media/screenshots/settings-hotkeys.png" alt="Shortcut settings" height="150"></a><br><b>Shortcuts</b><br>Two shortcuts per command, presets for TabForge, Classic and Alternative, and conflict warnings. Navigation keys also work from the FX chain, plug-in windows and other tool windows.</td>
<td width="33%" valign="top"><a href="media/screenshots/settings-search.png"><img src="media/screenshots/settings-search.png" alt="Settings search" height="150"></a><br><b>Searchable settings</b><br>Every setting can be found by name, with a live preview before you apply.</td>
<td width="33%" valign="top"><a href="media/screenshots/project-settings.png"><img src="media/screenshots/project-settings.png" alt="Project settings" height="150"></a><br><b>Project settings</b><br>Settings that belong to one song and are saved with it.</td>
</tr>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/tutorial-basic.png"><img src="media/screenshots/tutorial-basic.png" alt="Tutorial window" width="215"></a><br><b>Tutorial</b><br>Help > Tutorial: a Basic Guide in seven short chapters.</td>
<td width="33%" valign="top"><a href="media/screenshots/tutorial-detailed.png"><img src="media/screenshots/tutorial-detailed.png" alt="Detailed Guide with search" width="215"></a><br><b>Detailed Guide</b><br>A longer guide with search and Export PDF.</td>
</tr>
</table>

- **Document tabs in the title bar**: several songs open at once and several tabs can play at the same time; drag a tab to reorder, out to a new window, or onto another TabForge window.
- **Top bar icons** are grouped by kind and can be dragged into another order.
- **Message boxes** follow the light or dark theme.
- Settings import and export as one file; **Reset all** and **Import** cover every section, and a second TabForge window follows settings changed in the first.

**Preferences pages** (F12)

<table>
<tr>
<td align="center" valign="top"><a href="media/screenshots/settings-general.png"><img src="media/screenshots/settings-general.png" alt="General" width="92"></a><br>General</td>
<td align="center" valign="top"><a href="media/screenshots/settings-appearance-colours.png"><img src="media/screenshots/settings-appearance-colours.png" alt="Appearance and colours" width="92"></a><br>Appearance and colours</td>
<td align="center" valign="top"><a href="media/screenshots/settings-score-notation.png"><img src="media/screenshots/settings-score-notation.png" alt="Score and notation" width="92"></a><br>Score and notation</td>
<td align="center" valign="top"><a href="media/screenshots/settings-playback-sound.png"><img src="media/screenshots/settings-playback-sound.png" alt="Playback and sound" width="92"></a><br>Playback and sound</td>
<td align="center" valign="top"><a href="media/screenshots/settings-audio-vst.png"><img src="media/screenshots/settings-audio-vst.png" alt="Audio and plug-ins" width="92"></a><br>Audio and plug-ins</td>
<td align="center" valign="top"><a href="media/screenshots/settings-editing.png"><img src="media/screenshots/settings-editing.png" alt="Editing" width="92"></a><br>Editing</td>
</tr>
<tr>
<td align="center" valign="top"><a href="media/screenshots/settings-timeline-sections.png"><img src="media/screenshots/settings-timeline-sections.png" alt="Timeline" width="92"></a><br>Timeline</td>
<td align="center" valign="top"><a href="media/screenshots/settings-fretboard.png"><img src="media/screenshots/settings-fretboard.png" alt="Fretboard and keyboard" width="92"></a><br>Fretboard and keyboard</td>
<td align="center" valign="top"><a href="media/screenshots/settings-tabs-windows.png"><img src="media/screenshots/settings-tabs-windows.png" alt="Tabs and windows" width="92"></a><br>Tabs and windows</td>
<td align="center" valign="top"><a href="media/screenshots/preferences-common-settings.png"><img src="media/screenshots/preferences-common-settings.png" alt="Common settings" width="92"></a><br>Common settings</td>
<td align="center" valign="top"><a href="media/screenshots/preferences-search.png"><img src="media/screenshots/preferences-search.png" alt="Search results" width="92"></a><br>Search results</td>
</tr>
</table>

**Menus**

<table>
<tr>
<td align="center" valign="top"><a href="media/screenshots/0.9.0/menu-file.png"><img src="media/screenshots/0.9.0/menu-file.png" alt="File" height="140"></a><br>File</td>
<td align="center" valign="top"><a href="media/screenshots/menu-edit.png"><img src="media/screenshots/menu-edit.png" alt="Edit" height="140"></a><br>Edit</td>
<td align="center" valign="top"><a href="media/screenshots/menu-bar.png"><img src="media/screenshots/menu-bar.png" alt="Bar" height="140"></a><br>Bar</td>
<td align="center" valign="top"><a href="media/screenshots/menu-track.png"><img src="media/screenshots/menu-track.png" alt="Track" width="92"></a><br>Track</td>
<td align="center" valign="top"><a href="media/screenshots/menu-note.png"><img src="media/screenshots/menu-note.png" alt="Note" height="140"></a><br>Note</td>
<td align="center" valign="top"><a href="media/screenshots/0.9.0/menu-effects.png"><img src="media/screenshots/0.9.0/menu-effects.png" alt="Effects" height="140"></a><br>Effects</td>
</tr>
<tr>
<td align="center" valign="top"><a href="media/screenshots/0.9.0/menu-insert.png"><img src="media/screenshots/0.9.0/menu-insert.png" alt="Insert" height="140"></a><br>Insert</td>
<td align="center" valign="top"><a href="media/screenshots/0.9.0/menu-tools.png"><img src="media/screenshots/0.9.0/menu-tools.png" alt="Tools" height="140"></a><br>Tools</td>
<td align="center" valign="top"><a href="media/screenshots/0.9.0/menu-view.png"><img src="media/screenshots/0.9.0/menu-view.png" alt="View" height="140"></a><br>View</td>
<td align="center" valign="top"><a href="media/screenshots/menu-options.png"><img src="media/screenshots/menu-options.png" alt="Options" width="92"></a><br>Options</td>
<td align="center" valign="top"><a href="media/screenshots/menu-help.png"><img src="media/screenshots/menu-help.png" alt="Help" width="92"></a><br>Help</td>
</tr>
</table>

An **Insert** menu gathers everything you can add, Effects are grouped into submenus and Export is a File submenu.

<details><summary>More details</summary>

- **Layout**: score in the middle (standard notation + TAB, or either alone), fretboard on top, arrangement timeline and track list at the bottom, tool palette and sections on the right.
- **Drag any panel by its tab**; the landing place is highlighted in blue. Drop on the middle of a panel to add a tab there, near an edge to split, or outside the window to float it (drag back to re-dock). Resize splits by dragging; hide panels from the View menu; **Reset all panels to original positions** (View menu) restores everything.
- **Themes**: one click sets interface, accent, icons, transport buttons and score page together (the light theme is a soft grey). Score paper, notation, staff lines, playback colours and fonts can still be changed (Settings > Appearance & colours).
- A "Classic" shortcut preset is included; every command shows its shortcut, and every shortcut can be changed.
- **Themes**: dark, light and system presets or fully custom colours; interface scale applies live; score fonts per text area.
- **Help > About** shows the version, the independent-project notice and a **Licences** button.
- **Windows integration**: turn "Open .gp and .tforge files with TabForge" is on by default for your Windows account; turn it off in Settings > General.
- Shift+F10 or the Menu key opens the right-click menu in the score, timeline and fretboard.

</details>

### Keyboard basics

| Keys | Action |
|---|---|
| `0`–`9` | Enter a fret (type two digits quickly for 10+) |
| `←` `→` / `↑` `↓` | Previous/next beat / string up/down |
| `+` `-` `.` | Shorter / longer note (reversible in Settings), dotted |
| `Space` / `Shift+Space` | Play/pause / play from the start |
| `F9` | Loop on/off |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |

## Files, safety and updates

<table>
<tr>
<td width="33%" valign="top"><a href="media/screenshots/new-from-template.png"><img src="media/screenshots/new-from-template.png" alt="New from template" width="215"></a><br><b>Templates</b><br>File > <i>New from template</i> (built-in and your own) and <i>Save as template</i>.</td>
<td width="33%" valign="top"><a href="media/screenshots/autosave.png"><img src="media/screenshots/autosave.png" alt="Autosave setting" height="150"></a><br><b>Autosave and recovery</b><br>Unsaved songs are copied to the Recovery folder every 1 to 30 minutes and offered back after a crash.</td>
</tr>
</table>

- **Audio stays with the song:** *Copy audio with this song* copies outside audio into one song folder (or a Media folder beside it); *Find missing audio* relinks missing clips from a folder you choose.
- **Safe saving:** files are written to a temporary file and swapped in atomically; a `.gp` plus `.tfaudio` pair is restored to its last complete state if a save is interrupted.
- **Untrusted input:** every file, clipboard paste and settings file is size-checked and validated before use.
- **Plug-ins** outside Program Files load only after you scan or approve them, and only while the file is the one you approved. Plug-ins installed under Program Files load without asking by default (Preferences > Audio & plug-ins: "Load installed plug-ins without asking").
- **No telemetry, no auto-updates, no analytics.** The update check is the only network access TabForge starts on its own: one anonymous HTTPS GET, at most once a day, to `https://api.github.com/repos/cobhc95/TabForge/releases?per_page=10`. Switch it off in **Settings > General > Updates**; Help > *Check for updates…* checks now. The optional *Download SoundFont…* button fetches one file only when you click it.
- **Windows integration** ("Open .gp and .tforge files with TabForge") applies to your account only and can be turned off in Settings > General, which removes its entries.
- **Command line:** `TabForge.exe song.gp5 [more files]` opens files as tabs; `--theme dark|light|system` starts with that theme.

<details><summary>Security and privacy in full</summary>

TabForge treats every file, clipboard paste and settings file as **untrusted input**:

- **Bounded reads everywhere**: projects and score files are capped at 128 MB, settings at 2 MB, JSON
  nesting at 64 levels. Files are size-checked *before* they are read into memory.
- **Structure limits** checked before and after parsing — e.g. at most 256 tracks, 20,000 bars per track,
  1,000,000 notes, 16 strings, 64 techniques per note, 512 bend points — to keep the memory and CPU a crafted file can use bounded
  (hardened continuously; not a guarantee). Every loaded project, undo snapshot and paste is validated the same way.
- **Zip-bomb safe `.gp` reading**: the embedded project is inflated with a hard cap (its declared size is
  not trusted), and anything wrong falls back to the normal import.
- **Safe saving**: files are written to a temporary file and swapped in atomically, so a crash or full disk
  never leaves a half-written song; paths and extensions are checked before writing.
- **Crash safety**: an unexpected error is logged, every song with unsaved changes is copied to
  `%LOCALAPPDATA%\TabForge\Recovery`, and you are told where — the window stays open so you can save.
- **Plug-ins are isolated from the editor, for crashes**: folder discovery is bounded and does not run plug-in code inside
  TabForge. Plug-in identification and audio hosting use separate processes; an individual plug-in can still
  crash or hang its host, so keep backups. **This is crash isolation, not a security sandbox**: a plug-in runs
  with your user rights (in its own process too), so load only plug-ins you trust.
- **A plug-in is loaded only if you scan or approve it, and only while it is the file you approved**: a song can
  only name plug-in paths. Outside Program Files / Common Files an approval covers that exact file (its SHA-256),
  network and removable locations always need an approval, and a file that changed is not loaded until you
  approve it again (a new build signed with the same key is accepted).
- **Update check — the only network access TabForge starts on its own, and deliberately tiny.** No telemetry, no auto-updates, no
  analytics. The optional update check (on by default; switch at the top of **Settings > General > Updates**,
  and a tick box in the update window itself) is limited to:
  - **one anonymous HTTPS GET, at most once a day**, to a single fixed address —
    `https://api.github.com/repos/cobhc95/TabForge/releases?per_page=10` — about 8 seconds after start-up, in the background;
  - **nothing sent** except the User-Agent GitHub requires (`TabForge/<version>`): no account, no cookies, no
    identifiers, no file or song information;
  - **TLS 1.2/1.3 only** with normal Windows certificate validation; **no redirects followed**, no cookies stored,
    no credentials, no decompression; 10-second timeout; the reply is capped at **512 KB** (headers 64 KB) and
    must be JSON;
  - **only three fields are read** from each release (`tag_name`, `draft` and `prerelease`) and a tag is accepted only if it is a
    plain version such as `v0.1.0-beta.2` (strict pattern, time-limited regex) — no text, links or files from
    GitHub are shown, opened or saved;
  - the **download page is built locally** from that validated version on `github.com/cobhc95/TabForge`, and it
    only opens in your browser when you click *Open download page*; **TabForge never downloads or installs
    anything itself**;
  - **offline or blocked networks are silent** (no error pop-ups); nothing runs when it is switched off, except
    Help > *Check for updates…* when you choose it; the program's unattended command-line runs never contact the network.
  Diagnostics are opt-in, size-capped and never include your music.
- **Download SoundFont… (optional)**: only when you click it, TabForge fetches the GeneralUser GS SoundFont (about 32 MB) from
  its GitHub project once and checks it against a fixed SHA-256 before using it.
- **Windows integration is per-account and reversible**: it writes only TabForge's own per-user entries
  (`HKCU\Software\Classes\TabForge.*` and TabForge in each extension's *Open with* list), never takes over a
  file type another program already owns, and turning it off (or uninstalling) removes exactly those entries.
- **Minimal supply chain**: a small, listed set of dependencies (alphaTab, NAudio, MeltySynth, SoundTouch.Net, a native VST3
  bridge built from the Steinberg VST3 SDK, the original native 32-bit and 64-bit VST2 helpers, and on Linux a pinned Wine and FFmpeg; see [THIRD_PARTY.md](THIRD_PARTY.md)), pinned with lock files, restored
  in locked mode and checked by NuGet's vulnerability audit on every build.

</details>

<details><summary>Resources and performance</summary>

TabForge is built to stay light while you write and play:

- No per-frame work unless something is actually moving: a note change repaints only the playing line of the score,
  a ringing string only the strings over a kept picture of the neck, and the timeline and track list redraw only what
  changed; drum-hit glows animate only while they fade.
- Plug-ins load when first needed, not when a song opens, so a song you only read or edit stays light. The audio
  engine wakes about a third as often while idle, and memory left over from opening a song is handed back.
- Precompiled (ReadyToRun) and memory-conserving .NET settings; memory is returned to Windows after loading
  a song; undo history is stored compressed (about 1% of the raw size).
- Playback runs on its own high-precision thread; the interface only observes it, so a busy UI never
  makes the music stutter, and settings changes never restart the engine unless they change the sound.
- Adjustable in Settings: smooth-scroll frame-rate cap, follow mode (off / jump / smooth), fretboard and
  highlight effects, panels you do not need can be hidden.
- In normal use on a modern CPU it typically uses **~150–250 MB RAM and ~1–2% CPU**.

</details>

## Full feature reference

Every command can be found in the menus, the command palette and the shortcut settings (all rebindable).

<details><summary>Show the full feature reference</summary>

#### Files, tabs and export
- **New**, **New tab**, **Open**, **Open in new tab**, **Save**, **Save as**, **Close tab**, **Duplicate tab**, Exit.
- **Templates**: File > *New from template* (built-in and your own) and *Save as template* (your templates live in `%APPDATA%\TabForge\Templates`).

- **Export**: Standard MIDI, ASCII tab, **PDF** (engraved notation and tab as A4 pages, no printer needed), **MusicXML** (`.musicxml`, one part per track with a tab staff), clean **`.gp`**, **Render to audio** (File > Export > Audio file…, WAV / MP3, master and stems), **video export** (MP4) and **record video** (Ctrl+Alt+V).
- **Print** and **Print preview**; **Score information** (F5).
- **Command palette** (Ctrl+Shift+A): fuzzy-search every command by name, see its key, press Enter to run it.

- **Autosave and recovery**: unsaved songs are copied to the Recovery folder every 1 to 30 minutes (Settings > General); after a crash or power loss TabForge offers to reopen them. Your own files are never overwritten.

- **Safe saving**: atomic writes, and a `.gp` plus `.tfaudio` pair is restored to its last complete state if a save is interrupted.
- **Command line**: `TabForge.exe song.gp5 [more files]` opens files as tabs (Explorer double-click hands over to the running instance, or opens a new window, as you choose); `--theme dark|light|system` starts with that theme (kept in your settings).
- Drag-and-drop tabs between windows; **Windows 11 Snap Layouts** on the maximise button; full-screen mode (F11).

#### Editing and notation
- Note entry: fret digits, arrow navigation, **durations** (whole to 64th, increase/decrease, dotted, double dotted, triplet), rest, tie, fermata, accent, staccato, tenuto, **shift pitch** by semitone, **move to higher/lower string** (Alt+Up/Down in the score), copy last beat.
- **Effects**: dead, ghost, palm mute, let ring, hammer/pull, bend, legato and shift slide, vibrato, wide vibrato, tremolo bar, natural and artificial harmonics, tapping, slap, pop, trill, tremolo picking, fade in/out, wah open/close, brush and arpeggio up/down, grace note, chord names, beat text, lyrics.
- **Bars**: insert, delete, duplicate, repeat selection as bars, time signature, key signature, clef, triplet feel, repeat open/close, directions and endings, double bar, repeat one or two bars, section editor, **Check bars (F4)**, **Go to bar**, mix table points (F10).
- Insert/delete beats, cut/copy/paste, select whole track, full undo/redo (fast on large songs).
- **Copy, cut and paste** of beat ranges (from the cursor, flowing over bar lines by the target's time signature) and whole bars, shared with the timeline clipboard. Pasting onto notes inserts the copied beats in the bar, and "Remember my choice" (Preferences > Editing > Copy and paste) covers the rest (overwrite or insert before/after, keep the pitch or shift an octave, bar settings); it works between instruments (guitar to bass, other tunings, drums) and **Paste special** (Ctrl+Shift+V) offers Replace, repeats, an octave shift and keep string and fret. Every paste is one undo step and cutting bars empties them.
- **Tools menu**: Check bar duration, **Transpose**, **Scale finder**, **Tuner**, **Metronome**, **Count-in**, preview note sound.
- **Tuner**: a chromatic tuner; the engine detects the pitch of the armed input and shows the note, a cents needle and the track's string tunings with the nearest string highlighted.

- **Score views**: tab + standard, tab only, standard only; dark or light score page; continuous line or individual notes; page/continuous layout; vertical or horizontal scrolling; zoom 75 / 100 / 150 % and Ctrl+/- zoom; **stylesheet** (F7).
- **Screen-reader support**: the tab editor announces track, bar, beat, string, fret and note name as the cursor moves, and every control has an accessible name and a stable id.

#### Side panes and panels
- **Compact mode** (View menu, Ctrl+Alt+C): hides the track rows and the menu row, with a header strip of track capsules.
- **View and tempo** pane: zoom, speed, tempo, time and key in one row at the bottom of the side panel; **Lyrics** is its own pane beside Sections.
- **Tools** (durations, dynamics, effects, bar structure), **Structure**, **Rhythm** and **Layout** palette pages, pinnable quick strip.
- **Sections** panel; **Lyrics**; **Scale highlight**; **Song stats** and **Chord finder** in the **Tools** menu.
- **Zoom and speed** in the **View and tempo** pane at the bottom of the side panel: zoom out/in, zoom box and the single playback-speed box (25 to 200 %, presets 50 to 200 %). The View menu zoom items and the hotkeys also work.

- **Speed hotkeys**: Ctrl+Alt+Up (faster), Ctrl+Alt+Down (slower), Ctrl+Alt+0 (back to 100 %).
- **Layouts**: Compose, Practice and Mix (Ctrl+1/2/3), save, delete, reset; show/hide the side panel and the fretboard; *Reset all panels to original positions*.
- Loop settings (loop count, speed trainer that raises the tempo each pass), metronome and count-in settings (volume, click sound, subdivision, accent).

#### Fretboard, keyboard and drums
- Fretboard, 88-key (or 76/61/49/37/25) keyboard and drum percussion map, left-handed view, note names, 12/24 frets, scale highlight and *Clear highlight*.
- **Fretboard settings** (Modify under the Scales button, or right-click): Look, Notes and markers, Layout and size, Strings and capo, and Scale highlight, with Cancel, Apply, OK and a link to more settings in Preferences.
- **Looks:** presets Rosewood, Pearl fins, Ebony blocks, Maple and Flame maple, or any mix of wood, binding, inlay shape and material; separate neck and note brightness; string and capo finishes.
- **Number size** (Small, Medium, Large), **Lock fretboard size**, and a pane that scales its drawing when resized.
- **Show / hide the fretboard** (toolbar button and hotkey), *Switch instrument view*, per-track or all-tracks instrument view; Tab mode and the piano roll remember it separately.
- **Sounding strings** show a soft blur for their note's length, with level and speed settings.
- **Drag the drawn capo** on the fretboard to another fret; the song updates on release as one undo step.

#### Tracks and Mixer
- **Add** Guitar, Bass, Drums, Keys or any instrument (Track menu, the Add track lane or Ctrl+T), delete, move up/down, **Track properties** (F6), instrument picker, tuning editor, drum notation presets, **Global tuning**, next/previous track, multitrack (F3) and global (F8) views. The Add track window is the same window; it adds at the end of the song.
- **Capos**: full capo (Off to fret 12) plus up to 4 partial capos per track, each covering the ticked strings; the score shows a capo line and the track row a badge. Partial capos live only in .tforge songs (not written to .gp, .gp5 or MusicXML exports, not read from .gp or .gp5 imports).
- **Track list**: arm, mute, solo, level, pan, automation-lane toggles, FX button (red "!" if a plug-in was switched off), collapsible **groups**, *Show tracks in groups* (right-click empty space or from the Mixer), *Auto-resize track list to fit*, Alt+Up/Down to reorder.

- **Mixer**: instrument, compact or ungrouped views; Master row at the top with **MON** monitoring FX (live only, never rendered); **drag and drop** tracks or whole groups (the track list reorders in step, one undo step per drop, animated); group rows set level, pan and pitch for all tracks; Master row with FX, pan and volume; smooth sliders on one fine scale; **Alt+Up / Alt+Down** moves the selected track or group.

- **Selected-track routing** and MIDI output device; detailed track mixer; test selected track output; MIDI / audio setup.

#### Recording and clips
- **Record** (Ctrl+R) audio or MIDI clips, arm track, input meter, live monitoring, multiple take lanes, loop-recorded takes, recording offset.
- **Live input** (microphone button in the track list toolbar) and **audio inputs**: every input of the device, as channels or stereo pairs.
- **Clip commands** (bindable): delete, deselect, move (coarse and fine), move between lanes, copy, cut, paste, duplicate, mute, properties (F2), **Snap** on/off (Alt+S).

#### Plug-ins and audio engine
- **Built-in effects** (IR Loader, EQ, Compressor, Limiter, Delay, Noise Gate); **VST2 / VST3** instruments and effects, with 32-bit plug-ins through a helper process and optional **CLAP** ([details](#plug-ins)); serial chains, wiring window, MIDI processing (many processors, presets, search), **articulation maps**, **group bus FX**, **master FX** and **Monitor FX** chains, plug-in trust and *Review* prompts, crash isolation with a per-plug-in process option, automatic pitch matching, startup tracks.
- **Audio output**: WASAPI, ASIO, DirectSound (ALSA, PulseAudio, PipeWire or JACK on Linux); Windows MIDI latency (a typed delay in ms); every song plays through the audio engine by default (a Settings toggle).

- **Render window**: range, stems checklist, tail length, file names, WAV / MP3, progress and cancel.

#### Playback and practice
- Play / pause, play from start, stop, loop (F9), first/last bar, previous/next section, go to section.
- Repeats up to x99, D.S./D.C./Coda/Fine, tempo changes (also mid-bar), mix-table fades, dynamics, technique playback; **score follow** with page turn or smooth page turn (stops only on a genuine manual scroll).

#### Settings and help
- Preferences (F12) with search, themes (Dark, Light, System), colours, fonts, interface scale, project settings, import/export of all settings, **Keyboard shortcuts** (F1) with TabForge, Classic and Alternative presets, **Check for updates**, About, Windows file-association option.

#### Menus, panels and windows in detail

**Score / tab editor right-click menu**
- Show standard notation, pitch up / down a semitone, move to the string above / below, delete note, zoom in / out / fit width and zoom percentages, *Score settings…*.
- *Show note length outline*, *Page layout* (page / continuous, vertical / horizontal), *Score settings…* and *Show in piano roll (this track)*. The score page, ledger lines, page turns, playback colours and fonts are in Preferences.

**Fretboard / keyboard right-click menu**
- *Show this track as* (fretboard, keyboard, drum map or match the instrument), *Scale*, *Find scale…*, *Clear scale* and *Fretboard settings…*. Everything else about the look is in the Fretboard settings window.

**Arrangement timeline menus**
- *Selected area*: copy, cut, paste before, move (click the new position), delete, loop, skip during playback, play all skipped areas again, clear selection (Esc).
- *Bar*: copy bar (this track or all tracks), copy section, paste bar into this track or all tracks, paste section here, add bar in front / behind, delete bar (this track or all tracks).
- *Section*: add section at bar, copy, cut, paste, duplicate, delete, loop section, go to section, edit title, lock / unlock position, show section brackets, same colour for similar sections, group colours.
- *Clips*: duplicate, copy, cut, paste, delete, mute, properties, write into the track's notation, advanced notation conversion, add audio file.
- Tracks can be dragged to reorder; panels close or reset from their own menu (*Reset this panel to original position*, *Close panel*).

**Track list menus and controls**
- Empty-area menu: *Show tracks in groups*, *Auto-resize track list to fit*, *Reset column layout*.
- Track and group rows: mute, solo, arm, volume and pan as **knob or slider** (choice in Settings), *Centre pan*, *Set exact pan*, track colour tint, instrument-family group headers with their own controls, right-click a group to open the Mixer at it.

**FX chain window**
- Add / remove plug-ins, *Add FX chain to the end*, *Save FX chain*, *Load FX chain*, *Clear chain*, per-plug-in power (bypass), wet/dry, its own plug-in window (docked in the FX window or floating, always on top optional), *Through chain*, *GM sound* with auto-switch, *Match pitch automatically*, *Auto-load for this instrument*, *Add as a track on startup*, track volume, plug-in folders and audio settings, *Wiring* and *MIDI processing* buttons.
- **Wiring**: audio input channels, MIDI source and channel filter, sidechain 3/4, pass MIDI on, send MIDI output on, instrument output add / replace, MIDI output forwarding to another track.
- **MIDI processing**: search, add processors, reorder (move up / down), remove, enable / bypass, "Applies to" note filter with learn (learn fills the note filter from the notes you play), drum-map folder and reload, presets (save, load, delete), MIDI inputs (picked per track and in the live-input menu), and a live MIDI log. External MIDI clock / MTC sync is not supported: TabForge follows its own tempo.

**Render window**
- Source: master mix, stems (checked or all tracks), or master plus stems; range: entire song, time selection, selected bars, custom seconds; tail (auto until silent); file name wildcards and output folder; WAV 16 / 24 / 32-bit float and MP3 128 / 160 / 192 / 320 kbps; sample rate, mono mix-down, engine rate, threads, real-time pace for streaming samplers, open the folder when done, progress and cancel, warning when a plug-in did not load.

**Preferences (F12), every page**
- *General*: check for updates, open Explorer songs in a new tab or window, autosave, default save format, file association, toolbar and status-bar visibility, confirm before closing unsaved work, warn before discarding settings.
- *Appearance & colours*: theme, UI scale, track colour tint, density, reduce animations and animation speed, accent / selection / hover colours, custom palette, fonts, icon size, score fonts, spacing, ledger and staff opacity, bar numbers, section headings, hover and selection intensity, score paper, ink and line colours, edit cursor colour.
- *Score & notation*: default display (tab + staff, tab only, staff only), follow mode and its options (horizontal / vertical, look-ahead, trigger, manual-scroll pause, frame rate), the playing-note colour (*Playback highlight* in Appearance), and the duration tint and playback line thickness (Appearance > Playback look).
- *Playback & sound*: metronome (click and accent notes, volumes, subdivisions), count-in bars, playback speed, note preview, let-ring limit.
- *Audio & Plug-ins*: driver, device, ASIO channels and inputs, play through the engine, GM auto-switch, pitch matching, startup tracks, Windows MIDI latency (a typed delay), follow Windows volume, recording device and offset, sample rate, buffer size, plug-in folders, common folders, remember the list, plug-in window docking, always on top, per-plug-in process.
- *Editing*: default note value, advance after entering, reverse + / - keys, strict bar length, confirm bar deletion, wheel scroll distance, track volume / pan control style.
- *Timeline & sections*: same colour for similar sections, brackets, glow intensity, bracket thickness, confirm section deletion, animate section dragging, scrollbar.
- *Fretboard & Keyboard*: position, frets, left-handed, note names, default instrument view, whether to show the fretboard at startup and in the piano roll, wood, binding, inlays, string and capo finishes, string vibration, keyboard size and key colours, marker colour and brightness, number size, look-ahead notes, scale highlight, update during playback.
- *Tabs & windows*: window size memory, tab shape, widths, height, title size, close button, double-click / middle-click actions, new-tab position, opening behaviour, drag a tab out or onto another window, mark playing tabs.
- *Hotkeys* (search, presets, conflict warnings) and *Advanced*; settings import / export, reset page, reset all, live preview with Apply / Cancel.

**Side panes**
- *Tools*, *Structure*, *Rhythm* and *Layout* pages hold the tool palette (durations, dynamics, effects, bar structure, rhythm and layout tools); right-click a tool to pin it to the quick strip.
- *Sections*: add, edit, delete, colour, loop and go to a section. *Lyrics*: per-track lyrics editor. Song stats and chord finder are in the **Tools** menu. Zoom and speed are in the View and tempo pane (see above).

**Tool palette pages, every tool**
- *Tools* page: **Edit** (selection cursor, erase note, change accidental); **Duration** (whole to 64th note, dotted, double-dotted, tie, triplet, choose tuplet ratio); **Dynamic** (ppp, pp, p, mp, mf, f, ff, fff); **Beat** (chord, choose chord, text annotation, brush down / up, pick-stroke down / up); **Effects** (vibrato, bend, tremolo bar, slide, dead note, hammer-on / pull-off, ghost note, accent, heavy accent, let ring, natural harmonic, grace note, trill, tremolo picking, palm mute, staccato, tapping, slapping, popping, fade in).
- *Structure* page: **Key and bars** (key signature, triplet feel, free time, double barline); **Step through** (back / forward one beat); **Markers** (add marker, marker list, previous / next marker); **Repeats and directions** (one-bar repeat, two-bar repeat, score directions); **Bar editing** (insert bar before the cursor, add bar at the end, duplicate bar, delete bar, check bar durations); **Bars** (time signature, tempo change, repeat start, repeat end / count, alternate ending).
- *Rhythm* page: **Tuplets and ties** (N-tuplet, tie note, tie beat / chord); **Sounding pitch and duration** (sound duration, 8va, 8vb, 15ma, 15mb).
- *Layout* page: **Voices** (voice 1, voice 2, gray inactive voice); **Beaming** (automatic beaming, force beam group, break primary / secondary beam); **Stems** (automatic and inverted stem direction); **System layout** (force / prevent line break).
- Palette pages can be tabbed or side by side, and any tool can be pinned to the quick strip.

**Lyrics pane**
- *Lyrics*: its own pane beside Sections; type or paste the song's lyrics, they are stored with the song when you leave the box.
- *Fretboard options*: the view and the scale are in the fretboard's right-click menu; everything else is in the Fretboard settings window and Preferences > Fretboard & Keyboard.
- *Tools menu*: **Chord finder…** (root, type, show, insert name on the cursor beat), **Song stats…** (tracks, bars, notes, sections) and **Scale finder…**.
- *MIDI / Audio setup* (Options menu): the selected track's MIDI output device, with a test sound. The **Mixer** window (View menu) holds the track mixer.

**Preferences: Advanced page**
- An informational page only (no runtime toggles): it points to settings import / export, reset page and reset all, which are always available from the Preferences window footer.

</details>

## Verify your download

The downloads are not code-signed, so Windows SmartScreen may warn when you first run
them. Releases built by the GitHub Actions release workflow from the tagged source come with build attestations
(`gh attestation verify <file> --repo cobhc95/TabForge`), so you can verify that a
download matches that build.

Each release attaches the Windows installer (`TabForge-<version>-win-setup.exe`), the portable ZIP (`TabForge-<version>-win-x64-portable.zip`) and the two Linux packages. The installer offers a Start menu entry, an optional desktop icon and an optional *"Open .gp and .tforge files with TabForge"* association, for your Windows account only. Songs double-clicked in Explorer open as a new tab in the running TabForge (or a new window; see Settings > General).

A `licenses/` folder beside `TabForge.exe` (portable zip and installer) holds the full licence texts of every bundled component. SoundTouch.Net (LGPL-2.1) ships as a separate, replaceable `SoundTouch.Net.dll`; its source is linked in [THIRD_PARTY.md](THIRD_PARTY.md).
## Building from source

Requires the .NET 10 SDK (the version in `global.json`).

```cmd
dotnet build src\TabForge.Avalonia\TabForge.Avalonia.csproj -c Release
```

Or publish a self-contained build with `dotnet publish src\TabForge.Avalonia\TabForge.Avalonia.csproj -c Release`.

## Releases and issues

Downloads and release notes: [Releases](https://github.com/cobhc95/TabForge/releases). Bugs and ideas:
[Issues](https://github.com/cobhc95/TabForge/issues) - the song file and the steps to reproduce help a lot.

## Licence

The demo song in the screenshots, *Ashen Meridian*, is original and included (CC0).

MIT — see [LICENSE](../LICENSE). Score file reading/writing uses [alphaTab](https://github.com/CoderLine/alphaTab) (MPL-2.0);
see [THIRD_PARTY.md](THIRD_PARTY.md). Full licence texts ship in the `licenses/` folder (SoundTouch.Net, LGPL-2.1, is a replaceable DLL). TabForge is an independent project and is not affiliated with, sponsored or endorsed by any other software maker.
