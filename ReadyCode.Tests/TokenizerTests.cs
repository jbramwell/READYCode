// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="BasicTokenizer"/>.
/// </summary>
public class TokenizerTests
{
    #region Public Methods

    // ── Keyword tokens ────────────────────────────────────────────────────────

    [Fact]
    public void TokenizeLine_ForKeywordIsTokenized()
    {
        // FOR = 0x81
        var bytes = Tokenize("FORI=1TO10");
        Assert.Equal(0x81, bytes[0]);
    }

    [Fact]
    public void TokenizeLine_ToKeywordIsTokenized()
    {
        // Minified: no space between 1 and TO and 10 — greedy scan must still find TO.
        var bytes = Tokenize("FORI=1TO10");
        // Expected: FOR(0x81) I(0x49) =(0xB2) 1(0x31) TO(0xA4) 1(0x31) 0(0x30)
        Assert.Equal(0xA4, bytes[4]); // TO token at position 4
    }

    [Fact]
    public void TokenizeLine_ForLoop_FullByteSequence()
    {
        // FORI=1TO5 → [FOR][I][=][1][TO][5]
        var bytes = Tokenize("FORI=1TO5");
        byte[] expected = [0x81, 0x49, 0xB2, 0x31, 0xA4, 0x35];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void TokenizeLine_MinifiedCodeHasNoSpaceBytes()
    {
        // Minified source has no whitespace, so no 0x20 bytes in the token stream.
        var minified = Tokenize("FORI=1TO10");
        Assert.DoesNotContain((byte)' ', minified);
    }

    [Fact]
    public void TokenizeLine_SpacedCodePreservesSpaceBytes()
    {
        // Spaces from source are kept (collapsed to one per run) so that non-minified
        // transfers display with the original formatting when LISTed on the C64.
        var spaced = Tokenize("FOR I=1 TO 10");
        Assert.Contains((byte)' ', spaced);
    }

    [Fact]
    public void TokenizeLine_PrintKeywordIsTokenized()
    {
        // PRINT = 0x99
        var bytes = Tokenize("PRINT\"HI\"");
        Assert.Equal(0x99, bytes[0]);
    }

    [Fact]
    public void TokenizeLine_GotoKeywordIsTokenized()
    {
        // GOTO = 0x89
        var bytes = Tokenize("GOTO10");
        Assert.Equal(0x89, bytes[0]);
    }

    [Fact]
    public void TokenizeLine_IfThenKeywordsAreTokenized()
    {
        // IF=0x8B, THEN=0xA7
        var bytes = Tokenize("IFX>5THEN10");
        Assert.Equal(0x8B, bytes[0]);   // IF
        Assert.Contains((byte)0xA7, bytes); // THEN
    }

    [Fact]
    public void TokenizeLine_PrintHashTakesPrecedenceOverPrint()
    {
        // PRINT# (0x98) must be matched before PRINT (0x99)
        var bytes = Tokenize("PRINT#1,A");
        Assert.Equal(0x98, bytes[0]);
    }

    [Fact]
    public void TokenizeLine_OperatorsAreTokenized()
    {
        // = is 0xB2, + is 0xAA, - is 0xAB
        var bytes = Tokenize("X=A+B");
        Assert.Contains((byte)0xB2, bytes); // =
        Assert.Contains((byte)0xAA, bytes); // +
    }

    // ── String literals ───────────────────────────────────────────────────────

    [Fact]
    public void TokenizeLine_KeywordsInsideStringsNotTokenized()
    {
        // "FOR" inside a string must remain as raw bytes, not token 0x81
        var bytes = Tokenize("PRINT\"FOR\"");
        // byte[0]=PRINT(0x99), byte[1]='"'(0x22), byte[2]='F'(0x46), ...
        Assert.Equal(0x99, bytes[0]);
        Assert.Equal((byte)'"', bytes[1]);
        Assert.Equal((byte)'F', bytes[2]);
        Assert.Equal((byte)'O', bytes[3]);
        Assert.Equal((byte)'R', bytes[4]);
    }

    // ── REM ───────────────────────────────────────────────────────────────────

    [Fact]
    public void TokenizeLine_RemTokenizedAndCommentCopiedVerbatim()
    {
        // REM=0x8F; the comment text must be preserved as-is
        var bytes = Tokenize("REM THIS IS A COMMENT");
        Assert.Equal(0x8F, bytes[0]);
        // The comment text starts after the REM byte (and optional space)
        string comment = System.Text.Encoding.ASCII.GetString(bytes[1..]);
        Assert.Contains("COMMENT", comment.ToUpperInvariant());
    }

    #endregion

    #region Private Methods

    private static byte[] Tokenize(string code) =>
        new BasicTokenizer().TokenizeLine(code).Tokens;

    #endregion
}
