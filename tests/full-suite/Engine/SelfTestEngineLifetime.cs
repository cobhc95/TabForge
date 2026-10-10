using System.IO;
using EI = TabForge.AudioEngine.Isolation;
using Frames = TabForge.Audio.Contracts.Frames;
using EMix = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// Engine lifetimes: isolated plug-in request generations (a late answer is never accepted, in-flight buffers are
/// never reused) and the callback epoch barrier for retired chains / plug-ins, plus no leak on rejected plug-in state.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// One wait deadline per callback (start + 75 % of the block) shared by the isolated plug-ins in it. Chain: two on-time
    /// children, twelve children that never answer, one more on-time child. The first late child uses what is left of the budget,
    /// the rest (including the last on-time one) are bypassed without a request, and the whole chain stays inside the budget.
    /// </summary>
    private static void TestIsolatedCallbackBudget()
    {
        const int frames = 512, rate = 48000, lateCount = 12, total = 2 + lateCount + 1;
        var budgetMs = EI.PluginHostLink.CallbackBudgetMs(frames, rate);
        var stop = false;
        var blocks = new EI.PluginHostBlock[total];
        var views = new EI.PluginHostBlock[total];
        var links = new EI.PluginHostLink[total];
        var threads = new List<Thread>();
        var failures = 0;
        try
        {
            for (var p = 0; p < total; p++)
            {
                var id = Guid.NewGuid().ToString("N");
                blocks[p] = EI.PluginHostBlock.Create(id, frames);
                views[p] = EI.PluginHostBlock.Open(id, frames);
                links[p] = new EI.PluginHostLink(blocks[p], rate, () => Interlocked.Increment(ref failures));
                var late = p >= 2 && p < 2 + lateCount;
                if (late) continue;   // nobody answers: a child that misses every deadline
                var view = views[p];
                var t = new Thread(() =>
                {
                    while (!Volatile.Read(ref stop))
                    {
                        if (!view.TakeRequest(50, out var g)) continue;
                        var n = Math.Clamp(view.Frames, 0, frames);
                        for (var i = 0; i < n; i++) { view.Channel(2)[i] = view.Channel(0)[i] * 2; view.Channel(3)[i] = view.Channel(1)[i] * 2; }
                        view.Complete(g);
                    }
                }) { IsBackground = true, Name = $"selftest budget child {p}" };
                t.Start(); threads.Add(t);
            }
            var a = new[] { new float[frames], new float[frames] };
            var b = new[] { new float[frames], new float[frames] };
            var transport = new EP.TransportInfo { Tempo = 120 };
            Array.Fill(a[0], 0.1f); Array.Fill(a[1], 0.1f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EI.PluginHostLink.BeginCallback(System.Diagnostics.Stopwatch.GetTimestamp(), frames, rate);
            try
            {
                for (var p = 0; p < total; p++) { links[p].Process(a, b, frames, default, transport); (a, b) = (b, a); }
            }
            finally { EI.PluginHostLink.EndCallback(); }
            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var skipped = links.Sum(l => l.BudgetSkippedBlocks);
            var lateLinks = links.Skip(2).Take(lateCount).ToArray();
            // Old behaviour: every late child waited its own 2+ ms (here 8 ms each, ~100 ms for the chain) and the last child would process (x8).
            Check("a chain of late isolated children never exceeds the callback budget: the first late one waits the remainder, the rest are bypassed",
                Math.Abs(budgetMs - 8) < 1e-9 && elapsedMs < budgetMs + 40 && lateLinks[0].MissedBlocks == 1 && lateLinks[0].BudgetSkippedBlocks == 0
                && lateLinks.Skip(1).All(l => l.BudgetSkippedBlocks == 1 && l.MissedBlocks == 0) && skipped == lateCount,
                $"{elapsedMs:0.0} ms (budget {budgetMs} ms), skipped {skipped}, first late missed {lateLinks[0].MissedBlocks}");
            // Starved children are not charged: with the budget spent on every block for 100 ms (miss window 20 ms), a link is never failed.
            var starvedFailures = 0;
            var starved = new EI.PluginHostLink(blocks[total - 1], rate, () => Interlocked.Increment(ref starvedFailures), windowMs: 20);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            while (sw2.ElapsedMilliseconds < 100)
            {
                EI.PluginHostLink.BeginCallback(System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency, frames, rate);
                try { starved.Process(a, b, frames, default, transport); } finally { EI.PluginHostLink.EndCallback(); }
                Thread.Sleep(2);
            }
            Check("children bypassed only because the callback budget was spent are not charged misses: never disabled, however long it lasts",
                !starved.Failed && starvedFailures == 0 && starved.MissedBlocks == 0 && starved.BudgetSkippedBlocks >= 3,
                $"failed {starved.Failed}, reports {starvedFailures}, missed {starved.MissedBlocks}, skipped {starved.BudgetSkippedBlocks}");
            Check("children before the budget is spent still process; children after it pass the audio through, nothing fails",
                Math.Abs(a[0][frames - 1] - 0.4f) < 1e-6f && links[total - 1].BudgetSkippedBlocks == 1 && failures == 0,
                $"out {a[0][frames - 1]}, failures {failures}");
        }
        finally
        {
            Volatile.Write(ref stop, true);
            foreach (var t in threads) t.Join(1000);
            foreach (var x in blocks) x?.Dispose();
            foreach (var x in views) x?.Dispose();
        }
    }

    private static void TestIsolatedPluginGenerations()
    {
        // Protocol: a "done" for an earlier generation is not taken for the current request.
        var id = Guid.NewGuid().ToString("N");
        using (var host = EI.PluginHostBlock.Create(id, 64))
        using (var child = EI.PluginHostBlock.Open(id, 64))
        {
            host.Send(1);
            child.TakeRequest(0, out var g1);
            var early = host.WaitDone(1, 3);
            host.Send(2);                  // (the link itself never re-sends after a timeout; this isolates the generation check)
            child.Complete(g1);            // block 1's answer arrives late
            var stale = host.WaitDone(2, 10);
            child.TakeRequest(0, out var g2);
            child.Complete(g2);
            var current = host.WaitDone(2, 1000);
            Check("shared-block protocol: an unanswered request times out, a late answer to it is rejected, the current answer is accepted",
                !early && !stale && current && g1 == 1 && g2 == 2, $"early {early}, stale {stale}, current {current}, gens {g1}/{g2}");
        }

        // Three isolated plug-ins in series (fake plug-in processes on threads, doubling the audio); the middle one answers
        // its first block 150 ms late (budget: 40 ms at 4096 frames / 48 kHz).
        const int frames = 4096, rate = 48000, late = 1;
        var stop = false;
        var failures = new int[3];
        var hosts = new EI.PluginHostBlock[3];
        var views = new EI.PluginHostBlock[3];
        var links = new EI.PluginHostLink[3];
        var threads = new Thread[3];
        var lateDone = new ManualResetEventSlim(false);
        try
        {
            for (var p = 0; p < 3; p++)
            {
                var pid = Guid.NewGuid().ToString("N");
                hosts[p] = EI.PluginHostBlock.Create(pid, frames);
                views[p] = EI.PluginHostBlock.Open(pid, frames);
                var index = p;
                links[p] = new EI.PluginHostLink(hosts[p], rate, () => Interlocked.Increment(ref failures[index]));
                var view = views[p];
                threads[p] = new Thread(() =>
                {
                    var first = true;
                    while (!Volatile.Read(ref stop))
                    {
                        if (!view.TakeRequest(50, out var g)) continue;
                        var n = Math.Clamp(view.Frames, 0, frames);
                        if (index == late && first) Thread.Sleep(150);
                        for (var i = 0; i < n; i++)
                        {
                            var poison = index == late && first;
                            view.Channel(2)[i] = poison ? 9f : view.Channel(0)[i] * 2;
                            view.Channel(3)[i] = poison ? 9f : view.Channel(1)[i] * 2;
                        }
                        view.Complete(g);
                        if (index == late && first) lateDone.Set();
                        first = false;
                    }
                }) { IsBackground = true, Name = $"selftest fake plug-in host {p}" };
                threads[p].Start();
            }
            var a = new[] { new float[frames], new float[frames] };
            var b = new[] { new float[frames], new float[frames] };
            var transport = new EP.TransportInfo { Tempo = 120 };
            var allOk = true;
            string detail = "";
            float RunBlock(float value)
            {
                Array.Fill(a[0], value); Array.Fill(a[1], value);
                for (var p = 0; p < 3; p++) { links[p].Process(a, b, frames, default, transport); (a, b) = (b, a); }
                return a[0][frames - 1];
            }
            // Block 0 misses its deadline, block 1 arrives while the child is still busy with it: both bypass the middle plug-in.
            for (var blockNo = 0; blockNo < 2; blockNo++)
            {
                var v = (blockNo + 1) * 0.1f;
                var got = RunBlock(v);
                var expected = v * 2 * 2;   // plug-ins 0 and 2 double; the busy one passes the audio through
                if (Math.Abs(got - expected) > 1e-6f) { allOk = false; detail += $"block {blockNo}: {got} != {expected}; "; }
            }
            var inFlightInput = views[late].Channel(0)[0];
            var answered = lateDone.Wait(2000);
            Thread.Sleep(20);
            var after = RunBlock(0.5f);
            Check("a single missed deadline bypasses the block without disabling the plug-in; it resumes once the child completes (3 in series)",
                allOk && answered && Math.Abs(after - 4.0f) < 1e-6f && !links[late].Failed && failures.All(f => f == 0) && links[late].MissedBlocks == 2,
                $"{detail}after {after}, failures {string.Join("/", failures)}, missed {links[late].MissedBlocks}");
            Check("the late answer (poisoned 9.0 output) is never accepted and the in-flight request's input buffer is not rewritten while the child is busy",
                Math.Abs(inFlightInput - 0.2f) < 1e-6f /* block 0's input (0.1 doubled), not block 1's 0.4 */ && Math.Abs(after - 36f) > 1, $"in-flight input {inFlightInput}, next block {after}");

            // A hung child (never completes): bypassed during the grace period, then disabled and reported once.
            var hungId = Guid.NewGuid().ToString("N");
            using (var hungBlock = EI.PluginHostBlock.Create(hungId, frames))
            {
                var hungFailures = 0;
                var hung = new EI.PluginHostLink(hungBlock, rate, () => Interlocked.Increment(ref hungFailures), graceMs: 100);
                var hin = new[] { new float[frames], new float[frames] };
                var hout = new[] { new float[frames], new float[frames] };
                Array.Fill(hin[0], 0.3f); Array.Fill(hin[1], 0.3f);
                hung.Process(hin, hout, frames, default, transport);
                var stillOnDuringGrace = !hung.Failed && Math.Abs(hout[0][0] - 0.3f) < 1e-6f;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!hung.Failed && sw.ElapsedMilliseconds < 2000) { hung.Process(hin, hout, frames, default, transport); Thread.Sleep(5); }
                for (var i = 0; i < 5; i++) hung.Process(hin, hout, frames, default, transport);
                Check("a hung isolated child is bypassed during the grace period, then disabled and reported exactly once",
                    stillOnDuringGrace && hung.Failed && hungFailures == 1 && Math.Abs(hout[0][0] - 0.3f) < 1e-6f,
                    $"grace {stillOnDuringGrace}, failed {hung.Failed}, reports {hungFailures}");
            }

            // The audio path is allocation-free once running (measured on a plain thread like the audio callback: the
            // self-test's WPF thread has a SynchronizationContext whose wait hook allocates on every WaitOne).
            long allocated = -1;
            var measure = new Thread(() =>
            {
                RunBlock(0.25f);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 20; i++) RunBlock(0.25f);
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }) { IsBackground = true, Name = "selftest allocation probe" };
            measure.Start();
            measure.Join(10000);
            Check("isolated plug-in blocks allocate nothing on the audio thread", allocated == 0, $"{allocated} bytes");
            Check("the realtime wait is bounded by the block: 128 frames @ 48 kHz -> 2 ms floor, 1024 -> 16 ms, 8192 -> 40 ms cap",
                EI.PluginHostLink.BudgetMs(128, 48000) == 2 && Math.Abs(EI.PluginHostLink.BudgetMs(1024, 48000) - 16) < 1e-9 && EI.PluginHostLink.BudgetMs(8192, 48000) == 40);
        }
        finally
        {
            Volatile.Write(ref stop, true);
            foreach (var t in threads) t?.Join(1000);
            foreach (var h in hosts) h?.Dispose();
            foreach (var v in views) v?.Dispose();
        }
    }

    /// <summary>
    /// R-11: the isolated plug-in's control channel over in-process pipes, with a fake plug-in process on a thread. A GetState
    /// reply that arrives after its request timed out is dropped, not taken for the next request's reply; a handler that
    /// throws still answers (failure) at once instead of leaving the engine to wait out its timeout.
    /// </summary>
    private static void TestIsolatedControlChannel()
    {
        using var toHost = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var hostIn = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, toHost.ClientSafePipeHandle);
        using var toEngine = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
        using var engineIn = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, toEngine.ClientSafePipeHandle);
        var channel = new EI.PluginControlChannel(toHost);
        static void State(BinaryWriter w, string text) { var b = System.Text.Encoding.UTF8.GetBytes(text); w.Write(b.Length); w.Write(b); }

        var host = new Thread(() =>
        {
            uint? held = null;
            try
            {
                while (Frames.Read(hostIn) is { } frame)
                {
                    var (type, r) = frame;
                    if (type == EI.RemotePlugin.CmdQuit) break;
                    var id = r.ReadUInt32();
                    if (type == EI.RemotePlugin.CmdGetState && held is null) { held = id; continue; }   // slow: no answer yet
                    if (type == EI.RemotePlugin.CmdGetState)
                    {
                        // The first request's answer finally arrives, while the second request is waiting.
                        EI.PluginControlChannel.Answer(toEngine, held!.Value, () => (EI.RemotePlugin.MsgState, w => State(w, "OLD")));
                        Thread.Sleep(30);
                        EI.PluginControlChannel.Answer(toEngine, id, () => (EI.RemotePlugin.MsgState, w => State(w, "NEW")));
                        continue;
                    }
                    EI.PluginControlChannel.Answer(toEngine, id, () => throw new InvalidDataException("plug-in rejected the state"));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or EndOfStreamException) { }
        }) { IsBackground = true, Name = "selftest fake plug-in process" };
        var replies = new Thread(() =>
        {
            try { while (Frames.Read(engineIn) is { } frame) channel.Receive(frame.Type, frame.Reader); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or EndOfStreamException) { }
        }) { IsBackground = true, Name = "selftest control replies" };
        host.Start();
        replies.Start();
        try
        {
            var first = channel.Ask(EI.RemotePlugin.CmdGetState, null, EI.RemotePlugin.MsgState, 150, out var firstTimedOut);
            var second = channel.Ask(EI.RemotePlugin.CmdGetState, null, EI.RemotePlugin.MsgState, 3000, out var secondTimedOut);
            var text = second is { Length: >= 4 } ? System.Text.Encoding.UTF8.GetString(second, 4, BitConverter.ToInt32(second, 0)) : "(none)";
            Check("R-11: a GetState reply arriving after its request timed out is dropped, not taken for the next request's reply",
                first is null && firstTimedOut && !secondTimedOut && text == "NEW" && channel.Dropped == 1,
                $"first timed out {firstTimedOut}, second '{text}' (timed out {secondTimedOut}), dropped {channel.Dropped}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var set = channel.Ask(EI.RemotePlugin.CmdSetState, w => State(w, "X"), EI.RemotePlugin.MsgState, 3000, out var setTimedOut);
            Check("R-11: a command whose handler throws is still answered (failure status) at once, not left to time out",
                set is null && !setTimedOut && sw.ElapsedMilliseconds < 2000, $"reply {(set is null ? "null" : "data")}, timed out {setTimedOut}, {sw.ElapsedMilliseconds} ms");
        }
        finally
        {
            try { Frames.Write(toHost, EI.RemotePlugin.CmdQuit); } catch (IOException) { }
            host.Join(2000);
            toEngine.Dispose();
            replies.Join(2000);
        }
    }

    private sealed class FakePlugin : EP.IPluginInstance
    {
        public int Disposed;
        public bool RejectState;
        public ManualResetEventSlim? Stall, Entered;
        public string Path => "selftest-fake";
        public bool IsInstrument => false;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<EP.BlockMidi> midi, in EP.TransportInfo transport)
        {
            if (Stall is { } stall && !stall.IsSet) { Entered?.Set(); stall.Wait(5000); }
            input[0].AsSpan(0, frames).CopyTo(output[0]); input[1].AsSpan(0, frames).CopyTo(output[1]);
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { if (RejectState) throw new InvalidDataException("bad state"); }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    private sealed class Disposable : IDisposable
    {
        public int Disposed;
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    private static void TestRetirementEpochBarrier()
    {
        // Pure epoch logic: retired during a stalled callback -> kept past the old 500 ms grace, freed once it exits.
        var epoch = new EMix.CallbackEpoch();
        var queue = new EMix.RetireQueue();
        var a = new Disposable();
        epoch.Enter();
        queue.Add(a, epoch);
        queue.Collect();
        Thread.Sleep(600);
        queue.Collect();
        var heldWhileStalled = a.Disposed == 0;
        epoch.Exit();
        queue.Collect();
        Check("retired during a stalled callback: not disposed after 600 ms, disposed once the callback exits", heldWhileStalled && a.Disposed == 1, $"disposed {a.Disposed}");

        var b = new Disposable();
        queue.Add(b, epoch);
        queue.Collect();
        var c = new Disposable();
        epoch.Enter();
        queue.Add(c, epoch);
        epoch.Exit();
        epoch.Enter();   // a later callback (started after the retirement) cannot hold c
        queue.Collect();
        epoch.Exit();
        Check("retired outside a callback is freed at once; a callback that started later does not hold it back",
            b.Disposed == 1 && c.Disposed == 1 && queue.Count == 0, $"b {b.Disposed}, c {c.Disposed}, left {queue.Count}");

        // Real mixer: the master chain's plug-in stalls inside MixEngine.Read while the chain is replaced.
        using (var shared = TabForge.Audio.Contracts.SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}"))
        {
            var mix = new EMix.MixEngine(shared, 48000, 64);
            var stall = new ManualResetEventSlim(false);
            var entered = new ManualResetEventSlim(false);
            var plugin = new FakePlugin { Stall = stall, Entered = entered };
            mix.SetChain(EMix.MixEngine.MasterSlot, new EMix.TrackChain(EMix.MixEngine.MasterSlot, -1, null, new[] { new EMix.TrackChain.Effect(plugin, 1f, 0) }, 64));
            var callback = new Thread(() => mix.Read(new float[128], 0, 128)) { IsBackground = true, Name = "selftest stalled callback" };
            callback.Start();
            try
            {
                if (!entered.Wait(3000)) { Skip("a stalled mixer callback holds a replaced chain", "the fake plug-in was not called"); return; }
                var old = mix.SetChain(EMix.MixEngine.MasterSlot, new EMix.TrackChain(EMix.MixEngine.MasterSlot, -1, null, Array.Empty<EMix.TrackChain.Effect>(), 64));
                var retire = new EMix.RetireQueue();
                retire.Add(old!, mix.Epoch);
                retire.Collect();
                Thread.Sleep(600);
                retire.Collect();
                var held = plugin.Disposed == 0 && mix.Epoch.InCallback;
                stall.Set();
                callback.Join(3000);
                retire.Collect();
                Check("MixEngine: a chain replaced while the callback is stalled is disposed only after that callback returns",
                    held && plugin.Disposed == 1 && !mix.Epoch.InCallback, $"held {held}, disposed {plugin.Disposed}");
            }
            finally { stall.Set(); callback.Join(3000); }
        }

        // A plug-in whose saved state is rejected is disposed (no leaked instance / process), every time.
        var created = new List<FakePlugin>();
        var threw = 0;
        for (var i = 0; i < 50; i++)
        {
            try { EP.PluginLoading.CreateWithState(() => { var p = new FakePlugin { RejectState = true }; created.Add(p); return p; }, new byte[] { 1, 2, 3 }, out _); }
            catch (InvalidDataException) { threw++; }
        }
        var ok = EP.PluginLoading.CreateWithState(() => new FakePlugin(), new byte[] { 1 }, out _);
        Check("50 rejected state loads: each throws and its new instance is disposed exactly once; a good load is kept",
            threw == 50 && created.Count == 50 && created.All(p => p.Disposed == 1) && ((FakePlugin)ok).Disposed == 0,
            $"threw {threw}, created {created.Count}, disposed {created.Sum(p => p.Disposed)}");
    }
}
