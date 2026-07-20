// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Assembler;

namespace ReadyCode.Diagnostics;

/// <summary>
/// Analyzes 6502 assembly source by running it through <see cref="Asm6502Assembler"/> and
/// surfacing every assembly error as a live diagnostic. Unlike <see cref="BasicDiagnostics"/>,
/// each reported span covers the whole source line, since <see cref="AssemblyError"/> only
/// carries a line number, not a column.
/// </summary>
public static class AsmDiagnostics
{
    #region Public Methods

    /// <summary>
    /// Analyzes the given assembly source and returns every assembly error found, one
    /// line-spanning diagnostic per <see cref="AssemblyError"/>.
    /// </summary>
    /// <param name="source">The full assembly source to analyze.</param>
    public static IReadOnlyList<EditorDiagnostic> Analyze(string source)
    {
        var result = new Asm6502Assembler().Assemble(source);
        if (result.Success) return [];

        var lines = new List<(string Line, int Offset)>(BasicDiagnostics.EnumerateLines(source));
        var diagnostics = new List<EditorDiagnostic>(result.Errors.Count);

        foreach (var error in result.Errors)
        {
            int index = error.LineNumber - 1;
            if (index < 0 || index >= lines.Count) continue;

            var (line, offset) = lines[index];
            diagnostics.Add(new EditorDiagnostic(offset, line.Length, error.Message));
        }

        diagnostics.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return diagnostics;
    }

    #endregion
}
