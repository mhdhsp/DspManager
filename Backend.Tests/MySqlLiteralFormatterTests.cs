using HotelConfigAnalyser.Utilities;
using Xunit;

namespace HotelConfigAnalyser.Tests;

/// <summary>
/// Tests for MySqlLiteralFormatter — the most security-critical component.
/// A bug here produces SQL injection vulnerabilities.
/// </summary>
public sealed class MySqlLiteralFormatterTests
{
    // ── Null ──────────────────────────────────────────────────────────────

    [Fact]
    public void Format_Null_ReturnsNullKeyword()
    {
        Assert.Equal("NULL", MySqlLiteralFormatter.Format(null));
        Assert.Equal("NULL", MySqlLiteralFormatter.FormatNullableString(null));
    }

    // ── Booleans ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true,  "1")]
    [InlineData(false, "0")]
    public void Format_Bool_Returns1Or0(bool value, string expected)
    {
        Assert.Equal(expected, MySqlLiteralFormatter.Format(value));
    }

    // ── Integers ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,    "0")]
    [InlineData(42,   "42")]
    [InlineData(-1,   "-1")]
    [InlineData(int.MaxValue, "2147483647")]
    public void Format_Int_ReturnsUnquotedNumber(int value, string expected)
    {
        Assert.Equal(expected, MySqlLiteralFormatter.Format(value));
        Assert.Equal(expected, MySqlLiteralFormatter.FormatInt(value));
    }

    [Fact]
    public void FormatNullableInt_Null_ReturnsNullKeyword()
    {
        Assert.Equal("NULL", MySqlLiteralFormatter.FormatNullableInt(null));
    }

    [Fact]
    public void FormatNullableInt_Value_ReturnsUnquotedNumber()
    {
        Assert.Equal("7", MySqlLiteralFormatter.FormatNullableInt(7));
    }

    // ── Normal strings ────────────────────────────────────────────────────

    [Fact]
    public void Format_SimpleString_ReturnsSingleQuoted()
    {
        Assert.Equal("'hello'", MySqlLiteralFormatter.Format("hello"));
    }

    [Fact]
    public void Format_EmptyString_ReturnsEmptyQuotes()
    {
        Assert.Equal("''", MySqlLiteralFormatter.Format(string.Empty));
    }

    // ── Apostrophe (SQL injection risk) ───────────────────────────────────

    [Fact]
    public void Format_StringWithApostrophe_DoublesIt()
    {
        // O'Brien Hotel → 'O''Brien Hotel'
        Assert.Equal("'O''Brien Hotel'", MySqlLiteralFormatter.Format("O'Brien Hotel"));
    }

    [Fact]
    public void Format_StringWithMultipleApostrophes_DoublesEach()
    {
        Assert.Equal("'it''s a ''test'''", MySqlLiteralFormatter.Format("it's a 'test'"));
    }

    // ── Backslash ─────────────────────────────────────────────────────────

    [Fact]
    public void Format_Backslash_EscapesCorrectly()
    {
        Assert.Equal(@"'C:\\path\\to\\file'", MySqlLiteralFormatter.Format(@"C:\path\to\file"));
    }

    // ── Whitespace control characters ─────────────────────────────────────

    [Fact]
    public void Format_Newline_EscapesCorrectly()
    {
        Assert.Equal("'line1\\nline2'", MySqlLiteralFormatter.Format("line1\nline2"));
    }

    [Fact]
    public void Format_CarriageReturn_EscapesCorrectly()
    {
        Assert.Equal("'line1\\rline2'", MySqlLiteralFormatter.Format("line1\rline2"));
    }

    [Fact]
    public void Format_Tab_EscapesCorrectly()
    {
        Assert.Equal("'col1\\tcol2'", MySqlLiteralFormatter.Format("col1\tcol2"));
    }

    [Fact]
    public void Format_NullByte_EscapesCorrectly()
    {
        Assert.Equal("'abc\\0def'", MySqlLiteralFormatter.Format("abc\0def"));
    }

    // ── Unicode ───────────────────────────────────────────────────────────

    [Fact]
    public void Format_UnicodeString_PreservesCharacters()
    {
        Assert.Equal("'Hôtel Résidence'", MySqlLiteralFormatter.Format("Hôtel Résidence"));
    }

    [Fact]
    public void Format_ChineseCharacters_PreservesCharacters()
    {
        Assert.Equal("'酒店配置'", MySqlLiteralFormatter.Format("酒店配置"));
    }

    // ── DateTime ──────────────────────────────────────────────────────────

    [Fact]
    public void Format_DateTime_ReturnsIso8601QuotedString()
    {
        var dt = new DateTime(2026, 9, 15, 14, 30, 0);
        Assert.Equal("'2026-09-15 14:30:00'", MySqlLiteralFormatter.Format(dt));
    }

    // ── Numeric strings (config values like "0", "1") ─────────────────────

    [Fact]
    public void Format_StringZero_ReturnsSingleQuoted()
    {
        // Configuration values like "0" or "1" are strings, not ints
        Assert.Equal("'0'", MySqlLiteralFormatter.Format("0"));
        Assert.Equal("'1'", MySqlLiteralFormatter.Format("1"));
    }

    // ── Combined escaping ─────────────────────────────────────────────────

    [Fact]
    public void Format_ComplexString_EscapesAllSpecialChars()
    {
        // "it's\nline2" should become 'it''s\nline2'
        var input    = "it's\nline2";
        var expected = "'it''s\\nline2'";
        Assert.Equal(expected, MySqlLiteralFormatter.Format(input));
    }
}
