// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace ReadyCode.Debugger;

/// <summary>
/// Converts a user-entered replacement value for a live variable into the bytes to write and the
/// address to write them at, per the debugger spec's write-back rules: a float is re-encoded via
/// <see cref="FacFloat.Encode"/>, an integer is range-checked to a signed 16-bit big-endian value,
/// and a string may only be replaced with a same-length-or-shorter value (space-padded to the
/// original length) - safely reallocating the string heap for a longer value is out of scope.
/// </summary>
public static class VariableWriteBack
{
    #region Public Methods

    /// <summary>
    /// Encodes <paramref name="enteredText"/> as a replacement for <paramref name="variable"/>.
    /// </summary>
    /// <returns>The address to write to and the bytes to write there.</returns>
    /// <exception cref="FormatException">The entered text isn't a valid number for the variable's type.</exception>
    /// <exception cref="InvalidOperationException">
    /// The variable's current value hasn't finished loading yet, or a string replacement is
    /// longer than the current value.
    /// </exception>
    public static (ushort Address, byte[] Bytes) Encode(BasicVariable variable, string enteredText)
    {
        return variable.Type switch
        {
            BasicVariableType.Float => EncodeFloat(variable, enteredText),
            BasicVariableType.Integer => EncodeInteger(variable, enteredText),
            BasicVariableType.String => EncodeString(variable, enteredText),
            _ => throw new InvalidOperationException($"Unknown variable type '{variable.Type}'."),
        };
    }

    #endregion

    #region Private Methods

    private static (ushort, byte[]) EncodeFloat(BasicVariable variable, string enteredText)
    {
        if (!double.TryParse(enteredText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException($"\"{enteredText}\" is not a valid number.");

        var (exponentByte, m1, m2, m3, m4) = FacFloat.Encode(value);
        return (variable.ValueAddress, new[] { exponentByte, m1, m2, m3, m4 });
    }

    private static (ushort, byte[]) EncodeInteger(BasicVariable variable, string enteredText)
    {
        if (!short.TryParse(enteredText, NumberStyles.Integer, CultureInfo.InvariantCulture, out short value))
            throw new FormatException($"\"{enteredText}\" is not a valid integer (-32768 to 32767).");

        return (variable.ValueAddress, new[] { (byte)(value >> 8), (byte)value }); // big-endian
    }

    private static (ushort, byte[]) EncodeString(BasicVariable variable, string enteredText)
    {
        if (variable.Value is not ResolvedStringValue current)
            throw new InvalidOperationException("This string's value hasn't finished loading yet.");

        // Tolerate re-submitting the display format's surrounding quotes unchanged.
        string newText = enteredText;
        if (newText.Length >= 2 && newText.StartsWith('"') && newText.EndsWith('"'))
            newText = newText[1..^1];

        if (newText.Length > current.Text.Length)
        {
            string plural = current.Text.Length == 1 ? "" : "s";
            throw new InvalidOperationException(
                $"\"{newText}\" is longer than the current value ({current.Text.Length} character{plural}). " +
                "Live string editing supports same-length or shorter replacements only. Restart the session " +
                "with the new value hardcoded to test longer strings.");
        }

        string padded = newText.PadRight(current.Text.Length, ' ');
        byte[] bytes = padded.Select(c => (byte)c).ToArray();
        return (current.HeapPointer, bytes);
    }

    #endregion
}
