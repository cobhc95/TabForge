"""Prints a .gp5 file's first track bar by bar with pyguitarpro (an independent reader, to check TabForge's importer).
Usage: python dumpgp.py file.gp5 [more files]"""
import sys
import guitarpro as gp

def note_text(n):
    e = n.effect
    flags = []
    if n.type == gp.NoteType.tie: flags.append('tie')
    if n.type == gp.NoteType.dead: flags.append('dead')
    if n.effect.ghostNote: flags.append('ghost')
    if e.palmMute: flags.append('pm')
    if e.letRing: flags.append('ring')
    if e.vibrato: flags.append('vib')
    if e.hammer: flags.append('ho')
    if e.slides: flags.append('slide' + ','.join(str(s.value) for s in e.slides))
    if e.bend: flags.append('bend')
    if e.accentuatedNote: flags.append('acc')
    if e.heavyAccentuatedNote: flags.append('hacc')
    if e.harmonic: flags.append('harm')
    if e.staccato: flags.append('stacc')
    if e.grace: flags.append('grace')
    return f"{n.value}@{n.string}" + ('[' + ' '.join(flags) + ']' if flags else '')

for path in sys.argv[1:]:
    song = gp.parse(path)
    t = song.tracks[0]
    print(path, 'tempo', song.tempo, 'bars', len(t.measures), 'strings', [s.value for s in t.strings])
    for mi, m in enumerate(t.measures):
        h = m.header
        beats = []
        for v, voice in enumerate(m.voices):
            for b in voice.beats:
                d = b.duration
                dur = f"/{d.value}" + '.' * (1 if d.isDotted else 0) + ('.' if getattr(d, 'isDoubleDotted', False) else '') + (f"t{d.tuplet.enters}:{d.tuplet.times}" if d.tuplet.enters != 1 else '')
                notes = ','.join(note_text(n) for n in b.notes) if b.notes else ('r' if b.status != gp.BeatStatus.empty else 'empty')
                beats.append(f"v{v}:{notes}{dur}")
        print(f"  bar {mi + 1} {h.timeSignature.numerator}/{h.timeSignature.denominator.value}: " + ' '.join(beats))
