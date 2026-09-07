// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Assembler;

/// <summary>
/// The outcome of disassembling a whole .prg file via <see cref="PrgFileDisassembler.Disassemble"/>.
/// </summary>
public class PrgFileDisassemblyResult
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the disassembled machine-code source text, not including
    /// <see cref="StubCommentLines"/>.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the memory address each line of <see cref="Source"/> represents, keyed by
    /// 1-based line number within <see cref="Source"/> alone (i.e. not yet offset by any
    /// prepended <see cref="StubCommentLines"/>).
    /// </summary>
    public IReadOnlyDictionary<int, ushort> LineAddresses { get; set; } = new Dictionary<int, ushort>();

    /// <summary>
    /// Gets or sets the comment lines describing a detected BASIC loader stub (e.g.
    /// "10 SYS 2064"), meant to be prepended before <see cref="Source"/>, or null if the file
    /// had no such stub.
    /// </summary>
    public IReadOnlyList<string>? StubCommentLines { get; set; }

    #endregion
}

/// <summary>
/// Disassembles a standalone .prg file - as opposed to a live memory read - detecting and
/// skipping past a BASIC loader stub (e.g. "10 SYS 2064") first, if one is present, so the
/// machine code disassembles starting at its real origin instead of misinterpreting the stub's
/// own tokenized bytes as 6502 opcodes. Shared by the "Disassemble file" context action
/// (<c>MainWindow.DisassembleFileBytes</c>) and the File Compare feature's content resolver
/// (<c>CompareFileResolver</c>), which both need this exact same stub-detection behavior.
/// </summary>
public static class PrgFileDisassembler
{
    #region Public Methods

    /// <summary>
    /// Disassembles a .prg file's bytes (including its 2-byte load-address header), detecting
    /// and skipping a leading BASIC loader stub first if present.
    /// </summary>
    /// <param name="prgBytes">The .prg file's raw bytes, including the 2-byte load-address header.</param>
    /// <param name="mnemonicIndentColumn">See <see cref="Asm6502Disassembler.Disassemble"/>.</param>
    /// <param name="commentAlignColumn">See <see cref="Asm6502Disassembler.Disassemble"/>.</param>
    public static PrgFileDisassemblyResult Disassemble(byte[] prgBytes, int mnemonicIndentColumn = 9, int commentAlignColumn = 32)
    {
        ushort origin = (ushort)(prgBytes[0] | (prgBytes[1] << 8));
        byte[] codeBytes = prgBytes[2..];

        List<string>? stubCommentLines = null;
        if (new PrgConverter().TryDetectBasicStub(prgBytes, out IReadOnlyList<string> stubLines, out int codeOffset))
        {
            origin = (ushort)(origin + (codeOffset - 2));
            codeBytes = prgBytes[codeOffset..];
            stubCommentLines = ["; --- BASIC loader stub ---", .. stubLines.Select(line => $"; {line}")];
        }

        var result = new Asm6502Disassembler().Disassemble(codeBytes, origin, mnemonicIndentColumn, commentAlignColumn);

        return new PrgFileDisassemblyResult
        {
            Source = result.Source,
            LineAddresses = result.LineAddresses,
            StubCommentLines = stubCommentLines,
        };
    }

    #endregion
}
