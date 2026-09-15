using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HotelConfigAnalyser.Tests;

public sealed class SqlGeneratorTests
{
    private static SqlGenerator CreateGenerator() =>
        new(NullLogger<SqlGenerator>.Instance);

    private static NormalisedConfiguration BaseConfig(string env = "BETA") =>
        new()
        {
            Port               = "80",
            Version            = "6.0",
            Environment        = env,
            FileName           = "test.json",
            IncludeTransaction = true,
        };

    // ── Environment → table name ──────────────────────────────────────────

    [Fact]
    public void Generate_BetaEnvironment_UsesSetPrefix()
    {
        var config = BaseConfig("BETA") with
        {
            SettingsMaster = new List<SettingsMasterEntry>
            {
                new() { SettingHead = "Hotel", SettingsType = "S",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.Contains("`set_htl_settings_master`", sql);
        Assert.DoesNotContain("`live_", sql);
    }

    [Fact]
    public void Generate_LiveEnvironment_UsesLivePrefix()
    {
        var config = BaseConfig("LIVE") with
        {
            SettingsMaster = new List<SettingsMasterEntry>
            {
                new() { SettingHead = "Hotel", SettingsType = "S",
                        Port = "80", Version = "6.0", Environment = "LIVE" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.Contains("`live_htl_settings_master`", sql);
        Assert.DoesNotContain("`set_htl_", sql);
    }

    // ── AUTO_INCREMENT columns never included ─────────────────────────────

    [Fact]
    public void Generate_SettingsDetails_DoesNotContainSettingsDetailsId()
    {
        var config = BaseConfig() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Region", MemberValue = "CA",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.DoesNotContain("Settings_Details_ID", sql);
    }

    [Fact]
    public void Generate_SettingsMaster_DoesNotContainSettingsMasterId()
    {
        var config = BaseConfig() with
        {
            SettingsMaster = new List<SettingsMasterEntry>
            {
                new() { SettingHead = "Hotel", SettingsType = "S",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.DoesNotContain("SettingsMasterID", sql);
    }

    [Fact]
    public void Generate_Databases_DoesNotContainDatabaseId()
    {
        var config = BaseConfig() with
        {
            Databases = new List<DatabaseConfigEntry>
            {
                new() { Server = "db.example.com", Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.DoesNotContain("DataBase_ID", sql);
    }

    [Fact]
    public void Generate_ParamsMaster_DoesNotContainParamsMasterId()
    {
        var config = BaseConfig() with
        {
            ParamsMaster = new List<ParamsMasterEntry>
            {
                new() { ParamsHead = "HtlGeneral", SettingHead = "Hotel",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.DoesNotContain("ParamsMasterID", sql);
    }

    [Fact]
    public void Generate_ParamsSettings_DoesNotContainParamsHotelId()
    {
        var config = BaseConfig() with
        {
            ParamsSettings = new List<ParameterEntry>
            {
                new() { ParamsHead = "HtlGeneral", MemberName = "Decimals", MemberValue = "2",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.DoesNotContain("Params_Hotel_ID", sql);
    }

    // ── Dangerous SQL never generated ─────────────────────────────────────

    [Fact]
    public void Generate_NeverContainsDangerousSql()
    {
        var config = BaseConfig() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Key", MemberValue = "Val",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.DoesNotContain("DROP TABLE",     sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM",    sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE",    sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── Transaction wrapping ──────────────────────────────────────────────

    [Fact]
    public void Generate_WithTransaction_WrapsInStartCommit()
    {
        var config = BaseConfig() with { IncludeTransaction = true };
        var sql    = CreateGenerator().Generate(config);
        Assert.Contains("START TRANSACTION;", sql);
        Assert.Contains("COMMIT;", sql);
    }

    [Fact]
    public void Generate_WithoutTransaction_DoesNotContainTransactionStatements()
    {
        var config = BaseConfig() with { IncludeTransaction = false };
        var sql    = CreateGenerator().Generate(config);
        Assert.DoesNotContain("START TRANSACTION", sql);
        Assert.DoesNotContain("COMMIT",            sql);
    }

    // ── SQL escaping ──────────────────────────────────────────────────────

    [Fact]
    public void Generate_ApostropheInValue_EscapedInSql()
    {
        var config = BaseConfig() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Name",
                        MemberValue = "O'Brien Hotel",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.Contains("'O''Brien Hotel'", sql);
        // Must not contain the un-escaped version
        Assert.DoesNotContain("'O'Brien Hotel'", sql.Replace("'O''Brien Hotel'", "REPLACED"));
    }

    [Fact]
    public void Generate_NullValue_EmitsSqlNull()
    {
        var config = BaseConfig() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Region", MemberValue = null,
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        // MemberValue column should be NULL (not 'NULL')
        Assert.Contains("    NULL,", sql);
        Assert.DoesNotContain("'NULL'", sql);
    }

    // ── Header and USE statement ──────────────────────────────────────────

    [Fact]
    public void Generate_ContainsDatabaseNameInUseStatement()
    {
        var sql = CreateGenerator().Generate(BaseConfig());
        Assert.Contains("USE `wccorpcanadaprdmasdb`;", sql);
    }

    [Fact]
    public void Generate_ContainsEnvironmentInHeader()
    {
        var sql = CreateGenerator().Generate(BaseConfig("LIVE"));
        Assert.Contains("-- Environment:  LIVE", sql);
    }

    // ── SQL generation order ──────────────────────────────────────────────

    [Fact]
    public void Generate_OrderIsCorrect_DatabasesBeforeSettings()
    {
        var config = BaseConfig() with
        {
            Databases = new List<DatabaseConfigEntry>
            {
                new() { Server = "db.example.com", Port = "80", Version = "6.0", Environment = "BETA" }
            },
            SettingsMaster = new List<SettingsMasterEntry>
            {
                new() { SettingHead = "Hotel", SettingsType = "S", Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };

        var sql = CreateGenerator().Generate(config);

        var dbPos      = sql.IndexOf("set_htl_databases",       StringComparison.Ordinal);
        var masterPos  = sql.IndexOf("set_htl_settings_master", StringComparison.Ordinal);

        Assert.True(dbPos < masterPos, "Databases section should appear before settings_master.");
    }

    // ── Values are correctly embedded ─────────────────────────────────────

    [Fact]
    public void Generate_PortAndVersionAreInSql()
    {
        var config = BaseConfig() with
        {
            SettingsMaster = new List<SettingsMasterEntry>
            {
                new() { SettingHead = "Hotel", SettingsType = "S",
                        Port = "8080", Version = "6.0", Environment = "BETA" }
            }
        };
        var sql = CreateGenerator().Generate(config);
        Assert.Contains("'8080'", sql);
        Assert.Contains("'6.0'",  sql);
        Assert.Contains("'BETA'", sql);
    }
}
