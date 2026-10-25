"""Shrinks each cluster's repro to the fewest actions that still make the apps differ the same way (delta debugging over runs).

  python minimize.py init <clusters.json> <out> <top K>   start from the shortest example of the top K clusters
  python minimize.py round <out> <k>                      write round k's candidates (<out>/r<k>/seq) - run both apps on them
  python minimize.py update <out> <k>                     keep, per cluster, the shortest candidate that still diverges the same way
A candidate removes a chunk of actions (halves, quarters, ... single actions) but never the last one, the action that made
the difference. "The same way" = the first divergence is at the candidate's last action with the same signature (cluster.py).
"""
import json, os, shutil, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cluster

HERE = os.path.dirname(os.path.abspath(__file__))


def sig_of(first):
    kinds = sorted({cluster.detail(x) for x in first['diffs']})
    return f"{first['action']['a']} -> {kinds[0]}" + ('' if first.get('window', 1) == 1 else '  [in a window of actions]')


def same(sig, wanted):
    return sig.replace('  [in a window of actions]', '') == wanted.replace('  [in a window of actions]', '')


def init(clusters_path, out, k):
    data = json.load(open(clusters_path))
    state = []
    for i, c in enumerate(data['clusters'][:k]):
        state.append({'n': i, 'signature': c['signature'], 'count': c['count'], 'actions': c['repro_actions'], 'done': len(c['repro_actions']) <= 1,
                      'source': c['repro_id']})
    os.makedirs(out, exist_ok=True)
    json.dump(state, open(os.path.join(out, 'state.json'), 'w'), indent=1)


def candidates(acts):
    head, last = acts[:-1], acts[-1]
    out, seen = [], set()
    size = max(1, len(head) // 2)
    while True:
        for start in range(0, len(head), size):
            c = head[:start] + head[start + size:] + [last]
            key = json.dumps(c)
            if key not in seen and len(c) < len(acts):
                seen.add(key)
                out.append(c)
        if size == 1 or len(out) >= 40:
            break
        size = max(1, size // 2)
    return out[:40]


def round_(out, k):
    state = json.load(open(os.path.join(out, 'state.json')))
    rd = os.path.join(out, f'r{k}')
    os.makedirs(os.path.join(rd, 'seq'), exist_ok=True)
    shutil.copy(os.path.join(HERE, 'actions.json'), os.path.join(rd, 'actions.json'))
    count = 0
    for s in state:
        if s['done']:
            continue
        for j, c in enumerate(candidates(s['actions'])):
            sid = f"m{s['n']:03d}_{j:02d}"
            json.dump({'id': sid, 'seed': -1, 'actions': c}, open(os.path.join(rd, 'seq', sid + '.json'), 'w'))
            count += 1
    print(count)


def update(out, k):
    state = json.load(open(os.path.join(out, 'state.json')))
    rd = os.path.join(out, f'r{k}')
    changed = 0
    for s in state:
        if s['done']:
            continue
        best = None
        for name in sorted(os.listdir(os.path.join(rd, 'diff'))):
            if not name.startswith(f"m{s['n']:03d}_"):
                continue
            d = json.load(open(os.path.join(rd, 'diff', name)))
            seq = json.load(open(os.path.join(rd, 'seq', name)))
            f = d.get('first')
            if f and f['step'] == len(seq['actions']) - 1 and same(sig_of(f), s['signature']):
                if best is None or len(seq['actions']) < len(best['actions']):
                    best = {'actions': seq['actions'], 'first': f, 'id': name[:-5]}
        if best:
            s['actions'] = best['actions']
            s['first'] = best['first']
            changed += 1
            s['done'] = len(s['actions']) <= 1
        else:
            s['done'] = True   # no smaller candidate keeps the behaviour: this repro is minimal for chunk removal
    json.dump(state, open(os.path.join(out, 'state.json'), 'w'), indent=1)
    print(changed)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'init':
        init(sys.argv[2], sys.argv[3], int(sys.argv[4]))
    elif cmd == 'round':
        round_(sys.argv[2], int(sys.argv[3]))
    elif cmd == 'update':
        update(sys.argv[2], int(sys.argv[3]))
