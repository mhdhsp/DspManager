using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HotelConfigAnalyser.Tests;

public sealed class ConfigurationValidatorTests
{
    private static ConfigurationValidator CreateValidator() =>
        new(NullLogger<ConfigurationValidator>.Instance);

    private static NormalisedConfiguration ValidBase(
        string port = "80",
        string version = "6.0",
        string environment = "BETA") =>
        new()
        {
            Port        = port,
            Version     = version,
            Environment = environment,
            FileName    = "test.json",
        };

    // ── Required user inputs ──────────────────────────────────────────────

    [Fact]
    public void Validate_MissingPort_ProducesError()
    {
        var config = ValidBase(port: string.Empty);
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "MISSING_PORT" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Validate_MissingVersion_ProducesError()
    {
        var config = ValidBase(version: string.Empty);
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "MISSING_VERSION" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Validate_MissingEnvironment_ProducesError()
    {
        var config = ValidBase(environment: string.Empty);
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "MISSING_ENVIRONMENT" && i.Severity == IssueSeverity.Error);
    }

    [Theory]
    [InlineData("DEV")]
    [InlineData("STAGING")]
    [InlineData("PROD")]
    [InlineData("PRODUCTION")]  // normalisation is done upstream; validator receives already-normalised value
    public void Validate_InvalidEnvironment_ProducesError(string env)
    {
        var config = ValidBase(environment: env);
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "INVALID_ENVIRONMENT" && i.Severity == IssueSeverity.Error);
    }

    [Theory]
    [InlineData("BETA")]
    [InlineData("LIVE")]
    public void Validate_ValidEnvironments_NoEnvironmentError(string env)
    {
        var config = ValidBase(environment: env);
        var issues = CreateValidator().Validate(config);
        Assert.DoesNotContain(issues, i => i.Code == "INVALID_ENVIRONMENT");
    }

    // ── Length validation ─────────────────────────────────────────────────

    [Fact]
    public void Validate_VersionTooLong_ProducesError()
    {
        var config = ValidBase(version: "6.0.0.0"); // > 5 chars
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "VERSION_TOO_LONG");
    }

    [Fact]
    public void Validate_SettingsMemberNameTooLong_ProducesError()
    {
        var longName = new string('X', 51); // > 50
        var config = ValidBase() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = longName, MemberValue = "val",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "MEMBER_NAME_TOO_LONG" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Validate_SettingsMemberValueTooLong_ProducesError()
    {
        var longValue = new string('V', 1001); // > 1000
        var config = ValidBase() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Key", MemberValue = longValue,
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "MEMBER_VALUE_TOO_LONG");
    }

    [Fact]
    public void Validate_ParamsMemberValueTooLong_ProducesError()
    {
        var longValue = new string('V', 201); // > 200
        var config = ValidBase() with
        {
            ParamsSettings = new List<ParameterEntry>
            {
                new() { ParamsHead = "HtlGeneral", MemberName = "Key", MemberValue = longValue,
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "PARAM_VALUE_TOO_LONG");
    }

    // ── Duplicate detection ───────────────────────────────────────────────

    [Fact]
    public void Validate_DuplicateSettingEntry_ProducesWarning()
    {
        var entry = new SettingEntry
        {
            SettingsHead = "Hotel", MemberName = "Region", MemberValue = "CA",
            Port = "80", Version = "6.0", Environment = "BETA"
        };
        var config = ValidBase() with
        {
            SettingsDetails = new List<SettingEntry> { entry, entry } // exact duplicate
        };
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "DUPLICATE_SETTING" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void Validate_DuplicateParamEntry_ProducesWarning()
    {
        var entry = new ParameterEntry
        {
            ParamsHead = "HtlGeneral", MemberName = "Decimals", MemberValue = "2",
            Port = "80", Version = "6.0", Environment = "BETA"
        };
        var config = ValidBase() with
        {
            ParamsSettings = new List<ParameterEntry> { entry, entry }
        };
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "DUPLICATE_PARAM");
    }

    // ── Null value warnings ───────────────────────────────────────────────

    [Fact]
    public void Validate_NullMemberValue_ProducesWarning()
    {
        var config = ValidBase() with
        {
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Region", MemberValue = null,
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var issues = CreateValidator().Validate(config);
        Assert.Contains(issues, i => i.Code == "NULL_MEMBER_VALUE" && i.Severity == IssueSeverity.Warning);
    }

    // ── Clean configuration ───────────────────────────────────────────────

    [Fact]
    public void Validate_CleanConfiguration_ProducesNoErrors()
    {
        var config = ValidBase() with
        {
            SettingsMaster = new List<SettingsMasterEntry>
            {
                new() { SettingHead = "Hotel", SettingsType = "S", Port = "80", Version = "6.0", Environment = "BETA" }
            },
            SettingsDetails = new List<SettingEntry>
            {
                new() { SettingsHead = "Hotel", MemberName = "Region", MemberValue = "CA",
                        Port = "80", Version = "6.0", Environment = "BETA" }
            }
        };
        var issues = CreateValidator().Validate(config);
        Assert.Empty(issues.Where(i => i.Severity == IssueSeverity.Error));
    }
}
