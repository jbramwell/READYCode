// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="LanguageClassifier"/>.
/// </summary>
public class LanguageClassifierTests
{
    #region Public Methods

    [Theory]
    [InlineData("code.asm", EditorLanguage.Asm)]
    [InlineData("code.s", EditorLanguage.Asm)]
    [InlineData("code.bas", EditorLanguage.Basic)]
    [InlineData("code.prg", EditorLanguage.Basic)]
    [InlineData("noextension", EditorLanguage.Basic)]
    [InlineData("", EditorLanguage.Basic)]
    public void Classify_ByExtension_ReturnsExpectedLanguage(string nameOrPath, EditorLanguage expected)
    {
        Assert.Equal(expected, LanguageClassifier.Classify(nameOrPath));
    }

    [Fact]
    public void Classify_ExtensionIsCaseInsensitive()
    {
        Assert.Equal(EditorLanguage.Asm, LanguageClassifier.Classify("CODE.ASM"));
    }

    #endregion
}
