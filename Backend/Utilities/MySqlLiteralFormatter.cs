using System.Text;

namespace HotelConfigAnalyser.Utilities;

/// <summary>
/// Converts CLR values into safe MySQL literal strings suitable for embedding
/// directly inside INSERT statements.
///
/// Rules implemented:
///   - null            → NULL  (no quotes)
///   - bool            → 1 / 0
///   - numeric types   → unquoted numeric literal
///   - DateTime        → 'YYYY-MM-DD HH:MM:SS'
///   - string          → single-quoted with internal escaping:
///       '  → ''
///       \  → \\
///       \n → \n   (MySQL escape sequence)
///       \r → \r
///       \t → \t
///       \0 → \0   (null byte)
///       \x1a → \Z (ctrl-Z, MySQL EOF on Windows)
/// </summary>
public static class MySqlLiteralFormatter
{
    /// <summary>
    /// Format any CLR value as a MySQL literal.
    /// Pass a string, numeric, bool, DateTime, or null.
    /// </summary>
    public static string Format(object? value)
    {
        return value switch
        {
            null            => "NULL",
            bool b          => b ? "1" : "0",
            sbyte n         => n.ToString(),
            byte n          => n.ToString(),
            short n         => n.ToString(),
            ushort n        => n.ToString(),
            int n           => n.ToString(),
            uint n          => n.ToString(),
            long n          => n.ToString(),
            ulong n         => n.ToString(),
            float f         => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            double d        => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            decimal d       => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTime dt     => FormatDateTime(dt),
            string s        => FormatString(s),
            _               => FormatString(value.ToString() ?? string.Empty),
        };
    }

    /// <summary>Convenience overload for nullable strings.</summary>
    public static string FormatNullableString(string? value) =>
        value is null ? "NULL" : FormatString(value);

    /// <summary>Format an integer directly (avoids boxing for hot paths).</summary>
    public static string FormatInt(int value) => value.ToString();

    /// <summary>Format a nullable int: null → NULL, otherwise unquoted integer.</summary>
    public static string FormatNullableInt(int? value) =>
        value.HasValue ? value.Value.ToString() : "NULL";

    // ── Private helpers ───────────────────────────────────────────────────

    private static string FormatDateTime(DateTime dt) =>
        $"'{dt:yyyy-MM-dd HH:mm:ss}'";

    private static string FormatString(string value)
    {
        if (value.Length == 0)
            return "''";

        var sb = new StringBuilder(value.Length + 8);
        sb.Append('\'');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\'':  sb.Append("''");   break;   // Standard SQL single-quote escape
                case '\\':  sb.Append("\\\\"); break;
                case '\n':  sb.Append("\\n");  break;
                case '\r':  sb.Append("\\r");  break;
                case '\t':  sb.Append("\\t");  break;
                case '\0':  sb.Append("\\0");  break;
                case '\x1a': sb.Append("\\Z"); break;
                default:    sb.Append(ch);     break;
            }
        }

        sb.Append('\'');
        return sb.ToString();
    }
}
