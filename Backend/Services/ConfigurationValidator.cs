using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Validates a <see cref="NormalisedConfiguration"/> against:
///   - Required user inputs (Port, Version, Environment)
///   - Environment whitelist (BETA / LIVE)
///   - Column length constraints from <see cref="TableSchema"/>
///   - Duplicate settings/params detection
///   - Null or empty required fields
///
/// Returns a list of <see cref="ValidationIssue"/> in addition to any issues
/// already present on the normalised config (from parsing and mapping).
///
/// Does NOT modify the configuration; consumers merge the returned issues
/// with parseIssues to build the final issue list.
/// </summary>
public sealed class ConfigurationValidator
{
    private readonly ILogger<ConfigurationValidator> _logger;

    public ConfigurationValidator(ILogger<ConfigurationValidator> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ValidationIssue> Validate(NormalisedConfiguration config)
    {
        _logger.LogInformation("Validation started.");

        var issues = new List<ValidationIssue>();

        ValidateUserInputs(config, issues);
        ValidateSettingsMaster(config, issues);
        ValidateSettingsDetails(config, issues);
        ValidateParamsMaster(config, issues);
        ValidateParamsSettings(config, issues);
        ValidateDatabases(config, issues);
        ValidateDuplicates(config, issues);

        _logger.LogInformation("Validation completed. Errors={E}, Warnings={W}",
            issues.Count(i => i.Severity == IssueSeverity.Error),
            issues.Count(i => i.Severity == IssueSeverity.Warning));

        return issues;
    }

    // ── User inputs ───────────────────────────────────────────────────────

    private static void ValidateUserInputs(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(config.Port))
            issues.Add(ValidationIssue.Error("MISSING_PORT", "Port is required."));
        else if (config.Port.Length > TableSchema.SettingsDetails.PortMaxLength)
            issues.Add(ValidationIssue.Error("PORT_TOO_LONG",
                $"Port exceeds the maximum length of {TableSchema.SettingsDetails.PortMaxLength} characters."));

        if (string.IsNullOrWhiteSpace(config.Version))
            issues.Add(ValidationIssue.Error("MISSING_VERSION", "Version is required."));
        else if (config.Version.Length > TableSchema.SettingsDetails.VersionMaxLength)
            issues.Add(ValidationIssue.Error("VERSION_TOO_LONG",
                $"Version exceeds the maximum length of {TableSchema.SettingsDetails.VersionMaxLength} characters."));

        if (string.IsNullOrWhiteSpace(config.Environment))
        {
            issues.Add(ValidationIssue.Error("MISSING_ENVIRONMENT", "Environment is required."));
        }
        else if (!ConfigurationTableMapping.ValidEnvironments.Contains(config.Environment))
        {
            issues.Add(ValidationIssue.Error("INVALID_ENVIRONMENT",
                $"Unsupported environment: '{config.Environment}'. Must be BETA or LIVE."));
        }
        else if (config.Environment.Length > TableSchema.SettingsDetails.EnvironmentMaxLength)
        {
            issues.Add(ValidationIssue.Error("ENVIRONMENT_TOO_LONG",
                $"Environment exceeds the maximum length of {TableSchema.SettingsDetails.EnvironmentMaxLength} characters."));
        }
    }

    // ── Settings master ───────────────────────────────────────────────────

    private static void ValidateSettingsMaster(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        foreach (var entry in config.SettingsMaster)
        {
            var ctx = $"SettingsMaster:{entry.SettingHead}";

            RequireNonEmpty(entry.SettingHead, "SettingHead", "MISSING_SETTING_HEAD", ctx, issues);
            CheckMaxLength(entry.SettingHead, TableSchema.SettingsMaster.SettingHeadMaxLength,
                "SettingHead", "SETTING_HEAD_TOO_LONG", ctx, issues);

            CheckMaxLength(entry.SettingsType, TableSchema.SettingsMaster.SettingsTypeMaxLength,
                "SettingsType", "SETTINGS_TYPE_TOO_LONG", ctx, issues);
        }
    }

    // ── Settings details ──────────────────────────────────────────────────

    private static void ValidateSettingsDetails(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        foreach (var entry in config.SettingsDetails)
        {
            var ctx = $"SettingsDetails:{entry.SettingsHead}.{entry.MemberName}";

            RequireNonEmpty(entry.MemberName, "MemberName", "MISSING_MEMBER_NAME", ctx, issues);
            CheckMaxLength(entry.MemberName, TableSchema.SettingsDetails.MemberNameMaxLength,
                "MemberName", "MEMBER_NAME_TOO_LONG", ctx, issues);

            RequireNonEmpty(entry.SettingsHead, "SettingsHead", "MISSING_SETTINGS_HEAD", ctx, issues);
            CheckMaxLength(entry.SettingsHead, TableSchema.SettingsDetails.SettingsHeadMaxLength,
                "SettingsHead", "SETTINGS_HEAD_TOO_LONG", ctx, issues);

            if (entry.MemberValue is null)
                issues.Add(ValidationIssue.Warn("NULL_MEMBER_VALUE",
                    $"MemberValue is null for '{ctx}'. SQL NULL will be generated.", ctx));
            else
                CheckMaxLength(entry.MemberValue, TableSchema.SettingsDetails.MemberValueMaxLength,
                    "MemberValue", "MEMBER_VALUE_TOO_LONG", ctx, issues);

            if (entry.MemberDescription is not null)
                CheckMaxLength(entry.MemberDescription, TableSchema.SettingsDetails.MemberDescriptionMaxLength,
                    "MemeberDescription", "MEMBER_DESCRIPTION_TOO_LONG", ctx, issues);

            if (entry.MemberDataType is not null)
                CheckMaxLength(entry.MemberDataType, TableSchema.SettingsDetails.MemberDataTypeMaxLength,
                    "MemberDataType", "MEMBER_DATA_TYPE_TOO_LONG", ctx, issues);
        }
    }

    // ── Params master ─────────────────────────────────────────────────────

    private static void ValidateParamsMaster(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        foreach (var entry in config.ParamsMaster)
        {
            var ctx = $"ParamsMaster:{entry.ParamsHead}";

            RequireNonEmpty(entry.ParamsHead, "ParamsHead", "MISSING_PARAMS_HEAD", ctx, issues);
            CheckMaxLength(entry.ParamsHead, TableSchema.ParamsMaster.ParamsHeadMaxLength,
                "ParamsHead", "PARAMS_HEAD_TOO_LONG", ctx, issues);

            RequireNonEmpty(entry.SettingHead, "SettingHead", "MISSING_PARAMS_SETTING_HEAD", ctx, issues);
            CheckMaxLength(entry.SettingHead, TableSchema.ParamsMaster.SettingHeadMaxLength,
                "SettingHead", "PARAMS_SETTING_HEAD_TOO_LONG", ctx, issues);
        }
    }

    // ── Params settings ───────────────────────────────────────────────────

    private static void ValidateParamsSettings(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        foreach (var entry in config.ParamsSettings)
        {
            var ctx = $"ParamsSettings:{entry.ParamsHead}.{entry.MemberName}";

            RequireNonEmpty(entry.MemberName, "MemberName", "MISSING_PARAM_MEMBER_NAME", ctx, issues);
            CheckMaxLength(entry.MemberName, TableSchema.ParamsSettings.MemberNameMaxLength,
                "MemberName", "PARAM_MEMBER_NAME_TOO_LONG", ctx, issues);

            RequireNonEmpty(entry.ParamsHead, "ParamsHead", "MISSING_PARAM_PARAMS_HEAD", ctx, issues);
            CheckMaxLength(entry.ParamsHead, TableSchema.ParamsSettings.ParamsHeadMaxLength,
                "ParamsHead", "PARAM_PARAMS_HEAD_TOO_LONG", ctx, issues);

            if (entry.MemberValue is null)
                issues.Add(ValidationIssue.Warn("NULL_PARAM_VALUE",
                    $"MemberValue is null for parameter '{ctx}'. SQL NULL will be generated.", ctx));
            else
                CheckMaxLength(entry.MemberValue, TableSchema.ParamsSettings.MemberValueMaxLength,
                    "MemberValue", "PARAM_VALUE_TOO_LONG", ctx, issues);

            if (entry.MemberDescription is not null)
                CheckMaxLength(entry.MemberDescription, TableSchema.ParamsSettings.MemberDescriptionMaxLength,
                    "MemeberDescription", "PARAM_DESCRIPTION_TOO_LONG", ctx, issues);

            if (entry.ParentHead is not null)
                CheckMaxLength(entry.ParentHead, TableSchema.ParamsSettings.ParentHeadMaxLength,
                    "ParentHead", "PARENT_HEAD_TOO_LONG", ctx, issues);
        }
    }

    // ── Databases ─────────────────────────────────────────────────────────

    private static void ValidateDatabases(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        int index = 0;
        foreach (var entry in config.Databases)
        {
            var ctx = $"Database[{index}]:{entry.DataBaseName ?? entry.Server ?? "unknown"}";

            if (entry.Description is not null)
                CheckMaxLength(entry.Description, TableSchema.Databases.DescriptionMaxLength,
                    "Description", "DB_DESCRIPTION_TOO_LONG", ctx, issues);

            if (entry.UserName is not null)
                CheckMaxLength(entry.UserName, TableSchema.Databases.UserNameMaxLength,
                    "UserName", "DB_USERNAME_TOO_LONG", ctx, issues);

            if (entry.Password is not null)
                CheckMaxLength(entry.Password, TableSchema.Databases.PasswordMaxLength,
                    "Password", "DB_PASSWORD_TOO_LONG", ctx, issues);

            if (entry.DataBaseName is not null)
                CheckMaxLength(entry.DataBaseName, TableSchema.Databases.DataBaseNameMaxLength,
                    "DataBaseName", "DB_NAME_TOO_LONG", ctx, issues);

            if (entry.Server is not null)
                CheckMaxLength(entry.Server, TableSchema.Databases.ServerMaxLength,
                    "Server", "DB_SERVER_TOO_LONG", ctx, issues);

            if (entry.Provider is not null)
                CheckMaxLength(entry.Provider, TableSchema.Databases.ProviderMaxLength,
                    "Provider", "DB_PROVIDER_TOO_LONG", ctx, issues);

            index++;
        }
    }

    // ── Duplicate detection ───────────────────────────────────────────────

    private static void ValidateDuplicates(NormalisedConfiguration config, List<ValidationIssue> issues)
    {
        // Settings details: SettingsHead + MemberName
        var settingsSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in config.SettingsDetails)
        {
            var key = $"{entry.SettingsHead}|{entry.MemberName}";
            if (settingsSeen.TryGetValue(key, out var count))
            {
                if (count == 1) // Only warn on the second occurrence
                    issues.Add(ValidationIssue.Warn("DUPLICATE_SETTING",
                        $"Duplicate setting detected: SettingsHead='{entry.SettingsHead}', " +
                        $"MemberName='{entry.MemberName}'.",
                        $"{entry.SettingsHead}.{entry.MemberName}"));
                settingsSeen[key] = count + 1;
            }
            else
            {
                settingsSeen[key] = 1;
            }
        }

        // Params settings: ParamsHead + MemberName
        var paramsSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in config.ParamsSettings)
        {
            var key = $"{entry.ParamsHead}|{entry.MemberName}";
            if (paramsSeen.TryGetValue(key, out var count))
            {
                if (count == 1)
                    issues.Add(ValidationIssue.Warn("DUPLICATE_PARAM",
                        $"Duplicate parameter detected: ParamsHead='{entry.ParamsHead}', " +
                        $"MemberName='{entry.MemberName}'.",
                        $"{entry.ParamsHead}.{entry.MemberName}"));
                paramsSeen[key] = count + 1;
            }
            else
            {
                paramsSeen[key] = 1;
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static void RequireNonEmpty(
        string? value, string fieldName, string code, string context, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(ValidationIssue.Error(code,
                $"Required field '{fieldName}' is missing or empty.", context));
    }

    private static void CheckMaxLength(
        string? value, int maxLength, string fieldName, string code, string context, List<ValidationIssue> issues)
    {
        if (value is not null && value.Length > maxLength)
            issues.Add(ValidationIssue.Error(code,
                $"'{fieldName}' exceeds the maximum length of {maxLength} characters " +
                $"(actual: {value.Length}).", context));
    }
}
