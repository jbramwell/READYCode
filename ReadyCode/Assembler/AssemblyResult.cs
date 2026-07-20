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

    /// <summary>
    /// Gets or sets the address the assembled code starts at: the value given by an ".org"
    /// directive, or the default fixed origin ($080E) when none was present.
    /// </summary>
    public ushort Origin { get; set; }

    /// <summary>
    /// Gets or sets every label successfully resolved to an address during pass 1, keyed by
    /// name. Populated even when <see cref="Success"/> is false, since pass 1 assigns every
    /// label's address before any pass-2-only error could occur.
    /// </summary>
    public IReadOnlyDictionary<string, ushort> Labels { get; set; } = new Dictionary<string, ushort>();

    /// <summary>
    /// Gets or sets every "NAME = value" constant declared in the source, keyed by name.
    /// Populated even when <see cref="Success"/> is false, for the same reason as
    /// <see cref="Labels"/>.
    /// </summary>
    public IReadOnlyDictionary<string, int> Constants { get; set; } = new Dictionary<string, int>();

    #endregion
}
