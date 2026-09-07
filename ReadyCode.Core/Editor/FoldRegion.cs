// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Editor;

/// <summary>
/// A collapsible span of source text, as character offsets. The UI layer turns these into its
/// editor control's own folding objects (e.g. AvalonEdit's <c>NewFolding</c>).
/// </summary>
/// <param name="StartOffset">Offset where the fold begins (end of the fold's first line).</param>
/// <param name="EndOffset">Offset where the fold ends (end of the fold's last line).</param>
public readonly record struct FoldRegion(int StartOffset, int EndOffset);
