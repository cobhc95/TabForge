# Bundled Wine: pinned version and provenance

TabForge's Linux package bundles one pinned Wine tree. Nothing is downloaded on first run.

## Pin

| Item | Value |
| --- | --- |
| Wine version | 9.21 |
| Build name | `wine-9.21-amd64-wow64` |
| Upstream release | <https://github.com/Kron4ek/Wine-Builds/releases/tag/9.21> |
| Tarball | <https://github.com/Kron4ek/Wine-Builds/releases/download/9.21/wine-9.21-amd64-wow64.tar.xz> |
| Tarball SHA-256 | `020f8886750ff658d92ea39e696d2a57d3e8f056f00d8dfe45b65cddee5d6fa5` |
| Tarball size | 59,876,724 bytes |
| Wine source tag | `wine-9.21`, commit `c9a8333c0f274201faf33d27abda1ebb5ae07cec` |
| Wine source | <https://github.com/wine-mirror/wine/tree/wine-9.21> |
| Source tarball | <https://dl.winehq.org/wine/source/9.x/wine-9.21.tar.xz> |
| Source tarball SHA-256 | `4442b47ffd9b2ea457100e36ed5fd4e6f4d829d9db79a25e605175a988ca2fff` |
| Build scripts | <https://github.com/Kron4ek/Wine-Builds> (MIT; not shipped) |
| Licence | GNU LGPL 2.1 or later; text in `COPYING.LIB` beside this file |

**This is a third-party prebuilt, not built by TabForge.** The release packages use Kron4ek's
vanilla `amd64-wow64` build (not the Staging branch), which supports 32-bit Windows plug-ins in a 64-bit
prefix without 32-bit host libraries. A release build must either keep this exact SHA-256 and notice or
use TabForge's own Wine build with the same provenance fields.

**Packaging is not byte-for-byte the tarball.** `Source/tools/package-linux.sh` copies the tree, removes
`include/` and `share/man/`, deletes development files (`.a`, `.la`, `.h`, `.hpp`) and runs
`strip --strip-debug` on the ELF files. No Wine source is changed and no patch is applied. The removed
files are development material, not needed by the runtime; the source tarball above plus that packaging
step reproduces the shipped binaries' behaviour.

**Written offer.** For at least three years from the date of each TabForge release that includes this
tree, we will give any third party, on request, a complete machine-readable copy of the corresponding
Wine source (tag `wine-9.21`, commit above, plus the packaging step above), for a charge no more than
the cost of physically performing the distribution. Ask through
<https://github.com/cobhc95/TabForge/issues>.

The tree contains no `wine-mono` and no `wine-gecko`; the package script refuses a tree that contains
either. TabForge sets `WINEDLLOVERRIDES=mscoree,mshtml=` so Wine never offers to install them.

## Package layout

`Source/tools/package-linux.sh --wine <folder>` copies the tree to `<app>/wine` and ships:

```
wine/bin/wine
wine/bin/wineserver
wine/lib/wine/{i386-unix,i386-windows,x86_64-unix,x86_64-windows}/...
wine/share/wine/...
ThirdParty/wine/BUILD_PROVENANCE.md   (this file)
ThirdParty/wine/COPYING.LIB            (LGPL 2.1 text)
ThirdParty/wine/LICENSE                (Wine copyright notice)
ThirdParty/wine/AUTHORS                (Wine contributor list)
ThirdParty/wine/README.txt             (version, source link, SHA-256, written offer, replacement offer)
```

The packaged engine finds the loader at `<application folder>/wine/bin/wine`; `TABFORGE_WINE` can point
at another loader for tests. `WineRuntime` sets `WINELOADER`, `WINESERVER`, `WINEDLLPATH`, a fixed
`PATH` and a private `WINEPREFIX` from the loader that was actually found, so an AppImage mount and a
moved extracted folder both work.
