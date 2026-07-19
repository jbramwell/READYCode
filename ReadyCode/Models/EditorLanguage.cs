// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Models;

/// <summary>
/// The source language an <see cref="EditorTab"/> is edited as, selecting which colorizers,
/// completion provider, hover tooltips, and folding strategy are active for it.
/// </summary>
public enum EditorLanguage
{
    /// <summary>Commodore 64 BASIC V2 source.</summary>
    Basic,

    /// <summary>Standard 6502 assembly source.</summary>
    Asm,
}
