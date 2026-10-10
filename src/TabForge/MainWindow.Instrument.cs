using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, fretboard / instrument panel interaction.
// Owns: the fretboard and instrument panel's visualisation: the percussion pad lines and the instrument refresh.
// Does not own: the panel itself (InstrumentPanel).
// Tests: listed in docs/feature-map/editing-and-notation.md.
public partial class MainWindow
{
    // ---------- instrument visualisation ----------

    private void RefreshInstrument() => InstrumentPane.ShowInstrument();

    /// <summary>The TAB line a clicked drum pad writes to (see <see cref="InstrumentPanelController.PercussionPadLine"/>).</summary>
    internal static int PercussionPadLine(TrackModel drumTrack, int percussion) => InstrumentPanelController.PercussionPadLine(drumTrack, percussion);
}
