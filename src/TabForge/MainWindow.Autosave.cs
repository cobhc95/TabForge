using System.Windows;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow, background services: the autosave (dirty songs are copied to the Recovery folder), the update check and the approval notices. BackgroundServices builds
// each controller and is its host; the controllers are disposed with the window's owned subscriptions.
public partial class MainWindow
{
    private sealed class BackgroundServices : IAutosaveHost, IUpdateCheckHost, IApprovalHost
    {
        private readonly MainWindow _window;

        public AutosaveController Autosave { get; }
        public UpdateCheckController Update { get; }
        public ApprovalNoticeController Approvals { get; }
        /// <summary>The window the update dialog belongs to while a check runs from a modal Settings window (null: this window).</summary>
        public Window? DialogOwner { get; set; }

        public BackgroundServices(MainWindow window)
        {
            _window = window;
            var notices = new StatusNoticeBars(window.MainStatusBar);
            Autosave = new AutosaveController(this, notices);
            Update = new UpdateCheckController(this);
            Approvals = new ApprovalNoticeController(this, notices);
            window._lifetime.Add(Approvals.Dispose);
            window._lifetime.Add(Update.Dispose);
            window._lifetime.Add(Autosave.Dispose);
            Autosave.Start();
            Approvals.Start();
        }

        // IAutosaveHost
        public DocumentManager Documents => _window._documents;
        public int AutosaveMinutes => _window._settings.General.AutosaveMinutes;
        public bool IsClosed => _window._isClosed;
        public IReadOnlyList<DocumentSession> DocumentsInAllWindows() =>
            Application.Current?.Windows.OfType<MainWindow>().SelectMany(w => w.OpenDocuments).ToList() ?? new List<DocumentSession>();
        public void Post(Action work, DispatcherPriority priority) => _window.PostIfOpen(work, priority);
        public bool ConfirmRecovery(int songs) =>
            MessageBox.Show(_window,
                $"TabForge found {songs} unsaved song(s) from a session that ended unexpectedly.\n\nOpen them now? Choosing No discards them.",
                "Recover unsaved songs", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        public Task OpenDocumentAsync(string path) => _window.OpenDocumentFromPath(path);
        public void UpdateTitle() => _window.UpdateTitle();

        // IUpdateCheckHost
        public GeneralSettings General => _window._settings.General;
        public bool IsLoaded => _window.IsLoaded;
        public void SaveSettings() => _window.SaveSettings();
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public UpdateChoice ShowUpdateAvailable(ReleaseInfo? release, bool checkAutomatically)
        {
            var result = UpdateAvailableWindow.Show(DialogOwner ?? _window, release, AppInfo.Version, checkAutomatically);
            return new UpdateChoice(result.OpenPage, result.CheckAutomatically);
        }

        // IApprovalHost
        public DocumentSession ActiveDocument => _window.Doc;
        public AppSettings Settings => _window._settings;
        public void SyncAudioEngine() => _window.SyncAudioEngine();
        public void RefreshArrangement() => _window.RefreshArrangement();
        public void RefreshMixerWindow() => _window.RefreshMixerWindow();
        public void Post(Action work) => _window.PostIfOpen(work);
        public void ShowLinkedAudioReview(LinkedAudioReview review) => LinkedAudioReviewWindow.Show(_window, review);
        public void ShowPluginReview(PluginReview review) => PluginReviewWindow.Show(_window, review);
    }

    /// <summary>Start-up: offers songs a crashed or killed session left in the Recovery folder.</summary>
    internal void OfferAutosaveRecovery() => _services.Autosave.OfferRecovery();
}
