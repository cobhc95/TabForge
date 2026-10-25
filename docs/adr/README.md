# Architecture decision records

Short records of standing decisions: what was decided, why, and what it costs. Each file has the same parts: status, context, decision, consequences. A decision that is replaced gets a new record; the old one is marked superseded, not edited away.

| Record | Decision |
| --- | --- |
| [0001](0001-audio-engine-separate-process.md) | The audio engine runs in its own process |
| [0002](0002-import-worker-process.md) | .gp import runs in a limited worker process |
| [0003](0003-source-built-alphatab.md) | alphaTab is built from source with minimal patches |
| [0004](0004-gp-plus-tfaudio-sidecar.md) | A clean .gp file plus a .tfaudio sidecar, or an embedded project |
| [0005](0005-self-tests-in-the-app.md) | The self-tests ship inside the application |
| [0006](0006-per-document-media-context.md) | Each open document has its own media context |
| [0007](0007-optional-plugin-isolation.md) | Plug-in process isolation is optional and is not a sandbox |

To add a record, copy any file here and keep it to one page.
