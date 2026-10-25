"""Side-by-side pictures of GP5 and TabForge after the same actions.

  python side.py pick <big run> <side run> [per category]   choose sequences, write <side run>/seq, actions.json and shots.txt
  python side.py stitch <side run> <out dir>                 GP5 (left) | TabForge (right) per shot -> <out dir>/<id>_<step>.png
Pick: short sequences (at most 30 actions) whose last actions include ties, dots, rests, deletes, paste, techniques or a
selection, so the final screen shows them; each gets a shot after its last action, and a few also one after the action where
the songs first differed. Stitch needs Pillow (the system Python has it; the harness venv does not).
"""
import json, os, random, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CATEGORIES = {
    'ties': {'tie', 'tie_beat'}, 'dots': {'dot', 'double_dot'}, 'rests': {'rest'},
    'deletes': {'delete_note', 'delete_beat', 'backspace', 'delete_bar'}, 'paste': {'paste'},
    'techniques': {'palm_mute', 'let_ring', 'vibrato', 'slide', 'hammer', 'bend', 'ghost', 'dead', 'accent', 'harmonic', 'staccato', 'grace', 'heavy_accent'},
    'selections': {'sel_right', 'sel_left', 'select_all'},
}


def pick(big, side, per):
    rng = random.Random(7)
    os.makedirs(os.path.join(side, 'seq'), exist_ok=True)
    shutil.copy(os.path.join(HERE, 'actions.json'), os.path.join(side, 'actions.json'))
    done = {n[:-5] + '.json' for n in os.listdir(os.path.join(big, 'gp5log')) if n.endswith('.json')}
    names = sorted(n for n in os.listdir(os.path.join(big, 'seq')) if n in done)
    rng.shuffle(names)
    shots, chosen = [], set()
    for cat, acts in CATEGORIES.items():
        n = 0
        for name in names:
            if name in chosen:
                continue
            seq = json.load(open(os.path.join(big, 'seq', name)))
            a = seq['actions']
            if len(a) > 40 or not any(x['a'] in acts for x in a[-3:]):
                continue
            chosen.add(name)
            shutil.copy(os.path.join(big, 'seq', name), os.path.join(side, 'seq', name))
            shots.append(f"{seq['id']} {len(a) - 1}")
            diff = os.path.join(big, os.environ.get('GP5DIFF_DIFF', 'diff'), name)
            if n < 2 and os.path.exists(diff):   # the first-difference moment for a couple per category
                f = json.load(open(diff)).get('first')
                if f and f['step'] < len(a) - 1:
                    shots.append(f"{seq['id']} {f['step']}")
            n += 1
            if n >= per:
                break
    open(os.path.join(side, 'shots.txt'), 'w').write('\n'.join(shots) + '\n')
    json.dump({k: sorted(v) for k, v in CATEGORIES.items()}, open(os.path.join(side, 'categories.json'), 'w'))
    print(len(chosen), 'sequences,', len(shots), 'shots')


def stitch(side, out):
    from PIL import Image, ImageDraw
    os.makedirs(out, exist_ok=True)
    for line in open(os.path.join(side, 'shots.txt')):
        sid, step = line.split()
        key = f"{sid}_{int(step):03d}"
        g = os.path.join(side, 'side_gp5', key + '.png')
        t = os.path.join(side, 'side_tf', key + '.png')
        if not (os.path.exists(g) and os.path.exists(t)):
            continue
        gi = Image.open(g).convert('RGB')
        gi = gi.crop((0, 240, min(gi.width, 1085), min(gi.height, 655)))   # GP5's score area in its 1100x800 window
        ti = Image.open(t).convert('RGB')
        # The page ends where the dark window background starts: keep the page only.
        bottom = ti.height
        for y in range(60, ti.height):
            if sum(ti.getpixel((20, y))) < 120:   # the page margin, left of the staves
                bottom = y
                break
        ti = ti.crop((0, 0, ti.width, bottom))
        h = max(gi.height, ti.height)
        canvas = Image.new('RGB', (gi.width + ti.width + 30, h + 28), 'white')
        canvas.paste(gi, (0, 28))
        canvas.paste(ti, (gi.width + 30, 28))
        d = ImageDraw.Draw(canvas)
        d.text((8, 6), f"GP5  {sid} after action {int(step) + 1}", fill='black')
        d.text((gi.width + 38, 6), f"TabForge  {sid} after action {int(step) + 1}", fill='black')
        d.line([(gi.width + 15, 0), (gi.width + 15, h + 28)], fill='gray', width=2)
        canvas.save(os.path.join(out, key + '.png'))


def listing(side):
    for line in open(os.path.join(side, 'shots.txt')):
        sid, step = line.split()
        a = json.load(open(os.path.join(side, 'seq', sid + '.json')))['actions']
        st = int(step)
        print(sid, st, ' '.join(x['a'] + str(x.get('n', '')) for x in a[max(0, st - 3):st + 1]))


if __name__ == '__main__':
    if sys.argv[1] == 'list':
        listing(sys.argv[2])
    elif sys.argv[1] == 'pick':
        pick(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 9)
    else:
        stitch(sys.argv[2], sys.argv[3])
