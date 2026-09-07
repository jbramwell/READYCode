// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;

namespace ReadyCode.Models;

/// <summary>
/// Updates an <see cref="ObservableCollection{T}"/> to match a freshly-loaded list via targeted
/// Add/Remove/Move instead of a full Clear+repopulate. Shared by each front end's local file-tree
/// model and <see cref="C64UFileItem"/>'s tree-refresh logic, so a refresh that only actually
/// changed one entry doesn't force the bound TreeView to re-virtualize every row and lose per-row
/// UI state (expanded/rename/drop-target flags) on entries that didn't change.
/// </summary>
public static class ObservableCollectionDiffUtil
{
    #region Internal Methods

    /// <summary>
    /// Reconciles <paramref name="current"/> to contain exactly the items in
    /// <paramref name="desired"/>, in that order, identifying "the same" item across both lists
    /// by <paramref name="keySelector"/>. Items whose key survives keep their existing instance
    /// (and therefore their UI state); items with a key not present in <paramref name="desired"/>
    /// are removed; genuinely new keys are inserted.
    /// </summary>
    public static void Apply<T>(ObservableCollection<T> current, IReadOnlyList<T> desired,
        Func<T, string> keySelector)
    {
        var desiredKeys = new HashSet<string>(desired.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var item in desired)
            desiredKeys.Add(keySelector(item));

        for (int i = current.Count - 1; i >= 0; i--)
            if (!desiredKeys.Contains(keySelector(current[i])))
                current.RemoveAt(i);

        // Fast path for a first load (nothing survived the removal pass above, e.g. the
        // single lazy-expansion placeholder): append in order instead of the O(n) per-item
        // "is it already here" scan below, which only pays for itself when most entries
        // already match - not when everything is new.
        if (current.Count == 0)
        {
            foreach (var item in desired)
                current.Add(item);
            return;
        }

        for (int i = 0; i < desired.Count; i++)
        {
            string key = keySelector(desired[i]);

            int existingIndex = -1;
            for (int j = i; j < current.Count; j++)
            {
                if (string.Equals(keySelector(current[j]), key, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex == i) continue;          // already the right item in the right spot
            if (existingIndex > i) current.Move(existingIndex, i); // present further down - reposition
            else current.Insert(i, desired[i]);         // not found at or after i - genuinely new
        }
    }

    #endregion
}
