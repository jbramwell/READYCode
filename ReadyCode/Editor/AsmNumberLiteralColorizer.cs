// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace ReadyCode.Editor;

/// <summary>
/// Colors numeric literals in 6502 assembly source: immediate/hex/binary/decimal forms such as
/// "#$00", "#%00000001", "#10", "$D020", "%00000001", and a bare decimal like "53280". The
/// leading marker character ("#", "$", "%") is colored along with its digits. A bare decimal run
/// is only colored when not immediately preceded by an identifier character, so a label suffix
/// like the "2" in "LOOP2" is left uncolored rather than treated as a literal.
/// </summary>
public class AsmNumberLiteralColorizer : DocumentColorizingTransformer
{
    #region Private Fields

    private static readonly Regex _immHexPattern = new(@"\G#\$[0-9A-Fa-f]+", RegexOptions.Compiled);
    private static readonly Regex _immBinPattern = new(@"\G#%[01]+", RegexOptions.Compiled);
    private static readonly Regex _immDecPattern = new(@"\G#\d+", RegexOptions.Compiled);
    private static readonly Regex _hexPattern = new(@"\G\$[0-9A-Fa-f]+", RegexOptions.Compiled);
    private static readonly Regex _binPattern = new(@"\G%[01]+", RegexOptions.Compiled);
    private static readonly Regex _decPattern = new(@"\G\d+", RegexOptions.Compiled);

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets the brush used to draw numeric literals.
    /// </summary>
    public Brush NumberBrush { get; set; } = Brushes.Teal;

    #endregion

    #region Public Methods

    /// <summary>
    /// Colorizes all numeric literals found on the given line.
    /// </summary>
    /// <param name="line">The document line to colorize.</param>
    protected override void ColorizeLine(DocumentLine line)
    {
        string text = CurrentContext.Document.GetText(line);
        int i = 0;

        while (i < text.Length)
        {
            Match m = MatchAt(text, i);
            if (m.Success)
            {
                int start = line.Offset + i;
                int end = line.Offset + i + m.Length;
                ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(NumberBrush));
                i += m.Length;
                continue;
            }

            i++;
        }
    }

    #endregion

    #region Private Methods

    private static Match MatchAt(string text, int i)
    {
        Match m = _immHexPattern.Match(text, i);
        if (m.Success) return m;

        m = _immBinPattern.Match(text, i);
        if (m.Success) return m;

        m = _hexPattern.Match(text, i);
        if (m.Success) return m;

        m = _binPattern.Match(text, i);
        if (m.Success) return m;

        m = _immDecPattern.Match(text, i);
        if (m.Success) return m;

        if (char.IsDigit(text[i]) && (i == 0 || !IsWordChar(text[i - 1])))
        {
            m = _decPattern.Match(text, i);
            if (m.Success) return m;
        }

        return Match.Empty;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}
