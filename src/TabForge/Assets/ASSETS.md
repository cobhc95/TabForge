# TabForge assets: provenance

All artwork in this folder is original TabForge artwork under the project's MIT licence (see `LICENSE.txt`), except the
Bravura-derived glyph outlines listed below, which stay under the SIL Open Font License 1.1.

| Group | Path | Made by / how |
|---|---|---|
| App icon | `TabForge.ico` | Original TabForge artwork (a fretboard with three position dots on a rounded square), drawn by the TabForge contributors. |
| Instrument badges (141) | `Icons/Instruments/SVG/**`, `manifest.json` | Original flat SVG artwork (512x512, one colour circle per family) made for TabForge by the TabForge contributors. |
| Tool-palette icons (97) | `Icons/Tools/**` | Hand-authored 32x32 `currentColor` SVG by the TabForge contributors. The letters (`TXT`, `P.M.`, `L.R.`, `HO`, `P`, `S`, `T`) are hand-drawn strokes. The `More/*` badges (`8va`, `15ma`, ...) contain plain `<text>` that the app renders with the Windows system font at run time; no font is embedded or shipped. |
| Transport icons (11) | `Icons/Transport/*.svg` | Hand-authored 96x96 SVG by the TabForge contributors. |

## Bravura-derived glyphs (SIL OFL 1.1)

The following tool icons contain glyph outlines converted to SVG paths from the **Bravura** music font
(SMuFL reference font, from the alphaTab package):

- `Icons/Tools/Dynamic/{p,pp,ppp,f,ff,fff,mp,mf}.svg` (dynamics)
- `Icons/Tools/Composition/time_signature.svg`, `Edit/voice_1.svg`, `Edit/voice_2.svg`, `Effects/slides.svg` (numerals)
- `Icons/Tools/Effects/trill.svg` (the "tr" ornament; the wavy line under it is hand-drawn)

Bravura: Copyright (c) 2020, Steinberg Media Technologies GmbH (http://www.steinberg.net/), with Reserved Font Name "Bravura".
Licensed under the SIL Open Font License, Version 1.1 (http://scripts.sil.org/OFL). The outlines are used as vector artwork
only; no font file is distributed and the icons are not named "Bravura".

## Not used

No Guitar Pro (or other third-party application) images, icons, fonts or sounds are used or copied anywhere in TabForge.
The toolbar's functional layout and command names follow common notation-editor conventions; the artwork is not derived
from any of them.
