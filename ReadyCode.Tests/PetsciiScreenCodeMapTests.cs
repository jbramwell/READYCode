// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="PetsciiScreenCodeMap"/>.
/// </summary>
public class PetsciiScreenCodeMapTests
{
    #region Public Methods

    [Theory]
    [InlineData(0x00, 0x80)]
    [InlineData(0x20, 0x20)]
    [InlineData(0x40, 0x00)] // '@'
    [InlineData(0x41, 0x01)] // 'A' - uppercase letters land at screen codes 1-26
    [InlineData(0x5A, 0x1A)] // 'Z'
    [InlineData(0x60, 0x40)]
    [InlineData(0x61, 0x41)] // 'a'
    [InlineData(0x7A, 0x5A)] // 'z'
    [InlineData(0x93, 0xD3)] // CHR$(147), CLR/HOME
    [InlineData(0xA0, 0x60)]
    [InlineData(0xFF, 0x5E)] // pi - explicitly special-cased rather than derived from the offset table
    public void ToScreenCode_KnownPetsciiValues_MapToExpectedScreenCode(byte petscii, byte expectedScreenCode)
    {
        Assert.Equal(expectedScreenCode, PetsciiScreenCodeMap.ToScreenCode(petscii));
    }

    [Fact]
    public void ToScreenCode_EveryByteValue_DoesNotThrow()
    {
        for (int i = 0; i <= 255; i++)
            PetsciiScreenCodeMap.ToScreenCode((byte)i);
    }

    #endregion
}
