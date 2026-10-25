"""Seeded generator of human-like editing sequences for the GP5 differential harness.

Usage: python gen.py <run dir> <first seed> <count> [--trivial]
Writes <run dir>/seq/s<seed>.json and copies actions.json into the run dir. A sequence has 20-80 actions weighted toward
realistic tab writing: type frets and move on, change durations, add effects, change your mind (retype, undo, toggle twice),
select a phrase and copy / paste it, insert and delete beats and bars. --trivial writes hand-checkable sequences instead.
"""
import json, os, random, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
EFFECTS = ['palm_mute', 'let_ring', 'vibrato', 'slide', 'hammer', 'ghost', 'dead', 'accent', 'staccato', 'harmonic',
           'heavy_accent', 'bend', 'grace']
EFFECT_W = [8, 5, 6, 5, 6, 3, 3, 3, 2, 2, 1, 2, 1]


def fret(rng):
    # Mostly low frets; some two-digit ones (12, 15...) as a player types them.
    return rng.choice([0, 0, 1, 2, 2, 3, 3, 5, 5, 5, 7, 7, 8, 9, 10, 12, 12, 14, 15, 17])


# Masked mode (--masked): leaves out the navigation that the first big run showed differs at once (Up on the top string, Ctrl+Right
# on the last bar, End, duration changes on auto-filled rests), so the rarer editing differences come first.
MASK = '--masked' in sys.argv


def phrase(rng, out):
    """Write a few beats: fret, maybe a duration change, move right; sometimes a chord."""
    for _ in range(rng.randint(2, 6)):
        r = rng.random()
        if r < 0.12 and not MASK:
            out.append({'a': rng.choice(['shorter', 'longer'])})
        out.append({'a': 'fret', 'n': fret(rng)})
        if rng.random() < 0.15:  # chord: another string on the same beat
            if MASK:
                out += [{'a': 'down'}, {'a': 'fret', 'n': fret(rng)}, {'a': 'up'}]
            else:
                out.append({'a': rng.choice(['up', 'down'])})
                out.append({'a': 'fret', 'n': fret(rng)})
        if rng.random() < 0.18:
            out.append({'a': rng.choices(EFFECTS, EFFECT_W)[0]})
        out.append({'a': 'right'})


def gen(seed):
    rng = random.Random(seed)
    target = rng.randint(20, 80)
    out = []
    while len(out) < target:
        r = rng.random()
        if r < 0.40:
            phrase(rng, out)
        elif r < 0.52:  # move around
            if MASK:
                out.append({'a': rng.choices(['left', 'right', 'home'], [10, 6, 2])[0]})
            else:
                out.append({'a': rng.choices(['left', 'right', 'up', 'down', 'home', 'end', 'prev_bar', 'next_bar', 'first_bar', 'last_bar'],
                                             [10, 8, 6, 6, 2, 2, 2, 2, 1, 1])[0]})
        elif r < 0.60:  # change your mind about a note
            choice = rng.random()
            if choice < 0.35:
                out.append({'a': 'fret', 'n': fret(rng)})
            elif choice < 0.65:
                out.append({'a': 'delete_note'})
            else:
                out.append({'a': rng.choice(['backspace', 'delete_beat', 'insert_beat'])})
        elif r < 0.67:  # undo / redo
            out.append({'a': 'undo'})
            if rng.random() < 0.3:
                out.append({'a': 'redo'})
        elif r < 0.73:  # toggle an effect, sometimes twice
            e = rng.choices(EFFECTS, EFFECT_W)[0]
            out.append({'a': e})
            if rng.random() < 0.3:
                out.append({'a': e})
        elif r < 0.81:  # rhythm edits
            out.append({'a': rng.choices(['dot', 'triplet', 'rest', 'tie', 'shorter', 'longer', 'double_dot'], [5, 3, 4, 4, 0, 0, 1] if MASK else [5, 3, 4, 4, 4, 4, 1])[0]})
        elif r < 0.88:  # copy a phrase and paste it later
            out.append({'a': rng.choice(['left', 'home'])})
            for _ in range(rng.randint(1, 3)):
                out.append({'a': 'sel_right'})
            out.append({'a': rng.choice(['copy', 'copy', 'cut'])})
            for _ in range(rng.randint(1, 4)):
                out.append({'a': 'right' if MASK else rng.choice(['right', 'right', 'next_bar', 'end'])})
            out.append({'a': 'paste'})
        elif r < 0.93:  # bars
            out.append({'a': rng.choice(['insert_bar', 'insert_bar', 'delete_bar'])})
        else:  # less common commands
            out.append({'a': rng.choice(['semitone_up', 'semitone_down', 'note_string_up', 'note_string_down', 'tie_beat', 'key_c', 'sel_left', 'select_all'])})
    return {'id': f"{'q' if MASK else 's'}{seed:06d}", 'seed': seed, 'actions': out[:max(target, 20)]}


TRIVIAL = [
    [{'a': 'fret', 'n': 5}],
    [{'a': 'fret', 'n': 3}, {'a': 'right'}, {'a': 'fret', 'n': 5}, {'a': 'right'}, {'a': 'fret', 'n': 7}, {'a': 'right'}, {'a': 'fret', 'n': 8}],
    [{'a': 'fret', 'n': 12}, {'a': 'shorter'}, {'a': 'right'}, {'a': 'fret', 'n': 0}, {'a': 'right'}, {'a': 'fret', 'n': 2}, {'a': 'right'}, {'a': 'fret', 'n': 3}, {'a': 'right'}, {'a': 'down'}, {'a': 'fret', 'n': 5}],
    [{'a': 'fret', 'n': 5}, {'a': 'palm_mute'}, {'a': 'right'}, {'a': 'fret', 'n': 7}, {'a': 'vibrato'}, {'a': 'right'}, {'a': 'fret', 'n': 9}, {'a': 'let_ring'}, {'a': 'right'}, {'a': 'fret', 'n': 10}, {'a': 'hammer'}],
    [{'a': 'longer'}, {'a': 'fret', 'n': 1}, {'a': 'right'}, {'a': 'fret', 'n': 3}],
    [{'a': 'fret', 'n': 5}, {'a': 'accent'}, {'a': 'right'}, {'a': 'fret', 'n': 6}, {'a': 'staccato'}, {'a': 'right'}, {'a': 'fret', 'n': 7}, {'a': 'ghost'}, {'a': 'right'}, {'a': 'fret', 'n': 8}, {'a': 'dead'}],
    [{'a': 'fret', 'n': 5}, {'a': 'right'}, {'a': 'fret', 'n': 5}, {'a': 'right'}, {'a': 'fret', 'n': 5}, {'a': 'right'}, {'a': 'fret', 'n': 5}, {'a': 'right'}, {'a': 'fret', 'n': 7}],
    [{'a': 'fret', 'n': 2}, {'a': 'dot'}, {'a': 'right'}, {'a': 'shorter'}, {'a': 'fret', 'n': 4}],
]


def main():
    run, first, count = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
    os.makedirs(os.path.join(run, 'seq'), exist_ok=True)
    shutil.copy(os.path.join(HERE, 'actions.json'), os.path.join(run, 'actions.json'))
    if '--trivial' in sys.argv:
        for i, acts in enumerate(TRIVIAL):
            with open(os.path.join(run, 'seq', f't{i:03d}.json'), 'w') as f:
                json.dump({'id': f't{i:03d}', 'seed': -1, 'actions': acts}, f)
        return
    for seed in range(first, first + count):
        s = gen(seed)
        with open(os.path.join(run, 'seq', s['id'] + '.json'), 'w') as f:
            json.dump(s, f)


if __name__ == '__main__':
    main()
