// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;

namespace ReadyCode.Minify;

/// <summary>
/// Low-level BASIC source line transforms shared between <see cref="CodeMinifier"/> and
/// <see cref="ReadyCode.Prettify.CodePrettifier"/>, so a fix to one (e.g. the GOTO/GOSUB/THEN
/// line-reference regex) can't drift out of sync between the two independent copies that used
/// to exist here.
/// </summary>
internal static class BasicLineTransformUtil
{
    #region Internal Methods

    // Applies transform only to the segments of `code` that lie outside string literals.
    internal static string TransformOutsideStrings(string code, Func<string, string> transform)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            if (code[i] == '"')
            {
                int start = i++;
                while (i < code.Length && code[i] != '"') i++;
                if (i < code.Length) i++; // closing quote
                sb.Append(code[start..i]);
            }
            else
            {
                int start = i;
                while (i < code.Length && code[i] != '"') i++;
                sb.Append(transform(code[start..i]));
            }
        }
        return sb.ToString();
    }

    internal static string UpdateLineReferences(string code, Dictionary<int, int> mapping)
    {
        // No \b anchor: in minified code keywords appear without a preceding space
        // (e.g. "SGOTO24"), so a word boundary would silently skip them.
        return Regex.Replace(code,
            @"(GOTO|GOSUB|THEN|RESTORE|RUN)\s*(\d+(?:\s*,\s*\d+)*)",
            m =>
            {
                string keyword = m.Groups[1].Value;
                string nums = Regex.Replace(m.Groups[2].Value, @"\d+", n =>
                {
                    if (int.TryParse(n.Value, out int old) && mapping.TryGetValue(old, out int @new))
                        return @new.ToString(); // references are never zero-padded
                    return n.Value;
                });
                return keyword + " " + nums;
            },
            RegexOptions.IgnoreCase);
    }

    internal static List<string> SplitLines(string source) =>
        [.. source.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)];

    internal static string JoinLines(List<string> lines) => string.Join("\n", lines);

    #endregion
}
