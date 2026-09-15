using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HotelConfigAnalyser.Tests;

public sealed class JsonConfigurationParserTests
{
    private static JsonConfigurationParser CreateParser() =>
        new(NullLogger<JsonConfigurationParser>.Instance);

    // ── Invalid / empty input ─────────────────────────────────────────────

    [Fact]
    public void Parse_NullInput_ReturnsEmptyJsonError()
    {
        var result = CreateParser().Parse(null!);
        Assert.Contains(result.Issues, i => i.Code == "EMPTY_JSON" && i.Severity == IssueSeverity.Error);
        Assert.Empty(result.Sections);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyJsonError()
    {
        var result = CreateParser().Parse(string.Empty);
        Assert.Contains(result.Issues, i => i.Code == "EMPTY_JSON");
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsEmptyJsonError()
    {
        var result = CreateParser().Parse("   \t\n  ");
        Assert.Contains(result.Issues, i => i.Code == "EMPTY_JSON");
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsInvalidJsonError()
    {
        var result = CreateParser().Parse("{ this is not json }");
        Assert.Contains(result.Issues, i => i.Code == "INVALID_JSON" && i.Severity == IssueSeverity.Error);
        Assert.Empty(result.Sections);
    }

    [Fact]
    public void Parse_JsonArray_ReturnsJsonNotObjectError()
    {
        var result = CreateParser().Parse("[1, 2, 3]");
        Assert.Contains(result.Issues, i => i.Code == "JSON_NOT_OBJECT");
    }

    [Fact]
    public void Parse_EmptyObject_ReturnsEmptyConfigurationError()
    {
        var result = CreateParser().Parse("{}");
        Assert.Contains(result.Issues, i => i.Code == "EMPTY_CONFIGURATION");
    }

    // ── Settings sections ─────────────────────────────────────────────────

    [Fact]
    public void Parse_FlatSettingsSection_ProducesSettingsSection()
    {
        var json = """
        {
            "Hotel": {
                "Region": "CA",
                "GDSswitch": "0"
            }
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Empty(result.Issues.Where(i => i.Severity == IssueSeverity.Error));
        Assert.Single(result.Sections);
        var section = result.Sections[0];
        Assert.Equal("Hotel", section.Name);
        Assert.Equal(SectionKind.Settings, section.Kind);
        Assert.Equal(2, section.Entries.Count);
        Assert.Contains(section.Entries, e => e.Key == "Region" && e.Value == "CA");
        Assert.Contains(section.Entries, e => e.Key == "GDSswitch" && e.Value == "0");
    }

    [Fact]
    public void Parse_MultipleSettingsSections_ProducesMultipleSections()
    {
        var json = """
        {
            "Hotel": { "Region": "CA" },
            "Email": { "SMTPServer": "smtp.example.com" }
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Equal(2, result.Sections.Count);
        Assert.All(result.Sections, s => Assert.Equal(SectionKind.Settings, s.Kind));
    }

    // ── Params sections ───────────────────────────────────────────────────

    [Fact]
    public void Parse_HtlGeneralSection_ProducesParamsSection()
    {
        var json = """
        {
            "HtlGeneral": {
                "Decimals": "2",
                "HomeCurrencyCode": "CAD"
            }
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Single(result.Sections);
        Assert.Equal(SectionKind.Params, result.Sections[0].Kind);
    }

    // ── Database sections ─────────────────────────────────────────────────

    [Fact]
    public void Parse_DatabasesArray_ProducesDatabaseSections()
    {
        var json = """
        {
            "Databases": [
                { "Server": "db1.example.com", "DataBaseName": "hoteldb", "UserName": "admin" },
                { "Server": "db2.example.com", "DataBaseName": "reportdb", "UserName": "reader" }
            ]
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Equal(2, result.Sections.Count);
        Assert.All(result.Sections, s => Assert.Equal(SectionKind.Database, s.Kind));
    }

    // ── Null values ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullValue_ProducesNullEntryAndWarning()
    {
        var json = """
        {
            "Hotel": {
                "Region": null
            }
        }
        """;

        var result = CreateParser().Parse(json);
        var section = result.Sections[0];
        Assert.Contains(section.Entries, e => e.Key == "Region" && e.Value == null);
        Assert.Contains(result.Issues, i => i.Code == "NULL_VALUE" && i.Severity == IssueSeverity.Warning);
    }

    // ── Duplicate keys ────────────────────────────────────────────────────

    [Fact]
    public void Parse_DuplicateKeyInSection_ProducesDuplicateWarning()
    {
        // JSON spec allows duplicate keys; our parser detects and warns
        var json = """
        {
            "Hotel": {
                "Region": "CA",
                "Region": "US"
            }
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Contains(result.Issues, i => i.Code == "DUPLICATE_KEY" && i.Severity == IssueSeverity.Warning);
        // Only the first occurrence should be kept
        var regionEntries = result.Sections[0].Entries.Where(e => e.Key == "Region").ToList();
        Assert.Single(regionEntries);
        Assert.Equal("CA", regionEntries[0].Value);
    }

    // ── AppSettings wrapper ───────────────────────────────────────────────

    [Fact]
    public void Parse_AppSettingsWrapper_UnwrapsAndProcessesInnerSections()
    {
        var json = """
        {
            "AppSettings": {
                "Hotel": { "Region": "CA" },
                "HtlGeneral": { "Decimals": "2" }
            }
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Equal(2, result.Sections.Count);
        Assert.Contains(result.Issues, i => i.Code == "JSON_UNWRAPPED" && i.Severity == IssueSeverity.Warning);
    }

    // ── Data type inference ───────────────────────────────────────────────

    [Fact]
    public void Parse_NumberValue_InferredAsNumberDataType()
    {
        var json = """{ "Hotel": { "Port": 8080 } }""";
        var result = CreateParser().Parse(json);
        var entry = result.Sections[0].Entries[0];
        Assert.Equal("number", entry.DataType);
        Assert.Equal("8080", entry.Value);
    }

    [Fact]
    public void Parse_BooleanValue_InferredAsBoolDataType()
    {
        var json = """{ "Hotel": { "Enabled": true } }""";
        var result = CreateParser().Parse(json);
        var entry = result.Sections[0].Entries[0];
        Assert.Equal("bool", entry.DataType);
        Assert.Equal("true", entry.Value);
    }

    // ── Missing section ───────────────────────────────────────────────────

    [Fact]
    public void Parse_MissingSectionGraceful_NoException()
    {
        // Only has one section, others are simply absent
        var json = """{ "Hotel": { "Region": "CA" } }""";
        var exception = Record.Exception(() => CreateParser().Parse(json));
        Assert.Null(exception);
    }

    // ── Comments and trailing commas ─────────────────────────────────────

    [Fact]
    public void Parse_TrailingCommaInJson_HandledGracefully()
    {
        var json = """
        {
            "Hotel": {
                "Region": "CA",
            }
        }
        """;

        var result = CreateParser().Parse(json);
        Assert.Empty(result.Issues.Where(i => i.Severity == IssueSeverity.Error));
    }
}
