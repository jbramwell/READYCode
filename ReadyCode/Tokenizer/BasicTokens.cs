// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Tokenizer;

/// <summary>
/// Describes a single C64 BASIC V2 keyword: its token byte, plus the reference metadata used by
/// ghost-text completion, hover tooltips, and the BASIC Keywords reference panel. Metadata is
/// null for the eight single-character operator tokens, which aren't offered as standalone
/// completions.
/// </summary>
/// <param name="Token">The token byte (0x80-0xFF) the keyword crunches to.</param>
/// <param name="Snippet">The ghost-text/completion snippet, with '|' marking the caret position after insertion, or null.</param>
/// <param name="Description">The reference description shown in tooltips and the BASIC Keywords panel, or null.</param>
/// <param name="Category">The reference-panel category this keyword is grouped under, or null.</param>
public record KeywordInfo(byte Token, string? Snippet, string? Description, string? Category);

/// <summary>
/// Commodore 64 BASIC token definitions.
/// Maps keywords to their token values (0x80-0xFF) and reference metadata.
/// </summary>
public static class BasicTokens
{
    #region Public Properties

    /// <summary>
    /// Single source of truth for every BASIC V2 keyword: its token byte plus the completion
    /// snippet, description, and category shown elsewhere in the editor.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, KeywordInfo> Keywords = new Dictionary<string, KeywordInfo>(StringComparer.OrdinalIgnoreCase)
    {
        // Control Flow
        { "END",     new(0x80, "END",       "Ends program execution.",                                                  "Control Flow") },
        { "FOR",     new(0x81, "FOR | = ",  "Begins a counted loop. FOR var=start TO end [STEP n]",                     "Control Flow") },
        { "NEXT",    new(0x82, "NEXT |",    "Ends a FOR loop. NEXT [var]",                                              "Control Flow") },
        { "DATA",    new(0x83, "DATA |",    "Embeds literal values for READ. DATA val[,val,...]",                       "Variables & Data") },
        { "INPUT#",  new(0x84, "INPUT# |,", "Reads data from an open file. INPUT# file,var[,var,...]",                  "Input & Output") },
        { "INPUT",   new(0x85, "INPUT |",   "Accepts keyboard input. INPUT [\"prompt\";] var[,var,...]",                "Input & Output") },
        { "DIM",     new(0x86, "DIM |(|)",  "Declares an array. DIM var(size[,size,...])",                              "Variables & Data") },
        { "READ",    new(0x87, "READ |",    "Reads the next DATA value into a variable. READ var[,var,...]",            "Variables & Data") },
        { "LET",     new(0x88, "LET | = ",  "Assigns a value to a variable. LET var=expression",                        "Variables & Data") },
        { "GOTO",    new(0x89, "GOTO |",    "Jumps to a line number. GOTO line",                                        "Control Flow") },
        { "RUN",     new(0x8A, "RUN",       "Executes the program from the beginning, or from a line number.",          "Control Flow") },
        { "IF",      new(0x8B, "IF | THEN ","Branches conditionally. IF condition THEN statement/line",                 "Control Flow") },
        { "RESTORE", new(0x8C, "RESTORE",   "Resets the DATA pointer to the first DATA statement.",                     "Variables & Data") },
        { "GOSUB",   new(0x8D, "GOSUB |",   "Calls a subroutine at a line number. GOSUB line",                          "Control Flow") },
        { "RETURN",  new(0x8E, "RETURN",    "Returns from a GOSUB subroutine.",                                         "Control Flow") },
        { "REM",     new(0x8F, "REM |",     "Marks a comment; the rest of the line is not executed.",                   "Program Editing") },
        { "STOP",    new(0x90, "STOP",      "Pauses execution. Use CONT to resume.",                                    "Control Flow") },

        // Functions & I/O
        { "ON",      new(0x91, "ON | GOTO ", "Branches to a line based on a computed value. ON expr GOTO/GOSUB line[,line,...]", "Control Flow") },
        { "WAIT",    new(0x92, "WAIT |,",    "Halts until memory bits match a mask. WAIT addr,mask[,inv-mask]",         "System & Memory") },
        { "LOAD",    new(0x93, "LOAD \"|\",8", "Loads a program from a device. LOAD \"name\",device[,1]",               "Files & Devices") },
        { "SAVE",    new(0x94, "SAVE \"|\",8", "Saves the program to a device. SAVE \"name\",device[,1]",               "Files & Devices") },
        { "VERIFY",  new(0x95, "VERIFY \"|\",8", "Verifies a saved program matches memory. VERIFY \"name\",device",     "Files & Devices") },
        { "DEF",     new(0x96, "DEF FN |(|)=", "Defines a numeric function. DEF FN name(arg)=expression",               "Variables & Data") },
        { "POKE",    new(0x97, "POKE |,",    "Writes a byte to a memory address. POKE addr,value",                      "System & Memory") },
        { "PRINT#",  new(0x98, "PRINT# |,",  "Writes data to an open file. PRINT# file,expression",                    "Input & Output") },
        { "PRINT",   new(0x99, "PRINT \"|\"", "Displays output on the screen. PRINT [expression][;|,]",                 "Input & Output") },
        { "CONT",    new(0x9A, "CONT",       "Continues execution after STOP or END.",                                  "Control Flow") },
        { "LIST",    new(0x9B, "LIST",       "Lists program lines. LIST [start[-end]]",                                 "Program Editing") },
        { "CLR",     new(0x9C, "CLR",        "Clears all variables, arrays, and the GOSUB stack.",                      "Program Editing") },
        { "CMD",     new(0x9D, "CMD |",      "Redirects PRINT output to a device. CMD device[,string]",                 "Input & Output") },
        { "SYS",     new(0x9E, "SYS |",      "Executes machine code at an address. SYS addr",                          "System & Memory") },
        { "OPEN",    new(0x9F, "OPEN |,",    "Opens a logical file. OPEN file,device[,secondary[,\"name\"]]",           "Files & Devices") },
        { "CLOSE",   new(0xA0, "CLOSE |",    "Closes a logical file. CLOSE file",                                       "Files & Devices") },
        { "GET",     new(0xA1, "GET |",      "Reads a single keypress without waiting for input. GET var$",             "Input & Output") },
        { "NEW",     new(0xA2, "NEW",        "Erases the current program and all variables.",                          "Program Editing") },

        // More Keywords
        { "TAB",  new(0xA3, "TAB(|)", "Moves the PRINT cursor to column n. Used with PRINT. TAB(n)", "Input & Output") },
        { "TO",   new(0xA4, "TO |",   "Sets the upper bound of a FOR loop. FOR v=start TO end",       "Control Flow") },
        { "FN",   new(0xA5, "FN |(|)", "Calls a user-defined function. FN name(arg)",                 "Variables & Data") },
        { "SPC",  new(0xA6, "SPC(|)", "Prints n spaces. Used with PRINT. SPC(n)",                     "Input & Output") },
        { "THEN", new(0xA7, "THEN |", "Introduces the branch taken when an IF condition is true. IF cond THEN ...", "Control Flow") },
        { "NOT",  new(0xA8, "NOT ",   "Reverses a condition's truth value. NOT expression",           "Logical Operators") },
        { "STEP", new(0xA9, "STEP |", "Sets the step size in a FOR loop. FOR v=x TO y STEP n",         "Control Flow") },

        // Operators (no completion metadata - not offered as standalone completions)
        { "+", new(0xAA, null, null, null) },
        { "-", new(0xAB, null, null, null) },
        { "*", new(0xAC, null, null, null) },
        { "/", new(0xAD, null, null, null) },
        { "^", new(0xAE, null, null, null) },
        { "AND", new(0xAF, "AND ", "Combines two conditions; true only if both are true. expression1 AND expression2", "Logical Operators") },
        { "OR",  new(0xB0, "OR ",  "Combines two conditions; true if either is true. expression1 OR expression2",     "Logical Operators") },
        { ">", new(0xB1, null, null, null) },
        { "=", new(0xB2, null, null, null) },
        { "<", new(0xB3, null, null, null) },

        // Math/String Functions
        { "SGN",    new(0xB4, "SGN(|)",    "Returns the sign of a number: -1, 0, or 1. SGN(n)",             "Math Functions") },
        { "INT",    new(0xB5, "INT(|)",    "Rounds a number down to the nearest integer. INT(n)",           "Math Functions") },
        { "ABS",    new(0xB6, "ABS(|)",    "Returns the absolute value of a number. ABS(n)",                "Math Functions") },
        { "USR",    new(0xB7, "USR(|)",    "Calls a user machine-code function via the $0311 vector. USR(n)", "System & Memory") },
        { "FRE",    new(0xB8, "FRE(0)",    "Returns the number of bytes of free memory. FRE(0)",            "System & Memory") },
        { "POS",    new(0xB9, "POS(0)",    "Returns the current cursor column (0-based). POS(0)",           "System & Memory") },
        { "SQR",    new(0xBA, "SQR(|)",    "Returns the square root of a number. SQR(n)",                   "Math Functions") },
        { "RND",    new(0xBB, "RND(1)",    "Returns a random number, 0 ≤ n < 1. RND(1)  [RND(-n) reseeds]", "Math Functions") },
        { "LOG",    new(0xBC, "LOG(|)",    "Returns the natural logarithm of a number. LOG(n)",             "Math Functions") },
        { "EXP",    new(0xBD, "EXP(|)",    "Returns e raised to a power. EXP(n)",                           "Math Functions") },
        { "COS",    new(0xBE, "COS(|)",    "Returns the cosine of an angle, in radians. COS(n)",            "Math Functions") },
        { "SIN",    new(0xBF, "SIN(|)",    "Returns the sine of an angle, in radians. SIN(n)",              "Math Functions") },
        { "TAN",    new(0xC0, "TAN(|)",    "Returns the tangent of an angle, in radians. TAN(n)",           "Math Functions") },
        { "ATN",    new(0xC1, "ATN(|)",    "Returns the arc-tangent of a number, in radians. ATN(n)",       "Math Functions") },
        { "PEEK",   new(0xC2, "PEEK(|)",   "Reads a byte from a memory address. PEEK(addr)",                "System & Memory") },
        { "LEN",    new(0xC3, "LEN(|)",    "Returns the length of a string. LEN(str$)",                     "String Functions") },
        { "STR$",   new(0xC4, "STR$(|)",   "Converts a number to a string. STR$(n)",                        "String Functions") },
        { "VAL",    new(0xC5, "VAL(|)",    "Converts a string to a number. VAL(str$)",                      "String Functions") },
        { "ASC",    new(0xC6, "ASC(|)",    "Returns the PETSCII code of a string's first character. ASC(str$)", "String Functions") },
        { "CHR$",   new(0xC7, "CHR$(|)",   "Returns the character for a PETSCII code. CHR$(n)",             "String Functions") },
        { "LEFT$",  new(0xC8, "LEFT$(|,)", "Returns the leftmost n characters of a string. LEFT$(str$,n)",  "String Functions") },
        { "RIGHT$", new(0xC9, "RIGHT$(|,)", "Returns the rightmost n characters of a string. RIGHT$(str$,n)", "String Functions") },
        { "MID$",   new(0xCA, "MID$(|,,)", "Returns a substring starting at a position. MID$(str$,start[,len])", "String Functions") },
        { "GO",     new(0xCB, "GO TO |",   "Jumps to a line number (alternate form of GOTO). GO TO line",   "Control Flow") },
    };

    /// <summary>
    /// Dictionary of BASIC keywords to their token values, derived from <see cref="Keywords"/>.
    /// </summary>
    public static readonly Dictionary<string, byte> TokenMap =
        Keywords.ToDictionary(kv => kv.Key, kv => kv.Value.Token, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reverse map for converting token bytes back to keywords.
    /// </summary>
    public static readonly Dictionary<byte, string> ReverseTokenMap =
        TokenMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    /// <summary>
    /// Word-style keyword tokens (excludes the single-character operators), sorted longest-first
    /// so greedy matching prefers e.g. PRINT# over PRINT, GOSUB over GO, LEFT$ over LET. Used
    /// everywhere a line of BASIC is scanned for keywords outside of tokenization itself: syntax
    /// colorizing, hover tooltips, GOTO/GOSUB navigation, and the prettifier.
    /// </summary>
    public static readonly IReadOnlyList<string> WordKeywordsLongestFirst =
        Keywords.Keys
            .Where(k => char.IsLetter(k[0]))
            .OrderByDescending(k => k.Length)
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// All keyword tokens, including the single-character operators, sorted longest-first for
    /// greedy matching. Used by the tokenizer, which must also match +, -, AND, OR, etc.
    /// </summary>
    public static readonly IReadOnlyList<string> AllKeywordsLongestFirst =
        Keywords.Keys
            .OrderByDescending(k => k.Length)
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToArray();

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks whether a word is a recognized BASIC keyword token.
    /// </summary>
    public static bool IsToken(string word) => TokenMap.ContainsKey(word);

    /// <summary>
    /// Gets the token value for a keyword.
    /// </summary>
    public static bool TryGetToken(string word, out byte token) =>
        TokenMap.TryGetValue(word, out token);

    /// <summary>
    /// Finds the longest keyword from <paramref name="candidates"/> (expected to already be
    /// sorted longest-first, e.g. <see cref="WordKeywordsLongestFirst"/> or
    /// <see cref="AllKeywordsLongestFirst"/>) that matches <paramref name="text"/> at
    /// <paramref name="position"/>, case-insensitively. Mirrors the CBM BASIC ROM's greedy
    /// left-to-right CRUNCH scan, so "longest match wins" applies consistently everywhere a line
    /// of BASIC is scanned.
    /// </summary>
    public static bool TryMatchKeyword(string text, int position, IReadOnlyList<string> candidates, out string keyword)
    {
        foreach (string candidate in candidates)
        {
            if (position + candidate.Length > text.Length) continue;
            if (text.AsSpan(position, candidate.Length)
                    .Equals(candidate.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                keyword = candidate;
                return true;
            }
        }

        keyword = "";
        return false;
    }

    #endregion
}
