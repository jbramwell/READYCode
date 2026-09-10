// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using ReadyCode.Tokenizer;

namespace ReadyCode.Debugger;

/// <summary>
/// Maps BASIC line numbers to the memory address of their first token byte, and maps between
/// those line numbers and 1-based AvalonEdit document line numbers - built by walking the source
/// exactly as <see cref="PrgConverter.ConvertToPrg"/> does, so the addresses always match what
/// actually gets transferred to a running machine. Used to resolve breakpoint-gutter clicks to
/// BASIC line numbers, and to translate a halted line number back to an editor line to highlight.
/// </summary>
public sealed class BasicLineAddressTable
{
    #region Private Fields

    private readonly Dictionary<ushort, ushort> _lineAddresses;
    private readonly Dictionary<int, ushort> _documentLineToBasicLine;
    private readonly Dictionary<ushort, int> _basicLineToDocumentLine;

    #endregion

    #region Constructors

    private BasicLineAddressTable(
        Dictionary<ushort, ushort> lineAddresses,
        Dictionary<int, ushort> documentLineToBasicLine,
        Dictionary<ushort, int> basicLineToDocumentLine)
    {
        _lineAddresses = lineAddresses;
        _documentLineToBasicLine = documentLineToBasicLine;
        _basicLineToDocumentLine = basicLineToDocumentLine;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the memory address of each BASIC line's first token byte (i.e. just after its
    /// 2-byte link pointer and 2-byte line-number field), keyed by BASIC line number.
    /// </summary>
    public IReadOnlyDictionary<ushort, ushort> LineAddresses => _lineAddresses;

    /// <summary>
    /// Gets the BASIC line number for each 1-based document line that resolved to a real,
    /// tokenizable BASIC line. Blank lines and line-number-only lines (no code) have no entry,
    /// matching <see cref="PrgConverter.ConvertToPrg"/>'s own behavior of skipping them.
    /// </summary>
    public IReadOnlyDictionary<int, ushort> DocumentLineToBasicLine => _documentLineToBasicLine;

    /// <summary>
    /// Gets the 1-based document line for each BASIC line number - the inverse of
    /// <see cref="DocumentLineToBasicLine"/>. When a line number appears more than once in the
    /// source, this reflects its last occurrence, matching which one actually wins in the
    /// tokenized program.
    /// </summary>
    public IReadOnlyDictionary<ushort, int> BasicLineToDocumentLine => _basicLineToDocumentLine;

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds a line address table from BASIC source text.
    /// </summary>
    /// <param name="sourceCode">The BASIC source, exactly as it appears in the editor.</param>
    /// <param name="loadAddress">The address the program will be loaded at (standard C64 default is $0801).</param>
    public static BasicLineAddressTable Build(string sourceCode, ushort loadAddress = 0x0801)
    {
        var tokenizer = new BasicTokenizer();
        var lines = PrgConverter.SplitSourceLines(sourceCode);

        var lineAddresses = new Dictionary<ushort, ushort>();
        var documentLineToBasicLine = new Dictionary<int, ushort>();
        var basicLineToDocumentLine = new Dictionary<ushort, int>();

        ushort currentAddress = loadAddress;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmedLine = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            var parsed = PrgConverter.ParseLineNumberAndCode(trimmedLine);
            if (parsed == null)
                continue;

            ushort lineNumber = parsed.Value.lineNumber;
            string code = parsed.Value.code;

            // A line with only a line number and no code never becomes a real BASIC line -
            // ConvertToPrg skips it too, so it gets no address and can't be a breakpoint target.
            if (string.IsNullOrWhiteSpace(code))
                continue;

            var tokenResult = tokenizer.TokenizeLine(code);
            if (!tokenResult.Success)
                continue;

            int documentLine = i + 1; // 1-based, matching DisassemblyResult's convention
            ushort firstTokenAddress = (ushort)(currentAddress + 4); // past link pointer + line number

            lineAddresses[lineNumber] = firstTokenAddress;
            documentLineToBasicLine[documentLine] = lineNumber;
            basicLineToDocumentLine[lineNumber] = documentLine;

            int lineSize = 2 /* link */ + 2 /* line number */ + tokenResult.Tokens.Length + 1 /* terminator */;
            currentAddress = (ushort)(currentAddress + lineSize);
        }

        return new BasicLineAddressTable(lineAddresses, documentLineToBasicLine, basicLineToDocumentLine);
    }

    #endregion
}
