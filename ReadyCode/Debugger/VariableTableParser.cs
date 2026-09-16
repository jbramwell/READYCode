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
/// One array variable's shape, parsed from just its header - no element data. Used to show a
/// collapsed summary row (e.g. "Float[3]") in the Variables tree without reading (and, for a
/// string array, round-tripping to resolve) the array's actual contents until the user expands it
/// - see <see cref="VariableTableParser.TryParseArrayHeader"/> and
/// <see cref="VariableTableParser.ParseArrayElements"/>.
/// </summary>
/// <param name="Name">The array's own name (including any $ or % suffix), without a subscript, upper-invariant.</param>
/// <param name="ElementType">Every element's type - uniform across a single array.</param>
/// <param name="DimensionSizes">Each dimension's element count (DIM N gives N+1, for indices 0 through N), in declared order.</param>
/// <param name="EntryLength">This entry's total byte length, i.e. the offset from this entry's own start to the next one.</param>
/// <param name="DataOffset">The byte offset, relative to this entry's own start, where element data begins.</param>
public sealed record ArrayHeader(string Name, BasicVariableType ElementType, int[] DimensionSizes, int EntryLength, int DataOffset);

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
/// ignored. See <see cref="ParseArrayVariables"/> for the array table (ARYTAB onward), a
/// separate, variable-length record format.
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

    /// <summary>
    /// Parses one array entry's header - name, element type, dimension shape, this entry's total
    /// length, and where its element data begins - without decoding any element data itself.
    /// </summary>
    /// <remarks>
    /// Unlike the simple-variable table's uniform 7-byte entries, an array entry is variable-length:
    /// 2 name bytes (same type-flag convention as a simple variable), a 2-byte big-endian total
    /// entry length (the offset from this entry's own start to the next one - i.e. to STREND for
    /// the last array), a 1-byte dimension count D, then D 2-byte big-endian dimension sizes -
    /// big-endian, not little-endian like VARTAB/ARYTAB/STREND and the string heap pointer, because
    /// these are BASIC-computed 16-bit numeric values (like a simple integer variable's own value,
    /// already confirmed big-endian - see <see cref="ParseSimpleVariables"/>), not raw 6502
    /// pointers - and finally the element data itself (see <see cref="ParseArrayElements"/>). Reading only
    /// this small fixed+per-dimension header - not the (potentially large) element data after it -
    /// is what lets the Variables tree enumerate every array's shape for its collapsed summary row
    /// cheaply, one small read per array regardless of how much data that array actually holds.
    /// </remarks>
    /// <param name="memory">
    /// The bytes read live from the machine, starting exactly at this entry's own address (i.e.
    /// <c>memory[offset]</c> is the entry's first name byte) and covering at least the header -
    /// does not need to cover the element data that follows it.
    /// </param>
    /// <param name="offset">Where in <paramref name="memory"/> this entry starts.</param>
    /// <returns>The parsed header, or null if <paramref name="memory"/> doesn't cover it in full.</returns>
    public static ArrayHeader? TryParseArrayHeader(byte[] memory, int offset)
    {
        // Fixed part of the header: 2 name bytes + 2 length bytes + 1 dimension-count byte.
        if (offset + 5 > memory.Length) return null;

        byte nameByte1 = memory[offset];
        byte nameByte2 = memory[offset + 1];
        bool isInteger = (nameByte1 & 0x80) != 0 && (nameByte2 & 0x80) != 0;
        bool isString = (nameByte1 & 0x80) == 0 && (nameByte2 & 0x80) != 0;
        string name = BuildVariableName(nameByte1, nameByte2, isInteger, isString);
        BasicVariableType elementType = isInteger ? BasicVariableType.Integer : isString ? BasicVariableType.String : BasicVariableType.Float;

        // Big-endian, like a simple integer variable's own value - see the class remarks above.
        int entryLength = (memory[offset + 2] << 8) | memory[offset + 3];
        int dimensionCount = memory[offset + 4];
        if (entryLength <= 0 || dimensionCount <= 0) return null; // corrupt - a real array always has at least one dimension

        int dimensionsOffset = offset + 5;
        if (dimensionsOffset + dimensionCount * 2 > memory.Length) return null;

        var dimensionSizes = new int[dimensionCount];
        for (int d = 0; d < dimensionCount; d++)
        {
            int sizeOffset = dimensionsOffset + d * 2;
            dimensionSizes[d] = (memory[sizeOffset] << 8) | memory[sizeOffset + 1]; // big-endian
        }

        int dataOffset = dimensionsOffset + dimensionCount * 2 - offset;
        return new ArrayHeader(name, elementType, dimensionSizes, entryLength, dataOffset);
    }

    /// <summary>
    /// Decodes one array's element data into one <see cref="BasicVariable"/> per element (e.g.
    /// <c>A(0)</c>, <c>A(2,3)</c>) rather than a distinct model - the Variables tree, its value
    /// formatting, and its live edit-and-write-back pipeline (see
    /// <see cref="ReadyCode.Debugger.VariableWriteBack"/>) only care about a
    /// <see cref="BasicVariable"/>'s <see cref="BasicVariable.Type"/> and
    /// <see cref="BasicVariable.ValueAddress"/>, so an element row needs no changes anywhere else
    /// to be displayed - or edited - exactly like any simple variable. Elements are produced in
    /// row-major order (the LAST subscript varies fastest, matching how BASIC itself stores a
    /// multi-dimensional array), each the same width as a simple variable's data area of that
    /// type: 5 bytes (float), 2 bytes (integer), or 3 bytes (string descriptor - length + heap
    /// pointer, just like a simple string's).
    /// </summary>
    /// <param name="data">
    /// The element data only - i.e. NOT including the array's name/length/dimension header bytes
    /// (see <see cref="ArrayHeader.DataOffset"/>) - starting exactly at <paramref name="dataAddress"/>.
    /// </param>
    /// <param name="dataAddress">The live address <c>data[0]</c> corresponds to.</param>
    /// <param name="baseName">The array's own name (including any $ or % suffix), without a subscript.</param>
    /// <param name="elementType">Every element's type - uniform across a single array.</param>
    /// <param name="dimensionSizes">Each dimension's element count, in declared order.</param>
    /// <returns>
    /// One <see cref="BasicVariable"/> per element. If <paramref name="data"/> runs out partway
    /// through, parsing stops at that point and returns everything decoded so far, matching
    /// <see cref="ParseSimpleVariables"/>'s same partial-result philosophy.
    /// </returns>
    public static IReadOnlyList<BasicVariable> ParseArrayElements(
        byte[] data, ushort dataAddress, string baseName, BasicVariableType elementType, IReadOnlyList<int> dimensionSizes)
    {
        var variables = new List<BasicVariable>();
        int elementWidth = elementType switch { BasicVariableType.Integer => 2, BasicVariableType.String => 3, _ => 5 };
        int elementCount = 1;
        foreach (int size in dimensionSizes) elementCount *= size;

        var indices = new int[dimensionSizes.Count];
        for (int e = 0; e < elementCount; e++)
        {
            int elementOffset = e * elementWidth;
            if (elementOffset + elementWidth > data.Length) break; // snapshot ran out mid-array - keep whatever decoded so far

            string elementName = $"{baseName}({string.Join(",", indices)})";
            ushort elementAddress = (ushort)(dataAddress + elementOffset);

            switch (elementType)
            {
                case BasicVariableType.Integer:
                    short intValue = (short)((data[elementOffset] << 8) | data[elementOffset + 1]);
                    variables.Add(new BasicVariable(elementName, BasicVariableType.Integer, intValue, elementAddress));
                    break;
                case BasicVariableType.String:
                    byte length = data[elementOffset];
                    ushort pointer = (ushort)(data[elementOffset + 1] | (data[elementOffset + 2] << 8));
                    variables.Add(new BasicVariable(elementName, BasicVariableType.String, new StringDescriptor(length, pointer), elementAddress));
                    break;
                default:
                    double floatValue = FacFloat.Decode(
                        data[elementOffset], data[elementOffset + 1], data[elementOffset + 2],
                        data[elementOffset + 3], data[elementOffset + 4]);
                    variables.Add(new BasicVariable(elementName, BasicVariableType.Float, floatValue, elementAddress));
                    break;
            }

            // Advance the index tuple, last dimension fastest (e.g. for a 2x3 array: (0,0), (0,1),
            // (0,2), (1,0), (1,1), (1,2)).
            for (int d = dimensionSizes.Count - 1; d >= 0; d--)
            {
                indices[d]++;
                if (indices[d] < dimensionSizes[d]) break;
                indices[d] = 0;
            }
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
