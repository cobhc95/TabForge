using System.Numerics;
using System.Runtime.CompilerServices;

namespace TabForge.Services.Video;

// Owns: Desktop Duplication of the output under the region: acquiring a frame, copying only the region's rectangle on the GPU into a staging
// texture (halved on the GPU first, as mip levels, when the frame is at most half the region's size, so less crosses the bus), mapping it, turning it upright on a rotated (portrait) display and scaling it (bilinear, parallel rows) into the caller's top-down BGRA buffer with the same fit-and-black-bars layout as ScreenRegionGrabber.
// Does not own: choosing the region, timing, encoding, or the fallback when DXGI cannot be used (DesktopRegionGrabber). Any thread may call Grab (one at a time).
// Tests: TestLiveVideoRecord.
/// <summary>
/// When the desktop has not changed since the last call, the buffer from the last frame is still valid and Grab returns true without work.
/// Throws <see cref="DxgiUnavailableException"/> when duplication cannot be set up.
/// </summary>
internal sealed unsafe class DxgiRegionGrabber : IDisposable
{
    private readonly int _width, _height;
    private readonly Func<ScreenRect?> _region;
    private void* _device, _context, _dup, _staging, _mips, _mipView;
    private int _level;                                // mip level read back: the region halved this many times on the GPU
    private int _ox, _oy, _ow, _oh;                    // the duplicated output in desktop coordinates
    private int _rotation = 1;                         // DXGI_MODE_ROTATION: 1 identity, 2 = 90, 3 = 180, 4 = 270 degrees
    private uint[] _upright = Array.Empty<uint>();     // the region turned upright (rotated outputs only)
    private int _sw, _sh;                              // staging size
    private int _fx, _fy, _fw, _fh;                    // the rectangle (output coordinates) the staging texture holds; _fw 0 = none yet
    private byte[]? _last;                             // the buffer the last composed frame went into
    private int _lw, _lh, _lx, _ly, _lsw, _lsh;        // layout of that frame
    private int[] _xi = Array.Empty<int>(), _xw = Array.Empty<int>();
    private uint[] _hrows = Array.Empty<uint>();      // source rows stretched to the output width

    /// <summary>Frames composed from a new desktop picture (calls on an unchanged desktop reuse the buffer and are not counted).</summary>
    public int ComposedFrames { get; private set; }

    public DxgiRegionGrabber(int width, int height, Func<ScreenRect?> region) { _width = width; _height = height; _region = region; }

    /// <summary>Fills <paramref name="bgra"/> (width x height x 4); false when there is no region or no picture to give yet.</summary>
    public bool Grab(byte[] bgra)
    {
        if (_region() is not { Width: > 0, Height: > 0 } r) return false;
        if (_dup == null || !Contains(r)) { if (!OpenOutput(r)) return false; }
        int x0 = Math.Max(r.X, _ox) - _ox, y0 = Math.Max(r.Y, _oy) - _oy;
        int rw = Math.Min(r.X + r.Width, _ox + _ow) - _ox - x0, rh = Math.Min(r.Y + r.Height, _oy + _oh) - _oy - y0;
        if (rw <= 0 || rh <= 0) return false;
        var (bx, by, bw, bh) = TextureBox(x0, y0, rw, rh);
        var fit = ScreenRegionGrabber.Fit(_width, _height, rw, rh);
        var level = 0;
        while (level < 4 && rw >> (level + 1) >= fit.Width && rh >> (level + 1) >= fit.Height) level++;
        EnsureStaging(bw, bh, level);
        var fresh = Acquire(bx, by, bw, bh);
        for (var retry = 0; !fresh && _fw == 0 && _dup != null && retry < 4; retry++) fresh = Acquire(bx, by, bw, bh);   // the first picture may take a few presents to arrive
        int sw = rw >> level, sh = rh >> level;
        if (!fresh) return _dup != null && _fw == bw && _fh == bh && _fx == bx && _fy == by && ReferenceEquals(_last, bgra) && _lsw == sw && _lsh == sh;
        ComposedFrames++;
        return Compose(bgra, sw, sh);
    }

    /// <summary>
    /// Where an output rectangle (desktop orientation) lies in the duplicated texture, which keeps the panel's own orientation: a portrait
    /// (rotated) display gives a landscape texture.
    /// </summary>
    private (int X, int Y, int W, int H) TextureBox(int x, int y, int w, int h) => _rotation switch
    {
        2 => (y, _ow - x - w, h, w),
        3 => (_ow - x - w, _oh - y - h, w, h),
        4 => (_oh - y - h, x, h, w),
        _ => (x, y, w, h),
    };

    private bool Contains(ScreenRect r)
    {
        int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        return cx >= _ox && cx < _ox + _ow && cy >= _oy && cy < _oy + _oh;
    }

    private bool OpenOutput(ScreenRect r)
    {
        CloseOutput();
        int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        void* factory = null, adapter = null, output = null, found = null, foundAdapter = null;
        try
        {
            var desc = stackalloc int[24];   // DXGI_OUTPUT_DESC: name[32 chars], rect, attached, rotation, monitor
            var iid = DxgiCom.Factory1;
            if (DxgiCom.CreateDXGIFactory1(&iid, &factory) < 0) throw new DxgiUnavailableException("No DXGI factory.");
            for (uint i = 0; found == null && ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)DxgiCom.Slot(factory, 12))(factory, i, &adapter) >= 0; i++)
            {
                for (uint j = 0; found == null && ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)DxgiCom.Slot(adapter, 7))(adapter, j, &output) >= 0; j++)
                {
                    ((delegate* unmanaged[Stdcall]<void*, int*, int>)DxgiCom.Slot(output, 7))(output, desc);
                    int l = desc[16], t = desc[17], rr = desc[18], b = desc[19], rotation = desc[21];
                    if (cx >= l && cx < rr && cy >= t && cy < b)
                    {
                        found = output; foundAdapter = adapter; _ox = l; _oy = t; _ow = rr - l; _oh = b - t; _rotation = rotation is >= 2 and <= 4 ? rotation : 1;
                    }
                    else DxgiCom.Release(output);
                }
                if (found == null) DxgiCom.Release(adapter);
            }
            if (found == null) return false;
            void* dev = null, ctx = null;
            if (DxgiCom.D3D11CreateDevice(foundAdapter, 0, IntPtr.Zero, 0x20, null, 0, 7, &dev, null, &ctx) < 0) throw new DxgiUnavailableException("No Direct3D 11 device.");
            _device = dev; _context = ctx;
            if (DxgiCom.QueryInterface(found, DxgiCom.Output1, out var output1) < 0) throw new DxgiUnavailableException("No IDXGIOutput1.");
            void* dup = null;
            var hr = ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)DxgiCom.Slot(output1, 22))(output1, _device, &dup);
            DxgiCom.Release(output1);
            if (hr < 0) throw new DxgiUnavailableException($"Desktop duplication refused (0x{hr:X8}).");
            _dup = dup;
            return true;
        }
        catch { CloseOutput(); throw; }
        finally { DxgiCom.Release(found); DxgiCom.Release(foundAdapter); DxgiCom.Release(factory); }
    }

    /// <summary>The staging texture for a w x h box read back at <paramref name="level"/> (and, above level 0, the mip chain it comes from).</summary>
    private void EnsureStaging(int w, int h, int level)
    {
        if (_staging != null && _sw == w && _sh == h && _level == level) return;
        DxgiCom.Release(_staging); DxgiCom.Release(_mipView); DxgiCom.Release(_mips); _staging = _mipView = _mips = null; _fw = 0; _last = null;
        var d = stackalloc uint[11];   // D3D11_TEXTURE2D_DESC
        void* tex = null;
        if (level > 0)
        {
            d[0] = (uint)w; d[1] = (uint)h; d[2] = (uint)level + 1; d[3] = 1; d[4] = 87;   // B8G8R8A8_UNORM
            d[5] = 1; d[6] = 0; d[7] = 0; d[8] = 0x28; d[9] = 0; d[10] = 1;   // one sample; DEFAULT; shader resource + render target; GENERATE_MIPS
            if (((delegate* unmanaged[Stdcall]<void*, uint*, void*, void**, int>)DxgiCom.Slot(_device, 5))(_device, d, null, &tex) < 0) throw new DxgiUnavailableException("No mip texture.");
            _mips = tex; void* view = null;
            if (((delegate* unmanaged[Stdcall]<void*, void*, void*, void**, int>)DxgiCom.Slot(_device, 7))(_device, _mips, null, &view) < 0) throw new DxgiUnavailableException("No mip view.");
            _mipView = view; tex = null;
        }
        d[0] = (uint)(w >> level); d[1] = (uint)(h >> level); d[2] = 1; d[3] = 1; d[4] = 87;
        d[5] = 1; d[6] = 0; d[7] = 3; d[8] = 0; d[9] = 0x20000; d[10] = 0;   // one sample; STAGING; CPU read
        if (((delegate* unmanaged[Stdcall]<void*, uint*, void*, void**, int>)DxgiCom.Slot(_device, 5))(_device, d, null, &tex) < 0) throw new DxgiUnavailableException("No staging texture.");
        _staging = tex; _sw = w; _sh = h; _level = level;
    }

    /// <summary>True when a new desktop picture was copied into the staging texture.</summary>
    private bool Acquire(int x, int y, int w, int h)
    {
        var info = stackalloc long[8];   // DXGI_OUTDUPL_FRAME_INFO, 48 bytes; the first field is LastPresentTime
        void* resource = null;
        var hr = ((delegate* unmanaged[Stdcall]<void*, uint, long*, void**, int>)DxgiCom.Slot(_dup, 8))(_dup, _fw == 0 ? 100u : 0u, info, &resource);
        if (hr == DxgiCom.WaitTimeout) return false;
        if (hr == DxgiCom.AccessLost) { CloseOutput(); return false; }   // mode change or secure desktop: reopened on the next call
        if (hr < 0) throw new DxgiUnavailableException($"AcquireNextFrame failed (0x{hr:X8}).");
        var copied = false;
        try
        {
            if (info[0] != 0 && resource != null && DxgiCom.QueryInterface(resource, DxgiCom.Texture2D, out var tex) >= 0)
            {
                var box = stackalloc uint[6]; box[0] = (uint)x; box[1] = (uint)y; box[2] = 0; box[3] = (uint)(x + w); box[4] = (uint)(y + h); box[5] = 1;
                var copy = (delegate* unmanaged[Stdcall]<void*, void*, uint, uint, uint, uint, void*, uint, uint*, void>)DxgiCom.Slot(_context, 46);
                if (_level == 0) copy(_context, _staging, 0, 0, 0, 0, tex, 0, box);
                else
                {
                    copy(_context, _mips, 0, 0, 0, 0, tex, 0, box);
                    ((delegate* unmanaged[Stdcall]<void*, void*, void>)DxgiCom.Slot(_context, 54))(_context, _mipView);   // GenerateMips
                    copy(_context, _staging, 0, 0, 0, 0, _mips, (uint)_level, null);
                }
                DxgiCom.Release(tex);
                _fx = x; _fy = y; _fw = w; _fh = h; copied = true;
            }
        }
        finally
        {
            DxgiCom.Release(resource);
            ((delegate* unmanaged[Stdcall]<void*, int>)DxgiCom.Slot(_dup, 14))(_dup);
        }
        return copied;
    }

    private bool Compose(byte[] bgra, int rw, int rh)
    {
        var (x, y, w, h) = ScreenRegionGrabber.Fit(_width, _height, rw, rh);
        if (!ReferenceEquals(_last, bgra) || _lw != w || _lh != h || _lx != x || _ly != y || _lsw != rw || _lsh != rh)
        {
            Array.Clear(bgra);   // black bars
            _xi = new int[w]; _xw = new int[w];
            for (var i = 0; i < w; i++)
            {
                var s = Math.Max(0, (long)(2 * i + 1) * rw * 128 / w - 128);
                var i0 = (int)(s >> 8);
                if (i0 >= rw - 1) { _xi[i] = rw - 1; _xw[i] = 0; } else { _xi[i] = i0; _xw[i] = (int)(s & 255); }
            }
            _last = bgra; _lw = w; _lh = h; _lx = x; _ly = y; _lsw = rw; _lsh = rh;
        }
        var m = stackalloc uint[4];   // D3D11_MAPPED_SUBRESOURCE: data pointer (8 bytes), row pitch, depth pitch
        if (((delegate* unmanaged[Stdcall]<void*, void*, uint, int, uint, uint*, int>)DxgiCom.Slot(_context, 14))(_context, _staging, 0, 1, 0, m) < 0) return false;
        try
        {
            nint src = *(nint*)m; int pitch = (int)m[2];
            if (_rotation == 1) Scale(bgra, src, pitch, x, y, w, h, rw, rh);
            else
            {
                if (_upright.Length < rw * rh) _upright = new uint[rw * rh];
                fixed (uint* u = _upright) { Upright(src, pitch, (nint)u, rw, rh); Scale(bgra, (nint)u, rw * 4, x, y, w, h, rw, rh); }
            }
        }
        finally { ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)DxgiCom.Slot(_context, 15))(_context, _staging, 0); }
        return true;
    }

    /// <summary>
    /// Upscaling (the region is smaller than the frame, the usual case): each source row is stretched sideways once, then every output row is a
    /// vertical blend of two of those, done a vector at a time. Downscaling blends four source pixels per output pixel directly.
    /// </summary>
    private void Scale(byte[] bgra, nint src, int pitch, int x, int y, int w, int h, int rw, int rh)
    {
        var xi = _xi; var xw = _xw; var stride = _width * 4;
        var up = rh <= h;
        if (up && _hrows.Length < rh * w) _hrows = new uint[rh * w];
        fixed (byte* dstBase = bgra) fixed (uint* hrows = _hrows)
        {
            nint dst = (nint)dstBase + (y * _width + x) * 4, hb = (nint)hrows;
            if (up)
            {
                Parallel.For(0, rh, sy =>
                {
                    var p = (uint*)(src + sy * pitch); var o = (uint*)hb + sy * w;
                    for (var i = 0; i < w; i++) { int a = xi[i]; o[i] = Lerp(p[a], p[Math.Min(a + 1, rw - 1)], (uint)xw[i]); }
                });
                Parallel.For(0, h, row =>
                {
                    var sy = Math.Max(0, (long)(2 * row + 1) * rh * 128 / h - 128);
                    var y0 = (int)(sy >> 8); var wy = (uint)(sy & 255);
                    if (y0 >= rh - 1) { y0 = rh - 1; wy = 0; }
                    var r0 = (uint*)hb + y0 * w; var o = (uint*)(dst + row * stride);
                    if (wy == 0) { Buffer.MemoryCopy(r0, o, w * 4L, w * 4L); return; }
                    var r1 = r0 + w; var i = 0;
                    var mask = new Vector<uint>(0x00FF00FF); var vw = new Vector<uint>(wy); var iw = new Vector<uint>(256 - wy);
                    for (; i <= w - Vector<uint>.Count; i += Vector<uint>.Count)
                    {
                        var a = Unsafe.Read<Vector<uint>>(r0 + i); var b = Unsafe.Read<Vector<uint>>(r1 + i);
                        var rb = Vector.ShiftRightLogical((a & mask) * iw + (b & mask) * vw, 8) & mask;
                        var ag = Vector.AndNot((Vector.ShiftRightLogical(a, 8) & mask) * iw + (Vector.ShiftRightLogical(b, 8) & mask) * vw, mask);
                        Unsafe.Write(o + i, rb | ag);
                    }
                    for (; i < w; i++) o[i] = Lerp(r0[i], r1[i], wy);
                });
                return;
            }
            Parallel.For(0, h, row =>
            {
                var o = (uint*)(dst + row * stride);
                var sy = Math.Max(0, (long)(2 * row + 1) * rh * 128 / h - 128);
                var y0 = (int)(sy >> 8); var wy = (uint)(sy & 255);
                if (y0 >= rh - 1) { y0 = rh - 1; wy = 0; }
                var p0 = (uint*)(src + y0 * pitch); var p1 = (uint*)(src + Math.Min(y0 + 1, rh - 1) * pitch);
                for (var i = 0; i < w; i++)
                {
                    int a = xi[i], b = Math.Min(a + 1, rw - 1); var wx = (uint)xw[i];
                    o[i] = Lerp(Lerp(p0[a], p0[b], wx), Lerp(p1[a], p1[b], wx), wy);
                }
            });
        }
    }

    /// <summary>
    /// Turns the mapped texture of a rotated output (<paramref name="src"/>) into an upright rw x rh picture at <paramref name="dst"/>. Texture rows
    /// are read in order (mapped memory is slow to read out of order); each becomes an upright column.
    /// </summary>
    private void Upright(nint src, int pitch, nint dst, int rw, int rh)
    {
        var rotation = _rotation;
        var rows = rotation == 3 ? rh : rw;
        Parallel.For(0, rows, ty =>
        {
            var p = (uint*)(src + (long)ty * pitch); var o = (uint*)dst;
            switch (rotation)
            {
                case 2: for (var tx = 0; tx < rh; tx++) o[(long)tx * rw + rw - 1 - ty] = p[tx]; break;
                case 4: for (var tx = 0; tx < rh; tx++) o[(long)(rh - 1 - tx) * rw + ty] = p[tx]; break;
                default: var row = o + (long)(rh - 1 - ty) * rw; for (var tx = 0; tx < rw; tx++) row[rw - 1 - tx] = p[tx]; break;
            }
        });
    }

    /// <summary>(a * (256 - w) + b * w) / 256 on all four bytes of a pixel at once.</summary>
    private static uint Lerp(uint a, uint b, uint w)
    {
        var rb = (((a & 0x00FF00FF) * (256 - w)) + ((b & 0x00FF00FF) * w)) >> 8 & 0x00FF00FF;
        var ag = (((a >> 8) & 0x00FF00FF) * (256 - w)) + (((b >> 8) & 0x00FF00FF) * w) & 0xFF00FF00;
        return rb | ag;
    }

    private void CloseOutput()
    {
        DxgiCom.Release(_staging); DxgiCom.Release(_mipView); DxgiCom.Release(_mips); DxgiCom.Release(_dup); DxgiCom.Release(_context); DxgiCom.Release(_device);
        _staging = _mipView = _mips = _dup = _context = _device = null; _fw = _sw = _sh = _level = 0; _last = null;
        _ow = _oh = 0;
    }

    public void Dispose() => CloseOutput();
}
