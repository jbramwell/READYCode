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
