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
        Item("ABS",    "ABS(|)",           "Absolute value.  ABS(n)",                                       "Math Functions"),
        Item("ATN",    "ATN(|)",           "Arc-tangent in radians.  ATN(n)",                               "Math Functions"),
        Item("COS",    "COS(|)",           "Cosine in radians.  COS(n)",                                    "Math Functions"),
        Item("EXP",    "EXP(|)",           "e raised to a power.  EXP(n)",                                  "Math Functions"),
        Item("INT",    "INT(|)",           "Truncate to integer (floor).  INT(n)",                          "Math Functions"),
        Item("LOG",    "LOG(|)",           "Natural logarithm.  LOG(n)",                                    "Math Functions"),
        Item("RND",    "RND(1)",           "Random number 0 ≤ n < 1.  RND(1)  [RND(-n) reseeds]",           "Math Functions"),
        Item("SGN",    "SGN(|)",           "Sign of number: returns -1, 0, or 1.  SGN(n)",                  "Math Functions"),
        Item("SIN",    "SIN(|)",           "Sine in radians.  SIN(n)",                                      "Math Functions"),
        Item("SQR",    "SQR(|)",           "Square root.  SQR(n)",                                          "Math Functions"),
        Item("TAN",    "TAN(|)",           "Tangent in radians.  TAN(n)",                                   "Math Functions"),

        // ── String functions ─────────────────────────────────────────────────────
        Item("ASC",    "ASC(|)",           "PETSCII code of the first character.  ASC(str$)",               "String Functions"),
        Item("CHR$",   "CHR$(|)",          "Character for a PETSCII code.  CHR$(n)",                        "String Functions"),
        Item("LEFT$",  "LEFT$(|,)",        "Left n characters of a string.  LEFT$(str$,n)",                 "String Functions"),
        Item("LEN",    "LEN(|)",           "Length of a string.  LEN(str$)",                                "String Functions"),
        Item("MID$",   "MID$(|,,)",        "Substring.  MID$(str$,start[,len])",                            "String Functions"),
        Item("RIGHT$", "RIGHT$(|,)",       "Right n characters of a string.  RIGHT$(str$,n)",               "String Functions"),
        Item("STR$",   "STR$(|)",          "Convert a number to a string.  STR$(n)",                        "String Functions"),
        Item("VAL",    "VAL(|)",           "Convert a string to a number.  VAL(str$)",                      "String Functions"),

        // ── I/O & system functions ───────────────────────────────────────────────
        Item("FRE",    "FRE(0)",           "Bytes of free memory.  FRE(0)",                                 "System & Memory"),
        Item("PEEK",   "PEEK(|)",          "Read a byte from a memory address.  PEEK(addr)",                "System & Memory"),
        Item("POS",    "POS(0)",           "Current cursor column (0-based).  POS(0)",                      "System & Memory"),
        Item("USR",    "USR(|)",           "Call user machine-code function via $0311 vector.  USR(n)",     "System & Memory"),

        // ── Output / input keywords with TAB/SPC ────────────────────────────────
        Item("SPC",   "SPC(|)",           "Print n spaces.  Used inside PRINT.  SPC(n)",                   "Input & Output"),
        Item("TAB",   "TAB(|)",           "Move PRINT cursor to column n.  Used inside PRINT.  TAB(n)",    "Input & Output"),

        // ── Statements ───────────────────────────────────────────────────────────
        Item("CLR",    "CLR",              "Clear all variables, arrays, and GOSUB stack",                              "Program Editing"),
        Item("CLOSE",  "CLOSE |",          "Close a logical file.  CLOSE file",                                        "Files & Devices"),
        Item("CMD",    "CMD |",            "Redirect PRINT output to a device.  CMD device[,string]",                  "Input & Output"),
        Item("CONT",   "CONT",             "Continue after STOP or END",                                               "Control Flow"),
        Item("DATA",   "DATA |",           "Embed literal values for READ.  DATA val[,val,...]",                       "Variables & Data"),
        Item("DEF",    "DEF FN |(|)=",     "Define a numeric function.  DEF FN name(arg)=expression",                  "Variables & Data"),
        Item("DIM",    "DIM |(|)",         "Declare an array.  DIM var(size[,size,...])",                              "Variables & Data"),
        Item("END",    "END",              "End program execution",                                                    "Control Flow"),
        Item("FN",     "FN |(|)",          "Call a user-defined function.  FN name(arg)",                              "Variables & Data"),
        Item("FOR",    "FOR | = ",         "Begin a counted loop.  FOR var=start TO end [STEP n]",                     "Control Flow"),
        Item("GET",    "GET |",            "Read a single keypress without blocking.  GET var$",                       "Input & Output"),
        Item("GO",     "GO TO |",          "Alternative form of GOTO.  GO TO line",                                    "Control Flow"),
        Item("GOSUB",  "GOSUB |",          "Call subroutine at line number.  GOSUB line",                              "Control Flow"),
        Item("GOTO",   "GOTO |",           "Jump to a line number.  GOTO line",                                        "Control Flow"),
        Item("IF",     "IF | THEN ",       "Conditional branch.  IF condition THEN statement/line",                    "Control Flow"),
        Item("INPUT",  "INPUT |",          "Accept keyboard input.  INPUT [\"prompt\";] var[,var,...]",                "Input & Output"),
        Item("INPUT#", "INPUT# |,",        "Read data from an open file.  INPUT# file,var[,var,...]",                  "Input & Output"),
        Item("LET",    "LET | = ",         "Assign a value to a variable.  LET var=expression",                        "Variables & Data"),
        Item("LIST",   "LIST",             "List program lines.  LIST [start[-end]]",                                  "Program Editing"),
        Item("LOAD",   "LOAD \"|\",8",     "Load a program from a device.  LOAD \"name\",device[,1]",                  "Files & Devices"),
        Item("NEW",    "NEW",              "Erase the current program and all variables",                              "Program Editing"),
        Item("NEXT",   "NEXT |",           "End of a FOR loop.  NEXT [var]",                                           "Control Flow"),
        Item("ON",     "ON | GOTO ",       "Computed GOTO or GOSUB.  ON expr GOTO/GOSUB line[,line,...]",              "Control Flow"),
        Item("OPEN",   "OPEN |,",          "Open a logical file.  OPEN file,device[,secondary[,\"name\"]]",            "Files & Devices"),
        Item("POKE",   "POKE |,",          "Write a byte to a memory address.  POKE addr,value",                       "System & Memory"),
        Item("PRINT",  "PRINT \"|\"",      "Display output on screen.  PRINT [expression][;|,]",                       "Input & Output"),
        Item("PRINT#", "PRINT# |,",        "Write data to an open file.  PRINT# file,expression",                      "Input & Output"),
        Item("READ",   "READ |",           "Read next DATA value into a variable.  READ var[,var,...]",                "Variables & Data"),
        Item("REM",    "REM |",            "Remark / comment — not executed",                                          "Program Editing"),
        Item("RESTORE","RESTORE",          "Reset the DATA pointer to the first DATA statement",                       "Variables & Data"),
        Item("RETURN", "RETURN",           "Return from a GOSUB subroutine",                                           "Control Flow"),
        Item("RUN",    "RUN",              "Execute program from the beginning [or from a line number]",               "Control Flow"),
        Item("SAVE",   "SAVE \"|\",8",     "Save program to a device.  SAVE \"name\",device[,1]",                      "Files & Devices"),
        Item("STEP",   "STEP |",           "Step size in a FOR loop.  FOR v=x TO y STEP n",                            "Control Flow"),
        Item("STOP",   "STOP",             "Pause execution — use CONT to resume",                                     "Control Flow"),
        Item("SYS",    "SYS |",            "Execute machine code at address.  SYS addr",                               "System & Memory"),
        Item("VERIFY", "VERIFY \"|\",8",   "Verify a saved program matches memory.  VERIFY \"name\",device",           "Files & Devices"),
        Item("WAIT",   "WAIT |,",          "Halt until memory bits match a mask.  WAIT addr,mask[,inv-mask]",          "System & Memory"),

        // ── Logical / relational operators ───────────────────────────────────────
        Item("AND",    "AND ",             "Logical AND.  expression1 AND expression2",                     "Logical Operators"),
        Item("NOT",    "NOT ",             "Logical NOT.  NOT expression",                                  "Logical Operators"),
        Item("OR",     "OR ",              "Logical OR.  expression1 OR expression2",                       "Logical Operators"),

        // ── Clause keywords ──────────────────────────────────────────────────────
        Item("THEN",   "THEN |",           "Branch taken when IF condition is true.  IF cond THEN ...",     "Control Flow"),
        Item("TO",     "TO |",             "Upper bound of a FOR loop.  FOR v=start TO end",                "Control Flow"),
    ];

    private static BasicCompletionData Item(string text, string snippet, string description, string category)
        => new(text, snippet, description, category);

    #endregion
}
