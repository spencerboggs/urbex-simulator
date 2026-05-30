using UnityEngine;

/// <summary>Parses hex color strings for editor and runtime configuration.</summary>
public static class HexColorUtility
{
    /// <summary>
    /// Parses a hex color string such as #FF0000 or FF0000 into a <see cref="Color"/>.
    /// </summary>
    /// <param name="hex">Hex string with optional leading #.</param>
    /// <param name="color">Parsed color when successful.</param>
    /// <returns>True when parsing succeeds.</returns>
    public static bool TryParse(string hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        string value = hex.Trim();
        if (value.StartsWith("#"))
            value = value.Substring(1);

        if (value.Length != 6 && value.Length != 8)
            return false;

        if (!TryParseByte(value, 0, out byte r) ||
            !TryParseByte(value, 2, out byte g) ||
            !TryParseByte(value, 4, out byte b))
        {
            return false;
        }

        byte a = 255;
        if (value.Length == 8 && !TryParseByte(value, 6, out a))
            return false;

        color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }

    /// <summary>
    /// Parses a hex color string or returns <paramref name="fallback"/> when invalid.
    /// </summary>
    public static Color ParseOrDefault(string hex, Color fallback)
    {
        return TryParse(hex, out Color color) ? color : fallback;
    }

    private static bool TryParseByte(string value, int startIndex, out byte result)
    {
        result = 0;
        if (startIndex + 1 >= value.Length)
            return false;

        int high = HexDigitValue(value[startIndex]);
        int low = HexDigitValue(value[startIndex + 1]);
        if (high < 0 || low < 0)
            return false;

        result = (byte)((high << 4) | low);
        return true;
    }

    private static int HexDigitValue(char c)
    {
        if (c >= '0' && c <= '9')
            return c - '0';
        if (c >= 'a' && c <= 'f')
            return c - 'a' + 10;
        if (c >= 'A' && c <= 'F')
            return c - 'A' + 10;
        return -1;
    }
}
