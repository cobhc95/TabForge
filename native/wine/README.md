# Bundled Wine

The Linux package ships a pinned Wine 9.21 `amd64-wow64` tree. See `BUILD_PROVENANCE.md` for the exact
tarball, SHA-256, upstream Wine commit, licence, source links, written source offer, layout and the
third-party-prebuilt statement. `COPYING.LIB` is Wine's LGPL 2.1 text. The packager removes only headers,
development files and debug symbols; no Wine source is patched. No wine-mono or wine-gecko is shipped.
