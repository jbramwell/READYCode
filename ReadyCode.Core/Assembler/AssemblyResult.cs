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
    /// Gets or sets whether <see cref="Origin"/> came from an explicit ".org" directive in the
    /// source, rather than the implicit BASIC-loader-stub or standalone-output default. Used to
    /// decide whether showing per-line addresses (e.g. in the editor's gutter) would be
    /// meaningful - without an explicit origin, "address" is really just an implementation detail.
    /// </summary>
    public bool HasExplicitOrigin { get; set; }

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

    /// <summary>
    /// Gets or sets the address and bytes each source line assembled to, in source order - used
    /// to show real memory addresses in the editor's gutter (see
    /// <c>MainWindow.UpdateAsmGutterAddresses</c>). Empty unless <see cref="Success"/> is true.
    /// </summary>
    public IReadOnlyList<AsmListingEntry> ListingEntries { get; set; } = [];

    /// <summary>
    /// Gets or sets every source line as parsed during assembly, in source order. Populated
    /// regardless of <see cref="Success"/>, so a caller that also needs to index the source (e.g.
    /// <see cref="ReadyCode.Diagnostics.AsmSymbolIndex"/>) can reuse this instead of re-parsing
    /// the same text a second time.
    /// </summary>
    public IReadOnlyList<ParsedAsmLine> ParsedLines { get; set; } = [];

    #endregion
}
