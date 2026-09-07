// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// The bytes a single source line assembled to - used to show real memory addresses in the
/// editor's gutter (see <c>MainWindow.UpdateAsmGutterAddresses</c>). Only lines that actually emit
/// bytes (mnemonics, ".byte", ".word") get an entry - labels, comments, blank lines, constants,
/// and ".org" don't.
/// </summary>
/// <param name="LineNumber">1-based source line this entry covers.</param>
/// <param name="Address">The memory address the line's first byte was assembled to.</param>
/// <param name="Bytes">The bytes the line assembled to, in order.</param>
public readonly record struct AsmListingEntry(int LineNumber, ushort Address, IReadOnlyList<byte> Bytes);
