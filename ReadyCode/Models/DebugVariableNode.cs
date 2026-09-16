// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ReadyCode.Debugger;
using ReadyCode.Tokenizer;

namespace ReadyCode.Models;

/// <summary>
/// One row in the live debugger's Variables tree: either a leaf holding a simple variable's or
/// array element's current <see cref="BasicVariable"/>, or an array's collapsible root - shown
/// with its shape (e.g. "Float[3]") but with <see cref="Children"/> left empty until first
/// expanded, at which point <see cref="ReadyCode.ViewModels.MainViewModel.LoadArrayChildrenAsync"/>
/// fetches and decodes just that array's contents from the live session and populates them.
/// </summary>
public sealed class DebugVariableNode : INotifyPropertyChanged
{
    #region Private Fields

    private BasicVariable? _variable;
    private bool _isExpanded;
    private bool _isLoading;
    private bool _childrenLoaded;
    private bool _isEditing;
    private bool _isUpperCaseModeActive = true;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a leaf node for a simple variable or an already-decoded array element.
    /// </summary>
    public DebugVariableNode(BasicVariable variable)
    {
        Name = variable.Name;
        IsArray = false;
        TypeLabel = variable.Type.ToString();
        DimensionSizes = Array.Empty<int>();
        _variable = variable;
    }

    /// <summary>
    /// Initializes a collapsible array root. Its elements aren't fetched until
    /// <see cref="ReadyCode.ViewModels.MainViewModel.LoadArrayChildrenAsync"/> is called (normally
    /// triggered by the user expanding it - see <c>MainWindow.DebugVariablesTree_Expanded</c> - or
    /// by a refresh re-loading an array that's already expanded).
    /// </summary>
    public DebugVariableNode(string name, BasicVariableType elementType, IReadOnlyList<int> dimensionSizes, ushort dataAddress)
    {
        Name = name;
        IsArray = true;
        ElementType = elementType;
        DimensionSizes = dimensionSizes;
        DataAddress = dataAddress;
        TypeLabel = BuildTypeLabel(elementType, dimensionSizes);
    }

    #endregion

    #region Public Properties

    /// <summary>Gets the variable or array's own name, without any subscript.</summary>
    public string Name { get; }

    /// <summary>Gets whether this node is a collapsible array root rather than a leaf.</summary>
    public bool IsArray { get; }

    /// <summary>
    /// Gets the text shown in the Type column - "Float"/"Integer"/"String" for a leaf, or a shape
    /// summary like "Float[3]"/"Integer[2,3]" for an array root. See <see cref="RefreshArrayShape"/>
    /// for why this has a private setter.
    /// </summary>
    public string TypeLabel { get; private set; }

    /// <summary>
    /// Gets the current live variable a leaf node represents, or null for an array root (which
    /// has no value of its own - see <see cref="Children"/>).
    /// </summary>
    public BasicVariable? Variable
    {
        get => _variable;
        private set
        {
            _variable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStringValue));
            OnPropertyChanged(nameof(ValueDisplayText));
        }
    }

    /// <summary>
    /// Gets whether this leaf's value is a string - the only case the Value column's PETSCII
    /// ("Pet Me 64") font applies to, so a control-code byte in a string value renders as its real
    /// C64 glyph without forcing every plain float/integer value onto that same bitmap font too.
    /// </summary>
    public bool IsStringValue => Variable?.Type == BasicVariableType.String;

    /// <summary>
    /// Gets or sets whether the debugged tab's default "Upper Active" C64 charset is in effect, as
    /// opposed to "Upper Inactive"/Lower Case Mode (see <c>EditorTab.IsUpperCaseModeActive</c>) -
    /// only affects <see cref="ValueDisplayText"/> for a string value (a string's PETSCII graphics
    /// characters display as plain lower case letters instead, in Lower Case Mode - see
    /// <see cref="PetsciiScreenCodeMap.ToDisplayText(string, bool)"/>). Kept in sync with the
    /// debugged tab by <c>MainViewModel</c> (on every refresh, and every node's) and
    /// <c>MainWindow.RefreshDebugVariablesDisplayMode</c> (immediately when the debugged tab's own
    /// mode is toggled), rather than this node reaching out to the tab/session itself.
    /// </summary>
    public bool IsUpperCaseModeActive
    {
        get => _isUpperCaseModeActive;
        set
        {
            if (_isUpperCaseModeActive == value) return;
            _isUpperCaseModeActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueDisplayText));
        }
    }

    /// <summary>
    /// Gets this leaf's value formatted for display - a float to 9 significant digits (matching
    /// C64 BASIC's own display precision), a string in quotes with its case/PETSCII graphics
    /// matching <see cref="IsUpperCaseModeActive"/>, or an integer as a plain signed decimal.
    /// Empty for an array root (no <see cref="Variable"/> of its own) or a string whose value
    /// hasn't finished resolving yet.
    /// </summary>
    public string ValueDisplayText
    {
        get
        {
            if (Variable is not { } variable) return string.Empty;

            return variable.Type switch
            {
                BasicVariableType.Float when variable.Value is double d => d.ToString("G9", CultureInfo.InvariantCulture),
                BasicVariableType.String when variable.Value is ResolvedStringValue s =>
                    $"\"{PetsciiScreenCodeMap.ToDisplayText(s.Text, IsUpperCaseModeActive)}\"",
                BasicVariableType.String => string.Empty, // not yet resolved
                _ => variable.Value.ToString() ?? string.Empty,
            };
        }
    }

    /// <summary>Gets every element's type. Only meaningful when <see cref="IsArray"/> is true.</summary>
    public BasicVariableType ElementType { get; }

    /// <summary>
    /// Gets each dimension's element count. Only meaningful when <see cref="IsArray"/> is true. See
    /// <see cref="RefreshArrayShape"/> for why this has a private setter.
    /// </summary>
    public IReadOnlyList<int> DimensionSizes { get; private set; }

    /// <summary>
    /// Gets the live address of element (0,0,...). Only meaningful when <see cref="IsArray"/> is
    /// true. See <see cref="RefreshArrayShape"/> for why this has a private setter.
    /// </summary>
    public ushort DataAddress { get; private set; }

    /// <summary>
    /// Gets an array root's element rows, populated lazily on first expand. Always empty for a
    /// leaf node - an empty <c>ItemsSource</c> is what keeps a leaf's TreeViewItem from showing an
    /// expander arrow at all.
    /// </summary>
    public ObservableCollection<DebugVariableNode> Children { get; } = new();

    /// <summary>
    /// Gets or sets whether this node's TreeViewItem is expanded. Setting this to true on an
    /// array root whose children aren't loaded is what triggers the lazy fetch.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>Gets or sets whether this array root's elements are currently being fetched.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether this array root's <see cref="Children"/> reflect the machine's current
    /// stopped state. Reset on every debug-session refresh (see
    /// <see cref="ReadyCode.ViewModels.MainViewModel.RefreshDebugVariablesAndCallStackAsync"/>) so
    /// a stale expand can't show a previous stop's values, and checked before fetching so
    /// re-expanding within the same stop doesn't re-fetch unnecessarily.
    /// </summary>
    public bool ChildrenLoaded
    {
        get => _childrenLoaded;
        set { if (_childrenLoaded == value) return; _childrenLoaded = value; OnPropertyChanged(); }
    }

    /// <summary>Gets or sets whether this leaf's value is currently being edited inline.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set { if (_isEditing == value) return; _isEditing = value; OnPropertyChanged(); }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates a leaf node's live variable in place after a refresh or a successful edit
    /// write-back, notifying the UI without losing the node's identity (and so its
    /// selection/expansion state in the tree).
    /// </summary>
    public void UpdateVariable(BasicVariable variable) => Variable = variable;

    /// <summary>
    /// Updates an array root's shape/address in place after a refresh reuses this node's identity
    /// (see <c>MainViewModel.RefreshDebugVariablesAndCallStackAsync</c>) - a same-named array's
    /// dimensions or table position could in principle shift between stops (e.g. after other
    /// variables are added earlier in the table), so a reused node's stale metadata would
    /// otherwise load the wrong bytes the next time it's expanded.
    /// </summary>
    public void RefreshArrayShape(IReadOnlyList<int> dimensionSizes, ushort dataAddress)
    {
        DimensionSizes = dimensionSizes;
        DataAddress = dataAddress;
        TypeLabel = BuildTypeLabel(ElementType, dimensionSizes);
        OnPropertyChanged(nameof(DimensionSizes));
        OnPropertyChanged(nameof(DataAddress));
        OnPropertyChanged(nameof(TypeLabel));
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    // Shown as the DIM bound (the highest valid 0-based index), not the element count each
    // dimension actually stores - DIM X(3,3) should read "Float[3,3]", not "Float[4,4]"
    // (dimensionSizes[d] is 4, since DIM N always allocates N+1 elements for indices 0 through N -
    // see VariableTableParser.TryParseArrayHeader).
    private static string BuildTypeLabel(BasicVariableType elementType, IReadOnlyList<int> dimensionSizes)
    {
        var bounds = new int[dimensionSizes.Count];
        for (int i = 0; i < dimensionSizes.Count; i++)
            bounds[i] = dimensionSizes[i] - 1;
        return $"{elementType}[{string.Join(",", bounds)}]";
    }

    private void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    #endregion
}
