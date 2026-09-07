// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Models;

/// <summary>
/// Which live machine an <see cref="EditorTab"/> in disassembly mode reads memory from.
/// </summary>
public enum DisassemblySource
{
    /// <summary>Reads memory from the C64 Ultimate over its REST API.</summary>
    C64U,

    /// <summary>Reads memory from a running VICE instance over its binary monitor.</summary>
    Vice,
}
