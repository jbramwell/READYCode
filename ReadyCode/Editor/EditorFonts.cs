// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Windows.Media;
using ReadyCode.Settings;

namespace ReadyCode.Editor;

/// <summary>
/// Centralizes the two fonts used for BASIC/PETSCII-styled content (the code editor,
/// Print/Print Preview, and File Compare), so all three consumers resolve the user's
/// PETSCII/Consolas font setting the same way instead of each loading their own copy.
/// </summary>
public static class EditorFonts
{
    private static readonly FontFamily _petscii =
        new(new Uri("pack://application:,,,/ReadyCode;component/Assets/Fonts/"), "./#Pet Me 64");
    private static readonly FontFamily _consolas = new("Consolas");

    /// <summary>The embedded Pet Me 64 PETSCII font, always used by the PETSCII Reference panel.</summary>
    public static FontFamily Petscii => _petscii;

    /// <summary>
    /// Resolves the user's PETSCII-slot font choice from <paramref name="settings"/> -
    /// <see cref="Consolas"/> when <c>PetsciiFontFamily == "Consolas"</c>, otherwise
    /// <see cref="Petscii"/>.
    /// </summary>
    public static FontFamily ResolvePetsciiSlot(AppSettings settings) =>
        settings.PetsciiFontFamily == "Consolas" ? _consolas : _petscii;
}
