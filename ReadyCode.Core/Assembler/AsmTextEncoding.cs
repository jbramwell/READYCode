// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// How a ".byte"/".text" directive's quoted string literals are converted to bytes - set via the
/// ".encoding" directive (KickAssembler-style, e.g. <c>.encoding "petscii_mixed"</c>), and applied
/// to every such literal from that point in the source until the next ".encoding" directive.
/// </summary>
public enum AsmTextEncoding
{
    /// <summary>Each character's plain ASCII code, unchanged - the default, and the only mode before an ".encoding" directive is seen.</summary>
    Ascii,

    /// <summary>
    /// PETSCII bytes suitable for KERNAL CHROUT output, assuming the C64's default uppercase/graphics
    /// charset (charset 1) is active on screen - every letter maps to the plain $41-$5A PETSCII range
    /// regardless of the source's own case, since that charset has no separate "shifted letter" glyph.
    /// </summary>
    PetsciiUpper,

    /// <summary>
    /// PETSCII bytes suitable for KERNAL CHROUT output, assuming the upper/lowercase charset
    /// (charset 2) is active on screen - a lowercase source letter maps to the plain $41-$5A PETSCII
    /// range (which displays as lowercase in that charset) and an uppercase source letter maps to
    /// $C1-$DA (which displays as uppercase), the well-known PETSCII case inversion.
    /// </summary>
    PetsciiMixed,

    /// <summary>Screen codes (for POKEing directly into screen memory) equivalent to <see cref="PetsciiUpper"/>.</summary>
    ScreenCodeUpper,

    /// <summary>Screen codes (for POKEing directly into screen memory) equivalent to <see cref="PetsciiMixed"/>.</summary>
    ScreenCodeMixed,
}
