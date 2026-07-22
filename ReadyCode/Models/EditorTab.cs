// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ICSharpCode.AvalonEdit.Document;

namespace ReadyCode.Models;

/// <summary>
/// Represents a single open editor tab, including its document content, file association,
/// and the caret/scroll state to restore when the tab is reactivated.
/// </summary>
public class EditorTab : INotifyPropertyChanged
{
    #region Private Fields

    private bool _isModified;
    private string? _filePath;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the AvalonEdit document backing this tab's text content.
    /// </summary>
    public TextDocument Document { get; } = new();

    /// <summary>
    /// Gets or sets the full path to the file on disk, or null for an unsaved tab.
    /// </summary>
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
        }
    }

    /// <summary>
    /// Gets or sets the display name to use when this tab has no <see cref="FilePath"/>,
    /// such as a file opened from the C64 Ultimate rather than the local disk.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets a stable identifier for a tab opened from a "virtual" file with no real
    /// path on disk or FTP server (e.g. a program found inside a mounted .d64 image), used to
    /// detect and re-activate an already-open tab instead of opening a duplicate. Null for
    /// tabs backed by a real <see cref="FilePath"/>.
    /// </summary>
    public string? VirtualSourceId { get; set; }

    /// <summary>
    /// Gets or sets the raw bytes backing this tab when it's shown as a hex byte grid instead
    /// of text (e.g. a machine-language .prg, which has no meaningful text representation).
    /// Null for an ordinary text tab - mirrors the <see cref="VirtualSourceId"/> convention of
    /// a nullable field doubling as the tab's mode discriminator.
    /// </summary>
    public byte[]? RawBytes { get; set; }

    /// <summary>
    /// Gets whether this tab is displayed as a hex byte grid rather than as text in the editor.
    /// </summary>
    public bool IsHexMode => RawBytes != null;

    /// <summary>
    /// Gets the undo/redo history for edits made to <see cref="RawBytes"/> - the hex-mode
    /// analog of <see cref="Document"/>'s own built-in undo stack.
    /// </summary>
    public HexUndoStack UndoStack { get; } = new();

    /// <summary>
    /// Gets or sets this tab's file kind, as classified by <see cref="FileClassifier"/> when the
    /// tab was opened. Defaults to <see cref="C64UFileKind.Bas"/> for tabs with no backing file
    /// (a blank new tab, or an imported text file).
    /// </summary>
    public C64UFileKind Kind { get; set; } = C64UFileKind.Bas;

    /// <summary>
    /// Gets or sets the in-editor language this tab is edited as, classified by
    /// <see cref="LanguageClassifier"/> when the tab was opened. Selects which colorizers,
    /// completion provider, hover tooltips, and folding strategy are active for the tab.
    /// Independent of <see cref="Kind"/>, which is about C64/C64U file semantics.
    /// </summary>
    public EditorLanguage Language { get; set; } = EditorLanguage.Basic;

    /// <summary>
    /// Gets the display file name, falling back to <see cref="DisplayName"/> or "Untitled"
    /// if the tab has no <see cref="FilePath"/>.
    /// </summary>
    public string FileName => FilePath != null ? Path.GetFileName(FilePath) : (DisplayName ?? "Untitled");

    /// <summary>
    /// Gets or sets whether the tab has unsaved changes.
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (_isModified == value) return;
            _isModified = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the caret offset to restore when this tab is reactivated.
    /// </summary>
    public int CaretOffset { get; set; }

    /// <summary>
    /// Gets or sets the vertical scroll offset to restore when this tab is reactivated.
    /// </summary>
    public double ScrollOffsetY { get; set; }

    /// <summary>
    /// Gets the start offsets of folds that were collapsed the last time this tab was active, so
    /// switching away and back preserves fold state. In-memory only for the session, not persisted.
    /// </summary>
    public HashSet<int> CollapsedFoldStartOffsets { get; } = new();

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    #endregion
}
