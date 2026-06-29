// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace ReadyCode.Tokenizer;

/// <summary>
/// Converts tokenized BASIC to .prg binary format for Commodore 64.
/// </summary>
public class PrgConverter
{
    #region Private Fields

    // Standard C64 BASIC load address
    private const ushort _loadAddress = 0x0801;

    #endregion

    #region Public Properties

    /// <summary>
    /// Debug information from the last conversion.
    /// </summary>
    public string? LastDebugInfo { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Converts BASIC source code to .prg binary format.
    /// </summary>
    public byte[] ConvertToPrg(string sourceCode)
    {
        var tokenizer = new BasicTokenizer();
        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // First pass: parse and tokenize all lines
        var parsedLines = new List<(ushort lineNumber, byte[] tokens)>();
        var debugLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            debugLines.Add($"Parsing: '{trimmedLine}'");

            // Parse line number and code
            var parts = ParseLineNumberAndCode(trimmedLine);
            if (parts == null)
            {
                debugLines.Add($"  ERROR: Failed to parse line number");
                continue;
            }

            var lineNumber = parts.Value.lineNumber;
            var code = parts.Value.code;

            // Skip lines that have only a line number and no actual code
            if (string.IsNullOrWhiteSpace(code))
            {
                debugLines.Add($"  SKIP: line {lineNumber} has no code");
                continue;
            }

            debugLines.Add($"  LineNum: {lineNumber}, Code: '{code}'");

            // Tokenize the code part
            var tokenResult = tokenizer.TokenizeLine(code);
            if (!tokenResult.Success)
            {
                debugLines.Add($"  ERROR: Tokenization failed - {tokenResult.ErrorMessage}");
                continue;
            }

            debugLines.Add($"  OK: {tokenResult.Tokens.Length} bytes");
            parsedLines.Add((lineNumber, tokenResult.Tokens));
        }

        // Store debug info for later retrieval
        LastDebugInfo = string.Join("\n", debugLines);

        if (parsedLines.Count == 0)
        {
            // Return minimal valid PRG with just load address and end marker
            return [0x01, 0x08, 0x00, 0x00];
        }

        var programData = new List<byte>();

        // Add load address (little endian: low byte, high byte)
        programData.Add((byte)(_loadAddress & 0xFF));
        programData.Add((byte)((_loadAddress >> 8) & 0xFF));

        // Second pass: build program with proper next-line addresses
        for (int i = 0; i < parsedLines.Count; i++)
        {
            var (lineNumber, tokens) = parsedLines[i];

            // Build the line: [next address (2)] [line number (2)] [tokens] [0x00]
            var lineBytes = new List<byte>();

            // Placeholder for next line address (will be filled in later)
            int nextAddressOffset = lineBytes.Count;
            lineBytes.Add(0);
            lineBytes.Add(0);

            // Line number (little endian)
            lineBytes.Add((byte)(lineNumber & 0xFF));
            lineBytes.Add((byte)((lineNumber >> 8) & 0xFF));

            // Tokenized code
            lineBytes.AddRange(tokens);

            // Line terminator
            lineBytes.Add(0x00);

            // Calculate the current address in the program
            // Account for: load address header (2 bytes, not part of the in-memory program)
            ushort currentLineAddress = (ushort)(_loadAddress + programData.Count - 2);

            // Next line address = current address + this line's size.
            // Even the last line needs a valid link pointer - it points at the
            // trailing 0x00 0x00 end-of-program marker, which is what actually
            // signals "no more lines" to BASIC.
            ushort nextAddress = (ushort)(currentLineAddress + lineBytes.Count);

            // Fill in the next line address
            lineBytes[nextAddressOffset] = (byte)(nextAddress & 0xFF);
            lineBytes[nextAddressOffset + 1] = (byte)((nextAddress >> 8) & 0xFF);

            programData.AddRange(lineBytes);
        }

        // Program end marker: 0x00 0x00
        programData.Add(0x00);
        programData.Add(0x00);

        return [..programData];
    }

    /// <summary>
    /// Converts a .prg binary file back into its original BASIC source text.
    /// </summary>
    public string ConvertFromPrg(byte[] data)
    {
        if (data.Length < 4)
            throw new FormatException("File is too small to be a valid C64 program.");

        var lines = new List<string>();

        // Skip the 2-byte load address header
        int pos = 2;

        while (pos + 1 < data.Length)
        {
            // Link address - a value of 0x0000 marks the end of the program
            ushort link = (ushort)(data[pos] | (data[pos + 1] << 8));
            pos += 2;

            if (link == 0x0000)
                break;

            if (pos + 1 >= data.Length)
                break;

            // Line number (little endian)
            ushort lineNumber = (ushort)(data[pos] | (data[pos + 1] << 8));
            pos += 2;

            // Tokens run until the line terminator (0x00)
            var tokens = new List<byte>();
            while (pos < data.Length && data[pos] != 0x00)
            {
                tokens.Add(data[pos]);
                pos++;
            }

            // Skip the line terminator
            if (pos < data.Length)
                pos++;

            lines.Add($"{lineNumber} {DetokenizeLine([..tokens])}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Converts a line of token bytes back into its original BASIC text.
    /// </summary>
    private string DetokenizeLine(byte[] tokens)
    {
        var sb = new StringBuilder();
        bool inString = false;

        foreach (var b in tokens)
        {
            if (b == (byte)'"')
            {
                inString = !inString;
                sb.Append('"');
                continue;
            }

            // Inside a string literal bytes are literal character codes, never keywords.
            // This is the same rule the C64 BASIC interpreter follows: token expansion
            // stops at the opening quote and resumes after the closing quote.
            if (!inString && BasicTokens.ReverseTokenMap.TryGetValue(b, out var keyword))
                sb.Append(keyword);
            else
                sb.Append((char)b);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses the line number and code from a BASIC line.
    /// </summary>
    private (ushort lineNumber, string code)? ParseLineNumberAndCode(string line)
    {
        int i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;

        if (i == 0 || !ushort.TryParse(line[0..i], out var lineNumber))
            return null;

        // Skip the optional space between the line number and the first statement.
        // Minified code omits this space, so we handle both "10 PRINT" and "10PRINT".
        if (i < line.Length && line[i] == ' ')
            i++;

        return (lineNumber, i < line.Length ? line[i..] : string.Empty);
    }

    #endregion
}
