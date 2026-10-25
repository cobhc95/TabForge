namespace TabForge.KeyboardMode;

// Owns: the three sizes Keyboard mode can take (Normal, Large, Full), as the settings and the layout code name them.
// Does not own: the saved size text (KeyboardModeSettings) or the dock arrangement built for a size (KeyboardModeLayoutSwap).
// Tests: TestKeyboardModeLayout.

/// <summary>How much of the window Keyboard mode takes.</summary>
public enum KeyboardModeSize { Normal, Large, Full }
