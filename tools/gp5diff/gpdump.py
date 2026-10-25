"""Second reading of GP5's files, independent of TabForge's importer: pyguitarpro -> the dump shape of SelfTestGp5Diff.GdDump.

Usage: python gpdump.py <run dir>    (writes <run>/gp5py/<id>.json for every <run>/out/<id>.gp5 not yet read)
The comparator uses it to tell an editing difference from an importer difference: a difference that disappears against this
reading is marked "importer suspect".
"""
import json, os, sys
import guitarpro as gp

SLIDE = {1: 't:ShiftSlide', 2: 't:LegatoSlide'}


def note_fx(n):
    e = n.effect
    x = set()
    if n.type == gp.NoteType.tie: x.add('tie')
    if n.type == gp.NoteType.dead: x.add('dead')
    if e.ghostNote: x.add('ghost')
    if e.palmMute: x.add('t:PalmMute')
    if e.letRing: x.add('t:LetRing')
    if e.vibrato: x.add('t:Vibrato')
    if e.hammer: x.add('t:HOPO')
    for s in e.slides or []:
        x.add(SLIDE.get(s.value, f't:Slide{s.value}'))
    if e.bend: x.add('bend')
    if e.harmonic: x.add('harmonic')
    if e.grace: x.add('grace_note')
    return sorted(x)


def dump(path):
    song = gp.parse(path)
    t = song.tracks[0]
    bars = []
    for m in t.measures:
        ts = m.header.timeSignature
        beats = []
        for b in m.voices[0].beats:
            if b.status == gp.BeatStatus.empty:
                continue
            d = b.duration
            bx = set()
            for n in b.notes:
                if n.effect.accentuatedNote: bx.add('accent')
                if n.effect.heavyAccentuatedNote: bx.add('heavy_accent')
                if n.effect.staccato: bx.add('staccato')
            notes = [{'s': n.string, 'f': n.value, 'x': note_fx(n)} for n in sorted(b.notes, key=lambda n: n.string)]
            tup = f'{d.tuplet.enters}:{d.tuplet.times}' if d.tuplet.enters != 1 else ''
            beats.append({'d': d.value, 'dots': 1 if d.isDotted else 0, 'tup': tup, 'rest': not notes, 'notes': notes, 'bx': sorted(bx)})
        bars.append({'ts': f'{ts.numerator}/{ts.denominator.value}', 'beats': beats})
    # Notes in voice 2: the sequences never select it, so GP5 took a digit as Ctrl+digit (Edit voice 2) - a harness artifact.
    voice2 = any(n for m in t.measures if len(m.voices) > 1 for b in m.voices[1].beats for n in b.notes)
    return {'bars': bars, 'tempo': song.tempo, 'voice2': voice2}


def main():
    run = sys.argv[1]
    os.makedirs(os.path.join(run, 'gp5py'), exist_ok=True)
    for name in sorted(os.listdir(os.path.join(run, 'out'))):
        if not name.endswith('.gp5'):
            continue
        dst = os.path.join(run, 'gp5py', name[:-4] + '.json')
        if os.path.exists(dst):
            continue
        try:
            d = dump(os.path.join(run, 'out', name))
        except Exception as ex:  # a file pyguitarpro cannot read is reported, not fatal
            d = {'error': repr(ex)}
        json.dump(d, open(dst, 'w'))


if __name__ == '__main__':
    main()
