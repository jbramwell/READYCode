// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReadyCode.Settings;

namespace ReadyCode.ViewModels;

/// <summary>
/// View model for the Minify dialog's own persisted pass selections, independent of the
/// Settings window's transfer-time minify options.
/// </summary>
public class MinifyViewModel : INotifyPropertyChanged
{
    #region Private Fields

    private bool _removeWhitespace;
    private bool _replace0WithPeriod;
    private bool _useScientificNotation;
    private bool _removeComments;
    private bool _simplifyNextStatements;
    private bool _renumberLines;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MinifyViewModel"/> class, loading the
    /// dialog's persisted pass selections from <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">The application settings to read the initial selections from.</param>
    public MinifyViewModel(AppSettings settings)
    {
        _removeWhitespace      = settings.MinifyDialogRemoveWhitespace;
        _replace0WithPeriod    = settings.MinifyDialogReplaceZeroWithDot;
        _useScientificNotation = settings.MinifyDialogUseScientificNotation;
        _removeComments        = settings.MinifyDialogRemoveComments;
        _simplifyNextStatements = settings.MinifyDialogSimplifyNext;
        _renumberLines         = settings.MinifyDialogRenumberLines;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets whether the minify operation removes unnecessary whitespace.
    /// </summary>
    public bool RemoveWhitespace
    {
        get => _removeWhitespace;
        set { if (_removeWhitespace == value) return; _removeWhitespace = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the minify operation replaces zeroes with dots where possible.
    /// </summary>
    public bool Replace0WithPeriod
    {
        get => _replace0WithPeriod;
        set { if (_replace0WithPeriod == value) return; _replace0WithPeriod = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the minify operation converts long numbers to scientific notation.
    /// </summary>
    public bool UseScientificNotation
    {
        get => _useScientificNotation;
        set { if (_useScientificNotation == value) return; _useScientificNotation = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the minify operation removes comments (text after REM statements).
    /// </summary>
    public bool RemoveComments
    {
        get => _removeComments;
        set { if (_removeComments == value) return; _removeComments = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the minify operation simplifies NEXT statements.
    /// </summary>
    public bool SimplifyNextStatements
    {
        get => _simplifyNextStatements;
        set { if (_simplifyNextStatements == value) return; _simplifyNextStatements = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Gets or sets whether the minify operation renumbers lines to be sequential starting from 1.
    /// </summary>
    public bool RenumberLines
    {
        get => _renumberLines;
        set { if (_renumberLines == value) return; _renumberLines = value; OnPropertyChanged(); }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Writes the dialog's current pass selections back to <paramref name="settings"/>.
    /// </summary>
    /// <param name="settings">The application settings to persist the selections to.</param>
    public void ApplyTo(AppSettings settings)
    {
        settings.MinifyDialogRemoveWhitespace      = RemoveWhitespace;
        settings.MinifyDialogReplaceZeroWithDot    = Replace0WithPeriod;
        settings.MinifyDialogUseScientificNotation = UseScientificNotation;
        settings.MinifyDialogRemoveComments        = RemoveComments;
        settings.MinifyDialogSimplifyNext          = SimplifyNextStatements;
        settings.MinifyDialogRenumberLines         = RenumberLines;
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
