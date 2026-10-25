"""Online judge for the GP5 driver: tells, after each GP5 snapshot, whether GP5 and TabForge already differ.

Started once per worker: python judge.py <run dir>. Reads lines "<id> <step> <gp5 file>" on stdin and answers "same" or "diff"
(or "unknown" when TabForge's replay of that sequence is missing). The driver stops a sequence at its first difference: later
actions cannot change which action made the apps differ, and skipping them is most of the run time.
"""
import json, os, sys

# The TabForge replay folder to compare (GP5DIFF_TF, default tf): a newer TabForge build replays into its own folder.
TF = os.environ.get('GP5DIFF_TF', 'tf')

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import compare, gpdump

run = sys.argv[1]
cache = {}
for line in sys.stdin:
    try:
        sid, step, path = line.rstrip('\n').split(' ', 2)
        if sid not in cache:
            p = os.path.join(run, TF, sid + '.json')
            cache.clear()
            cache[sid] = json.load(open(p)).get('steps') if os.path.exists(p) else None
        steps = cache[sid]
        i = int(step)
        if not steps or i >= len(steps) or 'bars' not in steps[i]:
            answer = 'unknown'
        else:
            answer = 'diff' if compare.compare(steps[i], gpdump.dump(path)) else 'same'
    except Exception as ex:  # a file pyguitarpro cannot read, a bad line: the driver keeps going as without a judge
        answer = 'unknown'
    sys.stdout.write(answer + '\n')
    sys.stdout.flush()
