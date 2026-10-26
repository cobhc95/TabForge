"""Owns: the A/B pixel comparison of the WPF reference shots with the Avalonia renders: per-shot diff percentage after a blur,
the worst of nine region mean deltas, report.csv and report.md (worst first), and one pair image per compared shot.
Does not own: the renders (tests/TabForge.Avalonia.Tests ParityShotsTests), the shot list (tests/parity/parity.capture.json),
the reason a shot has no twin (ParityShotMap, read here from missing.txt).
Tests: run by hand through tools/ab/run.ps1 (see tools/ab/README.md). Uses PIL only; run with python -I.
"""
import argparse
import csv
import datetime
import os
import sys
import tempfile

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps, ImageStat

ROWS = ["top", "middle", "bottom"]
COLS = ["left", "centre", "right"]
PANEL_WIDTH = 720
PANEL_MAX_HEIGHT = 800
LABEL_BAND = 34
GAP = 12


def load_missing(path):
    """Shot name to reason, from missing.txt (one 'shot<TAB>reason' per line)."""
    reasons = {}
    if not os.path.exists(path):
        return reasons
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            shot, _, reason = line.rstrip("\n").partition("\t")
            if shot:
                reasons[shot] = reason
    return reasons


def blurred(image, radius):
    """The tolerance: a blur that forgives a difference of a few pixels in edge position."""
    return image.filter(ImageFilter.GaussianBlur(radius))


def difference_parts(wpf, ava, blur, pixel):
    """Returns the per-channel absolute difference of the blurred images and a 0/255 mask of the pixels whose largest channel delta exceeds pixel."""
    diff = ImageChops.difference(blurred(wpf, blur), blurred(ava, blur))
    red, green, blue = diff.split()
    largest = ImageChops.lighter(ImageChops.lighter(red, green), blue)
    return diff, largest.point(lambda value: 255 if value > pixel else 0)


def region_deltas(diff):
    """Mean channel delta of each of the nine regions, keyed 'row-col', from the difference image."""
    width, height = diff.size
    xs = [round(width * i / 3) for i in range(4)]
    ys = [round(height * i / 3) for i in range(4)]
    deltas = {}
    for r, row in enumerate(ROWS):
        for c, col in enumerate(COLS):
            box = (xs[c], ys[r], max(xs[c] + 1, xs[c + 1]), max(ys[r] + 1, ys[r + 1]))
            deltas[f"{row}-{col}"] = sum(ImageStat.Stat(diff.crop(box)).mean) / 3.0
    return deltas


def panel_size(size):
    """The size a panel is drawn at: fits PANEL_WIDTH by PANEL_MAX_HEIGHT and never enlarges past 2x."""
    width, height = size
    scale = min(PANEL_WIDTH / width, PANEL_MAX_HEIGHT / height, 2.0)
    return max(1, round(width * scale)), max(1, round(height * scale))


def write_pair_image(path, title, wpf, ava, mask, pct):
    """WPF left, Avalonia middle, a heat map right (red where the blurred images differ by more than the pixel threshold)."""
    dim = ImageOps.grayscale(ava).convert("RGB")
    heat = Image.composite(Image.new("RGB", ava.size, (255, 40, 40)), dim, mask)
    panels = [("WPF reference", wpf), ("Avalonia", ava), (f"diff {pct:.2f}% of pixels", heat)]
    pw, ph = panel_size(ava.size)
    canvas = Image.new("RGB", (3 * pw + 2 * GAP, ph + LABEL_BAND), (30, 30, 30))
    draw = ImageDraw.Draw(canvas)
    draw.text((6, 6), title, fill=(255, 255, 255))
    for index, (label, image) in enumerate(panels):
        x = index * (pw + GAP)
        draw.text((x + 2, 20), label, fill=(200, 200, 200))
        canvas.paste(image.resize((pw, ph), Image.LANCZOS), (x, LABEL_BAND))
    canvas.save(path)


def compare_shot(name, wpf_path, ava_path, args):
    """The result row for one shot that has both images."""
    wpf_raw = Image.open(wpf_path).convert("RGB")
    ava = Image.open(ava_path).convert("RGB")
    wpf_dip = (wpf_raw.width / args.wpf_scale, wpf_raw.height / args.wpf_scale)
    note = ""
    if abs(wpf_dip[0] - ava.width) > 2 or abs(wpf_dip[1] - ava.height) > 2:
        note = f"size differs: WPF {wpf_dip[0]:.0f}x{wpf_dip[1]:.0f} DIP, Avalonia {ava.width}x{ava.height}"
    wpf = wpf_raw.resize(ava.size, Image.LANCZOS)
    diff, mask = difference_parts(wpf, ava, args.blur, args.pixel)
    hist = mask.histogram()
    pct = 100.0 * hist[255] / (ava.width * ava.height)
    deltas = region_deltas(diff)
    worst = max(deltas, key=deltas.get)
    write_pair_image(os.path.join(args.out, "pairs", name + ".png"), name, wpf, ava, mask, pct)
    status = "pass" if pct <= args.threshold else "fail"
    return {"shot": name, "status": status, "diff_pct": round(pct, 3), "worst_region": worst,
            "worst_region_mean_delta": round(deltas[worst], 2), "wpf_dip": f"{wpf_dip[0]:.0f}x{wpf_dip[1]:.0f}",
            "avalonia_px": f"{ava.width}x{ava.height}", "note": note}


def write_reports(out, rows, missing_rows, args):
    """report.csv (every shot, worst first) and report.md (summary, worst-first table, missing list)."""
    compared = sorted(rows, key=lambda r: r["diff_pct"], reverse=True)
    passes = sum(1 for r in compared if r["status"] == "pass")
    fails = len(compared) - passes
    with open(os.path.join(out, "report.csv"), "w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["shot", "status", "diff_pct", "worst_region", "worst_region_mean_delta", "wpf_dip", "avalonia_px", "note"])
        for r in compared:
            writer.writerow([r["shot"], r["status"], r["diff_pct"], r["worst_region"], r["worst_region_mean_delta"], r["wpf_dip"], r["avalonia_px"], r["note"]])
        for shot, reason in missing_rows:
            writer.writerow([shot, "missing", "", "", "", "", "", reason])
    lines = [
        "# A/B parity report",
        "",
        f"Generated {datetime.datetime.now():%Y-%m-%d %H:%M}. WPF reference: `{args.wpf}`. Pass when at most {args.threshold}% of pixels "
        f"differ after a {args.blur}px blur (channel delta above {args.pixel}).",
        "",
        f"Summary: {passes} pass, {fails} fail, {len(missing_rows)} missing, {len(compared) + len(missing_rows)} shots.",
        "",
        "## Compared shots, worst first",
        "",
        "| Shot | Diff % | Worst region | Region delta | Status | Note |",
        "|---|---|---|---|---|---|",
    ]
    for r in compared:
        lines.append(f"| {r['shot']} | {r['diff_pct']:.2f} | {r['worst_region']} | {r['worst_region_mean_delta']} | {r['status']} | {r['note']} |")
    lines += ["", "## Missing (no Avalonia twin yet)", "", "| Shot | Reason |", "|---|---|"]
    lines += [f"| {shot} | {reason} |" for shot, reason in missing_rows]
    with open(os.path.join(out, "report.md"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    return passes, fails, compared


def main():
    # TF_AB_OUT is set by run.ps1 to a folder unique to the run; the fixed folder is only the fallback for a bare call.
    temp = os.environ.get("TF_AB_OUT") or os.path.join(tempfile.gettempdir(), "tf-ab")
    parser = argparse.ArgumentParser(description="Compare WPF reference shots with the Avalonia renders.")
    parser.add_argument("--wpf", default=os.environ.get("TF_AB_WPF"), help="folder of the WPF reference PNGs (or set TF_AB_WPF)")
    parser.add_argument("--avalonia", default=os.path.join(temp, "avalonia"))
    parser.add_argument("--out", default=temp)
    parser.add_argument("--missing", default=os.path.join(temp, "missing.txt"))
    parser.add_argument("--threshold", type=float, default=2.0, help="pass when at most this percentage of pixels differ")
    parser.add_argument("--blur", type=float, default=2.0, help="tolerance blur radius in pixels")
    parser.add_argument("--pixel", type=int, default=32, help="channel delta above which a pixel counts as different")
    parser.add_argument("--wpf-scale", type=float, default=2.0, help="DPI scale the WPF shots were taken at")
    args = parser.parse_args()
    if not args.wpf or not os.path.isdir(args.wpf):
        sys.exit("The WPF reference folder is missing: pass --wpf or set TF_AB_WPF.")
    os.makedirs(os.path.join(args.out, "pairs"), exist_ok=True)
    missing = load_missing(args.missing)
    rows, missing_rows = [], []
    for name in sorted(f[:-4] for f in os.listdir(args.wpf) if f.lower().endswith(".png")):
        ava_path = os.path.join(args.avalonia, name + ".png")
        if os.path.exists(ava_path):
            rows.append(compare_shot(name, os.path.join(args.wpf, name + ".png"), ava_path, args))
        else:
            missing_rows.append((name, missing.get(name, "No Avalonia render and no entry in missing.txt.")))
    passes, fails, compared = write_reports(args.out, rows, missing_rows, args)
    print(f"pass {passes}  fail {fails}  missing {len(missing_rows)}  total {len(compared) + len(missing_rows)}")
    print("worst 10:")
    for r in compared[:10]:
        print(f"  {r['diff_pct']:7.2f}%  {r['status']:4}  {r['shot']}  ({r['worst_region']}, mean delta {r['worst_region_mean_delta']})")
    print(f"report: {os.path.join(args.out, 'report.md')}")


if __name__ == "__main__":
    main()
