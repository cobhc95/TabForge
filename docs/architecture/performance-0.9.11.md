# TabForge 0.9.11 performance audit

## Baseline on Windows

The installed 0.9.10 build had Ashen Meridian loaded. The desktop owner stopped playback for the idle capture. The sample includes the main process and its audio helper.

| Metric | Installed TabForge 0.9.10 | GP5 from the supplied screenshots |
| --- | ---: | ---: |
| Idle CPU | 1.31% total over a short sample, 1.29% UI and 0.02% audio helper | 0.3–0.6% across the screenshots |
| Idle GPU 3D | 43.7% in the CPU sample and 49.8% in a separate ten-sample capture | 0% |
| Private working set | 297 MiB in the ten-sample capture, 272 MiB UI and 25 MiB audio helper | 64 MB |
| GPU process memory | 154 MiB dedicated and 16 MiB shared in a separate check | |

The memory target is roughly 128 MB in Task Manager. The CPU limit should be twice GP5's average in a matched workload. The screenshots give a provisional range of 0.6–1.2%. GPU memory is tracked separately from process RAM.

## Findings and changes

| Area | Evidence | 0.9.11 change and expected impact |
| --- | --- | --- |
| Idle fretboard | The stopped UI trace shows Avalonia re-rendering the fretboard after scheduled vibration frames. The panel used the presence of a preview note to decide whether to request another frame, even while the song clock was stationary. | The existing playback frame drives the vibration only during playback. Paused and stopped previews stay visible and stationary. A manually auditioned note still animates until its ring ends. This removes the persistent redraw source seen in the trace. |
| Timeline rendering | During playback, the arrangement repeatedly checked timing by walking the song's measures and cells. In a 12-second sample the trace attributed about 0.8 seconds on the UI thread to that path. The compositor also spent time replaying score, timeline and fretboard drawings. | The timeline reuses measured timing until the song layout or content changes. Repeat passes, clip folds and pass labels use the same timing. The rendering backend and playback refresh cadence are preserved. |
| Opening and preparing playback | Timing and transport queries compiled MIDI note and controller streams that they immediately discarded. MIDI clips also requested a full second compile to locate the start of the selected pass. | These paths run the same repeat, tempo and fermata calculation without emitting MIDI events. This reduces startup and playback preparation allocations. The full playback and MIDI export paths still build events. |
| Graphics memory | Fretboard textures stayed strongly referenced after a look was replaced. The 41 shipped textures would occupy about 72 MiB if all were decoded as 32-bit pixels. One wood texture can account for about 4 MiB. | The cache shares live textures and lets the GC reclaim them once no pane uses them. A detached pane or one switched from fretboard to keyboard or drums also drops its board bitmap. Revisiting a reclaimed look decodes it again. |
| Audio helper | At idle the helper still serviced the configured audio device at about 100 callbacks per second. The stopped sample attributed about 0.02% system CPU and 25 MiB private working set to it. Plug-in DSP, capture, and audio monitoring have their own cost when active. | Chains without effects skip at least 56 KiB of effects MIDI scratch plus block-size-dependent audio buffers per chain. The MIDI log obtains its roughly 16 KiB ring on first use. These allocations stay off audio callbacks. Device buffering, routing, plug-in tails and monitoring use their existing paths. |
| Other retained memory and timers | The code review covered score drawing caches, audio waveforms, undo, device callbacks, meters, hidden panes and background-tab plug-in chains. Undo is bounded per document and audio chains already have an expiry for background tabs. The paused scheduler and meter tick still wake periodically. | These remain areas to measure after the redraw fix. No package, native binary or asset was added. Installer and download sizes were not measured. |

## Measurements on the 0.9.11 code

A standalone timing probe imported the Ashen Meridian sample and compared the two timing paths in the same Release build. All 154 performed bars matched. The full compiler produced 24,355 MIDI events the timing consumer would have discarded.

| Timing calculation | Mean over 40 runs | Managed allocation per run |
| --- | ---: | ---: |
| Full MIDI compile | 39.94 ms | 23.98 MiB |
| Bar timing only | 0.47 ms | 0.22 MiB |

These are component measurements. The desktop CPU, GPU, startup and RAM totals for 0.9.11 need a new capture.

## Desktop verification

1. Rebuild 0.9.11 and open the same local copy of `samples/Tests/TabForge Demo - Ashen Meridian.gp5` in both apps. Use comparable window sizes and display scaling. Sample each app for 30 seconds with playback stopped, again paused on a sustained note, and then while playing the same passage with comparable instrument sounds.
2. Record average CPU, GPU 3D utilization and Task Manager memory for the whole TabForge group and GP5. Check the 2× CPU and RAM targets in each state. Record a blank window and a large score separately to distinguish runtime and graphics overhead from song data.
3. Confirm that stopped and paused preview notes stay visible without moving. Check playback and direct note auditions, Band view, seeking across repeats, audio clips, live monitoring and a plug-in with an audible tail.
4. Open a disposable copy of a modern `.gp`, edit it, press Ctrl+S, and reopen it. Confirm the edit persisted and Ctrl+Shift+S opens the chooser. A new song or an import requiring conversion still needs a destination.
