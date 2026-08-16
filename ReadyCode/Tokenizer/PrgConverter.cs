// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Text;
using ReadyCode.Models;

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
    /// Determines whether a BASIC-language file should be tokenized when saved. Assembly
    /// source is never tokenized regardless of extension, and <c>.bas</c> is always kept as
    /// plain PETSCII source text - only other BASIC-language targets (namely <c>.prg</c>)
    /// round-trip through the real tokenized binary format.
    /// </summary>
    /// <param name="language">The tab's editor language.</param>
    /// <param name="filePath">The path being saved to.</param>
    /// <returns>True if the source should be tokenized to PRG bytes before writing.</returns>
    public static bool ShouldTokenizeOnSave(EditorLanguage language, string filePath) =>
        language == EditorLanguage.Basic
        && !string.Equals(Path.GetExtension(filePath), ".bas", StringComparison.OrdinalIgnoreCase);

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
    /// Converts a .prg binary file back into its original BASIC source text. A buffer too short
    /// to hold even one line (e.g. an empty, freshly created .prg, or just the 2-byte load-address
    /// header) isn't an error - the loop below simply never runs, so it returns an empty listing.
    /// </summary>
    public string ConvertFromPrg(byte[] data)
    {
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

    /// <summary>
    /// Determines whether the given .prg data is a genuine tokenized BASIC program, as opposed
    /// to a machine-language file or a short BASIC "loader" stub with raw code appended after
    /// it. Unlike <see cref="ConvertFromPrg"/> (which is deliberately lenient so it never
    /// throws on legitimate edge cases), this strictly validates the line-chain structure:
    /// every line's link pointer must exactly match the address of the following line - the
    /// same relationship <see cref="ConvertToPrg"/> constructs, and one real machine code
    /// essentially never satisfies by chance. Token bytes themselves aren't validated - real
    /// historical programs sometimes carry stray high-bit bytes (leftover graphics/cursor
    /// characters, editor artifacts) that are harmless in practice but would otherwise cause
    /// false negatives on genuinely valid programs.
    /// </summary>
    /// <param name="data">The .prg data to check.</param>
    /// <returns>True if the data is a well-formed tokenized BASIC program.</returns>
    public bool IsBasicProgram(byte[] data)
    {
        if (data.Length < 4 || data[0] != (_loadAddress & 0xFF) || data[1] != (_loadAddress >> 8))
            return false;

        int pos = 2;

        while (true)
        {
            if (pos + 1 >= data.Length) return false;

            ushort link = (ushort)(data[pos] | (data[pos + 1] << 8));
            if (link == 0x0000)
                return pos + 2 == data.Length;

            pos += 2;
            if (pos + 1 >= data.Length) return false;
            pos += 2; // line number - any value is valid

            while (pos < data.Length && data[pos] != 0x00)
                pos++;

            if (pos >= data.Length) return false; // missing line terminator
            pos++;

            ushort expectedLink = (ushort)(_loadAddress + pos - 2);
            if (link != expectedLink) return false;
        }
    }

    /// <summary>
    /// Detects a short BASIC "loader" stub at the very start of .prg data - one or more
    /// well-formed tokenized BASIC lines, with machine code bytes following immediately after.
    /// Real C64 BASIC never validates a line's link pointer beyond following it, and a SYS-and-jump
    /// stub's machine code never gets reached by continuing to follow links (SYS transfers control
    /// away for good), so a real-world stub commonly has no trailing 0x0000 "end of program"
    /// marker at all - the raw code just starts right where the next line's link field would be.
    /// Accordingly, this stops consuming lines the moment it hits either a literal 0x0000 marker
    /// (consumed, since that's a real sentinel, not code) or anything that doesn't look like a
    /// forward-moving link (left alone - those bytes are the actual machine code). It also doesn't
    /// require a line's link to match the exact address arithmetic <see cref="ConvertToPrg"/>
    /// itself would produce, for the same reason: real BASIC doesn't validate that either, so
    /// third-party/hand-assembled stubs routinely have link values that don't match a from-scratch
    /// recomputation. Used by "Disassemble file" so a machine-language .prg with a real loader stub
    /// (e.g. "10 SYS 2064") disassembles starting at the code's actual origin instead of
    /// misinterpreting the stub's own tokenized bytes as 6502 opcodes.
    /// </summary>
    /// <param name="data">The .prg data (including its 2-byte load-address header) to check.</param>
    /// <param name="stubLines">The stub's decoded BASIC line text (e.g. "10 SYS 2064"), if found.</param>
    /// <param name="codeOffset">
    /// The byte offset from the start of <paramref name="data"/> (header included) where the stub
    /// ends and the trailing machine code begins, if found.
    /// </param>
    /// <returns>True if a stub was found with at least one byte of machine code following it.</returns>
    public bool TryDetectBasicStub(byte[] data, out IReadOnlyList<string> stubLines, out int codeOffset)
    {
        stubLines = Array.Empty<string>();
        codeOffset = 0;

        if (data.Length < 4 || data[0] != (_loadAddress & 0xFF) || data[1] != (_loadAddress >> 8))
            return false;

        var lines = new List<string>();
        int pos = 2;
        ushort previousAddress = _loadAddress;

        while (true)
        {
            if (pos + 1 >= data.Length) break;

            ushort link = (ushort)(data[pos] | (data[pos + 1] << 8));

            if (link == 0x0000)
            {
                pos += 2; // a real end-of-program sentinel, not code - consume it too
                break;
            }

            if (link <= previousAddress) break; // doesn't continue the chain - this is the code

            int afterLink = pos + 2;
            if (afterLink + 1 >= data.Length) break; // truncated - not a real line

            ushort lineNumber = (ushort)(data[afterLink] | (data[afterLink + 1] << 8));
            int tokenPos = afterLink + 2;
            while (tokenPos < data.Length && data[tokenPos] != 0x00)
                tokenPos++;

            if (tokenPos >= data.Length) break; // missing line terminator - not a real line

            lines.Add($"{lineNumber} {DetokenizeLine(data[(afterLink + 2)..tokenPos])}");
            previousAddress = link;
            pos = tokenPos + 1; // past this line's own terminator byte
        }

        if (lines.Count == 0 || pos >= data.Length) return false;

        stubLines = lines;
        codeOffset = pos;
        return true;
    }

    /// <summary>
    /// Determines whether a raw .prg needs a typed "SYS &lt;origin&gt;" command to actually start
    /// after loading, rather than relying on an emulator/hardware's native autostart RUN. True
    /// when the file has no runnable BASIC entry point at all - neither a complete tokenized BASIC
    /// program (<see cref="IsBasicProgram"/>) nor a loader stub followed by machine code
    /// (<see cref="TryDetectBasicStub"/>) - in which case autostart's RUN has nothing to execute,
    /// and <paramref name="origin"/> is instead read directly from the file's own 2-byte
    /// load-address header (SYS's target is always wherever that load address actually places the
    /// code, whether the file arrived as a real .prg or was just produced by assembling source
    /// with an explicit ".org").
    /// </summary>
    /// <param name="data">The .prg data (including its 2-byte load-address header) to check.</param>
    /// <param name="origin">The address to SYS into, if a typed command is needed.</param>
    /// <returns>True if the file needs a typed SYS command to run.</returns>
    public bool NeedsSysToRun(byte[] data, out ushort origin)
    {
        origin = 0;
        if (data.Length < 2) return false;
        if (IsBasicProgram(data) || TryDetectBasicStub(data, out _, out _)) return false;

        origin = (ushort)(data[0] | (data[1] << 8));
        return true;
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
    /// Parses the line number and code from a BASIC line. Internal (rather than private) so
    /// <see cref="ReadyCode.Debugger.BasicLineAddressTable"/> can parse lines identically to
    /// <see cref="ConvertToPrg"/> without duplicating this logic or letting the two drift apart.
    /// </summary>
    internal static (ushort lineNumber, string code)? ParseLineNumberAndCode(string line)
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
