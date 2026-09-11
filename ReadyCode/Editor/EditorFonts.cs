// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Windows.Media;

namespace ReadyCode.Editor;

/// <summary>
/// Centralizes the two fonts used for BASIC/PETSCII-styled content and assembly/plain-ASCII
/// content (the code editor, Print/Print Preview, and File Compare), so all consumers load the
/// same instances instead of each constructing their own copy.
/// </summary>
public static class EditorFonts
{
    private static readonly FontFamily _petscii =
        new(new Uri("pack://application:,,,/ReadyCode;component/Assets/Fonts/"), "./#Pet Me 64");
    private static readonly FontFamily _consolas = new("Consolas");

    /// <summary>The embedded Pet Me 64 PETSCII font, used for BASIC (.bas and .prg alike).</summary>
    public static FontFamily Petscii => _petscii;

    /// <summary>Standard monospace font, used for assembly (.asm/.s) and disassembled machine code.</summary>
    public static FontFamily Consolas => _consolas;
}
