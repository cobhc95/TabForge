namespace TabForge.Visualization;

// Owns: the three docking anchors of the fretboard along the pane (left, centre, right).
// Does not own: the layout that uses them (FretboardGeometryRules) or where the choice is stored (settings).
// Tests: no dedicated test class.
public enum FretboardHorizontalPosition { Left, Centre, Right }
