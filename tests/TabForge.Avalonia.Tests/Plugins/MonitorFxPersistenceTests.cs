using System.IO;
using System.Linq;
using System.Text.Json;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the app-wide Monitor FX chain persisting: it survives a settings save and load, a new or old song (no monitor keys) uses it, and the song's own chain only when unticked.
// Does not own: the chain window (MonitorFxTitleButtonTests), the live-only rule in renders (MonitorFxRenderTests).
// Tests: this file.
public sealed class MonitorFxPersistenceTests
{
    private static AppSettings WithChain()
    {
        var settings = new AppSettings();
        var chain = settings.Plugins.MonitorFx;
        chain.On = false;
        chain.Rig.Plugins.Add(new PluginSlot { Name = "IR Loader", Path = "builtin:irloader", State = "AQID", Enabled = false });
        chain.Rig.Plugins.Add(new PluginSlot { Name = "EQ", Path = "builtin:eq" });
        return settings;
    }

    [Fact]
    public void TheChainSurvivesARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-monfx-" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsFileService.SaveAtomic(path, WithChain());
            var chain = SettingsFileService.Load(path).Plugins.MonitorFx;
            Assert.False(chain.On);
            Assert.Equal(new[] { "IR Loader", "EQ" }, chain.Rig.Plugins.Select(p => p.Name));
            Assert.False(chain.Rig.Plugins[0].Enabled);
            Assert.Equal("AQID", chain.Rig.Plugins[0].State);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void NewAndOldSongsUseTheGlobalChain()
    {
        var global = WithChain().Plugins.MonitorFx;
        global.On = true;
        var fresh = new SongProject();
        Assert.True(fresh.Mixer.MonitorUseGlobal);
        Assert.Same(global, MixerBuses.MonitorChain(fresh, global));

        var old = JsonSerializer.Deserialize<MixerSettings>("{}")!;
        var song = new SongProject { Mixer = old };
        Assert.True(old.MonitorUseGlobal);
        Assert.Same(global, MixerBuses.MonitorChain(song, global));
        Assert.Contains(SongRigs.Running(song, global, SongRigKind.Monitor), r => ReferenceEquals(r.Rig, global.Rig));

        song.Mixer.MonitorUseGlobal = false;
        Assert.NotSame(global, MixerBuses.MonitorChain(song, global));
    }
}
