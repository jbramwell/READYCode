// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ICSharpCode.AvalonEdit.Document;
using ReadyCode.Diagnostics;
using ReadyCode.Diff;

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
            OnPropertyChanged(nameof(FullPath));
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
    /// Gets or sets whether this tab shows a read-only disassembly listing, generated from a
    /// live memory read rather than opened from a file. The editor stays read-only and the
    /// address-range toolbar stays visible while this is true; saving the tab (which requires
    /// choosing a real file path) clears it, turning the tab into an ordinary editable
    /// <c>.asm</c> tab from then on - see <c>MainWindow.FileSaveAs_Click</c>.
    /// </summary>
    public bool IsDisassemblyMode { get; set; }

    /// <summary>
    /// Gets or sets which live machine to read memory from while
    /// <see cref="IsDisassemblyMode"/> is true - the C64 Ultimate or a running VICE instance.
    /// Meaningless once disassembly mode is cleared.
    /// </summary>
    public DisassemblySource DisassemblySource { get; set; }

    /// <summary>
    /// Gets or sets the memory address each document line represents, keyed by 1-based line
    /// number, while <see cref="IsDisassemblyMode"/> is true - fed to
    /// <see cref="ReadyCode.Editor.AsmLineNumberMargin.LineAddresses"/> when this tab is active,
    /// so the gutter shows real addresses instead of sequential line numbers. Null once
    /// disassembly mode is cleared (see <c>MainWindow.FileSaveAs_Click</c>).
    /// </summary>
    public IReadOnlyDictionary<int, ushort>? DisassemblyLineAddresses { get; set; }

    /// <summary>
    /// Gets whether this tab shows the read-only File Compare view instead of the ordinary
    /// text/hex editor. Mirrors <see cref="IsHexMode"/>/<see cref="IsDisassemblyMode"/>'s
    /// "nullable field doubles as the mode discriminator" convention: true exactly when
    /// <see cref="CompareResult"/> is set.
    /// </summary>
    public bool IsCompareMode => CompareResult != null;

    /// <summary>
    /// Gets or sets the computed diff result backing this tab's File Compare view, or null for
    /// an ordinary tab.
    /// </summary>
    public FileCompareResult? CompareResult { get; set; }

    /// <summary>
    /// Gets or sets whether this tab's File Compare view is showing the unified (single-pane)
    /// layout rather than the default split (two-pane) layout. Meaningless unless
    /// <see cref="IsCompareMode"/> is true.
    /// </summary>
    public bool CompareIsUnified { get; set; }

    /// <summary>
    /// Gets or sets whether this tab's File Compare view is ignoring whitespace-only
    /// differences. Meaningless unless <see cref="IsCompareMode"/> is true.
    /// </summary>
    public bool CompareIgnoreWhitespace { get; set; }

    /// <summary>
    /// Gets the display file name, falling back to <see cref="DisplayName"/> or "Untitled"
    /// if the tab has no <see cref="FilePath"/>.
    /// </summary>
    public string FileName => FilePath != null ? Path.GetFileName(FilePath) : (DisplayName ?? "Untitled");

    /// <summary>
    /// Gets the untrimmed full path to show in a tab tooltip, falling back to
    /// <see cref="DisplayName"/> or "Untitled" if the tab has no <see cref="FilePath"/> - the
    /// same fallback <see cref="FileName"/> uses, but without trimming to just the file name.
    /// </summary>
    public string FullPath => FilePath ?? DisplayName ?? "Untitled";

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

    /// <summary>
    /// Gets or sets this tab's most recently computed diagnostics (squiggle-worthy problems).
    /// Recomputed whenever this tab is active and its document changes, or once right after it's
    /// first opened - stays valid while backgrounded, since <see cref="Document"/> can't change
    /// while this tab isn't the one loaded into the editor. Feeds the cross-tab Errors panel.
    /// </summary>
    public IReadOnlyList<EditorDiagnostic> Diagnostics { get; set; } = Array.Empty<EditorDiagnostic>();

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
