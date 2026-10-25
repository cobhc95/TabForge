using System;
using Avalonia.Controls;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Chord finder (Tools > Chord finder…): the notes of a chord for a root and a type. "Insert name" passes the chord name to the host's callback.
/// Owns: the control wiring and the click handlers. Does not own: the choices, note lines and status texts (ChordFinderPresenter),
/// the chord tables (MusicTheoryService), the score edit (the callback) and the colours (AvaloniaPalette resources, named in the XAML).
/// Tests: TabForge.Avalonia.Tests.ChordFinderWindowTests.
/// </summary>
public partial class ChordFinderWindow : Window
{
    private readonly Func<string, bool> _insertChord;

    /// <summary>For the XAML loader and designer only: the app always passes the insert callback.</summary>
    public ChordFinderWindow() : this(_ => false) { }

    /// <param name="insertChord">Returns false when no beat is under the score cursor.</param>
    public ChordFinderWindow(Func<string, bool> insertChord)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _insertChord = insertChord;
        InitializeComponent();
        RootBox.ItemsSource = ChordFinderPresenter.RootChoices;
        RootBox.SelectedIndex = 0;
        TypeBox.ItemsSource = ChordFinderPresenter.TypeChoices();
        TypeBox.SelectedIndex = 0;
        ShowButton.Click += (_, _) => ShowNotes();
        InsertButton.Click += (_, _) => InsertName();
        CloseButton.Click += (_, _) => Close();
        Opened += (_, _) => ShowNotes();
    }

    private void ShowNotes()
    {
        NotesList.ItemsSource = ChordFinderPresenter.NoteLines(SelectedRoot(), SelectedType());
        StatusText.Text = "";
    }

    private void InsertName()
    {
        var name = ChordFinderPresenter.ChordName(SelectedRoot(), SelectedType());
        StatusText.Text = ChordFinderPresenter.InsertStatus(_insertChord(name), name);
    }

    private string SelectedRoot() => ChordFinderPresenter.RootOrDefault(RootBox.SelectedItem);

    private string SelectedType() => ChordFinderPresenter.TypeOrDefault(TypeBox.SelectedItem);
}
