// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Debugger;
using ReadyCode.Tokenizer;
using Xunit;

namespace ReadyCode.Tests;

/// <summary>
/// Tests for <see cref="BasicLineAddressTable"/>.
/// </summary>
public class BasicLineAddressTableTests
{
    #region Public Methods

    [Fact]
    public void Build_SingleLine_FirstTokenAddressIsLoadAddressPlusFour()
    {
        var table = BasicLineAddressTable.Build("10 PRINT \"HI\"");

        // Link pointer (2) + line number (2) = 4 bytes before the first token.
        Assert.Equal(0x0801 + 4, table.LineAddresses[10]);
    }

    [Fact]
    public void Build_SecondLineAddress_MatchesActualTokenizedLayout()
    {
        const string source = "10 PRINT \"HI\"\n20 GOTO 10";
        var table = BasicLineAddressTable.Build(source);

        // Cross-check against the real tokenized PRG bytes: line 20's first token address is
        // the load address + the full byte size of line 10 + 4 (line 20's own link+linenum).
        byte[] prg = new PrgConverter().ConvertToPrg(source);
        int line10Size = 2 + 2 + new BasicTokenizer().TokenizeLine("PRINT \"HI\"").Tokens.Length + 1;
        ushort expectedLine20Address = (ushort)(0x0801 + line10Size + 4);

        Assert.Equal(expectedLine20Address, table.LineAddresses[20]);
        Assert.True(prg.Length > 0); // sanity: the PRG actually built successfully
    }

    [Fact]
    public void Build_DocumentLineMapping_IsOneBasedAndBidirectional()
    {
        var table = BasicLineAddressTable.Build("10 PRINT \"A\"\n20 PRINT \"B\"");

        Assert.Equal((ushort)10, table.DocumentLineToBasicLine[1]);
        Assert.Equal((ushort)20, table.DocumentLineToBasicLine[2]);
        Assert.Equal(1, table.BasicLineToDocumentLine[10]);
        Assert.Equal(2, table.BasicLineToDocumentLine[20]);
    }

    [Fact]
    public void Build_BlankLine_GetsNoDocumentLineEntry()
    {
        var table = BasicLineAddressTable.Build("10 PRINT \"A\"\n\n20 PRINT \"B\"");

        Assert.False(table.DocumentLineToBasicLine.ContainsKey(2));
        Assert.Equal((ushort)20, table.DocumentLineToBasicLine[3]);
    }

    [Fact]
    public void Build_LineNumberOnlyNoCode_GetsNoAddress()
    {
        var table = BasicLineAddressTable.Build("10\n20 PRINT \"HI\"");

        Assert.False(table.LineAddresses.ContainsKey(10));
        Assert.True(table.LineAddresses.ContainsKey(20));
    }

    [Fact]
    public void Build_DuplicateLineNumbers_LastOccurrenceWins()
    {
        var table = BasicLineAddressTable.Build("10 PRINT \"FIRST\"\n10 PRINT \"SECOND\"");

        // The second occurrence's document line should be what BasicLineToDocumentLine reports.
        Assert.Equal(2, table.BasicLineToDocumentLine[10]);
    }

    [Fact]
    public void Build_EmptySource_ProducesEmptyTables()
    {
        var table = BasicLineAddressTable.Build("");

        Assert.Empty(table.LineAddresses);
        Assert.Empty(table.DocumentLineToBasicLine);
        Assert.Empty(table.BasicLineToDocumentLine);
    }

    [Fact]
    public void Build_RemLine_StillGetsAnAddress()
    {
        // REM is real tokenized content - a breakpoint on a REM-only line is still technically
        // reachable, even though the UI may warn it's not useful to step "into".
        var table = BasicLineAddressTable.Build("10 REM A COMMENT");

        Assert.True(table.LineAddresses.ContainsKey(10));
    }

    #endregion
}
