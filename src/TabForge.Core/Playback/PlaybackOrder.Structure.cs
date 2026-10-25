using TabForge.Models;

namespace TabForge.Playback;

// Owns: jump/mark types and bar structure.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public static partial class PlaybackOrder
{
    private enum JumpKind { DaCapo, DalSegno, DalSegnoSegno }

    private readonly record struct Jump(JumpKind Kind, bool AlCoda);

    [Flags]
    private enum Mark
    {
        None = 0,
        Segno = 1,
        SegnoSegno = 2,
        Coda = 4,
        Fine = 8,
        ToCoda = 16,
    }

    /// <summary>
    /// The structural facts of every bar: opens, closes and their pass counts, endings (with each ending
    /// bracket's reach), destinations and jumps. Bars past the first track are plain.
    /// </summary>
    private sealed class BarStructure
    {
        private static readonly Jump[] NoJumps = Array.Empty<Jump>();

        private readonly bool[] _open;
        private readonly bool[] _close;
        private readonly int[] _passes;
        private readonly int[] _ownEnding;
        private readonly int[] _ending;
        private readonly int[] _bracketEnd;
        private readonly bool[] _leadingEnding;
        private readonly bool[] _droppedAfterJump;
        private readonly Mark[] _marks;
        private readonly Jump[][] _jumps;

        public int BarCount { get; }

        private BarStructure(int barCount)
        {
            BarCount = barCount;
            _open = new bool[barCount];
            _close = new bool[barCount];
            _passes = new int[barCount];
            _ownEnding = new int[barCount];
            _ending = new int[barCount];
            _bracketEnd = new int[barCount];
            _leadingEnding = new bool[barCount];
            _droppedAfterJump = new bool[barCount];
            _marks = new Mark[barCount];
            _jumps = new Jump[barCount][];
        }

        public static int CountBars(SongProject project) =>
            project?.Tracks is { Count: > 0 } tracks ? tracks.Max(t => t?.Measures?.Count ?? 0) : 0;

        public static BarStructure Read(SongProject project)
        {
            var score = new BarStructure(CountBars(project));
            var measures = project?.Tracks is { Count: > 0 } tracks ? tracks[0]?.Measures : null;
            var structured = Math.Min(measures?.Count ?? 0, score.BarCount);

            for (var bar = 0; bar < score.BarCount; bar++)
            {
                score._bracketEnd[bar] = bar;
                score._jumps[bar] = NoJumps;
                if (bar >= structured || measures![bar] is not { } m) continue;

                score._open[bar] = m.RepeatStart;
                score._close[bar] = m.RepeatEnd;
                score._passes[bar] = Math.Clamp(m.RepeatCount, 2, MaxRepeats);
                score._ownEnding[bar] = m.EndingPasses & 0xFF;
                ReadDirections(score, bar, m.Directions);
            }

            score.MarkLeadingEndings();
            score.SpanEndingBrackets();
            score.MarkEndingsDroppedAfterJump();
            return score;
        }

        private static void ReadDirections(BarStructure score, int bar, string? directions)
        {
            List<Jump>? jumps = null;
            foreach (var name in DirectionTokens(directions))
            {
                if (Is(name, "Segno")) score._marks[bar] |= Mark.Segno;
                else if (Is(name, "SegnoSegno")) score._marks[bar] |= Mark.SegnoSegno;
                else if (Is(name, "Coda") || Is(name, "DoubleCoda")) score._marks[bar] |= Mark.Coda;
                else if (Is(name, "Fine")) score._marks[bar] |= Mark.Fine;
                else if (Is(name, "ToCoda") || Is(name, "ToDoubleCoda")) score._marks[bar] |= Mark.ToCoda;
                else if (Is(name, "DaCapo")) (jumps ??= new()).Add(new Jump(JumpKind.DaCapo, false));
                else if (Is(name, "DaCapoAlCoda")) (jumps ??= new()).Add(new Jump(JumpKind.DaCapo, true));
                else if (Is(name, "DalSegno")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegno, false));
                else if (Is(name, "DalSegnoAlCoda")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegno, true));
                else if (Is(name, "DalSegnoSegno")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegnoSegno, false));
                else if (Is(name, "DalSegnoSegnoAlCoda")) (jumps ??= new()).Add(new Jump(JumpKind.DalSegnoSegno, true));
            }
            if (jumps is not null) score._jumps[bar] = jumps.ToArray();
        }

        private static bool Is(string name, string expected) => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// An ending bar that also opens a repeat, placed on the first bar or directly after a close, is the
        /// ending of the repeat before it.
        /// </summary>
        private void MarkLeadingEndings()
        {
            for (var bar = 0; bar < BarCount; bar++)
                _leadingEnding[bar] = _ownEnding[bar] != 0 && _open[bar] && (bar == 0 || _close[bar - 1]);
        }

        /// <summary>
        /// From an ending bar, looking forward: when a close comes before any other ending bar or open bar, the
        /// bracket covers every bar through that close, and the unmarked bars inside take its passes. Otherwise
        /// the bracket is the marked bar alone. A leading ending never reaches into its own repeat.
        /// </summary>
        private void SpanEndingBrackets()
        {
            for (var bar = 0; bar < BarCount; bar++)
            {
                if (_ownEnding[bar] == 0) continue;
                _ending[bar] = _ownEnding[bar];
                if (_leadingEnding[bar] || _close[bar]) continue;

                for (var next = bar + 1; next < BarCount; next++)
                {
                    if (_open[next] || _ownEnding[next] != 0) break;
                    if (!_close[next]) continue;
                    for (var inside = bar + 1; inside <= next; inside++) _ending[inside] = _ownEnding[bar];
                    _bracketEnd[bar] = next;
                    break;
                }
            }
        }

        /// <summary>True when an ending bar comes directly after the bar's bracket.</summary>
        private bool EndingFollows(int bar)
        {
            var next = _bracketEnd[bar] + 1;
            return next < BarCount && _ownEnding[next] != 0;
        }

        /// <summary>
        /// After a D.C./D.S., only a repeat's last ending plays: an ending bracket is left out when a later
        /// ending of the same repeat (the endings that follow it directly) names a higher pass, even when that
        /// later ending's bar also opens the next repeat.
        /// </summary>
        private void MarkEndingsDroppedAfterJump()
        {
            for (var bar = 0; bar < BarCount; bar++)
            {
                if (_ownEnding[bar] == 0) continue;
                var highest = HighestPass(_ownEnding[bar]);
                var dropped = false;
                for (var next = _bracketEnd[bar] + 1; next < BarCount && _ownEnding[next] != 0; next = _bracketEnd[next] + 1)
                {
                    if (HighestPass(_ownEnding[next]) > highest) { dropped = true; break; }
                    if (_open[next]) break;
                }
                if (!dropped) continue;
                for (var inside = bar; inside <= _bracketEnd[bar]; inside++) _droppedAfterJump[inside] = true;
            }
        }

        private static int HighestPass(int mask)
        {
            var highest = 0;
            for (var pass = 1; pass <= EndingPassCount; pass++)
                if ((mask & (1 << (pass - 1))) != 0) highest = pass;
            return highest;
        }

        public bool Open(int bar) => _open[bar];
        public bool Close(int bar) => _close[bar];
        public int Passes(int bar) => _passes[bar];
        /// <summary>Passes the bar plays on (its own ending or its bracket's), bit n = pass n+1; 0 = every pass.</summary>
        public int Ending(int bar) => _ending[bar];
        public bool LeadingEnding(int bar) => _leadingEnding[bar];
        public bool DroppedAfterJump(int bar) => _droppedAfterJump[bar];
        public bool Has(int bar, Mark mark) => (_marks[bar] & mark) != 0;
        public IReadOnlyList<Jump> Jumps(int bar) => _jumps[bar];

        /// <summary>
        /// The passes of the repeat a close with an ending ends: the close's count or the highest ending of its bracket and of the
        /// endings that follow it directly, whichever is larger. Playback never goes back from a close on the last pass.
        /// When no ending follows the close, and the close is on an ending bar of its own or its bracket names pass 1, the
        /// music after the close is reached one pass after that ending's highest pass (TuxGuitar, aces_high_2.gp3, genghis_khan.gp3).
        /// </summary>
        public int TotalPasses(int bar)
        {
            var total = Math.Max(_passes[bar], HighestPass(_ending[bar]));
            for (var next = _bracketEnd[bar] + 1; next < BarCount && _ownEnding[next] != 0; next = _bracketEnd[next] + 1)
            {
                total = Math.Max(total, HighestPass(_ownEnding[next]));
                if (_open[next]) break;
            }
            var lastEnding = (_ownEnding[bar] != 0 || (_ending[bar] & 1) != 0) && !EndingFollows(bar);
            return lastEnding ? Math.Max(total, HighestPass(_ending[bar]) + 1) : total;
        }

        /// <summary>True when another close follows this one before the next open bar.</summary>
        public bool CloseFollowsBeforeNextOpen(int bar)
        {
            for (var next = bar + 1; next < BarCount; next++)
            {
                if (_open[next]) return false;
                if (_close[next]) return true;
            }
            return false;
        }

        /// <summary>The first coda (or double coda) after the bar; when there is none, the first one in the song; -1 when there is none at all.</summary>
        public int NearestCoda(int bar)
        {
            for (var next = bar + 1; next < BarCount; next++)
                if (Has(next, Mark.Coda)) return next;
            for (var from = 0; from <= bar && from < BarCount; from++)
                if (Has(from, Mark.Coda)) return from;
            return -1;
        }
    }
}
