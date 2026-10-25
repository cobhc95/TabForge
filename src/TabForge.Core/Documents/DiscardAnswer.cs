namespace TabForge.Documents;

// Owns: the close/keep/save-first answer for one document with unsaved changes.
// Does not own: the prompt dialog or the document save itself.
// Tests: document lifecycle tests.

/// <summary>What the person answered for one document with unsaved changes.</summary>
public enum DiscardAnswer { Close, Keep, SaveFirst }
