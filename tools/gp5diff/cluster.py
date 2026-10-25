"""Groups the first divergences of a run (compare.py output) by root behaviour.

Usage: python cluster.py <run dir> [<run dir> ...]  ->  <first run>/clusters.json and a text table on stdout
A divergence's signature is the action that made the apps differ, the kind of difference, and the shape of the state it
happened in (what the action touched: a note, a rest, an empty spot past the last beat; whether the bar was full). The
smallest example of each signature (fewest actions before it) is kept as the cluster's repro.
"""
import json, os, re, sys

TF = os.environ.get('GP5DIFF_TF', 'tf')
DIFF = 'diff' if TF == 'tf' else 'diff_' + TF
from collections import defaultdict


def detail(x):
    k = x['kind']
    if k == 'note_effects':
        parts = []
        if x['missing_in_tf']:
            parts.append('TF lacks ' + ','.join(x['missing_in_tf']))
        if x['extra_in_tf']:
            parts.append('TF adds ' + ','.join(x['extra_in_tf']))
        return k + ': ' + '; '.join(parts)
    if k == 'beat_effects':
        return f"{k}: GP5 {x['gp5']} TF {x['tf']}"
    if k == 'beat_count':
        g, t = len(x['gp5']), len([b for b in x['tf']])
        return f"{k}: TF {'more' if t > g else 'fewer'} beats"
    if k == 'bar_count':
        return f"{k}: TF {'more' if x['tf'] > x['gp5'] else 'fewer'} bars"
    if k == 'notes':
        g, t = x['gp5'], x['tf']
        gn = 0 if g.startswith('r/') else g.split('/')[0].count('@')
        tn = 0 if t.startswith('r/') else t.split('/')[0].count('@')
        if tn > gn:
            return 'notes: TF has an extra note'
        if tn < gn:
            return 'notes: TF lacks a note'
        return 'notes: different fret or string'
    if k in ('duration', 'dots', 'tup', 'rest'):
        return f"{k}: GP5 {x['gp5'].split('/')[-1]} TF {x['tf'].split('/')[-1]}" if k != 'rest' else f"rest: GP5 {'rest' if x['gp5'].startswith('r/') else 'note'} TF {'rest' if x['tf'].startswith('r/') else 'note'}"
    return k


def shape(first):
    """A coarse description of where the action happened, from TabForge's state before it (GP5 had the same song then)."""
    bars = first['before_tf']
    cur = first.get('before_tf_cursor') or '0:0:0'
    b = int(cur.split(':')[0])
    text = bars[b] if b < len(bars) else ''
    beats = text.split() if text else []
    return f"{'empty bar' if not beats else 'bar with ' + str(len(beats)) + ' beats'}"


MAP = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'actions.json')))


def stale_clipboard(seq, first):
    """GP5's clipboard outlives the score (it is not the system clipboard): a paste that brings in a fret never typed in this
    sequence pasted something copied in an earlier one (a harness artifact, not a behaviour)."""
    typed = {x['n'] for x in seq['actions'] if x['a'] == 'fret'}
    for bar in first['after_gp5']:
        for beat in bar.split():
            if beat.startswith('r/'):
                continue
            for note in beat.split('/')[0].split(','):
                fret = note.split('@')[0]
                if fret.isdigit() and int(fret) not in typed and int(fret) != 0:
                    return True
    return False


def main():
    runs = sys.argv[1:]
    notcomparable = defaultdict(list)
    stale = []
    local = defaultdict(list)
    clusters = defaultdict(list)
    importer = defaultdict(list)
    crashes = defaultdict(list)
    for run in runs:
        for name in os.listdir(os.path.join(run, DIFF)):
            d = json.load(open(os.path.join(run, DIFF, name)))
            seq = json.load(open(os.path.join(run, 'seq', name)))
            if d.get('gp5_crash_step') is not None and 'first' not in d:
                st = d['gp5_crash_step']
                crashes[seq['actions'][st]['a'] if st < len(seq['actions']) else '?'].append((run, d['id'], st))
            if 'importer' in d and 'diffs' in d['importer']:
                for x in d['importer']['diffs'][:1]:
                    importer[detail(x)].append({'run': run, 'id': d['id'], 'step': d['importer']['step'],
                                                'pyguitarpro': d['importer']['pyguitarpro'], 'tabforge_import': d['importer']['tabforge_import']})
            for loc in d.get('local', []):
                la = loc['action']['a']
                if any(MAP.get(w['a'], {}).get('gp5') is None or MAP.get(w['a'], {}).get('tf') is None for w in seq['actions'][:loc['step'] + 1]):
                    continue   # an earlier action one app lacks (Delete bar, double dot) can leave state the snapshots do not show
                if la == 'paste' and stale_clipboard(seq, {'after_gp5': [loc['after_gp5']]}):
                    continue
                lk = sorted({detail(x) for x in loc['diffs']})
                local[f"{la} -> {lk[0]}"].append({'run': run, 'id': d['id'], **loc})
            f = d.get('first')
            if not f:
                continue
            a = f['action']['a']
            kinds = sorted({detail(x) for x in f['diffs']})
            window = seq['actions'][:f['step'] + 1]   # any earlier action one app lacks can leave state the snapshots do not show
            missing = [w['a'] for w in window if MAP.get(w['a'], {}).get('gp5') is None or MAP.get(w['a'], {}).get('tf') is None]
            if missing:
                # An action one app does not have ran only in the other: not a behaviour difference (listed under missing actions).
                notcomparable[missing[0]].append(d['id'])
                continue
            if a == 'paste' and stale_clipboard(seq, f):
                stale.append(d['id'])
                continue
            # A late divergence is only known to lie in a window of up to 5 actions (GP5 snapshots every 5th action there).
            key = f"{a} -> {kinds[0]}" + ('' if f.get('window', 1) == 1 else '  [in a window of actions]')
            clusters[key].append({'run': run, 'id': d['id'], 'step': f['step'], 'first': f, 'all_kinds': kinds})
    # TabForge exceptions during the replay (any sequence, whatever GP5 did), by action and throwing method.
    exceptions = defaultdict(list)
    for run in runs:
        for name in os.listdir(os.path.join(run, TF)):
            tf = json.load(open(os.path.join(run, TF, name)))
            for line in tf.get('log', []):
                if ': exception ' not in line:
                    continue
                st = int(line.split(':', 1)[0])
                seq = json.load(open(os.path.join(run, 'seq', name)))
                m = re.search(r'TabForge\.[\w\.`]+', line.split(' at ', 1)[-1])
                where = m.group(0) if m else '?'
                kind = line.split(': exception ', 1)[1].split(':', 1)[0]
                exceptions[f"{seq['actions'][st]['a'] if st < len(seq['actions']) else '?'} -> {kind} in {where}"].append({'run': run, 'id': name[:-5], 'step': st, 'line': line[:600]})
                break
    out = []
    for key, items in sorted(clusters.items(), key=lambda kv: -len(kv[1])):
        items.sort(key=lambda it: (it['step'], it['id']))
        rep = items[0]
        seq = json.load(open(os.path.join(rep['run'], 'seq', rep['id'] + '.json')))
        out.append({'signature': key, 'count': len(items), 'examples': [i['id'] for i in items[:10]],
                    'repro_id': rep['id'], 'repro_run': rep['run'], 'repro_actions': seq['actions'][:rep['step'] + 1],
                    'before_gp5': rep['first']['before_gp5'], 'before_tf': rep['first']['before_tf'],
                    'after_gp5': rep['first']['after_gp5'], 'after_tf': rep['first']['after_tf'],
                    'tf_cursor_before': rep['first']['before_tf_cursor'], 'tf_log': rep['first']['tf_log'], 'diffs': rep['first']['diffs']})
    imp = [{'signature': k, 'count': len(v), 'example': v[0]} for k, v in sorted(importer.items(), key=lambda kv: -len(kv[1]))]
    cr = [{'action': k, 'count': len(v), 'examples': v[:5]} for k, v in sorted(crashes.items(), key=lambda kv: -len(kv[1]))]
    ex = [{'signature': k, 'count': len(v), 'examples': sorted(v, key=lambda e: e['step'])[:5]} for k, v in sorted(exceptions.items(), key=lambda kv: -len(kv[1]))]
    nc = {k: {'count': len(v), 'examples': v[:5]} for k, v in notcomparable.items()}
    loc_out = []
    for key, items in sorted(local.items(), key=lambda kv: -len(kv[1])):
        items.sort(key=lambda it: (it['step'], it['id']))
        rep = items[0]
        seq = json.load(open(os.path.join(rep['run'], 'seq', rep['id'] + '.json')))
        loc_out.append({'signature': key, 'count': len(items), 'sequences': len({i['id'] for i in items}), 'examples': [f"{i['id']}@{i['step']}" for i in items[:10]],
                        'repro_id': rep['id'], 'repro_actions': seq['actions'][:rep['step'] + 1], 'bar': rep['bar'], 'before': rep['before'],
                        'after_gp5': rep['after_gp5'], 'after_tf': rep['after_tf'], 'tf_cursor_before': rep['tf_cursor_before'], 'tf_log': rep['tf_log'], 'diffs': rep['diffs']})
    json.dump({'clusters': out, 'local': loc_out, 'importer': imp, 'gp5_crashes': cr, 'tf_exceptions': ex, 'not_comparable': nc,
               'stale_gp5_clipboard': {'count': len(stale), 'examples': stale[:10]}}, open(os.path.join(runs[0], 'clusters.json' if TF == 'tf' else f'clusters_{TF}.json'), 'w'), indent=1)
    print('local (bar-level, later) divergences:')
    for c in loc_out[:40]:
        print(f"{c['count']:5} ({c['sequences']} seq)  {c['signature']}   e.g. {c['repro_id']}@{len(c['repro_actions']) - 1}")
    print('not comparable (action missing in one app):', {k: v['count'] for k, v in nc.items()}, ' stale GP5 clipboard:', len(stale))
    print('tabforge exceptions:')
    for c in ex:
        print(f"{c['count']:5}  {c['signature']}")
    for c in out:
        print(f"{c['count']:5}  {c['signature']}   e.g. {c['repro_id']} ({len(c['repro_actions'])} actions)")
    print('importer:')
    for c in imp:
        print(f"{c['count']:5}  {c['signature']}")
    print('gp5 crashes:', [(c['action'], c['count']) for c in cr])


if __name__ == '__main__':
    main()
