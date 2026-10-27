using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: the clap_host_t a plug-in sees (name, vendor, request_callback / request_restart / request_process) and the host extensions offered
//   (clap.gui resize requests, clap.params / clap.state / clap.latency notifications, all recorded and polled on the main thread).
// Does not own: what an instance does with a request (ClapPlugin).
// Threads: plug-ins may call these from any thread; each only sets a flag on the owning instance.

internal unsafe static class ClapHostCallbacks
{
    private static readonly byte* HostName, HostVendor, HostUrl, HostVersion;
    private static readonly ClapHostGui* Gui;
    private static readonly ClapHostParams* Params;
    private static readonly ClapHostState* State;
    private static readonly ClapHostLatency* Latency;

    static ClapHostCallbacks()
    {
        HostName = Utf8("TabForge");
        HostVendor = Utf8("TabForge");
        HostUrl = Utf8("");
        HostVersion = Utf8(typeof(ClapHostCallbacks).Assembly.GetName().Version?.ToString(3) ?? "0");
        Gui = (ClapHostGui*)NativeMemory.AllocZeroed((nuint)sizeof(ClapHostGui));
        Gui->ResizeHintsChanged = &GuiHintsChanged;
        Gui->RequestResize = &GuiRequestResize;
        Gui->RequestShow = &GuiAccept;
        Gui->RequestHide = &GuiAccept;
        Gui->Closed = &GuiClosed;
        Params = (ClapHostParams*)NativeMemory.AllocZeroed((nuint)sizeof(ClapHostParams));
        Params->Rescan = &ParamsRescan;
        Params->Clear = &ParamsClear;
        Params->RequestFlush = &NoteVoid;
        State = (ClapHostState*)NativeMemory.AllocZeroed((nuint)sizeof(ClapHostState));
        State->MarkDirty = &NoteVoid;
        Latency = (ClapHostLatency*)NativeMemory.AllocZeroed((nuint)sizeof(ClapHostLatency));
        Latency->Changed = &NoteVoid;
    }

    private static byte* Utf8(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var p = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    /// <summary>A native clap_host_t for <paramref name="owner"/>; free it with <see cref="Release"/> after the plug-in is destroyed.</summary>
    public static ClapHost* Create(ClapPlugin owner, out GCHandle handle)
    {
        handle = GCHandle.Alloc(owner);
        var host = (ClapHost*)NativeMemory.AllocZeroed((nuint)sizeof(ClapHost));
        host->Version = ClapVersion.Current;
        host->HostData = (void*)GCHandle.ToIntPtr(handle);
        host->Name = HostName; host->Vendor = HostVendor; host->Url = HostUrl; host->VersionText = HostVersion;
        host->GetExtension = &GetExtension;
        host->RequestRestart = &RequestRestart;
        host->RequestProcess = &RequestProcess;
        host->RequestCallback = &RequestCallback;
        return host;
    }

    public static void Release(ClapHost* host, GCHandle handle)
    {
        if (host != null) NativeMemory.Free(host);
        if (handle.IsAllocated) handle.Free();
    }

    private static ClapPlugin? Owner(ClapHost* host) =>
        host == null || host->HostData == null ? null : GCHandle.FromIntPtr((IntPtr)host->HostData).Target as ClapPlugin;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* GetExtension(ClapHost* host, byte* id)
    {
        if (id == null) return null;
        return ClapModule.Text(id) switch
        {
            ClapIds.ExtGui => Gui,
            ClapIds.ExtParams => Params,
            ClapIds.ExtState => State,
            ClapIds.ExtLatency => Latency,
            _ => null,
        };
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void RequestRestart(ClapHost* host) => Owner(host)?.NoteRestartRequested();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void RequestProcess(ClapHost* host) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void RequestCallback(ClapHost* host) => Owner(host)?.NoteCallbackRequested();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void GuiHintsChanged(ClapHost* host) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiRequestResize(ClapHost* host, uint width, uint height)
    {
        Owner(host)?.NoteResizeRequested((int)Math.Min(width, 16384u), (int)Math.Min(height, 16384u));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte GuiAccept(ClapHost* host) => 1;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void GuiClosed(ClapHost* host, byte wasDestroyed) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ParamsRescan(ClapHost* host, uint flags) => Owner(host)?.NoteParametersChanged();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ParamsClear(ClapHost* host, uint id, uint flags) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void NoteVoid(ClapHost* host) { }
}
