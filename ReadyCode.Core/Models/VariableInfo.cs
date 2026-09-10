// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReadyCode.Models;

/// <summary>
/// Represents a single variable in the active document's Variable Explorer tree, with every
/// read/write occurrence as a child node.
/// </summary>
public class VariableInfo : INotifyPropertyChanged
{
    #region Private Fields

    private bool _isExpanded;
    private bool _isRenaming;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableInfo"/> class.
    /// </summary>
    /// <param name="name">The variable's full name, including any trailing $ or % suffix.</param>
    public VariableInfo(string name)
    {
        Name = name;
        TypeBadge = name.EndsWith('$') ? "STR" : name.EndsWith('%') ? "INT" : "FLT";
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the variable's full name, including any trailing $ or % suffix.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the short type badge shown next to the variable's name ("STR", "INT", or "FLT"),
    /// inferred from the name's suffix - the same convention the hover tooltip uses.
    /// </summary>
    public string TypeBadge { get; }

    /// <summary>
    /// Gets every read/write occurrence of this variable, in source order.
    /// </summary>
    public ObservableCollection<VariableOccurrenceInfo> Occurrences { get; } = new();

    /// <summary>
    /// Gets or sets whether this variable's node is expanded in the tree view.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets whether this variable's name is currently being edited inline.
    /// </summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged();
        }
    }

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

/// <summary>
/// Represents a single read or write occurrence of a variable, shown as a leaf node under its
/// <see cref="VariableInfo"/> in the Variable Explorer tree.
/// </summary>
public class VariableOccurrenceInfo
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableOccurrenceInfo"/> class.
    /// </summary>
    /// <param name="documentLineNumber">The 1-based AvalonEdit document line this occurrence is on.</param>
    /// <param name="basicLineNumber">The BASIC line number shown to the user.</param>
    /// <param name="isWrite">Whether this occurrence assigns the variable, rather than just reading it.</param>
    public VariableOccurrenceInfo(int documentLineNumber, int basicLineNumber, bool isWrite)
    {
        DocumentLineNumber = documentLineNumber;
        BasicLineNumber = basicLineNumber;
        IsWrite = isWrite;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the 1-based AvalonEdit document line this occurrence is on, used to move the caret
    /// there when this node is clicked.
    /// </summary>
    public int DocumentLineNumber { get; }

    /// <summary>
    /// Gets the BASIC line number shown to the user.
    /// </summary>
    public int BasicLineNumber { get; }

    /// <summary>
    /// Gets whether this occurrence assigns the variable, rather than just reading it.
    /// </summary>
    public bool IsWrite { get; }

    /// <summary>
    /// Gets the text shown for this node, e.g. "Line 100 — Set".
    /// </summary>
    public string DisplayText => $"Line {BasicLineNumber} — {(IsWrite ? "Set" : "Read")}";

    #endregion
}
