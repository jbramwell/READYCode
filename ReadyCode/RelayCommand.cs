// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Input;

namespace ReadyCode;

/// <summary>
/// Simple <see cref="ICommand"/> implementation for data binding.
/// </summary>
public class RelayCommand : ICommand
{
    #region Private Fields

    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayCommand"/> class.
    /// </summary>
    /// <param name="execute">The action to invoke when the command is executed.</param>
    /// <param name="canExecute">An optional predicate that determines whether the command can currently execute.</param>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Occurs when the result of <see cref="CanExecute"/> may have changed.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Determines whether the command can execute in its current state.
    /// </summary>
    /// <param name="parameter">Data used by the command, or null.</param>
    /// <returns>true if the command can execute; otherwise, false.</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="parameter">Data used by the command, or null.</param>
    public void Execute(object? parameter) => _execute(parameter);

    #endregion
}
