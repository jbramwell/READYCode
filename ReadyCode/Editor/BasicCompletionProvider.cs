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
        Item("ABS",    "ABS(|)",           "Absolute value.  ABS(n)"),
        Item("ATN",    "ATN(|)",           "Arc-tangent in radians.  ATN(n)"),
        Item("COS",    "COS(|)",           "Cosine in radians.  COS(n)"),
        Item("EXP",    "EXP(|)",           "e raised to a power.  EXP(n)"),
        Item("INT",    "INT(|)",           "Truncate to integer (floor).  INT(n)"),
        Item("LOG",    "LOG(|)",           "Natural logarithm.  LOG(n)"),
        Item("RND",    "RND(1)",           "Random number 0 ≤ n < 1.  RND(1)  [RND(-n) reseeds]"),
        Item("SGN",    "SGN(|)",           "Sign of number: returns -1, 0, or 1.  SGN(n)"),
        Item("SIN",    "SIN(|)",           "Sine in radians.  SIN(n)"),
        Item("SQR",    "SQR(|)",           "Square root.  SQR(n)"),
        Item("TAN",    "TAN(|)",           "Tangent in radians.  TAN(n)"),

        // ── String functions ─────────────────────────────────────────────────────
        Item("ASC",    "ASC(|)",           "PETSCII code of the first character.  ASC(str$)"),
        Item("CHR$",   "CHR$(|)",          "Character for a PETSCII code.  CHR$(n)"),
        Item("LEFT$",  "LEFT$(|,)",        "Left n characters of a string.  LEFT$(str$,n)"),
        Item("LEN",    "LEN(|)",           "Length of a string.  LEN(str$)"),
        Item("MID$",   "MID$(|,,)",        "Substring.  MID$(str$,start[,len])"),
        Item("RIGHT$", "RIGHT$(|,)",       "Right n characters of a string.  RIGHT$(str$,n)"),
        Item("STR$",   "STR$(|)",          "Convert a number to a string.  STR$(n)"),
        Item("VAL",    "VAL(|)",           "Convert a string to a number.  VAL(str$)"),

        // ── I/O & system functions ───────────────────────────────────────────────
        Item("FRE",    "FRE(0)",           "Bytes of free memory.  FRE(0)"),
        Item("PEEK",   "PEEK(|)",          "Read a byte from a memory address.  PEEK(addr)"),
        Item("POS",    "POS(0)",           "Current cursor column (0-based).  POS(0)"),
        Item("USR",    "USR(|)",           "Call user machine-code function via $0311 vector.  USR(n)"),

        // ── Output / input keywords with TAB/SPC ────────────────────────────────
        Item("SPC(",   "SPC(|)",           "Print n spaces.  Used inside PRINT.  SPC(n)"),
        Item("TAB(",   "TAB(|)",           "Move PRINT cursor to column n.  Used inside PRINT.  TAB(n)"),

        // ── Statements ───────────────────────────────────────────────────────────
        Item("CLR",    "CLR",              "Clear all variables, arrays, and GOSUB stack"),
        Item("CLOSE",  "CLOSE |",          "Close a logical file.  CLOSE file"),
        Item("CMD",    "CMD |",            "Redirect PRINT output to a device.  CMD device[,string]"),
        Item("CONT",   "CONT",             "Continue after STOP or END"),
        Item("DATA",   "DATA |",           "Embed literal values for READ.  DATA val[,val,...]"),
        Item("DEF",    "DEF FN |(|)=",     "Define a numeric function.  DEF FN name(arg)=expression"),
        Item("DIM",    "DIM |(|)",         "Declare an array.  DIM var(size[,size,...])"),
        Item("END",    "END",              "End program execution"),
        Item("FN",     "FN |(|)",          "Call a user-defined function.  FN name(arg)"),
        Item("FOR",    "FOR | = ",         "Begin a counted loop.  FOR var=start TO end [STEP n]"),
        Item("GET",    "GET |",            "Read a single keypress without blocking.  GET var$"),
        Item("GO",     "GO TO |",          "Alternative form of GOTO.  GO TO line"),
        Item("GOSUB",  "GOSUB |",          "Call subroutine at line number.  GOSUB line"),
        Item("GOTO",   "GOTO |",           "Jump to a line number.  GOTO line"),
        Item("IF",     "IF | THEN ",       "Conditional branch.  IF condition THEN statement/line"),
        Item("INPUT",  "INPUT |",          "Accept keyboard input.  INPUT [\"prompt\";] var[,var,...]"),
        Item("INPUT#", "INPUT# |,",        "Read data from an open file.  INPUT# file,var[,var,...]"),
        Item("LET",    "LET | = ",         "Assign a value to a variable.  LET var=expression"),
        Item("LIST",   "LIST",             "List program lines.  LIST [start[-end]]"),
        Item("LOAD",   "LOAD \"|\",8",     "Load a program from a device.  LOAD \"name\",device[,1]"),
        Item("NEW",    "NEW",              "Erase the current program and all variables"),
        Item("NEXT",   "NEXT |",           "End of a FOR loop.  NEXT [var]"),
        Item("ON",     "ON | GOTO ",       "Computed GOTO or GOSUB.  ON expr GOTO/GOSUB line[,line,...]"),
        Item("OPEN",   "OPEN |,",          "Open a logical file.  OPEN file,device[,secondary[,\"name\"]]"),
        Item("POKE",   "POKE |,",          "Write a byte to a memory address.  POKE addr,value"),
        Item("PRINT",  "PRINT \"|\"",      "Display output on screen.  PRINT [expression][;|,]"),
        Item("PRINT#", "PRINT# |,",        "Write data to an open file.  PRINT# file,expression"),
        Item("READ",   "READ |",           "Read next DATA value into a variable.  READ var[,var,...]"),
        Item("REM",    "REM |",            "Remark / comment — not executed"),
        Item("RESTORE","RESTORE",          "Reset the DATA pointer to the first DATA statement"),
        Item("RETURN", "RETURN",           "Return from a GOSUB subroutine"),
        Item("RUN",    "RUN",              "Execute program from the beginning [or from a line number]"),
        Item("SAVE",   "SAVE \"|\",8",     "Save program to a device.  SAVE \"name\",device[,1]"),
        Item("STEP",   "STEP |",           "Step size in a FOR loop.  FOR v=x TO y STEP n"),
        Item("STOP",   "STOP",             "Pause execution — use CONT to resume"),
        Item("SYS",    "SYS |",            "Execute machine code at address.  SYS addr"),
        Item("VERIFY", "VERIFY \"|\",8",   "Verify a saved program matches memory.  VERIFY \"name\",device"),
        Item("WAIT",   "WAIT |,",          "Halt until memory bits match a mask.  WAIT addr,mask[,inv-mask]"),

        // ── Logical / relational operators ───────────────────────────────────────
        Item("AND",    "AND ",             "Logical AND.  expression1 AND expression2"),
        Item("NOT",    "NOT ",             "Logical NOT.  NOT expression"),
        Item("OR",     "OR ",              "Logical OR.  expression1 OR expression2"),

        // ── Clause keywords ──────────────────────────────────────────────────────
        Item("THEN",   "THEN |",           "Branch taken when IF condition is true.  IF cond THEN ..."),
        Item("TO",     "TO |",             "Upper bound of a FOR loop.  FOR v=start TO end"),
    ];

    private static BasicCompletionData Item(string text, string snippet, string description)
        => new(text, snippet, description);

    #endregion
}
