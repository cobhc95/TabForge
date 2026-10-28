using System.Numerics;

namespace TabForge.AudioEngine.Builtin;

// Owns: one uniformly-partitioned overlap-save stage of the convolver (partition N, FFT 2N, a frequency-domain delay line of K spectra), split from PartitionedConvolver.cs.
// Does not own: the direct head or the order the stages run in (PartitionedConvolver).
// Tests: BuiltinEffectTests.
public sealed partial class PartitionedConvolver
{
    /// <summary>Taps [start, end) in partitions of N; start is a multiple of N and at least N, so each output block is ready before it is due.</summary>
    private sealed class Stage
    {
        private readonly int _n, _bins, _count;
        private readonly Fft _fft;
        private readonly float[] _hRe, _hIm;          // K spectra of N+1 bins (the rest is the mirror image)
        private readonly float[] _xRe, _xIm;          // the delay line: the last K input spectra
        private readonly float[] _accRe, _accIm, _re, _im;
        private readonly float[] _prev, _cur, _ring;  // the ring holds start / N output blocks
        private int _curPos, _line, _ringPos;

        public Stage(ReadOnlySpan<float> ir, int n, int start, int end)
        {
            _n = n;
            _bins = n + 1;
            _count = (end - start + n - 1) / n;
            _fft = new Fft(2 * n);
            _hRe = new float[_count * _bins]; _hIm = new float[_count * _bins];
            _xRe = new float[_count * _bins]; _xIm = new float[_count * _bins];
            _accRe = new float[_bins]; _accIm = new float[_bins];
            _re = new float[2 * n]; _im = new float[2 * n];
            _prev = new float[n]; _cur = new float[n];
            _ring = new float[start / n * n];
            for (var k = 0; k < _count; k++)
            {
                Array.Clear(_re); Array.Clear(_im);
                var from = start + k * n;
                for (var i = 0; i < n && from + i < end; i++) _re[i] = ir[from + i];
                _fft.Forward(_re, _im);
                Array.Copy(_re, 0, _hRe, k * _bins, _bins);
                Array.Copy(_im, 0, _hIm, k * _bins, _bins);
            }
        }

        /// <summary>Takes one input sample and returns this stage's output for the same moment.</summary>
        public float Push(float x)
        {
            _cur[_curPos++] = x;
            var y = _ring[_ringPos];
            _ringPos = (_ringPos + 1) % _ring.Length;
            return y;
        }

        /// <summary>At a small-block boundary: when this stage's input block is full, computes the output block due start / N blocks later.</summary>
        public void BlockIfFull()
        {
            if (_curPos < _n) return;
            _curPos = 0;
            Array.Copy(_prev, 0, _re, 0, _n);
            Array.Copy(_cur, 0, _re, _n, _n);
            Array.Clear(_im);
            _fft.Forward(_re, _im);
            Array.Copy(_re, 0, _xRe, _line * _bins, _bins);
            Array.Copy(_im, 0, _xIm, _line * _bins, _bins);
            Array.Copy(_cur, _prev, _n);
            Array.Clear(_accRe); Array.Clear(_accIm);
            for (var k = 0; k < _count; k++) MultiplyAdd((_line - k + _count) % _count * _bins, k * _bins);
            _line = (_line + 1) % _count;
            for (var j = 0; j <= _n; j++) { _re[j] = _accRe[j]; _im[j] = _accIm[j]; }
            for (var j = 1; j < _n; j++) { _re[2 * _n - j] = _accRe[j]; _im[2 * _n - j] = -_accIm[j]; }
            _fft.Inverse(_re, _im);
            // The block just read out is the oldest in the ring: its positions take the new block (due start / N blocks after the one now in input).
            var write = (_ringPos - _n + _ring.Length) % _ring.Length;
            Array.Copy(_re, _n, _ring, write, _n);
        }

        private void MultiplyAdd(int x, int h)
        {
            var width = Vector<float>.Count;
            var j = 0;
            for (; j <= _bins - width; j += width)
            {
                var xr = new Vector<float>(_xRe, x + j); var xi = new Vector<float>(_xIm, x + j);
                var hr = new Vector<float>(_hRe, h + j); var hi = new Vector<float>(_hIm, h + j);
                (new Vector<float>(_accRe, j) + xr * hr - xi * hi).CopyTo(_accRe, j);
                (new Vector<float>(_accIm, j) + xr * hi + xi * hr).CopyTo(_accIm, j);
            }
            for (; j < _bins; j++)
            {
                float xr = _xRe[x + j], xi = _xIm[x + j], hr = _hRe[h + j], hi = _hIm[h + j];
                _accRe[j] += xr * hr - xi * hi;
                _accIm[j] += xr * hi + xi * hr;
            }
        }
    }
}
