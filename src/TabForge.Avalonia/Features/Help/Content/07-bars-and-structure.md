# Bars and structure

A song is a row of bars, and each bar holds the music for every track. This chapter shows you how to add, copy and remove bars, how to set the time signature and key, how to mark repeats, how to name the parts of a song with sections, how to add lyrics, and how to change the tempo.

The tools for almost all of this live in the **Structure** panel. You can also reach most of them from the **Bar** and **Insert** menus. Use whichever one you find easier, because the result is the same.

Many changes in this chapter apply to every track at once. That is the most important thing to remember. When you add a bar, every track gets an empty bar in the same place.

## Open the Structure panel

The **Structure** panel is one of the tool pages in the side panel. The others are **Tools**, **Rhythm** and **Layout**.

1. Look at the side panel and find the **Structure** tab.
2. If you cannot see it, choose **View > Panels** and tick **Structure**. The panel comes back.
3. Hover a button to read its name and its key, if it has one.

If the side panel is hidden, press the toolbar button that shows and hides the side panel, or choose **View > Panels**.

Tip: The **Structure** panel has these groups: **Bar editing**, **Step through**, **Bars**, **Key and bars**, **Repeats and directions** and **Markers**. Each group is explained below.

## Bar editing

These buttons change the number of bars or what a bar holds. They act on the bar under the edit cursor.

- **Insert bar before the cursor** adds an empty bar in front of the cursor bar. Use it when you need room in the middle of a song. Press `Ctrl+Insert`.
- **Add bar at the end** adds an empty bar at the end of the song. Use it to keep writing. It is also under **Insert > Bar > At end**.
- **Duplicate bar** copies the cursor bar into the bar after it. Use it for a riff that repeats. The copy takes the section name off, so the label does not appear twice. With bars selected, it copies the whole selection after its last bar and selects the copy.
- **Delete bar** removes the bar under the cursor from every track. Press `Ctrl+Delete`. The last bar of the song cannot be deleted.
- **Check bar durations** lists every bar that does not add up to its time signature. Press `F4`. Use it whenever a bar is red and you cannot see why.

Insert several bars at once with **Insert > Bar > Several bars…**. The window asks how many bars to add, and whether to add them before or after the selected bars, or at the end of the song. The whole change is one undo step.

Clear a bar with **Bar > Clear bar**. It removes every note from the bar, but the bar stays in the song. Use it when you want to start a bar again without losing its place.

Note: **Insert > Bar > Before selected** adds an empty bar before the selected bars, the same as **Insert bar before the cursor**. **After selected** adds one after them.

## Step through

The two **Step through** buttons move the cursor one beat at a time.

- **Step back one beat** moves the cursor one beat to the left.
- **Step forward one beat** moves the cursor one beat to the right.

Use them when you want to move slowly through a bar and check each beat. They have no default keys, so set your own key in **Options > Preferences > Shortcuts** if you want one.

## Time signature and key

The time signature tells you how many beats a bar holds, such as 4/4, which holds four quarter-note beats, or 3/4, which holds three. The key signature tells the score which sharps or flats to show, so the notes read correctly.

- **Time signature** changes the time signature from the cursor bar onward. Press `Ctrl+Shift+T`. Use it when a song changes meter, for example from 4/4 to 3/4.
- **Key signature** changes the key from the cursor bar onward. Press `Ctrl+K`. Use it when the song moves to a new key. A minor key shares its signature with a major key that has the same notes.

Both changes apply from the bar where you set them, on every track, until the next change. If you want a change to cover the whole song, set it on bar 1.

Tip: The **Project settings** button in the toolbar holds the title, tempo, time signature and key in one window. Use it if you want to see everything together.

Note: Because the change runs until the next change, check the bars after a later change. A change in bar 9 affects bars 9 onward, and bars 1 to 8 keep their old signature.

## Clef and triplet feel

Two more tools change how a bar reads.

- **Clef** (`K`) changes the clef of the track. Use it when a track needs a different clef to read well on the staff.
- **Triplet feel** toggles a triplet feel for the bar. Use it for a swung or shuffled passage, where the beats feel like triplets.

Tip: The clef and triplet feel are for the bar or track you choose. Check the notes after you change them, because the notes stay the same and only the reading changes.

## Repeats and directions

Repeats mark a passage that the player plays again. Directions tell the player where to jump. These tools are in the **Bars**, **Key and bars** and **Repeats and directions** groups.

- **Repeat start** puts a repeat sign at the start of the passage. Press `[`. Put the cursor on the first bar of the passage and press it.
- **Repeat end / count** puts a repeat sign at the end of the passage. Press `]`. Put the cursor on the last bar of the passage and press it.
- **Directions** opens the window for repeat, D.C. and D.S. directions. Press `D`. Its menu entry is **Insert > Repeat / ending > Directions / ending…**. Use it to add a jump sign such as D.C. or a coda.
- **Double barline** draws a double line at the end of the bar. Toggle it on and off. Use it at the end of a section.
- **One-bar repeat** marks the bar as a repeat of the bar before it. Use it for a bar that is the same as the one before.
- **Two-bar repeat** marks the bar as a repeat of the two bars before it. Use it for a pattern of two bars that comes back.

Tip: To make a passage repeat, add **Repeat start** on the first bar and **Repeat end / count** on the last bar. Then play the song to hear the repeat.

Note: The repeat signs and directions are marks in the score and in the playback. Check the bar numbers after you add them, so the repeat starts and ends where you expect.

## Bars and beats

Two more tools change how a bar is counted.

- **Triplet feel** (in the **Key and bars** group) toggles the triplet feel of the bar.
- **Free time** marks the bars as free time, which is music without a steady beat. Use it when a passage is not played in strict time.

## Markers and sections

A section is a named stretch of bars with a job in the song, such as Intro, Verse, Chorus or Solo. A marker is the same kind of thing: a label placed on a bar. Sections show in the section lane above the timeline and in the **Sections** panel.

To add a section:

1. Click the bar where the section starts, so the cursor is on it.
2. Press `M`. Or click **Add** under the list in the **Sections** panel.
3. Type a name in the window, such as `Verse`, and choose a colour.
4. Click **Add**.

Sections that share a base name, such as **Verse 1** and **Verse 2**, share a colour. This makes the song's structure easy to read at a glance. A song can have only one section that starts on each bar.

To move between sections:

- Click a name in the **Sections** panel to jump the cursor to that section.
- Click **Go** next to a section to jump to it.
- Press `Alt+Shift+Left` for the previous section, and `Alt+Shift+Right` for the next section.

To change a section's name or colour, click **Edit** next to it in the **Sections** panel. This reopens the name and colour window.

Lock a section to stop it from moving by accident. Right-click the section and tick **Lock section position**. A padlock then marks it. To unlock it, untick the same box.

Drag a section block on the timeline to move it with its bars. Hold `Ctrl` and drag to move only its marker, and the bars stay where they are.

Note: The timeline section lane shows when **View > Arrangement overview** is on. Press `F8` to show or hide the arrangement overview.

## Markers in the palette

The **Markers** group of the **Structure** panel has four tools for markers.

- **Add marker** adds a marker at the cursor bar.
- **Marker list** opens the list of every marker in the song, so you can see them all.
- **Previous marker** moves the cursor to the marker before the cursor.
- **Next marker** moves the cursor to the marker after the cursor.

Use the marker list when a song has many sections and you want to find one quickly.

## Lyrics

Lyrics are the words of the song. They are saved with the song, so they stay when you save and open again.

1. Choose **Insert > Lyrics…**. This opens the **Lyrics** pane and puts the cursor in it.
2. Type the words in the text box.
3. Click outside the box, or leave it. This commits the text, and one undo step covers it.
4. To place the words bar by bar, click **Edit by bar**.

The **Edit by bar** window has one row for each bar. Each row shows the bar number, its section name and a text box for that bar's syllables. Press `Enter` in a row to save it and move to the next bar. The bar under the playhead is highlighted while the song plays.

Note: Lyrics are in their own pane, not in the score. If you do not see them, open the **Lyrics** pane from **Insert > Lyrics…**.

## Tempo changes

The tempo is how fast the song is written to play, in beats per minute. You can change it for the whole song, or from one bar onward.

- **The BPM box** is in the top toolbar. Click it, type a value from 20 to 400, and press `Enter`. This changes the tempo of the whole song.
- **Tempo change** is a tool in the **Bars** group of the **Structure** panel. It changes the tempo from the cursor bar onward. Use it when a song speeds up or slows down partway through.
- **Insert > Tempo change…** does the same thing from the menu.

The **Speed** box is different. It changes only how fast the song plays back, and it does not change the written tempo. The rhythm and layout chapter explains the speed box.

Tip: Set the tempo on bar 1 first, then add tempo changes later in the song.

## Common questions

Question: Why did my time signature change in bars I did not touch?
Answer: A time signature change applies from its bar until the next change. Look for a later change in the song. Each one affects the bars after it.

Question: What is the difference between a section and a marker?
Answer: A section is a named stretch of bars with a colour. A marker is the same kind of label placed on a bar, and the **Markers** group gives you tools to find and move between them.

Question: How do I add a repeat?
Answer: Put the cursor on the first bar of the passage and press `[`. Then move to the last bar and press `]`.

Question: Can I change the tempo for just part of the song?
Answer: Yes. Use the **Tempo change** tool from the cursor bar onward. The BPM box changes the tempo of the whole song.

Question: Where do the lyrics go?
Answer: They go in the **Lyrics** pane, which opens from **Insert > Lyrics…**. Type the words there, or use **Edit by bar** to place them bar by bar.

## Troubleshooting

Problem: A bar turned red after I added a bar or changed the signature.
Fix: The bar does not add up to its time signature. Press `F4` to list the bars that do not add up. Then add, remove or change notes until the red goes away.

Problem: I cannot delete the last bar of the song.
Fix: The last bar cannot be deleted. Clear it with **Bar > Clear bar** instead, or add a new bar at the end and delete the earlier one.

Problem: The section does not move when I drag it on the timeline.
Fix: The section may be locked. Right-click it and untick **Lock section position**. To move it with its bars, drag it without holding `Ctrl`.

Problem: My section moved and some bars lost their name.
Fix: Dragging a section's edge changes how many bars it covers, and a shorter section leaves bars outside it. Undo with `Ctrl+Z`, then drag the edge back. To name bars that are left without a section, add a new section on the first of them with `M`.

Problem: My lyrics do not show in the score.
Fix: Lyrics are in the **Lyrics** pane, not in the score. Open the pane from **Insert > Lyrics…**, and check that the text box holds your words.

Problem: The time signature or key changed in bars I did not expect.
Fix: A change applies from its bar until the next change. Move the cursor to the bar where the change starts and check it. Undo with `Ctrl+Z` if you set it in the wrong place.

Problem: The repeat sign is not where I expected it.
Fix: Repeat start belongs on the first bar of the passage, and repeat end on the last bar. Move the cursor to the correct bar and press `[` or `]` again.

Problem: The tempo box accepted a number, but the song still plays at the old speed.
Fix: The Speed box is a playback speed, not the tempo. Check that the Speed box is at 100 %. Set the tempo in the BPM box instead.

Problem: I pressed `M` and no section window opened.
Fix: The cursor may be in a text box, such as the lyrics box, so `M` typed a letter. Click the score first, then press `M` again.

Problem: I cannot find the Structure panel.
Fix: Choose **View > Panels** and tick **Structure**. Or press the button that shows the side panel.

Related: Writing notes, Effects and expression, Rhythm and layout.
