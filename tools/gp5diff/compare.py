"""Comparator of the GP5 differential harness.

Usage: python compare.py <run dir>
Reads <run>/tf/<id>.json (TabForge's song after each action of the replayed sequence, SelfTestGp5Diff.GdDump) and GP5's file
after each action (<run>/steps/<id>/NNN.gp5). GP5's state is read twice: with pyguitarpro (gpdump.py, the reference: it keeps
GP5's empty placeholder beats apart from rests) and with TabForge's importer (<run>/gp5/<id>.json); where the two readings of the
same file differ, that is an importer difference, reported separately. Writes <run>/diff/<id>.json (the first action after
which the apps differ, with the bars before and after) and <run>/summary.json. Ignored on purpose: TabForge's automatic rest fill
after the last beat GP5 has in a bar (the owner keeps auto-fill), so trailing rests TabForge has beyond GP5's beats do not count.
"""
import json, os, sys

# The TabForge replay folder to compare (GP5DIFF_TF, default tf): a newer TabForge build replays into its own folder.
TF = os.environ.get('GP5DIFF_TF', 'tf')
DIFF = 'diff' if TF == 'tf' else 'diff_' + TF

# Technique names written by the editor and by the importer for the same mark.
ALIAS = {'t:PM': 't:PalmMute', 't:HOPOOrigin': 't:HOPO', 't:HOPODestination': None, 't:Ghost': None, 't:Dead': None,
         't:Harmonic': 'harmonic', 't:ArtificialHarmonic': 'harmonic', 't:Bend': None}


def norm_fx(xs):
    out = set()
    for x in xs:
        x = ALIAS.get(x, x)
        if x:
            out.add(x)
    return sorted(out)


def beat_key(b):
    return {
        'd': b['d'], 'dots': b['dots'], 'tup': b['tup'], 'rest': b['rest'],
        'notes': [(n['s'], n['f']) for n in b['notes']],
    }


def is_rest(b):
    return b['rest'] and not b['notes']


def norm_grace(state):
    """One shape for grace notes: TabForge keeps a grace note as an extra note on the same string of the beat, GP5 (pyguitarpro)
    as an effect of the note; both become the principal note with a 'grace' mark."""
    for bar in state.get('bars', []):
        for b in bar['beats']:
            notes = b['notes']
            graces = [n for n in notes if 'grace_note' in n['x']]
            keep = []
            for n in notes:
                if 'grace_note' in n['x'] and any(m is not n and m['s'] == n['s'] and 'grace_note' not in m['x'] for m in notes):
                    continue
                if 'grace_note' in n['x'] or any(g['s'] == n['s'] for g in graces):
                    n = dict(n, x=sorted({*(x for x in n['x'] if x not in ('grace_note', 't:GraceBefore', 't:GraceOnBeat')), 'grace'}))
                keep.append(n)
            b['notes'] = keep
    return state


def compare(tf, gp):
    tf, gp = norm_grace(json.loads(json.dumps(tf))), norm_grace(json.loads(json.dumps(gp)))
    diffs = []
    tb, gb = tf['bars'], gp['bars']
    # Trailing bars that are empty (or only rests) on one side and missing on the other are still a bar-count difference.
    if len(tb) != len(gb):
        diffs.append({'kind': 'bar_count', 'gp5': len(gb), 'tf': len(tb)})
    for i in range(min(len(tb), len(gb))):
        t, g = tb[i], gb[i]
        if t['ts'] != g['ts']:
            diffs.append({'kind': 'time_signature', 'bar': i + 1, 'gp5': g['ts'], 'tf': t['ts']})
        tbeats, gbeats = t['beats'], g['beats']
        # Auto-fill: TabForge's extra trailing rests after GP5's last beat are not compared.
        if len(tbeats) > len(gbeats) and all(is_rest(b) for b in tbeats[len(gbeats):]):
            tbeats = tbeats[:len(gbeats)]
        # An empty GP5 bar equals a TabForge bar of rests only.
        if not gbeats and all(is_rest(b) for b in tbeats):
            tbeats = []
        if len(tbeats) != len(gbeats):
            diffs.append({'kind': 'beat_count', 'bar': i + 1, 'gp5': [fmt(b) for b in gbeats], 'tf': [fmt(b) for b in t['beats']]})
            continue
        for j, (a, b) in enumerate(zip(tbeats, gbeats)):
            ka, kb = beat_key(a), beat_key(b)
            for field in ('d', 'dots', 'tup', 'rest', 'notes'):
                if ka[field] != kb[field]:
                    diffs.append({'kind': field if field != 'd' else 'duration', 'bar': i + 1, 'beat': j + 1, 'gp5': fmt(b), 'tf': fmt(a)})
                    break
            else:
                if sorted(a['bx']) != sorted(b['bx']):
                    diffs.append({'kind': 'beat_effects', 'bar': i + 1, 'beat': j + 1, 'gp5': b['bx'], 'tf': a['bx']})
                for na, nb in zip(a['notes'], b['notes']):
                    fa, fb = norm_fx(na['x']), norm_fx(nb['x'])
                    if fa != fb:
                        diffs.append({'kind': 'note_effects', 'bar': i + 1, 'beat': j + 1, 'string': na['s'],
                                      'gp5': fb, 'tf': fa, 'missing_in_tf': sorted(set(fb) - set(fa)), 'extra_in_tf': sorted(set(fa) - set(fb))})
    return diffs


def fmt(b):
    n = 'r' if is_rest(b) else ','.join(f"{x['f']}@{x['s']}" + ('[' + ' '.join(norm_fx(x['x'])) + ']' if norm_fx(x['x']) else '') for x in b['notes'])
    return f"{n}/{b['d']}{'.' * b['dots']}{('t' + b['tup']) if b['tup'] else ''}" + ('{' + ' '.join(b['bx']) + '}' if b['bx'] else '')


def bar_text(bar):
    return ' '.join(fmt(b) for b in bar['beats'])


def sig(x):
    return (x['kind'], x.get('bar'), x.get('beat'), x.get('string'))


MOVE_ONLY = {'left', 'right', 'up', 'down', 'home', 'end', 'first_bar', 'last_bar', 'prev_bar', 'next_bar', 'sel_left', 'sel_right', 'select_all', 'copy'}


def gp5_crash_step(run, sid):
    """The step at which GP5 reported an access violation (its own crash message), or None."""
    path = os.path.join(run, 'gp5log', sid + '.json')
    if not os.path.exists(path):
        return None
    for line in json.load(open(path)).get('log', []):
        if 'gp5_crash' in line or ('#32770 "Guitar Pro" (unexpected)' in line):
            try:
                return int(line.split(':', 1)[0])
            except ValueError:
                return 0
    return None


def bars(state):
    return [bar_text(x) for x in state.get('bars', [])]


def one(run, name):
    import gpdump
    sid = name[:-5]
    tf = json.load(open(os.path.join(run, TF, name)))
    seq = json.load(open(os.path.join(run, 'seq', name)))
    imp = json.load(open(os.path.join(run, 'gp5', name))) if os.path.exists(os.path.join(run, 'gp5', name)) else {}
    step_dir = os.path.join(run, 'steps', sid)
    files = sorted(f for f in os.listdir(step_dir) if f.endswith('.gp5')) if os.path.isdir(step_dir) else []
    ts = tf.get('steps') or []
    out = {'id': sid, 'actions': len(seq['actions']), 'tf_log': tf.get('log', [])}
    idx = [int(f[:3]) for f in files]
    if 'bars' not in tf or not ts or not files or idx[-1] >= len(ts):
        out['error'] = f"tf steps {len(ts)}, gp5 snapshots {len(files)}, tf error {'bars' not in tf}"
        return out
    crash = gp5_crash_step(run, sid)
    out['gp5_crash_step'] = crash
    # Snapshots GP5 may not have written: after a cursor move, selection or copy the driver (from the big run's restart on) kept
    # the previous file when Ctrl+S wrote nothing, but GP5 does not count a bar or beat that Right / Ctrl+Right creates as a
    # change. Such a snapshot (GP5 unchanged while TabForge's song changed) is unknown: it is left out, and a difference is then
    # placed in the window up to the next snapshot.
    all_gs = [gpdump.dump(os.path.join(step_dir, f)) for f in files]
    empty_song = {'bars': [{'ts': '4/4', 'beats': []}]}
    keep = []
    stale = False   # a skipped snapshot leaves GP5's file behind until an action that surely writes (not a move) or a change shows
    for k in range(len(files)):
        i = idx[k]
        prev_g = all_gs[k - 1] if k else (empty_song if i == 0 else None)
        prev_t = ts[i - 1] if i else empty_song
        move = seq['actions'][i]['a'] in MOVE_ONLY
        same = prev_g is not None and json.dumps(all_gs[k]['bars']) == json.dumps(prev_g['bars'])
        if move and same and (stale or ((k == 0 or idx[k - 1] == i - 1) and compare(ts[i], prev_t))):
            stale = True
            continue
        stale = False
        keep.append(k)
    files = [files[k] for k in keep]
    idx = [idx[k] for k in keep]
    gs = [all_gs[k] for k in keep]
    # GP5 typed into voice 2 at some point (a digit taken as Ctrl+digit): the sequence is not a valid reference.
    if any(g.get('voice2') for g in gs):
        out['artifact'] = 'gp5 voice 2'
        return out
    isteps = imp.get('steps') or []
    isteps = [isteps[k] for k in keep if k < len(isteps)]
    empty = {'bars': [{'ts': '4/4', 'beats': []}]}
    prev_i, prev_t, prev_g = -1, empty, empty
    for i, b in zip(idx, gs):
        if crash is not None and i >= crash:
            break
        a = ts[i]
        d = compare(a, b)
        if d:
            # GP5 snapshots are every action early on, then every 5th: the divergence lies in actions prev_i+1 .. i.
            out['first'] = {
                'step': i, 'window': i - prev_i, 'action': seq['actions'][i], 'window_actions': seq['actions'][prev_i + 1:i + 1],
                'context': seq['actions'][max(0, prev_i + 1 - 8):prev_i + 1], 'diffs': d,
                'before_gp5': bars(prev_g), 'before_tf': bars(prev_t), 'before_tf_cursor': prev_t.get('cursor'),
                'after_gp5': bars(b), 'after_tf': bars(a), 'after_tf_cursor': a.get('cursor'),
                'tf_log': [l for l in tf.get('log', []) if prev_i < int(l.split(':', 1)[0]) <= i] if tf.get('log') else [],
            }
            break
        prev_i, prev_t, prev_g = i, a, b
    out['local'] = local_divergences(seq, ts, idx, gs, crash, tf.get('log', []))
    # TabForge's importer against pyguitarpro on the same GP5 files: the first step where they read it differently.
    for i, (a, b) in enumerate(zip(isteps, gs)):
        if 'bars' not in a:
            out['importer'] = {'step': i, 'error': a.get('error')}
            break
        d = compare(a, b)
        if d:
            out['importer'] = {'step': i, 'diffs': d, 'pyguitarpro': bars(b), 'tabforge_import': bars(a)}
            break
    return out


def local_divergences(seq, ts, idx, gs, crash, log):
    """
    Later, independent differences: after the songs first differ (often only by an extra bar at the end), a bar that was the same
    in both apps before an action and differs after it was changed differently by that action. Only actions with a GP5 snapshot
    before and after them count (one action, no window). Each bar is counted once, at its first difference.
    """
    found, diverged = [], set()
    prev = None
    for i, g in zip(idx, gs):
        if crash is not None and i >= crash:
            break
        t = ts[i]
        if prev is not None and prev[0] == i - 1:
            pt, pg = prev[1], prev[2]
            n = min(len(pt['bars']), len(pg['bars']), len(t['bars']), len(g['bars']))
            for bi in range(n):
                if bi in diverged:
                    continue
                if compare({'bars': [pt['bars'][bi]]}, {'bars': [pg['bars'][bi]]}):
                    diverged.add(bi)   # already different before: not this action's doing
                    continue
                d = compare({'bars': [t['bars'][bi]]}, {'bars': [g['bars'][bi]]})
                if d:
                    diverged.add(bi)
                    found.append({'step': i, 'bar': bi + 1, 'action': seq['actions'][i], 'context': seq['actions'][max(0, i - 6):i], 'diffs': d,
                                  'before': bar_text(pt['bars'][bi]), 'after_gp5': bar_text(g['bars'][bi]), 'after_tf': bar_text(t['bars'][bi]),
                                  'tf_cursor_before': pt.get('cursor'), 'tf_log': [l for l in log if l.startswith(f'{i}:')]})
        else:
            # Without the snapshot just before, bars that differ now cannot be blamed on one action: they are set aside.
            n = min(len(t['bars']), len(g['bars']))
            for bi in range(n):
                if bi not in diverged and compare({'bars': [t['bars'][bi]]}, {'bars': [g['bars'][bi]]}):
                    diverged.add(bi)
        prev = (i, t, g)
    return found


def main():
    run = sys.argv[1]
    os.makedirs(os.path.join(run, DIFF), exist_ok=True)
    stats = {'compared': 0, 'identical_every_step': 0, 'diverged': 0, 'gp5_crashed_first': 0, 'errors': 0,
             'first_diff_kinds': {}, 'importer_differences': 0}
    for name in sorted(os.listdir(os.path.join(run, TF))):
        if not os.path.isdir(os.path.join(run, 'steps', name[:-5])) or not os.path.exists(os.path.join(run, 'out', name[:-5] + '.gp5')):
            continue
        out = one(run, name)
        stats['compared'] += 1
        if out.get('error'):
            stats['errors'] += 1
        elif out.get('artifact'):
            stats['gp5_voice2_artifact'] = stats.get('gp5_voice2_artifact', 0) + 1
        elif 'first' in out:
            stats['diverged'] += 1
            for x in out['first']['diffs']:
                stats['first_diff_kinds'][x['kind']] = stats['first_diff_kinds'].get(x['kind'], 0) + 1
        elif out.get('gp5_crash_step') is not None:
            stats['gp5_crashed_first'] += 1
        else:
            stats['identical_every_step'] += 1
        if 'importer' in out:
            stats['importer_differences'] += 1
        json.dump(out, open(os.path.join(run, DIFF, name), 'w'), indent=1)
    json.dump(stats, open(os.path.join(run, 'summary.json' if TF == 'tf' else f'summary_{TF}.json'), 'w'), indent=1)
    print(json.dumps(stats))


if __name__ == '__main__':
    main()