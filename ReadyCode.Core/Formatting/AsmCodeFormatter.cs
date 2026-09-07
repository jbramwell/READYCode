// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using ReadyCode.Assembler;

namespace ReadyCode.Formatting;

/// <summary>
/// Applies the Assembly Formatting settings (mnemonic indent column, comment alignment column) to
/// every line of 6502 assembly source at once - the "Format Code" command's implementation.
/// Applies the same per-line rules AvalonEdit's Enter-key auto-indent applies as you type (see
/// MainWindow.InsertAsmNewlineWithIndent), just without any caret tracking, so both can share
/// <see cref="TryParseAsmMnemonicLine"/> to recognize a bare mnemonic line.
/// </summary>
public static class AsmCodeFormatter
{
    #region Private Fields

    private static readonly Regex _labelOnlyLinePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*:\s*$", RegexOptions.Compiled);

    #endregion

    #region Public Methods

    /// <summary>
    /// Reformats every line of assembly source: a bare mnemonic line (not preceded by a label) is
    /// re-indented to <paramref name="mnemonicIndentColumn"/> with its mnemonic upper-cased, and
    /// any inline ";" comment with real code before it is realigned to
    /// <paramref name="commentAlignColumn"/>. A whole-line comment (nothing before the ";") and an
    /// origin directive (".org" or "* =") are moved to column 1, regardless of how they were
    /// indented in source - both are always meant to stand out at the left margin. A label-only
    /// line or a blank line is left untouched.
    /// </summary>
    public static string Format(string source, int mnemonicIndentColumn, int commentAlignColumn)
    {
        string[] lines = source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
            lines[i] = FormatLine(lines[i], mnemonicIndentColumn, commentAlignColumn);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Determines whether the given trimmed line is a bare mnemonic line - not a label, not
    /// blank, not a directive/comment - splitting it into the mnemonic token and everything after
    /// it (operand, trailing comment, etc., untouched).
    /// </summary>
    public static bool TryParseAsmMnemonicLine(string trimmedLine, out string mnemonic, out string rest)
    {
        mnemonic = "";
        rest = "";
        if (trimmedLine.Length == 0 || _labelOnlyLinePattern.IsMatch(trimmedLine)) return false;

        int spaceIndex = trimmedLine.IndexOfAny([' ', '\t']);
        string firstWord = spaceIndex >= 0 ? trimmedLine[..spaceIndex] : trimmedLine;
        if (!OpcodeTable.Modes.ContainsKey(firstWord)) return false;

        mnemonic = firstWord;
        rest = spaceIndex >= 0 ? trimmedLine[spaceIndex..] : "";
        return true;
    }

    #endregion

    #region Private Methods

    private static string FormatLine(string lineText, int mnemonicIndentColumn, int commentAlignColumn)
    {
        string trimmedStart = lineText.TrimStart();
        string trimmed = trimmedStart.TrimEnd();

        string workingLine = lineText;
        if (trimmed.StartsWith(';') || IsOriginDirectiveLine(trimmed))
        {
            workingLine = trimmedStart;
        }
        else if (TryParseAsmMnemonicLine(trimmed, out string mnemonic, out string rest))
        {
            string indent = new string(' ', Math.Max(0, mnemonicIndentColumn - 1));
            workingLine = indent + mnemonic.ToUpperInvariant() + rest;
        }

        int semicolonIndex = workingLine.IndexOf(';');
        if (semicolonIndex > 0)
        {
            string codePart = workingLine[..semicolonIndex];
            if (!string.IsNullOrWhiteSpace(codePart))
            {
                string commentPart = workingLine[semicolonIndex..];
                string trimmedCode = codePart.TrimEnd();
                int targetLength = Math.Max(0, commentAlignColumn - 1);
                string alignedCode = trimmedCode.Length < targetLength ? trimmedCode.PadRight(targetLength) : trimmedCode + "  ";
                workingLine = alignedCode + commentPart;
            }
        }

        return workingLine;
    }

    // Recognizes an origin-setting line - ".org $c000" or its "* = $c000" alias (see
    // AsmLineParser's own handling of both spellings) - so Format can move it to column 1
    // regardless of how it was indented.
    private static bool IsOriginDirectiveLine(string trimmedLine)
    {
        if (trimmedLine.StartsWith(".org", StringComparison.OrdinalIgnoreCase) &&
            (trimmedLine.Length == 4 || char.IsWhiteSpace(trimmedLine[4])))
            return true;

        return trimmedLine.Length > 0 && trimmedLine[0] == '*' && trimmedLine[1..].TrimStart().StartsWith('=');
    }

    #endregion
}
