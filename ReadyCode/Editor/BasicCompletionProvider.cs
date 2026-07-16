// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Editor;

/// <summary>
/// Full C64 BASIC V2 keyword completion table.
/// Snippets use '|' to mark the initial caret position after insertion.
/// </summary>
public static class BasicCompletionProvider
{
    #region Public Properties

    /// <summary>
    /// Gets the full list of completion entries for the C64 BASIC V2 keyword set.
    /// </summary>
    public static readonly IReadOnlyList<BasicCompletionData> AllItems = Build();

    /// <summary>
    /// Gets the display order for keyword categories (e.g. for the "BASIC Keywords" reference panel).
    /// </summary>
    public static readonly IReadOnlyList<string> CategoryOrder =
    [
        "Control Flow",
        "Program Editing",
        "Variables & Data",
        "Math Functions",
        "String Functions",
        "Input & Output",
        "Files & Devices",
        "System & Memory",
        "Logical Operators",
    ];

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns all items whose Text starts with <paramref name="prefix"/> (case-insensitive),
    /// sorted alphabetically so the first entry is always the predictable ghost-text suggestion.
    /// </summary>
    public static List<BasicCompletionData> GetMatches(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return [];
        return [.. AllItems
            .Where(i => i.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase)];
    }

    #endregion

    #region Private Methods

    private static BasicCompletionData[] Build() =>
    [
        // ── Math functions ───────────────────────────────────────────────────────
        Item("ABS",    "ABS(|)",           "Returns the absolute value of a number. ABS(n)",                "Math Functions"),
        Item("ATN",    "ATN(|)",           "Returns the arc-tangent of a number, in radians. ATN(n)",       "Math Functions"),
        Item("COS",    "COS(|)",           "Returns the cosine of an angle, in radians. COS(n)",            "Math Functions"),
        Item("EXP",    "EXP(|)",           "Returns e raised to a power. EXP(n)",                           "Math Functions"),
        Item("INT",    "INT(|)",           "Rounds a number down to the nearest integer. INT(n)",           "Math Functions"),
        Item("LOG",    "LOG(|)",           "Returns the natural logarithm of a number. LOG(n)",             "Math Functions"),
        Item("RND",    "RND(1)",           "Returns a random number, 0 ≤ n < 1. RND(1)  [RND(-n) reseeds]", "Math Functions"),
        Item("SGN",    "SGN(|)",           "Returns the sign of a number: -1, 0, or 1. SGN(n)",             "Math Functions"),
        Item("SIN",    "SIN(|)",           "Returns the sine of an angle, in radians. SIN(n)",              "Math Functions"),
        Item("SQR",    "SQR(|)",           "Returns the square root of a number. SQR(n)",                   "Math Functions"),
        Item("TAN",    "TAN(|)",           "Returns the tangent of an angle, in radians. TAN(n)",           "Math Functions"),

        // ── String functions ─────────────────────────────────────────────────────
        Item("ASC",    "ASC(|)",           "Returns the PETSCII code of a string's first character. ASC(str$)", "String Functions"),
        Item("CHR$",   "CHR$(|)",          "Returns the character for a PETSCII code. CHR$(n)",             "String Functions"),
        Item("LEFT$",  "LEFT$(|,)",        "Returns the leftmost n characters of a string. LEFT$(str$,n)",  "String Functions"),
        Item("LEN",    "LEN(|)",           "Returns the length of a string. LEN(str$)",                     "String Functions"),
        Item("MID$",   "MID$(|,,)",        "Returns a substring starting at a position. MID$(str$,start[,len])", "String Functions"),
        Item("RIGHT$", "RIGHT$(|,)",       "Returns the rightmost n characters of a string. RIGHT$(str$,n)", "String Functions"),
        Item("STR$",   "STR$(|)",          "Converts a number to a string. STR$(n)",                        "String Functions"),
        Item("VAL",    "VAL(|)",           "Converts a string to a number. VAL(str$)",                      "String Functions"),

        // ── I/O & system functions ───────────────────────────────────────────────
        Item("FRE",    "FRE(0)",           "Returns the number of bytes of free memory. FRE(0)",            "System & Memory"),
        Item("PEEK",   "PEEK(|)",          "Reads a byte from a memory address. PEEK(addr)",                "System & Memory"),
        Item("POS",    "POS(0)",           "Returns the current cursor column (0-based). POS(0)",           "System & Memory"),
        Item("USR",    "USR(|)",           "Calls a user machine-code function via the $0311 vector. USR(n)", "System & Memory"),

        // ── Output / input keywords with TAB/SPC ────────────────────────────────
        Item("SPC",   "SPC(|)",           "Prints n spaces. Used with PRINT. SPC(n)",                       "Input & Output"),
        Item("TAB",   "TAB(|)",           "Moves the PRINT cursor to column n. Used with PRINT. TAB(n)",    "Input & Output"),

        // ── Statements ───────────────────────────────────────────────────────────
        Item("CLR",    "CLR",              "Clears all variables, arrays, and the GOSUB stack.",                        "Program Editing"),
        Item("CLOSE",  "CLOSE |",          "Closes a logical file. CLOSE file",                                        "Files & Devices"),
        Item("CMD",    "CMD |",            "Redirects PRINT output to a device. CMD device[,string]",                  "Input & Output"),
        Item("CONT",   "CONT",             "Continues execution after STOP or END.",                                   "Control Flow"),
        Item("DATA",   "DATA |",           "Embeds literal values for READ. DATA val[,val,...]",                       "Variables & Data"),
        Item("DEF",    "DEF FN |(|)=",     "Defines a numeric function. DEF FN name(arg)=expression",                  "Variables & Data"),
        Item("DIM",    "DIM |(|)",         "Declares an array. DIM var(size[,size,...])",                              "Variables & Data"),
        Item("END",    "END",              "Ends program execution.",                                                  "Control Flow"),
        Item("FN",     "FN |(|)",          "Calls a user-defined function. FN name(arg)",                              "Variables & Data"),
        Item("FOR",    "FOR | = ",         "Begins a counted loop. FOR var=start TO end [STEP n]",                     "Control Flow"),
        Item("GET",    "GET |",            "Reads a single keypress without waiting for input. GET var$",              "Input & Output"),
        Item("GO",     "GO TO |",          "Jumps to a line number (alternate form of GOTO). GO TO line",              "Control Flow"),
        Item("GOSUB",  "GOSUB |",          "Calls a subroutine at a line number. GOSUB line",                          "Control Flow"),
        Item("GOTO",   "GOTO |",           "Jumps to a line number. GOTO line",                                        "Control Flow"),
        Item("IF",     "IF | THEN ",       "Branches conditionally. IF condition THEN statement/line",                 "Control Flow"),
        Item("INPUT",  "INPUT |",          "Accepts keyboard input. INPUT [\"prompt\";] var[,var,...]",                "Input & Output"),
        Item("INPUT#", "INPUT# |,",        "Reads data from an open file. INPUT# file,var[,var,...]",                  "Input & Output"),
        Item("LET",    "LET | = ",         "Assigns a value to a variable. LET var=expression",                        "Variables & Data"),
        Item("LIST",   "LIST",             "Lists program lines. LIST [start[-end]]",                                  "Program Editing"),
        Item("LOAD",   "LOAD \"|\",8",     "Loads a program from a device. LOAD \"name\",device[,1]",                  "Files & Devices"),
        Item("NEW",    "NEW",              "Erases the current program and all variables.",                            "Program Editing"),
        Item("NEXT",   "NEXT |",           "Ends a FOR loop. NEXT [var]",                                              "Control Flow"),
        Item("ON",     "ON | GOTO ",       "Branches to a line based on a computed value. ON expr GOTO/GOSUB line[,line,...]", "Control Flow"),
        Item("OPEN",   "OPEN |,",          "Opens a logical file. OPEN file,device[,secondary[,\"name\"]]",            "Files & Devices"),
        Item("POKE",   "POKE |,",          "Writes a byte to a memory address. POKE addr,value",                       "System & Memory"),
        Item("PRINT",  "PRINT \"|\"",      "Displays output on the screen. PRINT [expression][;|,]",                   "Input & Output"),
        Item("PRINT#", "PRINT# |,",        "Writes data to an open file. PRINT# file,expression",                     "Input & Output"),
        Item("READ",   "READ |",           "Reads the next DATA value into a variable. READ var[,var,...]",            "Variables & Data"),
        Item("REM",    "REM |",            "Marks a comment; the rest of the line is not executed.",                   "Program Editing"),
        Item("RESTORE","RESTORE",          "Resets the DATA pointer to the first DATA statement.",                     "Variables & Data"),
        Item("RETURN", "RETURN",           "Returns from a GOSUB subroutine.",                                         "Control Flow"),
        Item("RUN",    "RUN",              "Executes the program from the beginning, or from a line number.",          "Control Flow"),
        Item("SAVE",   "SAVE \"|\",8",     "Saves the program to a device. SAVE \"name\",device[,1]",                  "Files & Devices"),
        Item("STEP",   "STEP |",           "Sets the step size in a FOR loop. FOR v=x TO y STEP n",                    "Control Flow"),
        Item("STOP",   "STOP",             "Pauses execution. Use CONT to resume.",                                    "Control Flow"),
        Item("SYS",    "SYS |",            "Executes machine code at an address. SYS addr",                            "System & Memory"),
        Item("VERIFY", "VERIFY \"|\",8",   "Verifies a saved program matches memory. VERIFY \"name\",device",          "Files & Devices"),
        Item("WAIT",   "WAIT |,",          "Halts until memory bits match a mask. WAIT addr,mask[,inv-mask]",          "System & Memory"),

        // ── Logical / relational operators ───────────────────────────────────────
        Item("AND",    "AND ",             "Combines two conditions; true only if both are true. expression1 AND expression2", "Logical Operators"),
        Item("NOT",    "NOT ",             "Reverses a condition's truth value. NOT expression",             "Logical Operators"),
        Item("OR",     "OR ",              "Combines two conditions; true if either is true. expression1 OR expression2", "Logical Operators"),

        // ── Clause keywords ──────────────────────────────────────────────────────
        Item("THEN",   "THEN |",           "Introduces the branch taken when an IF condition is true. IF cond THEN ...", "Control Flow"),
        Item("TO",     "TO |",             "Sets the upper bound of a FOR loop. FOR v=start TO end",         "Control Flow"),
    ];

    private static BasicCompletionData Item(string text, string snippet, string description, string category)
        => new(text, snippet, description, category);

    #endregion
}
