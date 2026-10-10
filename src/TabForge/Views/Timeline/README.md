# Views/Timeline

Helpers of the arrangement timeline (`TrackTimeline`, `ArrangementPanel` in `Views/`) and of the track list: section drag and edge scroll, section colours, track-control columns and widgets, track-row menus and hover, and the track properties and colour dialogs. Does not own the song or the edit transaction (`DocumentEdits.Run`). Files keep the namespace `TabForge.Views`; the folder is for finding them only.

## Files
| File | Purpose |
| --- | --- |
| `SectionAutoScrollController.cs` | Scrolls the timeline sideways while a section is dragged near either edge of the view |
| `SectionColorBrushConverter.cs` | Sidebar section-list colours: the timeline hue as a translucent tint, with a dim option for inactive rows |
| `SectionColours.cs` | The colour a section is shown in, shared by the timeline and the sidebar section list |
| `SectionDragOverlay.cs` | Separate light visual for section drags, so pointer movement never redraws the timeline grid |
| `SectionEdgeController.cs` | Section edge resize state and the Ctrl+drag marker-only move state |
| `SectionInsertionIndicator.cs` | The destination caret shown while a section is dragged |
| `SectionTipController.cs` | The section lane's hover hint (ToolTip, open delay, hint text) |
| `TrackColoursDialog.cs` | Colour tracks by hand: pick tracks or a range, then a colour |
| `TrackColumnLayout.cs` | Track-control columns: order, widths, hidden columns, header strip, per-row cells |
| `TrackControlWidgets.cs` | Stateless track-control widgets shared by the track list and the Mixer (colour palette, volume and pan) |
| `TrackGridDragController.cs` | Drag of a row of the practice panel's track grid to reorder tracks |
| `TrackOutputWindow.cs` | The track output window's layout (Tools and Sound windows) |
| `TrackPropertiesDialog.cs` | Track properties and Add track dialog for a MIDI track: details, instrument, drum notation, mixer, tuning |
| `TrackPropertiesDialog.Audio.cs` | The audio-track part of the same dialog: input, mix and waveform picture (partial of `TrackPropertiesDialog`) |
| `TrackPropertiesWindow.cs` | Track properties window: instrument picture, identity, MIDI sound, mixer knobs and a visual tuning editor with presets |
| `TrackRowHover.cs` | The hover shade of one track-list row |
| `TrackRowMenus.cs` | Contents of the track row's right-click menu (plain data, one menu for every track kind) |
| `TrackRowWidgets.cs` | Per-row mix widget behaviour: pan text and menu, mix-edit gestures on sliders, the instrument icon button |
| `TrackSilhouette.cs` | Track-row instrument icons (the owner's line art from `Assets/Icons/TrackRows/owner-icons.json`) |

Hosts: `ITrackColumnHost`, `ISectionEdgeHost`, `ISectionTipHost`, `ITrackRowWidgetHost` (declared in the files above). `TrackTimeline` and `ArrangementPanel` partials stay in `Views/` beside their main files.
