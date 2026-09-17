// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Reflection;

namespace ReadyCode.Sid;

/// <summary>
/// Loads the SID chip oscillator frequency table from the embedded sid_notes.csv resource.
/// </summary>
public static class SidNoteProvider
{
    #region Public Properties

    /// <summary>
    /// Gets the full SID note table, in the order it appears in sid_notes.csv.
    /// </summary>
    public static readonly IReadOnlyList<SidNote> AllNotes = Load();

    #endregion

    #region Private Methods

    private static List<SidNote> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("sid_notes.csv", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var notes = new List<SidNote>();
        reader.ReadLine(); // header row

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] f = line.Split(',');
            notes.Add(new SidNote(
                note: int.Parse(f[0]),
                octave: f[1],
                decimalNtsc: int.Parse(f[2]),
                hiNtsc: int.Parse(f[3]),
                lowNtsc: int.Parse(f[4]),
                decimalPal: ParseOrNull(f[5]),
                hiPal: ParseOrNull(f[6]),
                lowPal: ParseOrNull(f[7])));
        }

        return notes;
    }

    // A note's PAL frequency fields are left blank in the CSV when the note is out of
    // PAL's representable range (e.g. the highest note, B-7).
    private static int? ParseOrNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : int.Parse(value);

    #endregion
}
