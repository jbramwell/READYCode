// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// The outcome of assembling a 6502 source program via <see cref="Asm6502Assembler"/>.
/// </summary>
public class AssemblyResult
{
    #region Public Properties

    /// <summary>
    /// Gets or sets whether assembly succeeded with no errors.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the complete, ready-to-transfer .prg bytes (BASIC loader stub followed by
    /// the assembled machine code), or null if assembly failed.
    /// </summary>
    public byte[]? PrgBytes { get; set; }

    /// <summary>
    /// Gets or sets every problem found while assembling, empty if <see cref="Success"/> is true.
    /// </summary>
    public IReadOnlyList<AssemblyError> Errors { get; set; } = [];

    #endregion
}
