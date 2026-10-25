"""Prints clusters (clusters.json from cluster.py) with their repro: python show.py <clusters.json> [first] [count]"""
import json, sys

c = json.load(open(sys.argv[1]))
first = int(sys.argv[2]) if len(sys.argv) > 2 else 0
count = int(sys.argv[3]) if len(sys.argv) > 3 else 20
if '--local' in sys.argv:
    for i, x in enumerate(c['local'][first:first + count], first):
        print(f"L{i} [{x['count']} in {x['sequences']} seq] {x['signature']}  ({x['repro_id']}@{len(x['repro_actions']) - 1}, bar {x['bar']})")
        print('   last actions:', ' '.join(a['a'] + str(a.get('n', '')) for a in x['repro_actions'][-7:]))
        print('   bar before:', x['before'], '| after GP5:', x['after_gp5'], '| after TF:', x['after_tf'], '| TF cursor', x['tf_cursor_before'], x['tf_log'][:1])
    sys.exit()
for i, x in enumerate(c['clusters'][first:first + count], first):
    print(f"#{i} [{x['count']}] {x['signature']}   ({x['repro_id']}, others {', '.join(x['examples'][1:4])})")
    print('   actions:', ' '.join(a['a'] + (str(a['n']) if 'n' in a else '') for a in x['repro_actions']))
    print('   before  GP5', x['before_gp5'], '| TF', x['before_tf'], 'TF cursor', x['tf_cursor_before'])
    print('   after   GP5', x['after_gp5'], '| TF', x['after_tf'])
    if x.get('tf_log'):
        print('   TF log', x['tf_log'][:2])
