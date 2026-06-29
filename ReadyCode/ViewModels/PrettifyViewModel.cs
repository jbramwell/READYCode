// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReadyCode.Settings;

namespace ReadyCode.ViewModels;

/// <summary>
/// View model for the Prettify dialog's own persisted pass selections, independent of the
/// Settings window's transfer-time prettify options.
/// </summary>
public class PrettifyViewModel : INotifyPropertyChanged
{
    #region Private Fields

    private bool _addWhitespace;
    private bool _replacePeriodWithZero;
    private bool _useStandardNotation;
    private bool _addNextVariables;
    private bool _renumberLines;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="PrettifyViewModel"/> class, loading the
    /// dialog's persisted pass selections from <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">The application settings to read the initial selections from.</param>
    public PrettifyViewModel(AppSettings settings)
    {
        _addWhitespace         = settings.PrettifyDialogAddWhitespace;
        _replacePeriodWithZero = settings.PrettifyDialogReplacePeriodWithZero;
        _useStandardNotation   = settings.PrettifyDialogUseStandardNotation;
        _addNextVariables      = settings.PrettifyDialogAddNextVariables;
        _renumberLines         = settings.PrettifyDialogRenumberLines;
        LineNumberIncrement    = settings.AutoNumberIncrement;
        LineNumberPadding      = settings.LineNumberPadding;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the line number increment used when renumbering, read from the Text Editor settings.
    /// </summary>
    public int LineNumberIncrement { get; }

    /// <summary>
    /// Gets the line number zero-padding digit count used when renumbering, read from the
    /// Text Editor settings.
    /// </summary>
    public int LineNumberPadding   { get; }

    /// <summary>
    /// Gets or sets whether the prettify operation adds whitespace around keywords.
    /// </summary>
    public bool AddWhitespace
    {
        get => _addWhitespace;
        set { if (_addWhitespace == value) return; _addWhitespace = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the prettify operation replaces dots with leading zeroes.
    /// </summary>
    public bool ReplacePeriodWithZero
    {
        get => _replacePeriodWithZero;
        set { if (_replacePeriodWithZero == value) return; _replacePeriodWithZero = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the prettify operation expands scientific notation back to standard form.
    /// </summary>
    public bool UseStandardNotation
    {
        get => _useStandardNotation;
        set { if (_useStandardNotation == value) return; _useStandardNotation = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the prettify operation adds the loop variable back to NEXT statements.
    /// </summary>
    public bool AddNextVariables
    {
        get => _addNextVariables;
        set { if (_addNextVariables == value) return; _addNextVariables = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the prettify operation renumbers lines using <see cref="LineNumberIncrement"/>
    /// and <see cref="LineNumberPadding"/>.
    /// </summary>
    public bool RenumberLines
    {
        get => _renumberLines;
        set { if (_renumberLines == value) return; _renumberLines = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets a human-readable description of the renumbering scheme that will be applied.
    /// </summary>
    public string RenumberDescription =>
        LineNumberPadding > 0
            ? $"Starts at {LineNumberIncrement}, step {LineNumberIncrement}, {LineNumberPadding}-digit padding  (Text Editor settings)"
            : $"Starts at {LineNumberIncrement}, step {LineNumberIncrement}, no padding  (Text Editor settings)";

    #endregion

    #region Public Methods

    /// <summary>
    /// Writes the dialog's current pass selections back to <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">The application settings to persist the selections to.</param>
    public void ApplyTo(AppSettings settings)
    {
        settings.PrettifyDialogAddWhitespace         = AddWhitespace;
        settings.PrettifyDialogReplacePeriodWithZero = ReplacePeriodWithZero;
        settings.PrettifyDialogUseStandardNotation   = UseStandardNotation;
        settings.PrettifyDialogAddNextVariables      = AddNextVariables;
        settings.PrettifyDialogRenumberLines         = RenumberLines;
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}
