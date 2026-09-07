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
    /// <param name="isFunction">Whether this entry represents a <c>DEF FN</c> user function rather than a plain variable.</param>
    /// <param name="localToFunction">
    /// The name of the <c>DEF FN</c> function this entry's parameter belongs to, or <see langword="null"/>
    /// for an ordinary global variable. On real hardware, a <c>DEF FN</c> parameter aliases the
    /// same storage as a same-named global for the duration of the call and is restored
    /// afterward, so it behaves as a variable local to that one function - distinct from any
    /// global variable that happens to share its name.
    /// </param>
    public VariableInfo(string name, bool isFunction = false, string? localToFunction = null)
    {
        Name = name;
        IsFunction = isFunction;
        LocalToFunction = localToFunction;
        TypeBadge = isFunction ? "FN" : name.EndsWith('$') ? "STR" : name.EndsWith('%') ? "INT" : "FLT";
        DisplayName = localToFunction == null ? name : $"{name} (local to FN {localToFunction})";
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the variable's full name, including any trailing $ or % suffix. Used as-is for
    /// document text edits (renaming); see <see cref="DisplayName"/> for what the tree shows.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether this entry represents a <c>DEF FN</c> user function rather than a plain variable.
    /// </summary>
    public bool IsFunction { get; }

    /// <summary>
    /// Gets the name of the <c>DEF FN</c> function this entry's parameter is local to, or
    /// <see langword="null"/> for an ordinary global variable.
    /// </summary>
    public string? LocalToFunction { get; }

    /// <summary>
    /// Gets the text shown for this entry's name in the Variable Explorer tree - <see cref="Name"/>
    /// itself for a global variable or a function, or "{Name} (local to FN {LocalToFunction})" for
    /// a variable scoped to one <c>DEF FN</c>'s parameter.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the short type badge shown next to the variable's name ("STR", "INT", "FLT", or "FN"
    /// for a <c>DEF FN</c> function), inferred from the name's suffix - the same convention the
    /// hover tooltip uses.
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
    /// <param name="isWrite">Whether this occurrence assigns the variable (or, for a function, defines it), rather than just reading/calling it.</param>
    /// <param name="isFunction">Whether this occurrence belongs to a <c>DEF FN</c> function rather than a plain variable.</param>
    public VariableOccurrenceInfo(int documentLineNumber, int basicLineNumber, bool isWrite, bool isFunction = false)
    {
        DocumentLineNumber = documentLineNumber;
        BasicLineNumber = basicLineNumber;
        IsWrite = isWrite;
        IsFunction = isFunction;
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
    /// Gets whether this occurrence assigns the variable (or, for a function, defines it), rather
    /// than just reading/calling it.
    /// </summary>
    public bool IsWrite { get; }

    /// <summary>
    /// Gets whether this occurrence belongs to a <c>DEF FN</c> function rather than a plain variable.
    /// </summary>
    public bool IsFunction { get; }

    /// <summary>
    /// Gets the text shown for this node, e.g. "Line 100 — Set" for a variable or
    /// "Line 100 — Defined" for a function.
    /// </summary>
    public string DisplayText => IsFunction
        ? $"Line {BasicLineNumber} — {(IsWrite ? "Defined" : "Called")}"
        : $"Line {BasicLineNumber} — {(IsWrite ? "Set" : "Read")}";

    #endregion
}
