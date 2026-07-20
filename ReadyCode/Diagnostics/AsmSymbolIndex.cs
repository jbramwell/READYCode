// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Assembler;

namespace ReadyCode.Diagnostics;

/// <summary>
/// What role a symbol plays at a particular <see cref="AsmSymbolOccurrence"/>.
/// </summary>
public enum AsmSymbolKind
{
    /// <summary>The line where a label is defined (before the colon).</summary>
    LabelDefinition,

    /// <summary>The line where a "NAME = value" constant is declared.</summary>
    ConstantDefinition,

    /// <summary>A line that references the symbol as an operand or ".word" entry.</summary>
    Reference,
}

/// <summary>
/// A single occurrence of a label or constant name at a specific source line.
/// </summary>
/// <param name="Name">The symbol's name.</param>
/// <param name="LineNumber">The 1-based source line the occurrence is on.</param>
/// <param name="Kind">What role the symbol plays at this occurrence.</param>
public readonly record struct AsmSymbolOccurrence(string Name, int LineNumber, AsmSymbolKind Kind);

/// <summary>
/// Scans 6502 assembly source for every label/constant definition and reference, by line. Reuses
/// <see cref="AsmLineParser"/> directly (rather than re-matching its regexes independently) so
/// there is exactly one source of truth for what counts as a symbol.
/// </summary>
public static class AsmSymbolIndex
{
    #region Public Methods

    /// <summary>
    /// Analyzes the given assembly source and returns every label/constant definition and
    /// reference found, in source order.
    /// </summary>
    /// <param name="source">The full assembly source to analyze.</param>
    public static IReadOnlyList<AsmSymbolOccurrence> Analyze(string source)
    {
        var parser = new AsmLineParser();
        string[] rawLines = source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var occurrences = new List<AsmSymbolOccurrence>();

        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = parser.ParseLine(rawLines[i], i + 1);

            if (line.Label != null)
                occurrences.Add(new AsmSymbolOccurrence(line.Label, line.LineNumber, AsmSymbolKind.LabelDefinition));

            if (line.ConstantName != null)
                occurrences.Add(new AsmSymbolOccurrence(line.ConstantName, line.LineNumber, AsmSymbolKind.ConstantDefinition));

            if (line.SymbolName != null)
                occurrences.Add(new AsmSymbolOccurrence(line.SymbolName, line.LineNumber, AsmSymbolKind.Reference));

            if (line.WordData != null)
                foreach (var entry in line.WordData)
                    if (entry.SymbolName != null)
                        occurrences.Add(new AsmSymbolOccurrence(entry.SymbolName, line.LineNumber, AsmSymbolKind.Reference));
        }

        return occurrences;
    }

    #endregion
}
