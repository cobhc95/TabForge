"""Compare two capture folders (A = before, B = after), big picture first.

For every PNG pair it answers, in order:
  1. Built?      both shots exist (a missing shot means a step or window failed).
  2. Organised?  same size (a different size means layout: a panel, row or window grew or shrank).
  3. Shifted?    the changed area matches the other image moved by a few pixels (alignment / margin shift).
  4. Pixels?     only then: how many pixels differ, where, and a cropped side-by-side for review.
Writes <out>/triage.md and <out>/<shot>-ab.png (before | after | difference) for every shot that is not identical.
Usage: py -I triage.py <dirA> <dirB> <out>
"""
import os, sys
from PIL import Image, ImageChops

def bbox_diff(a, b):
    return ImageChops.difference(a, b).getbbox()

def shift_match(a, b, box, limit=6):
    """Does the changed region of b equal a moved by (dx, dy)? Returns the shift or None."""
    x0, y0, x1, y1 = box
    region = b.crop(box)
    for dy in range(-limit, limit + 1):
        for dx in range(-limit, limit + 1):
            if dx == 0 and dy == 0:
                continue
            src = (x0 - dx, y0 - dy, x1 - dx, y1 - dy)
            if src[0] < 0 or src[1] < 0 or src[2] > a.width or src[3] > a.height:
                continue
            if ImageChops.difference(a.crop(src), region).getbbox() is None:
                return dx, dy
    return None

def count(a, b, box):
    d = ImageChops.difference(a.crop(box), b.crop(box)).convert('L')
    return sum(1 for v in d.tobytes() if v)

def side_by_side(a, b, box, path, pad=24):
    x0, y0, x1, y1 = box
    box = (max(0, x0 - pad), max(0, y0 - pad), min(a.width, x1 + pad), min(a.height, y1 + pad))
    ca, cb = a.crop(box), b.crop(box)
    diff = ImageChops.difference(ca, cb).convert('L').point(lambda v: 255 if v else 0)
    red = Image.new('RGBA', ca.size, (255, 0, 0, 255))
    marked = Image.composite(red, ca.convert('RGBA'), diff)
    w, h = ca.size
    sheet = Image.new('RGBA', (w * 3 + 16, h), (128, 128, 128, 255))
    sheet.paste(ca, (0, 0)); sheet.paste(cb, (w + 8, 0)); sheet.paste(marked, (2 * w + 16, 0))
    sheet.save(path)

def main(da, db, out):
    os.makedirs(out, exist_ok=True)
    names = sorted(set(f for f in os.listdir(da) if f.endswith('.png')) | set(f for f in os.listdir(db) if f.endswith('.png')))
    rows, same = [], 0
    for n in names:
        pa, pb = os.path.join(da, n), os.path.join(db, n)
        if not os.path.exists(pa) or not os.path.exists(pb):
            rows.append((n, 'BUILD', f"missing in {'A' if not os.path.exists(pa) else 'B'}")); continue
        a, b = Image.open(pa).convert('RGBA'), Image.open(pb).convert('RGBA')
        if a.size != b.size:
            rows.append((n, 'LAYOUT', f'size {a.size} -> {b.size}'))
            sheet = Image.new('RGBA', (a.width + b.width + 8, max(a.height, b.height)), (128, 128, 128, 255))
            sheet.paste(a, (0, 0)); sheet.paste(b, (a.width + 8, 0))
            sheet.save(os.path.join(out, n[:-4] + '-ab.png'))
            continue
        box = bbox_diff(a, b)
        if box is None:
            same += 1; continue
        shift = shift_match(a, b, box)
        kind = 'SHIFT' if shift else 'PIXELS'
        detail = f'box {box}, {count(a, b, box)} px' + (f', matches A moved by {shift}' if shift else '')
        rows.append((n, kind, detail))
        side_by_side(a, b, box, os.path.join(out, n[:-4] + '-ab.png'))
    order = {'BUILD': 0, 'LAYOUT': 1, 'SHIFT': 2, 'PIXELS': 3}
    rows.sort(key=lambda r: (order[r[1]], r[0]))
    with open(os.path.join(out, 'triage.md'), 'w', encoding='utf-8') as f:
        f.write(f'# A/B triage\n\n{len(names)} shots: {same} identical, {len(rows)} different.\n\n| Shot | Kind | Detail |\n|---|---|---|\n')
        for r in rows: f.write(f'| {r[0]} | {r[1]} | {r[2]} |\n')
    print(f'AB: {len(names)} shots, {same} identical, {len(rows)} different')
    for r in rows: print(' ', r[1], r[0], r[2])

if __name__ == '__main__':
    main(*sys.argv[1:4])
