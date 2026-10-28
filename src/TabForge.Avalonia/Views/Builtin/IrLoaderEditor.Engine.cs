using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Builtin;
using TabForge.AudioEngine.Builtin.IrLoader;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the IR Loader window's talk with the engine (the parameter list asked for at the poll rate, one question at a time; values sent as parameters; a file
//   sent as a partial state and kept in the song as one undo step), the recent-files list, and the picture of the current file computed once on a worker.
// Does not own: the controls (IrLoaderEditor.cs), the sound (IrLoaderEffect).
// Tests: BuiltinEffectTests.
public sealed partial class IrLoaderEditor
{
    private const int PicturePoints = 600, CurvePoints = 400;
    private int _waiting;   // ticks the last question is out (0: none); asked again after FxParameterPane.LostAfterTicks
    private bool? _problemShown;
    private string _pictureOf = "\0";
    private int _pictureGeneration;

    private void WireEngine()
    {
        _poll.Tick += (_, _) => Ask();
        AttachedToVisualTree += (_, _) =>
        {
            if (_host.Engine is { } engine) engine.ParametersReceived += OnParameters;
            Ask();
            _poll.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _poll.Stop();
            if (_host.Engine is { } e) e.ParametersReceived -= OnParameters;
        };
        if (_host.Engine is not { IsRunning: true }) _status.Text = "The audio engine is not running: changes apply when it starts.";
    }

    private void Ask()
    {
        if (_host.Engine is not { IsRunning: true } engine) return;
        if (_waiting > 0 && ++_waiting <= FxParameterPane.LostAfterTicks) return;
        _waiting = 1;
        engine.RequestParameters(_track, Slot);
    }

    private void Send(uint id, double value) => _host.Engine?.SetParameter(_track, Slot, id, value);

    private void OnParameters(int engineSlot, int index, IReadOnlyList<PluginParameterInfo> list)
    {
        if (_host.Engine is not { } engine || engineSlot != engine.SlotOf(_track) || index != _track.Rig.Plugins.IndexOf(Slot)) return;
        _waiting = 0;
        var byId = list.ToDictionary(p => p.Id);
        double V(uint id) => byId.TryGetValue(id, out var p) ? p.Value : 0;
        string T(uint id) => byId.TryGetValue(id, out var p) ? p.Text : "";
        _applying = true;
        try
        {
            if (!_mix.IsPointerOver) _mix.Value = V(IrLoaderEffect.MixId);
            if (!_gain.IsPointerOver) _gain.Value = V(IrLoaderEffect.GainId);
            _autoLevel.IsChecked = V(IrLoaderEffect.AutoLevelId) >= 0.5;
            _trim.IsChecked = V(IrLoaderEffect.TrimId) >= 0.5;
            _separate.IsChecked = V(IrLoaderEffect.SeparateId) >= 0.5;
            _ab.IsChecked = V(IrLoaderEffect.BypassId) >= 0.5;
        }
        finally { _applying = false; }
        _readout.Text = $"Latency: {T(IrLoaderEffect.LatencyId)}   ·   CPU: {V(IrLoaderEffect.CpuId):0.0} %   ·   Length: {V(IrLoaderEffect.LengthId):0} ms   ·   Auto level: {V(IrLoaderEffect.AutoGainId):+0.0;-0.0;0.0} dB";
        var problem = V(IrLoaderEffect.StatusId) >= IrLoaderEffect.StatusProblem;
        _status.Text = T(IrLoaderEffect.StatusId);
        if (problem != _problemShown)
        {
            _problemShown = problem;
            _status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(problem ? "Danger" : "TextMuted");
        }
        ShowFiles(T(IrLoaderEffect.FileId), T(IrLoaderEffect.LeftFileId), T(IrLoaderEffect.RightFileId));
    }

    /// <summary>The text value of <paramref name="key"/> in the slot's saved state ("" without one): what the window shows before the engine answers.</summary>
    private string StateText(string key)
    {
        try { return Slot.State is { Length: > 0 } s ? BuiltinState.Parse(Convert.FromBase64String(s)).Text.GetValueOrDefault(key, "") : ""; }
        catch (Exception ex) when (ex is FormatException or System.IO.InvalidDataException) { return ""; } // Not logged: a state from a newer version shows no file
    }

    /// <summary>Sets one IR file: the engine loads it now (a partial state: the other settings stay), the song keeps it (one undo step), it joins the recent list.</summary>
    public void SetFile(string key, string path)
    {
        if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) { _status.Text = "Choose a .wav file."; return; }
        var partial = Convert.ToBase64String(BuiltinState.OneText(key, path));
        _host.Edit(_track, () =>
        {
            Slot.State = Merged(Slot.State, key, path);
            _host.Engine?.SetState(_track, Slot, partial);
        });
        var recent = _host.PluginSettings.RecentImpulseFiles;
        recent.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        if (recent.Count > PluginSettings.MaxRecentImpulseFiles) recent.RemoveRange(PluginSettings.MaxRecentImpulseFiles, recent.Count - PluginSettings.MaxRecentImpulseFiles);
        _host.SaveSettings();
        var file = key == IrLoaderEffect.FileKey ? path : StateText(IrLoaderEffect.FileKey);
        ShowFiles(file, key == IrLoaderEffect.LeftKey ? path : StateText(IrLoaderEffect.LeftKey), key == IrLoaderEffect.RightKey ? path : StateText(IrLoaderEffect.RightKey));
        Ask();
    }

    private static string Merged(string? state, string key, string path)
    {
        BuiltinState parsed;
        try { parsed = state is { Length: > 0 } ? BuiltinState.Parse(Convert.FromBase64String(state)) : new BuiltinState(); }
        catch (Exception ex) when (ex is FormatException or System.IO.InvalidDataException) { parsed = new BuiltinState(); } // Not logged: an unreadable old state is replaced
        parsed.Text[key] = path;
        return Convert.ToBase64String(parsed.ToBytes());
    }

    /// <summary>Shows the file paths and, when the file the picture is of changed, computes the new picture once on a worker.</summary>
    private void ShowFiles(string file, string left, string right)
    {
        _fileText.Text = file.Length > 0 ? file : "(none)";
        _leftText.Text = left.Length > 0 ? left : "(none)";
        _rightText.Text = right.Length > 0 ? right : "(none)";
        var shown = _separate.IsChecked == true ? (left.Length > 0 ? left : right) : file;
        if (shown == _pictureOf) return;
        _pictureOf = shown;
        var generation = ++_pictureGeneration;
        if (shown.Length == 0) { _graph.SetData(Array.Empty<float>(), Array.Empty<float>(), "Drop a .wav impulse response here, or choose one above"); return; }
        _ = Task.Run(() =>
        {
            float[] peaks = Array.Empty<float>(), curve = Array.Empty<float>();
            var hint = "";
            try
            {
                var audio = IrWaveFile.Read(shown);
                peaks = IrPictures.Peaks(audio.Channels[0], PicturePoints);
                curve = IrPictures.ResponseCurve(audio.Channels[0], audio.SampleRate, CurvePoints);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
            {
                hint = ex is System.IO.FileNotFoundException ? $"The IR file is missing: {shown}" : ex.Message;
            }
            _ui.Post(() => { if (generation == _pictureGeneration) _graph.SetData(peaks, curve, hint); });
        });
    }
}
