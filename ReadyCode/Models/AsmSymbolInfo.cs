// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReadyCode.Diagnostics;

namespace ReadyCode.Models;

/// <summary>
/// Represents a single label or constant in the active assembly document's Symbol Explorer tree,
/// with every definition/reference occurrence as a child node.
/// </summary>
public class AsmSymbolInfo : INotifyPropertyChanged
{
    #region Private Fields

    private bool _isExpanded;
    private string _typeBadge;
    private string? _valueText;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AsmSymbolInfo"/> class.
    /// </summary>
    /// <param name="name">The symbol's name.</param>
    public AsmSymbolInfo(string name)
    {
        Name = name;
        _typeBadge = "LABEL";
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the symbol's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the short type badge shown next to the symbol's name ("LABEL" or "CONST").
    /// </summary>
    public string TypeBadge
    {
        get => _typeBadge;
        set
        {
            if (_typeBadge == value) return;
            _typeBadge = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the symbol's resolved value (e.g. "$080E"), or null if it couldn't be
    /// resolved (an undefined label, or a document with assembly errors).
    /// </summary>
    public string? ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value) return;
            _valueText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets every definition/reference occurrence of this symbol, in source order.
    /// </summary>
    public ObservableCollection<AsmSymbolOccurrenceInfo> Occurrences { get; } = new();

    /// <summary>
    /// Gets or sets whether this symbol's node is expanded in the tree view.
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
/// Represents a single definition or reference occurrence of a symbol, shown as a leaf node
/// under its <see cref="AsmSymbolInfo"/> in the Symbol Explorer tree.
/// </summary>
public class AsmSymbolOccurrenceInfo
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AsmSymbolOccurrenceInfo"/> class.
    /// </summary>
    /// <param name="documentLineNumber">The 1-based AvalonEdit document line this occurrence is on.</param>
    /// <param name="kind">What role the symbol plays at this occurrence.</param>
    public AsmSymbolOccurrenceInfo(int documentLineNumber, AsmSymbolKind kind)
    {
        DocumentLineNumber = documentLineNumber;
        Kind = kind;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the 1-based AvalonEdit document line this occurrence is on, used to move the caret
    /// there when this node is clicked.
    /// </summary>
    public int DocumentLineNumber { get; }

    /// <summary>
    /// Gets what role the symbol plays at this occurrence.
    /// </summary>
    public AsmSymbolKind Kind { get; }

    /// <summary>
    /// Gets the text shown for this node, e.g. "Line 10 — Defined" or "Line 42 — Used".
    /// </summary>
    public string DisplayText =>
        $"Line {DocumentLineNumber} — {(Kind == AsmSymbolKind.Reference ? "Used" : "Defined")}";

    #endregion
}
