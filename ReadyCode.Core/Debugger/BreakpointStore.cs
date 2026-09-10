// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReadyCode.Debugger;

/// <summary>
/// A single BASIC line breakpoint, identified by source file and BASIC line number (not a memory
/// address or document line - both of those can shift as the file is edited or retargeted).
/// </summary>
public sealed class Breakpoint : INotifyPropertyChanged
{
    #region Private Fields

    private bool _isEnabled = true;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the path of the file this breakpoint belongs to.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the BASIC line number this breakpoint halts execution at.
    /// </summary>
    public required ushort LineNumber { get; init; }

    /// <summary>
    /// Gets or sets whether this breakpoint is currently active.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region Interface Implementations

    /// <summary>
    /// Occurs when <see cref="IsEnabled"/> changes, so a bound Breakpoints panel row can refresh
    /// its checkbox without the host needing to rebuild the whole list.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Private Methods

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion
}

/// <summary>
/// Holds every breakpoint set across the currently open project, independent of which files are
/// open in tabs right now - breakpoints are per-file, not per-tab, so they survive a tab being
/// closed and reopened within the same session. Persistence across app restarts is handled
/// separately (app-level storage, not this in-memory store).
/// </summary>
public sealed class BreakpointStore
{
    #region Private Fields

    private readonly ObservableCollection<Breakpoint> _breakpoints = new();

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets every breakpoint currently set, across all files - an <see cref="ObservableCollection{T}"/>
    /// so a bound Breakpoints panel updates itself as breakpoints are added/removed.
    /// </summary>
    public ObservableCollection<Breakpoint> Breakpoints => _breakpoints;

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets the breakpoint at the given file/line, or null if none is set there.
    /// </summary>
    public Breakpoint? Find(string filePath, ushort lineNumber) =>
        _breakpoints.FirstOrDefault(b => IsSameFile(b.FilePath, filePath) && b.LineNumber == lineNumber);

    /// <summary>
    /// Gets the BASIC line numbers with an enabled breakpoint in the given file.
    /// </summary>
    public IEnumerable<ushort> EnabledLinesFor(string filePath) =>
        _breakpoints.Where(b => IsSameFile(b.FilePath, filePath) && b.IsEnabled).Select(b => b.LineNumber);

    /// <summary>
    /// Gets the BASIC line numbers with a disabled breakpoint in the given file.
    /// </summary>
    public IEnumerable<ushort> DisabledLinesFor(string filePath) =>
        _breakpoints.Where(b => IsSameFile(b.FilePath, filePath) && !b.IsEnabled).Select(b => b.LineNumber);

    /// <summary>
    /// Adds a new enabled breakpoint if none exists at the given file/line, or removes it if one
    /// already does - the gutter-click behavior (click to set, click again to remove).
    /// </summary>
    /// <returns>The breakpoint that was added, or null if one was removed instead.</returns>
    public Breakpoint? Toggle(string filePath, ushort lineNumber)
    {
        var existing = Find(filePath, lineNumber);
        if (existing != null)
        {
            _breakpoints.Remove(existing);
            return null;
        }

        var breakpoint = new Breakpoint { FilePath = filePath, LineNumber = lineNumber };
        _breakpoints.Add(breakpoint);
        return breakpoint;
    }

    /// <summary>
    /// Removes the breakpoint at the given file/line, if one exists.
    /// </summary>
    public void Remove(string filePath, ushort lineNumber)
    {
        var existing = Find(filePath, lineNumber);
        if (existing != null)
            _breakpoints.Remove(existing);
    }

    /// <summary>
    /// Enables or disables the breakpoint at the given file/line, if one exists.
    /// </summary>
    public void SetEnabled(string filePath, ushort lineNumber, bool enabled)
    {
        var existing = Find(filePath, lineNumber);
        if (existing != null)
            existing.IsEnabled = enabled;
    }

    /// <summary>
    /// Removes every breakpoint, across all files.
    /// </summary>
    public void Clear() => _breakpoints.Clear();

    /// <summary>
    /// Replaces every breakpoint with the given set - used to load a project's breakpoints from
    /// persisted app-level storage, wholesale, rather than toggling them in one at a time.
    /// </summary>
    public void ReplaceAll(IEnumerable<Breakpoint> breakpoints)
    {
        _breakpoints.Clear();
        foreach (var breakpoint in breakpoints)
            _breakpoints.Add(breakpoint);
    }

    #endregion

    #region Private Methods

    private static bool IsSameFile(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    #endregion
}
