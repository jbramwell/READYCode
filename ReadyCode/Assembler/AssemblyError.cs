// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Assembler;

/// <summary>
/// A single problem found while assembling a line of 6502 assembly source.
/// </summary>
/// <param name="LineNumber">1-based source line the problem was found on.</param>
/// <param name="Message">Human-readable description of the problem.</param>
public readonly record struct AssemblyError(int LineNumber, string Message);
