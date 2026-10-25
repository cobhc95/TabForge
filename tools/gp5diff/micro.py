"""Hand-written minimal repros for the main clusters: python micro.py <run dir>  (writes <run>/seq/m*.json and actions.json).
Each is run in both apps like any sequence; show their results with: python micro.py <run dir> --show"""
import json, os, shutil, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
HERE = os.path.dirname(os.path.abspath(__file__))


def f(n):
    return {'a': 'fret', 'n': n}


def a(name):
    return {'a': name}


MICRO = {
    'm01_right_twice_after_note': [f(5), a('right'), a('right'), f(7)],
    'm02_right_after_full_bar': [f(5), a('right'), f(6), a('right'), f(7), a('right'), f(8), a('right'), f(9)],
    'm03_right_after_whole_note': [a('longer'), a('longer'), f(5), a('right'), f(7)],
    'm04_right_in_overfull_bar': [a('longer'), f(5), a('right'), a('longer'), f(6), a('right'), f(7)],
    'm05_right_on_new_song': [a('right'), f(5)],
    'm06_undo_after_right': [f(5), a('right'), f(7), a('right'), a('undo')],
    'm07_tie_on_empty_beat': [a('tie')],
    'm08_tie_on_beat_after_note': [f(5), a('right'), a('tie')],
    'm09_rest_then_longer': [a('rest'), a('longer')],
    'm10_cut_one_beat': [f(5), a('right'), f(7), a('right'), f(9), a('home'), a('sel_right'), a('cut')],
    'm11_insert_bar_undo': [a('insert_bar'), a('undo')],
    'm12_string_down_on_empty': [a('note_string_down'), f(3)],
    'm13_accent_on_empty_then_fret': [a('accent'), f(5)],
    'm14_dot_undo_fret': [a('dot'), a('undo'), f(1)],
    'm15_longer_insert_bar_fret': [a('longer'), a('insert_bar'), f(5)],
    'm16_key_c': [f(5), a('right'), f(3), a('key_c')],
    'm17_paste_at_end': [f(5), a('right'), f(3), a('home'), a('sel_right'), a('copy'), a('end'), a('right'), a('paste')],
    'm18_grace_on_chord_string2': [f(5), a('down'), f(3), a('grace')],
    'm19_dot_on_rest': [f(5), a('right'), a('rest'), a('dot')],
    'm20_next_bar_on_new_song': [a('next_bar'), f(5)],
    'm21_cut_whole_bar_selection': [a('insert_bar'), a('home'), a('sel_right'), a('cut')],
    'm22_fret_then_fret_same_spot': [f(1), f(3)],
    'm23_undo_after_fret': [f(7), a('undo')],
    'm24_delete_beat': [f(5), a('right'), f(7), a('right'), f(9), a('home'), a('delete_beat')],
    'm25_backspace': [f(5), a('right'), f(7), a('backspace')],
    'm26_insert_beat': [f(5), a('right'), f(7), a('home'), a('insert_beat')],
    'm27_triplet_undo_fret': [a('triplet'), a('undo'), f(2)],
    'm28_select_all_vibrato': [f(1), a('right'), f(2), a('select_all'), a('vibrato')],
    'm29_rest_on_empty_bar': [a('rest')],
    'm30_shorter_on_new_song': [a('shorter'), f(5), a('right'), f(7)],
}


def bars_text(state):
    import compare
    return [compare.bar_text(b) for b in state.get('bars', [])]


if __name__ == '__main__':
    run = sys.argv[1]
    if '--show' in sys.argv:
        import gpdump
        tfdir = os.environ.get('GP5DIFF_TF', 'tf')
        for sid, acts in MICRO.items():
            tf = json.load(open(os.path.join(run, tfdir, sid + '.json')))
            print(f"## {sid}: {' '.join(x['a'] + str(x.get('n', '')) for x in acts)}")
            steps = os.path.join(run, 'steps', sid)
            if os.path.isdir(steps):
                for i, name in enumerate(sorted(os.listdir(steps))):
                    g = gpdump.dump(os.path.join(steps, name))
                    print(f"  {i}: GP5 {bars_text(g)}  |  TF {bars_text(tf['steps'][int(name[:3])])}  cursor {tf['steps'][int(name[:3])].get('cursor')}")
        sys.exit()
    os.makedirs(os.path.join(run, 'seq'), exist_ok=True)
    shutil.copy(os.path.join(HERE, 'actions.json'), os.path.join(run, 'actions.json'))
    for sid, acts in MICRO.items():
        json.dump({'id': sid, 'seed': -1, 'actions': acts}, open(os.path.join(run, 'seq', sid + '.json'), 'w'))
    print(len(MICRO))
