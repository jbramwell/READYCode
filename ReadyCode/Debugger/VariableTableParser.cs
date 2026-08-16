// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Debugger;

/// <summary>
/// The C64 BASIC storage type of a parsed variable.
/// </summary>
public enum BasicVariableType
{
    Float,
    Integer,
    String,
}

/// <summary>
/// A simple (non-array) BASIC variable parsed from a live memory snapshot of the variable table.
/// <see cref="Value"/> holds a <see cref="double"/> for <see cref="BasicVariableType.Float"/>, a
/// <see cref="short"/> for <see cref="BasicVariableType.Integer"/>, or a
/// <see cref="StringDescriptor"/> for <see cref="BasicVariableType.String"/> - a string variable's
/// actual characters live on the string heap and need a follow-up memory read, so only its
/// length/pointer are available at this stage (see <see cref="ResolvedStringValue"/> for the
/// shape <see cref="Value"/> takes once that follow-up read has happened).
/// </summary>
public sealed record BasicVariable(string Name, BasicVariableType Type, object Value, ushort ValueAddress);

/// <summary>
/// A string variable's descriptor as stored in the variable table: how many characters it has,
/// and where those characters live (either program string-literal text or the string heap).
/// </summary>
public sealed record StringDescriptor(byte Length, ushort HeapPointer);

/// <summary>
/// A string variable's actual character data, resolved from its <see cref="StringDescriptor"/>
/// via a follow-up memory read - replaces a <see cref="BasicVariable"/>'s <see cref="StringDescriptor"/>
/// <see cref="BasicVariable.Value"/> once resolved, retaining <see cref="HeapPointer"/> (needed
/// to write a new value back to the same location) alongside the now-readable <see cref="Text"/>.
/// </summary>
public sealed record ResolvedStringValue(string Text, ushort HeapPointer);

/// <summary>
/// Parses C64 BASIC's simple (non-array) variable table from a live memory snapshot.
///
/// Every entry - regardless of type - occupies a uniform 7 bytes: 2 name bytes followed by 5
/// data bytes (verified against two independent sources - a documented byte-level writeup and a
/// working third-party C64 BASIC variable encoder/decoder - since the feature spec's own account
/// of this format turned out to have two errors: it described integer/string entries as 4-5
/// variable-sized bytes rather than a uniform 7, and described the string flag as bit 6 of the
/// second name byte rather than bit 7). Type is flagged using only bit 7 (0x80) of the name
/// bytes: for an integer, BOTH name bytes have bit 7 set; for a string, only the SECOND name byte
/// has bit 7 set; for a float, neither does. A 1-character name's second byte is 0 (before any
/// type flag is applied). Unused trailing bytes in an integer's or string's 5-byte data area are
/// ignored. Array variable inspection is a future enhancement, not handled here.
/// </summary>
public static class VariableTableParser
{
    #region Private Fields

    private const int _entrySize = 7;

    #endregion

    #region Public Methods

    /// <summary>
    /// Parses every simple variable between VARTAB and ARYTAB.
    /// </summary>
    /// <param name="memory">
    /// The bytes read live from the machine, starting exactly at <paramref name="vartabAddress"/>
    /// (i.e. <c>memory[0]</c> corresponds to <paramref name="vartabAddress"/>) and covering at
    /// least up to <paramref name="arytabAddress"/>.
    /// </param>
    /// <param name="vartabAddress">The address of the first variable entry (BASIC's VARTAB pointer).</param>
    /// <param name="arytabAddress">The address one past the last variable entry (BASIC's ARYTAB pointer).</param>
    /// <returns>
    /// The variables successfully parsed. If the table is corrupted or the snapshot runs out of
    /// data partway through, parsing stops at that point and returns everything parsed so far,
    /// rather than throwing - matching how a partially-corrupt live variable table should still
    /// show whatever variables are readable.
    /// </returns>
    public static IReadOnlyList<BasicVariable> ParseSimpleVariables(byte[] memory, ushort vartabAddress, ushort arytabAddress)
    {
        var variables = new List<BasicVariable>();
        int address = vartabAddress;

        while (address < arytabAddress)
        {
            int offset = address - vartabAddress;
            if (offset + _entrySize > memory.Length)
                break;

            byte nameByte1 = memory[offset];
            byte nameByte2 = memory[offset + 1];

            bool isInteger = (nameByte1 & 0x80) != 0 && (nameByte2 & 0x80) != 0;
            bool isString = (nameByte1 & 0x80) == 0 && (nameByte2 & 0x80) != 0;
            string name = BuildVariableName(nameByte1, nameByte2, isInteger, isString);

            int dataOffset = offset + 2;
            ushort valueAddress = (ushort)(address + 2);

            if (isInteger)
            {
                short value = (short)((memory[dataOffset] << 8) | memory[dataOffset + 1]);
                variables.Add(new BasicVariable(name, BasicVariableType.Integer, value, valueAddress));
            }
            else if (isString)
            {
                byte length = memory[dataOffset];
                ushort pointer = (ushort)(memory[dataOffset + 1] | (memory[dataOffset + 2] << 8));
                variables.Add(new BasicVariable(name, BasicVariableType.String, new StringDescriptor(length, pointer), valueAddress));
            }
            else
            {
                double value = FacFloat.Decode(memory[dataOffset], memory[dataOffset + 1], memory[dataOffset + 2], memory[dataOffset + 3], memory[dataOffset + 4]);
                variables.Add(new BasicVariable(name, BasicVariableType.Float, value, valueAddress));
            }

            address += _entrySize;
        }

        return variables;
    }

    #endregion

    #region Private Methods

    private static string BuildVariableName(byte nameByte1, byte nameByte2, bool isInteger, bool isString)
    {
        char firstChar = (char)(nameByte1 & 0x7F);
        char secondChar = (char)(nameByte2 & 0x7F);

        string name = secondChar == 0 ? firstChar.ToString() : $"{firstChar}{secondChar}";

        if (isInteger) name += "%";
        else if (isString) name += "$";

        return name;
    }

    #endregion
}
