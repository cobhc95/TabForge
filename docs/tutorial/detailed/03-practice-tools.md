---
title: Practice tools
id: practice-tools
order: 3
keywords: practice, practise, learn a song, slow down, slow, speed, speed trainer, loop, repeat, loop a section, loop count, metronome, click, count-in, count in, skip, skip section, tempo, bpm
summary: Slow a song down, loop a hard stretch, play along with a click and raise the speed a little on each pass.
est-minutes: 30
---

# Practice tools

A hard bar rarely gets easier by playing the whole song again and again. It gets easier when you play that one bar slowly, in time, over and over. TabForge has a tool for each of those three habits: slow down, loop and click.

In this chapter you use the demo song to learn the main melody of its first chorus. By the end you can slow any song down, repeat any stretch, and bring it back up to speed one step at a time.

## What you will learn

- Slow a song down and speed it up again, and tell speed from tempo.
- Turn on the metronome and the count-in, and change how they sound.
- Loop the whole song, a section or a few bars, and set how many times it repeats.
- Use the speed trainer so each pass is faster than the last.
- Skip parts of a song and jump between sections.
- Right-click the fretboard to preview the next notes on it.

## Before you start

Open **Ashen Meridian** with **File > Open…** and keep the default window layout. The transport buttons (**Play**, **Stop**, **Next section**, **Count-in**, **Metronome** and **Loop**) sit at the left end of the timeline header, so keep the timeline visible. If you moved panels around, choose **View > Reset all panels to default positions**.

## Why practise this way

Three habits make practice work. Play slowly enough that every note is clean. Repeat a short stretch instead of the whole song. Play with a steady click so your timing improves while your fingers learn the notes.

The tools in this chapter do the repeating, the counting and the slowing for you. That leaves your attention free for your hands.

> **Tip:** Slow down until you can play the stretch perfectly, then only raise the speed. Playing fast and sloppy teaches your fingers the mistakes.

## Speed is not tempo

The song has a written tempo, shown in the **BPM** box in the toolbar. Ashen Meridian starts at 150 BPM. That number belongs to the song and is saved with it.

The **Speed** box is different. It is a listening percentage from 25% to 200%, and it only changes how fast the song plays back. The written tempo and the pitch of the notes do not change. At 50% the song plays like 75 BPM, and at 75% it plays like 112 BPM.

The **Speed** box lives in the top toolbar, left of the tempo box, next to the score zoom box.

1. Find the **Speed** box in the top toolbar.
2. Click the arrow in the **Speed** box. A list opens with 50%, 75%, 100%, 125%, 150% and 200%.
3. Choose `75%`. The song now plays at three quarters of its speed.

You can also type a value in the box. Type `75`, `75%` or `0.75`, then press `Enter`. All three mean 75%. From the keyboard, `Ctrl+Alt+Up` and `Ctrl+Alt+Down` raise and lower the speed, and `Ctrl+Alt+0` returns it to 100%.

> **Note:** Typing `90` in the **Speed** box means 90% speed. It never means 90 BPM. To change the written tempo, use the **BPM** box in the toolbar instead.

## Click along with the metronome

The metronome plays a click on every beat while the song plays. The first beat of each bar is louder, so you can hear where each bar starts.

Turn it on with the **Metronome** button, or with **Tools > Metronome**. The command has no default key, but you can give it one in **Preferences > Shortcuts**. A left-click on the button switches the click on and off. The change takes effect at once, even while the song is playing.

A right-click on the button opens **Metronome settings**. Here you can set:

- **Metronome volume**, the overall level of the click.
- **Accent / first beat** and **Regular click**, the level of the first beat and of the other beats.
- **Click sound**: Classic metronome, Woodblock, Side stick or Hand clap.
- **Boosted (layered, much louder)**, which is ticked by default and makes the click cut through a loud song.
- **Subdivision**: Quarter beats, Eighth notes, Eighth-note triplets or Sixteenth notes.

A finer subdivision helps with fast or tricky rhythms, because you hear the notes between the beats. At slow speeds, Eighth notes is a good place to start.

### Try it: Set your own click

*Goal: make the click softer and woodier, with a tick between the beats.*

1. Right-click the **Metronome** button. The **Metronome settings** popup opens.
2. Open the **Click sound** list and choose `Woodblock`.
3. Open the **Subdivision** list and choose `Eighth notes`.
4. Lower **Regular click** a little, so the beats between the accents are quieter.
5. Click outside the popup to close it, then click the **Metronome** button so that it lights up.
6. Press `Space`. You hear a woodblock click twice per beat, with a stronger click on the first beat of each bar.
7. Press `Space` again to pause.

## Count yourself in

A count-in plays clicks before the song starts, so you can get your hands ready. It is very useful when you loop a fast stretch, because you hear the tempo before the first note.

Click the **Count-in** button to switch it on, or choose **Tools > Count-in**. Like the metronome, it has no default key and can be bound in **Preferences > Shortcuts**. A right-click opens **Count-in settings**:

- **Volume** sets the level of the count-in clicks.
- **Length** sets 1, 2, 3 or 4 bars.
- **Click sound** offers Same as metronome, Woodblock, Side stick, Hand clap, Cowbell and Classic metronome.
- **Only when starting from bar 1** limits the count-in to the start of the song.
- **Also before every section while playing** counts in again at each new section.
- **Also before every loop repeat** counts in before each pass of a loop.

> **Note:** If you switch the count-in on or off while the song is playing, the change starts at the next **Play**. The metronome, in contrast, changes at once.

## Loop a stretch

A loop repeats part of the song until you stop it. The **Loop** button decides whether looping is on. What it repeats depends on what you have selected.

- **Nothing selected:** the whole song loops, not the section you are in.
- **A selected area:** only that area loops.
- **A section:** the section loops when you choose it from the section's menu.

To select an area, drag sideways across the bars you want, either on the timeline or in the score. A short, deliberate drag is needed; a tiny wobble counts as a click. The bar under the pointer is shaded as you move across the timeline, so you can see where you are. While the song plays, a line on the timeline marks the current position. If you prefer a bar marker, or both, change **Playback position marker** in **Preferences > Timeline & Tracks**.

Press the **Loop** button, or press `F9`, to switch looping on. Hover the **Loop** button to see the key that works now. The status bar names the bars it will repeat.

Right-click inside a selected area to open its menu. The entry **Loop selection** switches looping on for that area. The other entries copy, cut, paste and delete the bars, so choose carefully.

To loop a whole section, right-click the section on the timeline and choose **Loop section**.

### Loop settings

Right-click the **Loop** button to open **Loop settings**.

- **Loop area** tells you which bars will repeat.
- **Number of loops** sets how many times to repeat. Leave it empty for no end. A badge beside the button counts the loops left.
- **Count-in clicks before every loop** adds a count-in to each pass.
- **Forget the area when loop is turned off** clears the selected bars when you switch the loop off.
- **Loop button loops what is playing (no area needed)** makes the button loop the current section, bar or whole song, as you choose below it.
- **Clear loop area (Esc)** removes the selected area.

> **Note:** Pressing `Esc` clears the selected area but leaves **Loop** on. The whole song then loops again from the start. Switch **Loop** off too if you want the song to play through once.

### Try it: Loop only bars 40 and 41

*Goal: repeat two bars four times, then stop looping.*

1. Click a bar on the timeline, then drag across bars 40 and 41. Both bars are selected on the timeline and in the score.
2. Right-click the **Loop** button. **Loop settings** opens.
3. Type `4` in the **Number of loops** box, then click outside the popup to close it.
4. Press `F9`. The **Loop** button lights up and the status bar reads "Looping bars 40-41".
5. Press `Space`. The two bars play, and the badge beside **Loop** counts down from 4.
6. Wait for the fourth pass to finish. The song stops, or press `Space` to pause sooner.
7. Press `Esc` to clear the selection, then press `F9` to switch looping off.

## Train your speed

The speed trainer raises the speed on every pass of a loop. You start slow, and the tool brings you up to full speed without you touching the **Speed** box.

Right-click the **Loop** button and choose **Speed trainer** instead of **Simple loop**. Then fill in three boxes: **Tempo** from a percentage to a percentage, and **Increase tempo each loop by** a percentage. The defaults are 50% to 100%, in steps of 10%.

> **Note:** The speed trainer adds the step on every pass, up to the second percentage. After that, every pass plays at that speed until you stop.

### Try it: Learn the Chorus 1 melody

*Goal: learn the lead melody of Chorus 1 by looping it, from 60% up to full speed.*

1. Click the **Lead Gtr** track in the track list. The score and the fretboard now show the lead guitar.
2. On the timeline, right-click the **Chorus 1** section (bars 40 to 47) and choose **Loop section**. The **Loop** button lights up.
3. In the top toolbar, type `60` in the **Speed** box and press `Enter`.
4. Click the **Metronome** button, then the **Count-in** button, so that both light up.
5. Right-click the **Loop** button and choose **Speed trainer**. Set **Tempo** from `60` to `100` and **Increase tempo each loop by** `10`. Click outside the popup to close it.
6. Press `Space` and play along. Each pass is a little faster: 60%, 70%, 80%, 90%, then 100%.
7. Press `Space` to pause. Press `F9` to switch **Loop** off.

You know it worked when the chorus repeats after a short count-in, and the playing gets faster each time round.

**Stuck?** If you hear nothing, see **Chapter 14: Troubleshooting and FAQ**.

## Skip parts and jump around

Sometimes you want to play past a part rather than repeat it. Select the bars, right-click inside the selection, open **Arrange** and choose **Skip during playback**. The skipped bars are left out each time the song plays. Choose **Play all skipped areas again** to put them back.

To jump forward by section, click the **Next section** button. Hover it to see its key. To jump to any bar or section, press `Ctrl+G`, type a bar number or part of a section name, and press `Enter`. You can also click a section in the **Sections** panel.

## Fretboard practice aids

The fretboard's right-click menu collects the aids for learning a song.

- **Preview next notes** outlines the next notes on the fretboard before you reach them. The **Preview** slider sets how many, from 1 to 10.
- **Note names** writes the name of each note on the fretboard.
- **Left-handed** flips the fretboard for left-handed players.
- **Scale highlight** shades the notes of a scale on the fretboard. **Chapter 4: Reading tab and notation** shows how to find a scale.
- **Song stats**, **Chord finder** and **Scale finder** are in the **Tools** menu.

Open **Preferences > Fretboard & Keyboard** to change **Look-ahead notes**, or how strong the scale highlight looks with **Scale highlight strength** (10% to 150%). The mixer, which **Chapter 9: Shaping the sound** explains, opens from **View > Mixer / VST**.

## A practice routine that works

Here is a routine for any hard stretch. Use it as it is, or adapt it.

1. Pick a stretch of two to four bars and select it.
2. Set the speed to 60%.
3. Switch the metronome on, and the count-in if you like.
4. Switch **Loop** on and play until you get it right several times in a row.
5. Add 10% to the speed and repeat. Stop raising it when mistakes come back.

Stop for the day when you have played it cleanly at a new speed. Short, regular sessions beat one long one.

## Quick recap

- The **Speed** box is a listening percentage from 25% to 200%. The written tempo in the **BPM** box does not change.
- The **Metronome** and **Count-in** buttons switch on with a left-click and open their settings with a right-click.
- **Loop** with nothing selected repeats the whole song. Select bars or loop a section to repeat less.
- `Esc` clears the selected area but leaves **Loop** on.
- The speed trainer in the **Loop settings** raises the speed on each pass.
- **Skip during playback** leaves out bars, and **Play all skipped areas again** brings them back.

## What next

You can now slow a song down, loop a hard stretch and play with a click. Go on to **Chapter 4: Reading tab and notation**, which explains what the numbers and symbols in the score mean, so you know what you are practising.
