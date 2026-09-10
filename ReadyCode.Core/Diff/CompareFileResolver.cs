// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using ReadyCode.Assembler;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Diff;

/// <summary>
/// Turns a file's raw bytes into text ready for the File Compare feature to diff, branching on
/// its <see cref="C64UFileKind"/>: plain text for .bas/.asm, detokenized BASIC source for a
/// confirmed-BASIC .prg, and disassembled machine code source for a machine-language .prg. Free
/// of WPF/AvalonEdit types so it's directly unit testable.
/// </summary>
public static class CompareFileResolver
{
    #region Public Methods

    /// <summary>
    /// Determines whether two files are eligible to be compared with each other - they must
    /// share the same <see cref="C64UFileKind"/> (so, for example, ".asm" and ".s" can be
    /// compared with each other since both classify as <see cref="C64UFileKind.Asm"/>, but a
    /// ".bas" cannot be compared with a ".prg") and that kind must itself be one File Compare
    /// supports (folders, disk images, and unrecognized "Other" files are never comparable).
    /// </summary>
    public static bool CanCompare(ComparableFileRef left, ComparableFileRef right) =>
        left.Kind == right.Kind && IsComparableKind(left.Kind);

    /// <summary>
    /// Gets whether <paramref name="kind"/> is one File Compare knows how to resolve to text.
    /// </summary>
    public static bool IsComparableKind(C64UFileKind kind) =>
        kind is C64UFileKind.Bas or C64UFileKind.Prg or C64UFileKind.Ml or C64UFileKind.Asm;

    /// <summary>
    /// Resolves a file's raw bytes to comparable text.
    /// </summary>
    /// <param name="name">The file's display name.</param>
    /// <param name="bytes">The file's raw bytes.</param>
    /// <param name="kind">The file's kind, as classified when it was selected.</param>
    public static Resolved Resolve(string name, byte[] bytes, C64UFileKind kind)
    {
        switch (kind)
        {
            case C64UFileKind.Bas:
            case C64UFileKind.Asm:
                return new Resolved(name, DecodeSourceText(bytes), IsAsciiStyled: true, Warning: null);

            case C64UFileKind.Prg:
                try
                {
                    string text = new PrgConverter().ConvertFromPrg(bytes);
                    return new Resolved(name, text, IsAsciiStyled: false, Warning: null);
                }
                catch (Exception ex)
                {
                    return new Resolved(name, string.Empty, IsAsciiStyled: false,
                        Warning: $"Could not detokenize '{name}': {ex.Message}");
                }

            case C64UFileKind.Ml:
                if (bytes.Length < 2)
                    return new Resolved(name, string.Empty, IsAsciiStyled: true,
                        Warning: $"'{name}' is too small to disassemble.");
                try
                {
                    var disassembly = PrgFileDisassembler.Disassemble(bytes);
                    string text = disassembly.StubCommentLines != null
                        ? string.Join(Environment.NewLine, disassembly.StubCommentLines) + Environment.NewLine + disassembly.Source
                        : disassembly.Source;
                    return new Resolved(name, text, IsAsciiStyled: true, Warning: null);
                }
                catch (Exception ex)
                {
                    return new Resolved(name, string.Empty, IsAsciiStyled: true,
                        Warning: $"Could not disassemble '{name}': {ex.Message}");
                }

            default:
                return new Resolved(name, DecodeSourceText(bytes), IsAsciiStyled: true, Warning: null);
        }
    }

    /// <summary>
    /// Decodes bytes into source text, stripping a leading UTF-8 byte-order-mark if present, the
    /// same as <see cref="System.IO.File.ReadAllText(string)"/>'s own encoding detection does for
    /// a file opened directly from disk. A bare <see cref="Encoding.UTF8"/>.GetString does not
    /// strip it, and a leftover BOM character corrupts a source file's very first line - e.g.
    /// breaking comment/".org" recognition for a line that should start with ";" or "*", since the
    /// line then starts with U+FEFF instead (which, unlike ordinary whitespace,
    /// <see cref="string.Trim()"/> does not remove either). Public (rather than kept as a private
    /// member of the WPF-hosting window) so both this class and MainWindow.xaml.cs's Load/Run and
    /// tab-opening code paths share one implementation.
    /// </summary>
    public static string DecodeSourceText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        return Encoding.UTF8.GetString(bytes);
    }

    #endregion

    /// <summary>
    /// The result of resolving a file's bytes to comparable text.
    /// </summary>
    /// <param name="DisplayName">The file's display name, unchanged from the input.</param>
    /// <param name="Text">
    /// The resolved text, or empty if resolution failed (see <paramref name="Warning"/>).
    /// </param>
    /// <param name="IsAsciiStyled">
    /// Whether this file should render in the ASCII/Consolas editor font rather than the PETSCII
    /// font - true for everything except a detokenized BASIC .prg, which represents what would
    /// actually appear on a real C64 screen.
    /// </param>
    /// <param name="Warning">
    /// A message describing why <paramref name="Text"/> is empty/incomplete, or null if
    /// resolution succeeded.
    /// </param>
    public sealed record Resolved(string DisplayName, string Text, bool IsAsciiStyled, string? Warning);
}
