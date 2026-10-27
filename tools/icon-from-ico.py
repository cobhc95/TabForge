#!/usr/bin/env python3
"""Cuts the PNG frames out of TabForge.ico (every frame is PNG) and writes a PNG and an ICNS from them. Standard library only.

usage: icon-from-ico.py <TabForge.ico> --png <out.png> [--icns <out.icns>]
The PNG is the largest frame (256 px). The ICNS holds the 16, 32, 64, 128 and 256 px frames that exist.
"""
import argparse
import struct

ICNS_TYPES = {16: b"icp4", 32: b"icp5", 64: b"icp6", 128: b"ic07", 256: b"ic08"}


def frames(path):
    data = open(path, "rb").read()
    _, kind, count = struct.unpack("<HHH", data[:6])
    if kind != 1:
        raise SystemExit("not an .ico file")
    found = {}
    for i in range(count):
        w, _h, _c, _r, _planes, _bpp, size, offset = struct.unpack("<BBBBHHII", data[6 + 16 * i:22 + 16 * i])
        blob = data[offset:offset + size]
        if blob[:4] == b"\x89PNG":
            found[w or 256] = blob
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ico")
    ap.add_argument("--png", required=True)
    ap.add_argument("--icns")
    args = ap.parse_args()
    found = frames(args.ico)
    if not found:
        raise SystemExit("no PNG frames in the .ico")
    open(args.png, "wb").write(found[max(found)])
    if args.icns:
        body = b"".join(ICNS_TYPES[w] + struct.pack(">I", 8 + len(b)) + b for w, b in sorted(found.items()) if w in ICNS_TYPES)
        open(args.icns, "wb").write(b"icns" + struct.pack(">I", 8 + len(body)) + body)


main()
