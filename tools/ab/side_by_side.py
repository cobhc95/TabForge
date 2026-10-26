"""Owns: the owner-review picture for one shot: WPF and Avalonia at the same scale, labelled, side by side (or stacked when wide).
Does not own: rendering either side (tools/ab/run.ps1 renders Avalonia; the VM capture renders WPF) or pixel scoring (compare.py).
Tests: none; run `python -I tools/ab/side_by_side.py <wpf.png> <avalonia.png> <out.png> <label> [max_edge_px]` and look at the result.
"""
import sys
from PIL import Image, ImageDraw, ImageFont

LABEL_HEIGHT = 44
GAP = 30
WIDE_RATIO = 1.3  # wider than this is stacked top/bottom instead of left/right


def main(wpf_path, avalonia_path, out_path, label, max_edge=None):
    wpf = Image.open(wpf_path).convert("RGB")
    avalonia = Image.open(avalonia_path).convert("RGB")
    if abs(wpf.width / avalonia.width - 1) > 0.1:  # the WPF capture is at 200 % DPI, the Avalonia render at 100 %
        scale = wpf.width / avalonia.width
        avalonia = avalonia.resize((round(avalonia.width * scale), round(avalonia.height * scale)), Image.LANCZOS)
    try:
        font = ImageFont.truetype("segoeui.ttf", 30)
    except OSError:
        font = ImageFont.load_default()
    white, black = (255, 255, 255), (0, 0, 0)
    if wpf.width > wpf.height * WIDE_RATIO:
        canvas = Image.new("RGB", (max(wpf.width, avalonia.width), wpf.height + avalonia.height + LABEL_HEIGHT * 2), white)
        canvas.paste(wpf, (0, LABEL_HEIGHT))
        canvas.paste(avalonia, (0, wpf.height + LABEL_HEIGHT * 2))
        draw = ImageDraw.Draw(canvas)
        draw.text((8, 4), f"{label}   TOP = WPF (current app)", fill=black, font=font)
        draw.text((8, wpf.height + LABEL_HEIGHT + 4), f"{label}   BOTTOM = Avalonia (new)", fill=black, font=font)
    else:
        canvas = Image.new("RGB", (wpf.width + avalonia.width + GAP, max(wpf.height, avalonia.height) + LABEL_HEIGHT), white)
        canvas.paste(wpf, (0, LABEL_HEIGHT))
        canvas.paste(avalonia, (wpf.width + GAP, LABEL_HEIGHT))
        draw = ImageDraw.Draw(canvas)
        draw.text((8, 4), f"{label}   LEFT = WPF", fill=black, font=font)
        draw.text((wpf.width + GAP + 6, 4), "RIGHT = Avalonia", fill=black, font=font)
    if max_edge:  # describer copies are shrunk: an image costs tokens in proportion to its size
        scale = int(max_edge) / max(canvas.size)
        if scale < 1:
            canvas = canvas.resize((round(canvas.width * scale), round(canvas.height * scale)), Image.LANCZOS)
    canvas.save(out_path)


if __name__ == "__main__":
    if len(sys.argv) not in (5, 6):
        sys.exit("usage: side_by_side.py <wpf.png> <avalonia.png> <out.png> <label> [max_edge_px]")
    main(*sys.argv[1:])
