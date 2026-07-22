// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Models;

/// <summary>
/// Records byte-range edits made to a hex-mode tab's <see cref="EditorTab.RawBytes"/> so they
/// can be undone/redone - the hex-grid analog of AvalonEdit's per-<c>TextDocument</c> undo
/// stack. Kept on <see cref="EditorTab"/> itself, alongside <see cref="EditorTab.RawBytes"/>,
/// so switching tabs swaps undo history along with the bytes, the same way switching
/// <c>Editor.Document</c> switches AvalonEdit's undo history for text tabs.
/// </summary>
public sealed class HexUndoStack
{
    #region Private Fields

    private readonly record struct Edit(int Offset, byte[] OldValues, byte[] NewValues);

    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();

    #endregion

    #region Public Properties

    /// <summary>Gets whether there is an edit available to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether there is an edit available to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    #endregion

    #region Public Methods

    /// <summary>
    /// Records a completed edit and clears the redo history, matching standard undo/redo
    /// semantics (a fresh edit invalidates whatever was previously available to redo).
    /// </summary>
    /// <param name="offset">The byte offset the edit starts at.</param>
    /// <param name="oldValues">The bytes as they were before the edit.</param>
    /// <param name="newValues">The bytes as they are after the edit.</param>
    public void Push(int offset, byte[] oldValues, byte[] newValues)
    {
        _undo.Push(new Edit(offset, oldValues, newValues));
        _redo.Clear();
    }

    /// <summary>
    /// Reverts the most recent edit by writing its old values back into <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">The byte array to write the reverted values into.</param>
    /// <returns>The affected offset and length, or null if there was nothing to undo.</returns>
    public (int Offset, int Length)? Undo(byte[] bytes)
    {
        if (_undo.Count == 0) return null;

        Edit edit = _undo.Pop();
        Array.Copy(edit.OldValues, 0, bytes, edit.Offset, edit.OldValues.Length);
        _redo.Push(edit);
        return (edit.Offset, edit.OldValues.Length);
    }

    /// <summary>
    /// Reapplies the most recently undone edit by writing its new values back into
    /// <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">The byte array to write the reapplied values into.</param>
    /// <returns>The affected offset and length, or null if there was nothing to redo.</returns>
    public (int Offset, int Length)? Redo(byte[] bytes)
    {
        if (_redo.Count == 0) return null;

        Edit edit = _redo.Pop();
        Array.Copy(edit.NewValues, 0, bytes, edit.Offset, edit.NewValues.Length);
        _undo.Push(edit);
        return (edit.Offset, edit.NewValues.Length);
    }

    #endregion
}
