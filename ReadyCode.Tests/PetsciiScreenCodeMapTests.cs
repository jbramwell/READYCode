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

    // ── ToPetscii ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x05)] // white
    [InlineData(0x12)] // reverse-on
    [InlineData(0x1D)] // cursor-right
    [InlineData(0x1F)]
    [InlineData(0x80)]
    [InlineData(0x93)] // CLR/HOME
    [InlineData(0x9F)]
    public void ToPetscii_ControlCodeRanges_RoundTripsExactly(byte petscii)
    {
        // The control-code ranges (0x00-0x1F, 0x80-0x9F) this feature targets don't collide with
        // anything else in the underlying table, unlike some other ranges (see ToPetscii's doc
        // comment) - so these must round-trip byte-for-byte.
        byte screenCode = PetsciiScreenCodeMap.ToScreenCode(petscii);
        Assert.Equal(petscii, PetsciiScreenCodeMap.ToPetscii(screenCode));
    }

    // ── NeedsGlyphSubstitution ───────────────────────────────────────────────

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x1D, true)]  // cursor-right
    [InlineData(0x20, false)] // space
    [InlineData(0x41, false)] // 'A'
    [InlineData(0x5B, false)] // '['
    [InlineData(0x5C, true)]  // £, not \
    [InlineData(0x5D, false)] // ']'
    [InlineData(0x5E, true)]  // ↑
    [InlineData(0x7E, true)]
    [InlineData(0x93, true)]  // CLR/HOME
    public void NeedsGlyphSubstitution_MatchesExpectedRanges(byte petscii, bool expected)
    {
        Assert.Equal(expected, PetsciiScreenCodeMap.NeedsGlyphSubstitution(petscii));
    }

    // ── ToDisplayText / FromDisplayText ──────────────────────────────────────

    [Fact]
    public void ToDisplayText_ThenFromDisplayText_RoundTripsRealWorldControlCodeString()
    {
        // The exact bytes behind colortest_prg.prg's A$="BLACK" DATA entry: cursor-right,
        // reverse-on, white, "BLACK", cursor-right - the string that surfaced this feature's bug
        // (those 4 control bytes were invisible in the grid, so editing the string silently wiped
        // them out via space-padding). Built via char concatenation rather than string-literal
        // escapes - C#'s \x escape is greedy/variable-length and would swallow "B" into \x05B
        // (=$5B, '[') instead of stopping after two hex digits.
        string raw = (char)0x1D + "" + (char)0x12 + (char)0x05 + "BLACK" + (char)0x1D;

        string display = PetsciiScreenCodeMap.ToDisplayText(raw);
        Assert.Equal(raw.Length, display.Length);
        Assert.Contains("BLACK", display);

        Assert.Equal(raw, PetsciiScreenCodeMap.FromDisplayText(display));
    }

    [Fact]
    public void ToDisplayText_PlainAsciiText_PassesThroughUnchanged()
    {
        Assert.Equal("HELLO WORLD", PetsciiScreenCodeMap.ToDisplayText("HELLO WORLD"));
    }

    [Fact]
    public void FromDisplayText_PlainAsciiText_IsNoOp()
    {
        Assert.Equal("HELLO WORLD", PetsciiScreenCodeMap.FromDisplayText("HELLO WORLD"));
    }

    #endregion
}
