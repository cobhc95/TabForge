"""Reproducibility check: re-runs examples of each cluster in a quiet GP5 and says whether the same difference comes back.

  python verify.py pick <clusters.json> <verify run> [per cluster] [top]   copy examples (first and later divergences) with their ids
  python verify.py check <clusters.json> <verify run>                      after both apps ran: reproduced / total per cluster
The verify run is compared with the same TabForge replay folder (GP5DIFF_TF) as the clusters; a cluster counts as reproducible
when its re-run examples show the same signature at the same action.
"""
import json, os, shutil, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cluster

TF = os.environ.get('GP5DIFF_TF', 'tf')
DIFF = 'diff' if TF == 'tf' else 'diff_' + TF
HERE = os.path.dirname(os.path.abspath(__file__))


def pick(path, out, per, top):
    data = json.load(open(path))
    os.makedirs(os.path.join(out, 'seq'), exist_ok=True)
    shutil.copy(os.path.join(HERE, 'actions.json'), os.path.join(out, 'actions.json'))
    plan = []
    for kind in ('clusters', 'local'):
        for c in data[kind][:top]:
            ids = [e.split('@')[0] for e in c['examples']][:per]
            for sid in ids:
                for run in json.loads(os.environ.get('GP5DIFF_RUNS', '[]')) or [os.path.dirname(path)]:
                    src = os.path.join(run, 'seq', sid + '.json')
                    if os.path.exists(src):
                        shutil.copy(src, os.path.join(out, 'seq', sid + '.json'))
                        os.makedirs(os.path.join(out, TF), exist_ok=True)
                        shutil.copy(os.path.join(run, TF, sid + '.json'), os.path.join(out, TF, sid + '.json'))
                        plan.append({'kind': kind, 'signature': c['signature'], 'id': sid})
                        break
    json.dump(plan, open(os.path.join(out, 'plan.json'), 'w'), indent=1)
    print(len(plan), 'examples,', len({p['id'] for p in plan}), 'sequences')


def signatures(d, seq):
    sig = set()
    f = d.get('first')
    if f:
        kinds = sorted({cluster.detail(x) for x in f['diffs']})
        sig.add(('clusters', f"{f['action']['a']} -> {kinds[0]}" + ('' if f.get('window', 1) == 1 else '  [in a window of actions]')))
    for loc in d.get('local', []):
        kinds = sorted({cluster.detail(x) for x in loc['diffs']})
        sig.add(('local', f"{loc['action']['a']} -> {kinds[0]}"))
    return sig


def check(path, out):
    plan = json.load(open(os.path.join(out, 'plan.json')))
    result = {}
    for p in plan:
        key = (p['kind'], p['signature'])
        r = result.setdefault(key, [0, 0])
        dpath = os.path.join(out, DIFF, p['id'] + '.json')
        if not os.path.exists(dpath):
            continue
        d = json.load(open(dpath))
        seq = json.load(open(os.path.join(out, 'seq', p['id'] + '.json')))
        r[1] += 1
        if key in signatures(d, seq):
            r[0] += 1
    rows = [{'kind': k[0], 'signature': k[1], 'reproduced': v[0], 'rerun': v[1]} for k, v in result.items()]
    json.dump(rows, open(os.path.join(out, 'reproducibility.json'), 'w'), indent=1)
    for r in rows:
        print(f"{r['reproduced']}/{r['rerun']}  [{r['kind']}] {r['signature']}")


if __name__ == '__main__':
    if sys.argv[1] == 'pick':
        pick(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 3, int(sys.argv[5]) if len(sys.argv) > 5 else 40)
    else:
        check(sys.argv[2], sys.argv[3])
