"""Generates the TabForge demo song "Ashen Meridian" (original composition, CC0).

Every riff, melody and drum pattern here was written from scratch for TabForge.
Run with the pyguitarpro venv:  work\\tools\\venv\\Scripts\\python samples\\make_demo_song.py
"""
import os
import guitarpro as gp

TITLE = 'Ashen Meridian'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), f'TabForge Demo - {TITLE}.gp5')

TEMPO_MAIN = 160
TEMPO_HEAVY = 130   # from the breakdown on

# Dynamics (GP5 velocities)
PP, P, MP, MF, F, FF = 31, 47, 63, 79, 95, 111

WHOLE = 3840
DUR = {  # name -> (value, dotted, tuplet)
    '1': (1, False, False), '2.': (2, True, False), '2': (2, False, False),
    '4.': (4, True, False), '4': (4, False, False), '8.': (8, True, False),
    '8': (8, False, False), '16': (16, False, False), '8t': (8, False, True),
}


def ticks(d):
    value, dotted, trip = DUR[d]
    t = WHOLE // value
    if dotted:
        t = t * 3 // 2
    if trip:
        t = t * 2 // 3
    return t


# ---------------------------------------------------------------- note DSL
def N(string, fret, *flags):
    return (string, fret, set(flags))


def B(dur, *notes, dyn=None, tempo=None):
    return {'dur': dur, 'notes': list(notes), 'dyn': dyn, 'tempo': tempo}


def R(dur):
    return {'dur': dur, 'notes': [], 'dyn': None, 'tempo': None}


def chord(root, *flags, strings=(6, 5, 4)):
    """Drop-C power chord: one finger across the three low strings."""
    return [N(s, root, *flags) for s in strings]


# ---------------------------------------------------------------- rhythm guitar
# strings: 1=D4 2=A3 3=F3 4=C3 5=G2 6=C2 (drop C)
def arp(shape, dyn=None):
    # the low root is not marked let ring: its "let ring" label would sit on the ledger-line note head
    return [B('8', N(s, f, *(() if s >= 5 else ('lr',))), dyn=dyn if i == 0 else None) for i, (s, f) in enumerate(shape)]


ARP_CM = [(6, 0), (4, 3), (3, 0), (2, 5), (1, 3), (2, 5), (3, 0), (4, 3)]
ARP_AB = [(5, 1), (4, 0), (3, 5), (2, 3), (1, 1), (2, 3), (3, 5), (4, 0)]
ARP_BB = [(5, 3), (4, 3), (3, 0), (2, 1), (1, 0), (2, 1), (3, 0), (4, 3)]


def pm(fret=0, s=6):
    return N(s, fret, 'pm')


def gallop(first):
    """8th + two palm-muted 16ths."""
    return [B('8', *first), B('16', pm()), B('16', pm())]


def verse_a(last, dyn=None):
    bar = gallop(chord(0, 'acc'))
    bar[0]['dyn'] = dyn
    bar += gallop([pm()])
    bar += gallop(chord(3, 'acc'))
    bar += last
    return bar


def verse_b(beat4, dead=False):
    return ([B('16', pm()) for _ in range(4)]
            + [B('8', *chord(1, 'acc')), B('8', N(6, 0, 'dead') if dead else pm())]
            + [B('8t', pm()) for _ in range(3)]
            + [B('4', *beat4)])


def chorus_bar(root, first_dyn=None):
    bar = []
    for i in range(8):
        if i in (2, 3, 6, 7):
            bar.append(B('8', pm(root) if root < 10 else N(5, 3, 'pm')))
        else:
            flags = ('acc',) if i == 0 else ()
            bar.append(B('8', *chord(root, *flags)))
    bar[0]['dyn'] = first_dyn
    return bar


def breakdown_a(hit, dyn=None, tempo=None):
    return [B('8', N(6, 0, 'acc'), dyn=dyn, tempo=tempo), B('16', pm()), R('16'),
            R('16'), B('16', pm()), B('8', pm()),
            B('4', *chord(hit, 'acc')),
            R('8'), B('8', pm())]


def breakdown_b():
    return [B('8', N(6, 0, 'acc')), B('16', pm()), R('16'),
            R('16'), B('16', pm()), B('8', pm()),
            B('16', pm()), B('16', pm()), B('16', pm()), B('16', pm()),
            B('8', N(6, 0, 'dead')), B('8', pm())]


rhythm = [
    # Intro (1-4)
    arp(ARP_CM, P), arp(ARP_AB), arp(ARP_BB, MP),
    [B('2', N(4, 12, 'nh', 'lr')), *[B('16', pm(), dyn=MF if i == 0 else None) for i in range(8)]],
    # Verse (5-12), 5-8 repeated
    verse_a([B('8', *chord(1, 'acc')), B('8', N(6, 0, 'dead'))], dyn=F),
    verse_a([B('4', *chord(5, 'acc'))]),
    verse_a([B('8', *chord(8, 'acc')), B('8', *chord(7, 'acc'))]),
    gallop(chord(0, 'acc')) + gallop([pm()])
    + [B('16', N(5, 0, 'pm')), B('16', N(4, 3, 'ham')), B('16', N(4, 5, 'pull')), B('16', N(4, 3)),
       B('8', N(5, 5, 'sl')), B('8', N(5, 7))],
    verse_b(chord(3)), verse_b(chord(5), dead=True), verse_b(chord(3)),
    [B('2', *chord(7, 'acc')), B('4', *chord(7, 'tie')), *[B('16', pm()) for _ in range(4)]],
    # Chorus (13-20): Ab Eb Bb Cm twice
    chorus_bar(8, FF), chorus_bar(3), chorus_bar(10), chorus_bar(0),
    chorus_bar(8), chorus_bar(3), chorus_bar(10),
    [B('2.', *chord(0, 'acc', 'vib')), R('4')],
    # Breakdown (21-28), tempo drop
    breakdown_a(1, FF, TEMPO_HEAVY), breakdown_b(), breakdown_a(1),
    [B('8', N(6, 0, 'acc')), B('8', pm()), B('2', N(5, 1, 'ph', 'vib')), R('8'), B('8', pm())],
    breakdown_a(3), breakdown_b(), breakdown_a(1),
    [B('1', *chord(0, 'acc'))],
    # Outro (29-34)
    arp(ARP_CM, MF), arp(ARP_AB), arp(ARP_BB), arp(ARP_CM, MP),
    [B('1', N(6, 0), N(5, 0), N(4, 0), N(3, 3), dyn=P)],
    [B('1', N(6, 0, 'tie'), N(5, 0, 'tie'), N(4, 0, 'tie'), N(3, 3, 'tie'))],
]

# ---------------------------------------------------------------- lead guitar
# harmonised thirds on the two top strings: (s1 fret, s2 fret)
C5, BB4, AB4, G4, F4, EB5, D5 = (10, 11), (8, 10), (6, 8), (5, 6), (3, 5), (13, 15), (12, 13)
C5_EB4 = (10, 6)


def ds(dur, pair, *flags, dyn=None):
    return B(dur, N(1, pair[0], *flags), N(2, pair[1], *flags), dyn=dyn)


bend_release = 'bendrel'
lead = [
    [R('1')], [R('1')],
    [B('2', N(1, 5, 'vib'), dyn=MP), B('4', N(1, 3)), B('4', N(1, 1))],
    [B('2', N(2, 3, 'vib')), R('2')],
    [R('1')], [R('1')], [R('1')], [R('1')],
    [R('1')], [R('1')],
    [B('4', N(1, 3), dyn=MF), B('4', N(1, 5)), B('4', N(1, 6)), B('4', N(1, 8))],
    [B('2', N(1, 10)), B('4', N(1, 10, 'tie')), R('4')],
    # Chorus
    [ds('4.', C5, dyn=F), ds('8', BB4), ds('4', AB4), ds('4', C5)],
    [ds('2', BB4, 'vib'), ds('4', G4), ds('4', BB4)],
    [ds('4.', D5), ds('8', C5), ds('4', BB4), ds('4', F4)],
    [ds('2', EB5), ds('4', D5), ds('4', C5)],
    [ds('4.', C5), ds('8', BB4), ds('4', AB4), ds('4', EB5)],
    [ds('2', D5, 'vib'), ds('4', BB4), ds('4', G4)],
    [B('2', N(1, 8, bend_release)), B('8', N(1, 6, 'ham')), B('8', N(1, 8)), B('8', N(1, 10, 'sl')), B('8', N(1, 12))],
    [ds('2', C5_EB4, 'vib'), ds('2', C5_EB4, 'tie')],
    # Breakdown
    [R('1')], [R('1')], [R('1')], [R('1')],
    [B('1', N(1, 13), dyn=MF)], [B('1', N(1, 13, 'tie'))], [B('1', N(1, 11, 'vib'))], [B('1', N(1, 10, 'vib'))],
    # Outro
    [B('2', N(1, 10), dyn=MP), B('2', N(1, 8))],
    [B('2', N(1, 6)), B('2', N(1, 10))],
    [B('2', N(1, 12)), B('2', N(1, 8))],
    [B('1', N(1, 10, 'vib'))],
    [R('1')], [R('1')],
]

# ---------------------------------------------------------------- bass (derived from the rhythm part)
# bass strings: 1=F2 2=C2 3=G1 4=C1
BASS_STR = {1: 41, 2: 36, 3: 31, 4: 24}
GTR_STR = {1: 62, 2: 57, 3: 53, 4: 48, 5: 43, 6: 36}


def bass_note(pitch, flags):
    pitch -= 12
    while pitch < 24:
        pitch += 12
    s = 4 if pitch - 24 <= 7 else 3
    keep = {f for f in flags if f in ('tie', 'dead', 'acc')}
    return N(s, pitch - BASS_STR[s], *keep)


def bass_from(bar):
    out = []
    for b in bar:
        if not b['notes']:
            out.append(R(b['dur']))
            continue
        low = max(b['notes'], key=lambda n: n[0])   # lowest string
        out.append(B(b['dur'], bass_note(GTR_STR[low[0]] + low[1], low[2]), dyn=b['dyn'], tempo=None))
    return out


def root(fret_on_c1, dur='1', dyn=None, *flags):
    return B(dur, bass_note(36 + fret_on_c1, set(flags)), dyn=dyn)


bass = [
    [root(0, '1', P)], [root(8)], [root(10, '1', MP)],
    [R('2'), *[root(0, '8', MF if i == 0 else None) for i in range(4)]],
]
bass += [bass_from(bar) for bar in rhythm[4:12]]
bass += [[root(r, '8', FF if (r == 8 and i == 0 and n == 0) else None) for i in range(8)]
         for n, r in enumerate([8, 3, 10, 0, 8, 3, 10])]
bass += [[root(0, '2.'), R('4')]]
bass += [bass_from(bar) for bar in rhythm[20:27]]
bass += [[root(0, '1')]]
bass += [[root(0, '1', MF)], [root(8)], [root(10)], [root(0, '1', MP)], [root(0, '1', P)], [root(0, '1', None, 'tie')]]

# ---------------------------------------------------------------- drums (16th grids)
KICK, SNARE, HH, OHH, RIDE, CRASH, CHINA = 36, 38, 42, 46, 51, 49, 52
HTOM, MTOM, LTOM, FTOM = 50, 47, 45, 43


def grid(dyn=None, **lanes):
    """lanes: name -> 16-char pattern ('x' hit). Names map to GM notes."""
    names = {'k': KICK, 's': SNARE, 'h': HH, 'o': OHH, 'r': RIDE, 'c': CRASH, 'ch': CHINA,
             't1': HTOM, 't2': MTOM, 't3': LTOM, 't4': FTOM}
    slots = [[] for _ in range(16)]
    for lane, pat in lanes.items():
        assert len(pat) == 16, (lane, pat)
        for i, c in enumerate(pat):
            if c == 'x':
                slots[i].append(names[lane])
    onsets = [i for i in range(16) if slots[i]]
    beats = []
    if not onsets or onsets[0] > 0:
        fill_rest(beats, onsets[0] if onsets else 16)
    for j, i in enumerate(onsets):
        length = (onsets[j + 1] if j + 1 < len(onsets) else 16) - i
        first = max(d for d in (1, 2, 3, 4, 6, 8, 12, 16) if d <= length and valid_at(i, d))
        beats.append(B(SIXTEENTHS[first], *[N(1 + k % 6, note) for k, note in enumerate(slots[i])],
                       dyn=dyn if j == 0 else None))
        fill_rest(beats, length - first, i + first)
    return beats


SIXTEENTHS = {1: '16', 2: '8', 3: '8.', 4: '4', 6: '4.', 8: '2', 12: '2.', 16: '1'}


def valid_at(pos, d):
    return d <= 4 or pos % 4 == 0 or d == 6 and pos % 2 == 0


def fill_rest(beats, length, pos=0):
    while length > 0:
        d = max(x for x in (1, 2, 4, 8) if x <= length and pos % x == 0)
        beats.append(R(SIXTEENTHS[d]))
        pos += d
        length -= d


GALLOP_K = 'x.xxx.xxx.xxx...'
drums = [
    grid(P, c='x...............', k='x.......x.......', r='x...x...x...x...'),
    grid(k='x.......x.......', r='x...x...x...x...'),
    grid(MP, k='x.......x.......', r='x...x...x...x...', s='............x...'),
    grid(MF, k='x...............', s='........xxxxxxxx'),
    # Verse
    grid(F, c='x...............', k=GALLOP_K, s='....x.......x...', h='..x.x.x.x.x.x.x.'),
    grid(k=GALLOP_K, s='....x.......x...', h='x.x.x.x.x.x.x.x.'),
    grid(k=GALLOP_K, s='....x.......x...', h='x.x.x.x.x.x.x.x.'),
    grid(k='x.xxx.xx........', s='....x...xxxx....', h='x.x.x.x.........', t2='............xx..', t4='..............xx'),
    grid(c='x...............', k='xxxxx.x.x.x.x...', s='....x.......x...', r='..x.x.x.x.x.x.x.'),
    grid(k='xxxxx.x.x.x.x...', s='....x.......x...', r='x.x.x.x.x.x.x.x.'),
    grid(k='xxxxx.x.x.x.x...', s='....x.......x...', r='x.x.x.x.x.x.x.x.'),
    grid(k='x.......x...xxxx', s='....x.......xxxx', c='x...............'),
    # Chorus: double bass
    grid(FF, c='x...............', k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='..x.x.x.x.x.x.x.'),
    grid(k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='x.x.x.x.x.x.x.x.'),
    grid(k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='x.x.x.x.x.x.x.x.'),
    grid(k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='x.x.x.x.x.x.x.x.', c='............x...'),
    grid(c='x...............', k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='..x.x.x.x.x.x.x.'),
    grid(k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='x.x.x.x.x.x.x.x.'),
    grid(k='xxxxxxxxxxxxxxxx', s='....x.......x...', r='x.x.x.x.x.x.x.x.'),
    grid(c='x...............', k='x.......x.......', s='....x...xx......', t1='..........xx....', t3='............xx..', t4='..............xx'),
    # Breakdown: half-time
    grid(FF, c='x...............', k='xx...xx.x.....x.', s='........x.......', ch='....x.......x...'),
    grid(k='xx...xx.xxxxx.x.', s='........x.......', ch='x...x...x...x...'),
    grid(k='xx...xx.x.....x.', s='........x.......', ch='x...x...x...x...'),
    grid(k='xx..x.......x.x.', s='........x.......', ch='x...x...x...x...'),
    grid(c='x...............', k='xx...xx.x.....x.', s='........x.......', ch='....x...x...x...'),
    grid(k='xx...xx.xxxxx.x.', s='........x.......', ch='x...x...x...x...'),
    grid(k='xx...xx.x.....x.', s='........x.......', ch='x...x...x...x...'),
    grid(c='x...............', k='x...............'),
    # Outro
    grid(MF, c='x...............', k='x.....x.x.......', s='........x.......', r='x...x...x...x...'),
    grid(k='x.....x.x.......', s='........x.......', r='x...x...x...x...'),
    grid(k='x.....x.x.......', s='........x.......', r='x...x...x...x...'),
    grid(MP, k='x.....x.x.......', s='........x...x.xx', r='x...x...x.......'),
    grid(P, c='x...............', k='x...............'),
    grid(),
]

MARKERS = {1: 'Intro', 5: 'Verse', 13: 'Chorus', 21: 'Breakdown', 29: 'Outro'}
MARKER_COLORS = {'Intro': (80, 140, 220), 'Verse': (90, 180, 110), 'Chorus': (230, 150, 40),
                 'Breakdown': (210, 60, 60), 'Outro': (150, 110, 200)}


# ---------------------------------------------------------------- build the song
def make_song():
    bars = len(rhythm)
    for name, part in (('lead', lead), ('bass', bass), ('drums', drums)):
        assert len(part) == bars, (name, len(part), bars)

    song = gp.Song()
    song.title = TITLE
    song.artist = 'TabForge Demo'
    song.album = 'Examples'
    song.music = 'TabForge contributors'
    song.copyright = '(c) 2026 TabForge contributors, CC0'
    song.tab = 'TabForge'
    song.instructions = 'Original composition written for TabForge. Public domain (CC0 1.0).'
    song.tempo = TEMPO_MAIN
    song.tempoName = 'Allegro'
    song.hideTempo = False
    song.key = gp.KeySignature.CMinor

    song.measureHeaders = []
    start = gp.Duration.quarterTime
    for i in range(bars):
        h = gp.MeasureHeader(number=i + 1, start=start, keySignature=gp.KeySignature.CMinor)
        h.timeSignature = gp.TimeSignature(numerator=4, denominator=gp.Duration(value=4), beams=[2, 2, 2, 2])
        if i + 1 in MARKERS:
            title = MARKERS[i + 1]
            h.marker = gp.Marker(title=title, color=gp.Color(*MARKER_COLORS[title]))
        if i + 1 == 5:
            h.isRepeatOpen = True
        if i + 1 == 8:
            h.repeatClose = 1          # play bars 5-8 twice
        song.measureHeaders.append(h)
        start += WHOLE
    song.measureHeaders[3].hasDoubleBar = True     # end of intro
    song.measureHeaders[19].hasDoubleBar = True    # end of chorus

    song.tracks = []
    specs = [
        # name, part, strings, channel, fx channel, program, volume, pan, colour
        ('Rhythm Guitar', rhythm, [62, 57, 53, 48, 43, 36], 0, 1, 30, 100, 32, (220, 70, 60)),
        ('Lead Guitar', lead, [62, 57, 53, 48, 43, 36], 2, 3, 30, 92, 96, (240, 170, 40)),
        ('Bass', bass, [41, 36, 31, 24], 4, 5, 33, 108, 64, (60, 120, 220)),
        ('Drums', drums, [0, 0, 0, 0, 0, 0], 9, 9, 0, 104, 64, (120, 120, 120)),
    ]
    for number, (name, part, strings, ch, fx, prog, vol, pan, color) in enumerate(specs, start=1):
        track = gp.Track(song, number=number, name=name)
        track.strings = [gp.GuitarString(k + 1, v) for k, v in enumerate(strings)]
        track.isPercussionTrack = ch == 9
        track.channel = gp.MidiChannel(channel=ch, effectChannel=fx, instrument=prog, volume=vol,
                                       balance=pan, reverb=24 if ch != 9 else 16)
        track.color = gp.Color(*color)
        track.fretCount = 24
        track.measures = []
        velocity = F
        for bar_index, (header, bar) in enumerate(zip(song.measureHeaders, part)):
            total = sum(ticks(b['dur']) for b in bar)
            assert total == WHOLE, f'{name} bar {bar_index + 1}: {total} ticks'
            measure = gp.Measure(track, header)
            voice = measure.voices[0]
            for b in bar:
                if b['dyn'] is not None:
                    velocity = b['dyn']
                beat = gp.Beat(voice)
                value, dotted, trip = DUR[b['dur']]
                beat.duration = gp.Duration(value=value, isDotted=dotted,
                                            tuplet=gp.Tuplet(3, 2) if trip else gp.Tuplet(1, 1))
                beat.status = gp.BeatStatus.normal if b['notes'] else gp.BeatStatus.rest
                if b['tempo'] is not None:
                    beat.effect.mixTableChange = gp.MixTableChange(
                        tempo=gp.MixTableItem(value=b['tempo'], duration=0), tempoName='Heavy', hideTempo=False)
                for s, fret, flags in b['notes']:
                    note = gp.Note(beat, value=fret, velocity=velocity, string=s)
                    note.type = (gp.NoteType.tie if 'tie' in flags else
                                 gp.NoteType.dead if 'dead' in flags else gp.NoteType.normal)
                    e = note.effect
                    e.palmMute = 'pm' in flags
                    e.accentuatedNote = 'acc' in flags
                    e.letRing = 'lr' in flags
                    e.vibrato = 'vib' in flags
                    e.hammer = 'ham' in flags or 'pull' in flags
                    if 'sl' in flags:
                        e.slides = [gp.SlideType.shiftSlideTo]
                    if 'nh' in flags:
                        e.harmonic = gp.NaturalHarmonic()
                    if 'ph' in flags:
                        e.harmonic = gp.PinchHarmonic()
                    if 'bendrel' in flags:
                        e.bend = gp.BendEffect(type=gp.BendType.bendRelease, value=4, points=[
                            gp.BendPoint(0, 0), gp.BendPoint(3, 4), gp.BendPoint(6, 4),
                            gp.BendPoint(9, 0), gp.BendPoint(12, 0)])
                    beat.notes.append(note)
                voice.beats.append(beat)
            # second voice: a single empty beat, as Guitar Pro writes it
            empty = gp.Beat(measure.voices[1])
            empty.status = gp.BeatStatus.empty
            measure.voices[1].beats.append(empty)
            track.measures.append(measure)
        song.tracks.append(track)
    return song


if __name__ == '__main__':
    song = make_song()
    gp.write(song, OUT, version=(5, 1, 0))
    back = gp.parse(OUT)
    print(f'wrote {OUT}: {len(back.tracks)} tracks, {len(back.measureHeaders)} bars, tempo {back.tempo}')
