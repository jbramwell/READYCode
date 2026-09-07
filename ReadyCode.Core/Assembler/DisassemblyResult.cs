// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// The outcome of disassembling a byte range via <see cref="Asm6502Disassembler"/>.
/// </summary>
public class DisassemblyResult
{
    #region Public Properties

    /// <summary>
    /// Gets or sets the disassembled source text.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the memory address each instruction/data line in <see cref="Source"/>
    /// represents, keyed by 1-based line number. Lines with no entry (the ".org" line, and the
    /// blank line after it) aren't real memory locations.
    /// </summary>
    public IReadOnlyDictionary<int, ushort> LineAddresses { get; set; } = new Dictionary<int, ushort>();

    #endregion
}
