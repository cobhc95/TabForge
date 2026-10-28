# Views/Articulations: the Articulations tab of the MIDI processing window

| Class | Job |
|---|---|
| `ArticulationsTab` | The one hook the window calls for an instrument: wraps the processors and the new tab in a tab control. |
| `ArticulationsPane` (+ `.Files`) | Preset drop-down (rows and Manage presets… from `Views/Presets/PresetRows`), Save as, Import, Export, Delete (instant, with Undo), the lead, the table; every change is one edit of the slot. |
| `ArticulationRow` | One table row (technique, action, value, value 2, mode, Test) and reading it back as a rule. |
| `ArticulationTester` | The Test button: the rule, then a short note, sent live through `IFxChainHost.SendInstrumentMidi`. |
