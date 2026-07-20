// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using ReadyCode.Tokenizer;

namespace ReadyCode.Models;

/// <summary>
/// Classifies a file or folder by its extension into a <see cref="C64UFileKind"/>. Shared by the
/// local Folder Explorer (<see cref="FileTreeItem"/>) and the C64 Ultimate FTP Explorer
/// (<see cref="C64UFileItem"/>), which previously each carried their own copy of this switch.
/// </summary>
public static class FileClassifier
{
    /// <summary>
    /// Classifies a folder, or a file by its extension.
    /// </summary>
    /// <param name="nameOrPath">The file or folder's name or full path.</param>
    /// <param name="isFolder">Whether the entry is a folder.</param>
    /// <param name="readPrgBytes">
    /// For a ".prg" file, a callback that returns its raw bytes so they can be sniffed with
    /// <see cref="PrgConverter.IsBasicProgram"/> to distinguish <see cref="C64UFileKind.Prg"/>
    /// (BASIC) from <see cref="C64UFileKind.Ml"/> (machine language). If the callback is null or
    /// throws, ".prg" is classified as <see cref="C64UFileKind.Prg"/> - e.g. for remote entries
    /// whose content hasn't been downloaded yet, or a local file that can't be read.
    /// </param>
    public static C64UFileKind Classify(string nameOrPath, bool isFolder, Func<byte[]>? readPrgBytes = null)
    {
        if (isFolder) return C64UFileKind.Folder;

        switch (Path.GetExtension(nameOrPath).ToLowerInvariant())
        {
            case ".bas": return C64UFileKind.Bas;
            case ".asm":
            case ".s": return C64UFileKind.Asm;
            case ".d64": return C64UFileKind.D64;
            case ".d81": return C64UFileKind.D81;
            case ".prg":
                if (readPrgBytes == null) return C64UFileKind.Prg;
                try
                {
                    return new PrgConverter().IsBasicProgram(readPrgBytes()) ? C64UFileKind.Prg : C64UFileKind.Ml;
                }
                catch
                {
                    return C64UFileKind.Prg;
                }
            default: return C64UFileKind.Other;
        }
    }

    /// <summary>
    /// Gets whether this kind is a disk image format (.d64 or .d81) that can be browsed in
    /// place, mounted to a drive, or authored.
    /// </summary>
    public static bool IsDiskImageKind(this C64UFileKind kind) => kind is C64UFileKind.D64 or C64UFileKind.D81;
}
