using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using TabForge.Services;

namespace TabForge.Diagnostics;

internal interface IProfileHandoverStatusHost
{
    Dispatcher Dispatcher { get; }
    bool IsVisible { get; }
    long Handle { get; }
    IReadOnlyList<string> DocumentPaths { get; }
}

// Owns: profile-local status snapshots for the real-process handover release test.
// Does not own: the window, named pipe, or document loading.
// Tests: TestSingleInstanceProcessHandover.
internal sealed class ProfileHandoverTestStatus : IDisposable
{
    private sealed class MainWindowStatusHost(MainWindow window) : IProfileHandoverStatusHost
    {
        public Dispatcher Dispatcher => window.Dispatcher;
        public bool IsVisible => window.IsVisible;
        public long Handle => new System.Windows.Interop.WindowInteropHelper(window).Handle.ToInt64();
        public IReadOnlyList<string> DocumentPaths => window.OpenDocuments.Select(document => document.Path)
            .Where(path => path is not null).Cast<string>().ToArray();
    }

    private readonly IProfileHandoverStatusHost _host;
    private readonly DispatcherTimer _timer;

    public ProfileHandoverTestStatus(MainWindow window)
        : this(new MainWindowStatusHost(window)) { }

    private ProfileHandoverTestStatus(IProfileHandoverStatusHost host)
    {
        _host = host;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Write(), host.Dispatcher);
        Write();
        _timer.Start();
    }

    private void Write()
    {
        if (!SingleInstanceService.ProfileHandoverTestEnabled) return;
        try
        {
            var path = Path.Combine(UserPaths.Local, "test-single-instance.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            var state = new
            {
                visible = _host.IsVisible,
                handle = _host.Handle,
                documents = _host.DocumentPaths
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Services.Trace.Error(Services.Trace.Ui, "handover status: write: " + ex.Message); }
    }

    public void Dispose() => _timer.Stop();
}
