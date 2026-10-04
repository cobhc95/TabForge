using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow: scale finder (Tools menu, fretboard right-click, the Scales button under the fretboard legend)
// and the instrument panel's view choice (fretboard / keyboard / drums).
public partial class MainWindow : IInstrumentPanelHost
{
    private InstrumentPanelController? _instrumentPane;
    private InstrumentPanelController InstrumentPane => _instrumentPane ??= new InstrumentPanelController(this);
    InstrumentPanel IInstrumentPanelHost.Instrument => Instrument;
    System.Windows.Controls.ComboBox IInstrumentPanelHost.ScaleHighlightCombo => ScaleHighlightCombo;
    FrameworkElement IInstrumentPanelHost.ScaleFinderButton => ScaleFinderButton;
    FrameworkElement IInstrumentPanelHost.InstrumentOverlay => InstrumentOverlay;
    (int Start, int End)? IInstrumentPanelHost.LoopBars => _loopHasArea ? (_loopStartBar, _loopEndBar) : null;
    bool IInstrumentPanelHost.IsInitialized => _mainWindowInitialized;
    void IInstrumentPanelHost.RefreshInstrument() => RefreshInstrument();

    private void ScaleFinderButton_Click(object sender, RoutedEventArgs e) => InstrumentPane.OpenScaleFinder();
}
