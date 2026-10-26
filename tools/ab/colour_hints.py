"""Owns: free colour hints for an A/B pair: main colours present in one image with no near colour in the other.
Does not own: the pixel comparison (compare.py) or the renders (run.ps1).
Tests: run by hand. Usage: colour_hints.py <wpf.png> <avalonia.png>
       colour_hints.py --dir <run folder> --out <json> [--wpf <WPF folder>]  (WPF folder defaults to TF_AB_WPF)
Uses PIL only; run with python -I."""
import argparse
import json
import os
import sys

from PIL import Image

MAIN_SHARE = 0.004
NEAR = 14
QUANTUM = 8
MAX_LINES = 6
MIN_OTHER_PIXELS = 3


def colour_counts(path):
    """Exact RGB colour to pixel count, and the pixel total."""
    image = Image.open(path).convert("RGB")
    return dict((rgb, n) for n, rgb in image.getcolors(image.width * image.height)), image.width * image.height


def main_colours(counts, total):
    """Quantized colour (centre of its bucket) to share of pixels, for buckets over MAIN_SHARE."""
    buckets = {}
    for (r, g, b), n in counts.items():
        key = (r // QUANTUM * QUANTUM + QUANTUM // 2, g // QUANTUM * QUANTUM + QUANTUM // 2, b // QUANTUM * QUANTUM + QUANTUM // 2)
        buckets[key] = buckets.get(key, 0) + n
    return {k: n / total for k, n in buckets.items() if n / total > MAIN_SHARE}


def has_near(colour, other_counts):
    return any(n >= MIN_OTHER_PIXELS and max(abs(colour[0] - o[0]), abs(colour[1] - o[1]), abs(colour[2] - o[2])) <= NEAR
               for o, n in other_counts.items())


def hints(wpf_path, ava_path):
    """Up to MAX_LINES lines, biggest share first, or 'no colour differences'."""
    wpf, wpf_total = colour_counts(wpf_path)
    ava, ava_total = colour_counts(ava_path)
    found = []
    for colour, share in main_colours(wpf, wpf_total).items():
        if not has_near(colour, ava):
            found.append((share, f"WPF has #{colour[0]:02X}{colour[1]:02X}{colour[2]:02X} ({share * 100:.1f}%) not in Avalonia"))
    for colour, share in main_colours(ava, ava_total).items():
        if not has_near(colour, wpf):
            found.append((share, f"Avalonia has #{colour[0]:02X}{colour[1]:02X}{colour[2]:02X} ({share * 100:.1f}%) not in WPF"))
    found.sort(reverse=True)
    return "\n".join(line for _, line in found[:MAX_LINES]) or "no colour differences"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pair", nargs="*")
    parser.add_argument("--dir")
    parser.add_argument("--out")
    parser.add_argument("--wpf", default=os.environ.get("TF_AB_WPF"))
    args = parser.parse_args()
    if args.dir:
        if not args.out or not args.wpf or not os.path.isdir(args.wpf):
            sys.exit("--dir needs --out and a WPF folder (--wpf or TF_AB_WPF).")
        ava_dir = os.path.join(args.dir, "avalonia")
        result = {}
        for name in sorted(f[:-4] for f in os.listdir(ava_dir) if f.lower().endswith(".png")):
            wpf_path = os.path.join(args.wpf, name + ".png")
            if os.path.exists(wpf_path):
                result[name] = hints(wpf_path, os.path.join(ava_dir, name + ".png"))
        with open(args.out, "w", encoding="utf-8") as handle:
            json.dump(result, handle, indent=1)
        print(f"{len(result)} windows -> {args.out}")
    elif len(args.pair) == 2:
        print(hints(args.pair[0], args.pair[1]))
    else:
        sys.exit("Pass <wpf.png> <avalonia.png> or --dir <run folder> --out <json>.")


if __name__ == "__main__":
    main()
