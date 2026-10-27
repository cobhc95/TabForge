# Platform/Controls: the small reusable controls

These are the controls more than one window shares. Each paints itself from theme keys and named constants; none owns a feature.

| Class | Job |
|---|---|
| `KnobControl` | The small rotary dial: drag around the dial (Shift for fine), wheel and arrow keys step, Ctrl+click or Home returns to `DefaultValue`, double-click/right-click/F2/Enter type an exact value. Pointer, keys, drawing and the type-in flyout are here; the arithmetic is Core `KnobMath` / `KnobValueParser`. |
| `HotkeyTip` | The one hotkey-tooltip mechanism: a control carries `CommandId` and `Text`, a window sets the inherited `Source` to the live hotkey settings, and the tooltip is composed when it opens, so a shortcut changed in Settings shows at once. |
| `TextLinkButton` | The look of a text link button: transparent at rest, the soft hover shade under the pointer, rounded corner. The pointer shape never changes on hover. |
| `FlatToolButton` | The flat tool button of a header strip: transparent at rest, the hover shade and an accent outline under the pointer. Used by the compact strip's caption and close X. |
| `RadioCard`, `RadioCardGroup` | The choice card of a prompt (Copy audio with this song, Audio crosses this section, Clips overlap): a radio circle, title, hint and optional preview in one bordered card. The whole card selects; hover (soft fill, stronger border), pressed, selected (accent border and tint) and keyboard-focus states; arrows move between the cards of the group, Space selects. |
| `TrackPillButton` | One track's capsule in a header strip: a dot in the track's colour and its name, with the Core "TrackPill" look and its idle style. |
| `CompactTrackStrip`, `HeaderModeCaption`, `BandViewMenu`, `ToolbarMenuRows` | The compact/Band header strip, the "mode entered last" caption, the Band view's layout menu, and the rows that stand for the menu-row toggles inside another menu. |
| `SettingsNavigationGlyphs`, `SettingsNavigationIconView` | The Settings rail glyphs (24 × 24 grid, one line weight) and the square control that paints one in the rail's colour. |

Rules: colours are theme keys, sizes and timings are named constants, the pointer shape never changes, and nothing here does per-frame layout.

Tests: `CompactTrackStripTests`, `CompactBandHeaderTests`, `KnobControlTests`, `HotkeyTipTests`, `PreferencesWindowTests`,
`StatusBarViewTests`, `PreferencesChangesLinkTests`, `NoHoverCursorTests`, `MediaCollectPromptTests` (RadioCard). Run the Avalonia test project locally with
`--filter "FullyQualifiedName~CompactTrackStripTests"`.
