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

    // ── Environment normalisation ─────────────────────────────────────────

    [Theory]
    [InlineData("beta",  "BETA")]
    [InlineData("BETA",  "BETA")]
    [InlineData("live",  "LIVE")]
    [InlineData("LIVE",  "LIVE")]
    public void Map_Environment_NormalisedToUpperCase(string inputEnv, string expectedEnv)
    {
        var config = CreateMapper().Map(new ParseResult([], []), DefaultInput(inputEnv));
        Assert.Equal(expectedEnv, config.Environment);
    }

    // ── TABLE 2: Settings Master whitelist ────────────────────────────────

    [Theory]
    [InlineData("GenSettings",      "S")]
    [InlineData("EmailSettings",    "S")]
    [InlineData("TemplateSettings", "S")]
    [InlineData("Hotel",            "P")]
    public void Map_AllowedSettingsHead_ProducesMasterRow(string head, string expectedType)
    {
        var sections    = new List<ParsedSection> { new(head, SectionKind.Settings, [new("K", "V", "string")]) };
        var config      = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.SettingsMaster);
        Assert.Equal(head,         config.SettingsMaster[0].SettingHead);
        Assert.Equal(expectedType, config.SettingsMaster[0].SettingsType);
        Assert.Equal(0,            config.SettingsMaster[0].RecordStatus);
    }

    [Theory]
    [InlineData("AirSettings")]
    [InlineData("SMSSettings")]
    [InlineData("CacheGenSettings")]
    [InlineData("BucketStoreSettings")]
    [InlineData("WhatsAppSettings")]
    public void Map_NonWhitelistedSettingsHead_IsSkipped(string head)
    {
        var sections    = new List<ParsedSection> { new(head, SectionKind.Settings, [new("K", "V", "string")]) };
        var config      = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Empty(config.SettingsMaster);
        Assert.Empty(config.SettingsDetails);
    }

    [Fact]
    public void Map_NonWhitelistedSettings_ProducesSkippedWarning()
    {
        var sections    = new List<ParsedSection> { new("AirSettings", SectionKind.Settings, [new("K", "V", "string")]) };
        var config      = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Contains(config.ParseIssues, i => i.Code == "SETTINGS_HEAD_SKIPPED");
    }

    // ── TABLE 3: Settings Details — Hotel excluded ────────────────────────

    [Theory]
    [InlineData("GenSettings")]
    [InlineData("EmailSettings")]
    [InlineData("TemplateSettings")]
    public void Map_AllowedDetailHead_ProducesDetailRows(string head)
    {
        var entries  = new List<ParsedEntry> { new("K1", "V1", "string"), new("K2", "V2", "string") };
        var sections = new List<ParsedSection> { new(head, SectionKind.Settings, entries) };
        var config   = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Equal(2, config.SettingsDetails.Count);
        Assert.All(config.SettingsDetails, d => Assert.Equal(head, d.SettingsHead));
        Assert.All(config.SettingsDetails, d => Assert.Equal(0, d.RecordStatus));
        Assert.All(config.SettingsDetails, d => Assert.Null(d.Aui));
    }

    [Fact]
    public void Map_HotelSettingsSection_ProducesMasterButNoDetails()
    {
        var sections = new List<ParsedSection>
        {
            new("Hotel", SectionKind.Settings, [new("ChannelCodes", "DY,GT", "string")])
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        // Hotel gets a master row with SettingsType='P'
        Assert.Single(config.SettingsMaster);
        Assert.Equal("P", config.SettingsMaster[0].SettingsType);

        // But NO detail rows for Hotel
        Assert.Empty(config.SettingsDetails);
    }

    // ── TABLE 4 & 5: Params Master whitelist ─────────────────────────────

    [Theory]
    [InlineData("Hotel")]
    [InlineData("HtlGeneral")]
    [InlineData("ZealConnect")]
    public void Map_AllowedParamsHead_ProducesMasterRow(string paramsHead)
    {
        var encodedName = paramsHead == "Hotel" ? "Hotel" : $"Hotel|{paramsHead}";
        var sections    = new List<ParsedSection> { new(encodedName, SectionKind.Params, [new("K", "V", "string")]) };
        var config      = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.ParamsMaster);
        Assert.Equal(paramsHead, config.ParamsMaster[0].ParamsHead);
        Assert.Equal("Hotel",    config.ParamsMaster[0].SettingHead);
        Assert.Equal(0,          config.ParamsMaster[0].RecordStatus);
    }

    [Theory]
    [InlineData("Airline")]
    [InlineData("Insurance")]
    [InlineData("Payment")]
    [InlineData("Utility")]
    public void Map_NonWhitelistedParamsHead_IsSkipped(string paramsHead)
    {
        var sections = new List<ParsedSection> { new(paramsHead, SectionKind.Params, [new("K", "V", "string")]) };
        var config   = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Empty(config.ParamsMaster);
        Assert.Empty(config.ParamsSettings);
    }

    [Fact]
    public void Map_NonWhitelistedParams_ProducesSkippedWarning()
    {
        var sections = new List<ParsedSection> { new("Airline", SectionKind.Params, [new("K", "V", "string")]) };
        var config   = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Contains(config.ParseIssues, i => i.Code == "PARAMS_HEAD_SKIPPED");
    }

    // ── TABLE 5: ParentHead encoding ──────────────────────────────────────

    [Fact]
    public void Map_HotelParams_HasNullParentHead()
    {
        var sections = new List<ParsedSection>
        {
            new("Hotel", SectionKind.Params, [new("ChannelCodes", "DY,GT", "string")])
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.ParamsSettings);
        Assert.Null(config.ParamsSettings[0].ParentHead);
        Assert.Equal("Hotel",        config.ParamsSettings[0].ParamsHead);
        Assert.Equal("ChannelCodes", config.ParamsSettings[0].MemberName);
    }

    [Theory]
    [InlineData("HtlGeneral")]
    [InlineData("ZealConnect")]
    public void Map_ChildParams_HasHotelAsParentHead(string childName)
    {
        var sections = new List<ParsedSection>
        {
            new($"Hotel|{childName}", SectionKind.Params, [new("Decimals", "2", "string")])
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.ParamsSettings);
        Assert.Equal(childName, config.ParamsSettings[0].ParamsHead);
        Assert.Equal("Hotel",   config.ParamsSettings[0].ParentHead);
    }

    // ── TABLE 1: Database selection rules ─────────────────────────────────

    [Fact]
    public void Map_DatabaseGroup_RuleA_ActiveEntrySelected()
    {
        // Type 3: two entries, one active (aerospike) and one inactive (mysql)
        var mysql = new List<ParsedEntry>
        {
            new("DataBaseType", "3", "number"), new("Description", "Cache", "string"),
            new("ActiveStatus", "false", "string"), new("ReadEnable", "true", "string"),
            new("WriteEnable", "true", "string"), new("Provider", "mysql", "string"),
        };
        var aerospike = new List<ParsedEntry>
        {
            new("DataBaseType", "3", "number"), new("Description", "Cache", "string"),
            new("ActiveStatus", "true", "string"), new("ReadEnable", "true", "string"),
            new("WriteEnable", "true", "string"), new("Provider", "aerospike", "string"),
        };

        var sections = new List<ParsedSection>
        {
            new("Databases", SectionKind.Database, mysql),
            new("Databases", SectionKind.Database, aerospike),
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        // Rule A: only the active entry is inserted
        Assert.Single(config.Databases);
        Assert.Equal("aerospike", config.Databases[0].Provider);
        Assert.Equal(0, config.Databases[0].RecordStatus);
    }

    [Fact]
    public void Map_DatabaseGroup_RuleB_SingleInactiveEntry()
    {
        // Single entry that is inactive
        var entries = new List<ParsedEntry>
        {
            new("DataBaseType", "2", "number"), new("Description", "Store", "string"),
            new("ActiveStatus", "false", "string"), new("ReadEnable", "false", "string"),
            new("WriteEnable", "false", "string"), new("Provider", "mysql", "string"),
        };
        var sections = new List<ParsedSection>
        {
            new("Databases", SectionKind.Database, entries),
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        // Rule B: inserted with ReadEnable=0, WriteEnable=0, RecordStatus=0
        Assert.Single(config.Databases);
        var db = config.Databases[0];
        Assert.Equal(0, db.ReadEnable);
        Assert.Equal(0, db.WriteEnable);
        Assert.Equal(0, db.RecordStatus);
    }

    [Fact]
    public void Map_DatabaseGroup_RuleC_MultipleAllInactive_TakesFirst()
    {
        // Multiple entries all inactive — only first is taken, RecordStatus=1
        var entry1 = new List<ParsedEntry>
        {
            new("DataBaseType", "16", "number"), new("Description", "Cache Write", "string"),
            new("ActiveStatus", "false", "string"), new("Provider", "mysql", "string"),
            new("DataBaseName", "b2ccachedb", "string"),
        };
        var entry2 = new List<ParsedEntry>
        {
            new("DataBaseType", "16", "number"), new("Description", "Cache Write", "string"),
            new("ActiveStatus", "false", "string"), new("Provider", "aerospike", "string"),
            new("DataBaseName", "cache:cache", "string"),
        };
        var sections = new List<ParsedSection>
        {
            new("Databases", SectionKind.Database, entry1),
            new("Databases", SectionKind.Database, entry2),
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        // Rule C: only first entry, RecordStatus=1, ReadEnable=0, WriteEnable=0
        Assert.Single(config.Databases);
        var db = config.Databases[0];
        Assert.Equal("b2ccachedb", db.DataBaseName);
        Assert.Equal(1, db.RecordStatus);
        Assert.Equal(0, db.ReadEnable);
        Assert.Equal(0, db.WriteEnable);
    }

    [Fact]
    public void Map_DatabaseGroup_RecordStatus_ActiveIsZero_InactiveIsOne()
    {
        var active = new List<ParsedEntry>
        {
            new("DataBaseType", "1", "number"), new("ActiveStatus", "true", "string"),
            new("Provider", "mysql", "string"),
        };
        var sections = new List<ParsedSection> { new("Databases", SectionKind.Database, active) };
        var config   = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.Databases);
        Assert.Equal(0, config.Databases[0].RecordStatus); // active → 0
        Assert.Equal(1, config.Databases[0].ActiveStatus);
    }

    [Fact]
    public void Map_Database_AuiAlwaysNull()
    {
        var entries = new List<ParsedEntry>
        {
            new("DataBaseType", "1", "number"), new("ActiveStatus", "true", "string"),
            new("AUI", "somevalue", "string"), // even if present in source
        };
        var sections = new List<ParsedSection> { new("Databases", SectionKind.Database, entries) };
        var config   = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Null(config.Databases[0].Aui);
    }

    // ── Port / Version / Environment injected ────────────────────────────

    [Fact]
    public void Map_PortVersionEnvironment_InjectedIntoAllRows()
    {
        var sections = new List<ParsedSection>
        {
            new("GenSettings",  SectionKind.Settings, [new("K", "V", "string")]),
            new("Hotel|HtlGeneral", SectionKind.Params, [new("K", "V", "string")]),
        };
        var input  = new ConfigurationInput
        {
            JsonContent = "{}", FileName = "f.json",
            Port = "8080", Version = "6.0", Environment = "LIVE", IncludeTransaction = false
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), input);

        Assert.All(config.SettingsMaster,  r => Assert.Equal("8080", r.Port));
        Assert.All(config.SettingsDetails, r => Assert.Equal("8080", r.Port));
        Assert.All(config.ParamsMaster,    r => Assert.Equal("8080", r.Port));
        Assert.All(config.ParamsSettings,  r => Assert.Equal("8080", r.Port));
        Assert.All(config.SettingsDetails, r => Assert.Equal("LIVE", r.Environment));
        Assert.All(config.ParamsSettings,  r => Assert.Equal("LIVE", r.Environment));
    }

    // ── Null value preservation ───────────────────────────────────────────

    [Fact]
    public void Map_NullParsedEntry_PreservesNullMemberValue()
    {
        var sections = new List<ParsedSection>
        {
            new("GenSettings", SectionKind.Settings, [new("Region", null, "null")])
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.SettingsDetails);
        Assert.Null(config.SettingsDetails[0].MemberValue);
    }

    // ── Duplicate detection ───────────────────────────────────────────────

    [Fact]
    public void Map_DuplicateAllowedSettingsSectionName_OneMasterRowProduced()
    {
        var sections = new List<ParsedSection>
        {
            new("GenSettings", SectionKind.Settings, [new("K1", "V1", "string")]),
            new("GenSettings", SectionKind.Settings, [new("K2", "V2", "string")]),
        };
        var config = CreateMapper().Map(new ParseResult(sections, []), DefaultInput());

        Assert.Single(config.SettingsMaster);
    }
}
