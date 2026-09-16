using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HotelConfigAnalyser.Tests;

public sealed class ConfigurationMapperTests
{
    private static ConfigurationMapper CreateMapper() =>
        new(NullLogger<ConfigurationMapper>.Instance);

    private static ConfigurationInput DefaultInput(string env = "BETA") =>
        new()
        {
            JsonContent        = "{}",
            FileName           = "test.json",
            Port               = "80",
            Version            = "6.0",
            Environment        = env,
            IncludeTransaction = true,
        };

    // ── Environment mapping ───────────────────────────────────────────────

    [Theory]
    [InlineData("beta",  "BETA")]
    [InlineData("BETA",  "BETA")]
    [InlineData("live",  "LIVE")]
    [InlineData("LIVE",  "LIVE")]
    public void Map_Environment_NormalisedToUpperCase(string inputEnv, string expectedEnv)
    {
        var parseResult = new ParseResult([], []);
        var input       = DefaultInput(inputEnv);
        var config      = CreateMapper().Map(parseResult, input);
        Assert.Equal(expectedEnv, config.Environment);
    }

    // ── Settings section → settings master + details ───────────────────────

    [Fact]
    public void Map_SettingsSection_ProducesOneMasterAndManyDetails()
    {
        var entries = new List<ParsedEntry>
        {
            new("Region",    "CA",  "string"),
            new("GDSswitch", "0",   "string"),
            new("MobileGDS", "AM",  "string"),
        };
        var sections = new List<ParsedSection>
        {
            new("GenSettings", SectionKind.Settings, entries)   // not "Hotel" → type "S"
        };
        var parseResult = new ParseResult(sections, []);

        var config = CreateMapper().Map(parseResult, DefaultInput());

        // One master row for "GenSettings"
        Assert.Single(config.SettingsMaster);
        Assert.Equal("GenSettings", config.SettingsMaster[0].SettingHead);
        Assert.Equal("S",           config.SettingsMaster[0].SettingsType);

        // Three detail rows
        Assert.Equal(3, config.SettingsDetails.Count);
        Assert.All(config.SettingsDetails, d => Assert.Equal("GenSettings", d.SettingsHead));
        Assert.Contains(config.SettingsDetails, d => d.MemberName == "Region" && d.MemberValue == "CA");
    }

    [Fact]
    public void Map_DuplicateSettingsSectionName_OneMasterRowProduced()
    {
        var sections = new List<ParsedSection>
        {
            new("Hotel", SectionKind.Settings, [new("Region", "CA", "string")]),
            new("Hotel", SectionKind.Settings, [new("Country", "US", "string")]),
        };
        var parseResult = new ParseResult(sections, []);
        var config      = CreateMapper().Map(parseResult, DefaultInput());

        // Only one master row despite two sections with same name
        Assert.Single(config.SettingsMaster);
    }

    // ── Params section → params master + settings ─────────────────────────

    [Fact]
    public void Map_ParamsSection_ProducesOneMasterAndManySettings()
    {
        var entries = new List<ParsedEntry>
        {
            new("Decimals",         "2",    "string"),
            new("HomeCurrencyCode", "CAD",  "string"),
        };
        var sections = new List<ParsedSection>
        {
            new("HtlGeneral", SectionKind.Params, entries)
        };
        var parseResult = new ParseResult(sections, []);
        var config      = CreateMapper().Map(parseResult, DefaultInput());

        Assert.Single(config.ParamsMaster);
        Assert.Equal("HtlGeneral", config.ParamsMaster[0].ParamsHead);
        Assert.Equal("Hotel",      config.ParamsMaster[0].SettingHead); // derived from "Htl" prefix

        Assert.Equal(2, config.ParamsSettings.Count);
        Assert.All(config.ParamsSettings, p => Assert.Equal("HtlGeneral", p.ParamsHead));
    }

    // ── Port/Version/Environment injected into every row ──────────────────

    [Fact]
    public void Map_PortVersionEnvironment_InjectedIntoAllRows()
    {
        var sections = new List<ParsedSection>
        {
            new("Hotel",      SectionKind.Settings, [new("Key", "Val", "string")]),
            new("HtlGeneral", SectionKind.Params,   [new("Key", "Val", "string")]),
        };
        var parseResult = new ParseResult(sections, []);
        var input       = new ConfigurationInput
        {
            JsonContent = "{}", FileName = "f.json",
            Port = "8080", Version = "6.0", Environment = "LIVE", IncludeTransaction = false
        };
        var config = CreateMapper().Map(parseResult, input);

        Assert.All(config.SettingsMaster,  r => Assert.Equal("8080", r.Port));
        Assert.All(config.SettingsDetails, r => Assert.Equal("8080", r.Port));
        Assert.All(config.ParamsMaster,    r => Assert.Equal("8080", r.Port));
        Assert.All(config.ParamsSettings,  r => Assert.Equal("8080", r.Port));

        Assert.All(config.SettingsDetails, r => Assert.Equal("LIVE", r.Environment));
        Assert.All(config.ParamsSettings,  r => Assert.Equal("LIVE", r.Environment));
    }

    // ── Null value preservation ───────────────────────────────────────────

    [Fact]
    public void Map_NullParsedEntry_ProducesNullMemberValue()
    {
        var sections = new List<ParsedSection>
        {
            new("Hotel", SectionKind.Settings, [new("Region", null, "null")])
        };
        var parseResult = new ParseResult(sections, []);
        var config      = CreateMapper().Map(parseResult, DefaultInput());

        Assert.Single(config.SettingsDetails);
        Assert.Null(config.SettingsDetails[0].MemberValue);
    }

    // ── Database section ──────────────────────────────────────────────────

    [Fact]
    public void Map_DatabaseSection_ProducesDatabaseEntry()
    {
        var entries = new List<ParsedEntry>
        {
            new("Server",       "db.example.com", "string"),
            new("DataBaseName", "hoteldb",         "string"),
            new("UserName",     "admin",           "string"),
            new("Password",     "s3cr3t",          "string"),
            new("DataBaseType", "1",               "number"),
        };
        var sections    = new List<ParsedSection> { new("Databases", SectionKind.Database, entries) };
        var parseResult = new ParseResult(sections, []);
        var config      = CreateMapper().Map(parseResult, DefaultInput());

        Assert.Single(config.Databases);
        var db = config.Databases[0];
        Assert.Equal("db.example.com", db.Server);
        Assert.Equal("hoteldb",         db.DataBaseName);
        Assert.Equal("admin",           db.UserName);
        Assert.Equal("s3cr3t",          db.Password);
        Assert.Equal(1,                 db.DataBaseType);
    }
}
