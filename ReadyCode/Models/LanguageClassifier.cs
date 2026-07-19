// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;

namespace ReadyCode.Models;

/// <summary>
/// Classifies a file by its extension into the <see cref="EditorLanguage"/> the editor should
/// use for it. Independent of <see cref="FileClassifier"/>, which classifies C64/C64U file
/// semantics (runnability, transfer, icons) rather than in-editor language behavior.
/// </summary>
public static class LanguageClassifier
{
    /// <summary>
    /// Classifies a file by its name or path's extension. Any extension other than ".asm"/".s"
    /// (including no extension, e.g. a blank untitled tab) is classified as BASIC.
    /// </summary>
    public static EditorLanguage Classify(string nameOrPath) =>
        Path.GetExtension(nameOrPath).ToLowerInvariant() switch
        {
            ".asm" or ".s" => EditorLanguage.Asm,
            _ => EditorLanguage.Basic,
        };
}
