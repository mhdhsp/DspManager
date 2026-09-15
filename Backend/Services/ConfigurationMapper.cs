using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Converts a <see cref="ParseResult"/> and user inputs into a
/// <see cref="NormalisedConfiguration"/>.
///
/// Responsibilities:
///   - Map each ParsedSection to the correct normalised model.
///   - Inject Port / Version / Environment into every row.
///   - Produce one SettingsMasterEntry per unique SettingsHead.
///   - Produce one ParamsMasterEntry per unique ParamsHead.
///   - Map database sections to DatabaseConfigEntry.
///   - Forward parse issues into the result.
///   - Never throw; accumulate issues instead.
/// </summary>
public sealed class ConfigurationMapper
{
    private readonly ILogger<ConfigurationMapper> _logger;

    public ConfigurationMapper(ILogger<ConfigurationMapper> logger)
    {
        _logger = logger;
    }

    public NormalisedConfiguration Map(
        ParseResult parseResult,
        ConfigurationInput input)
    {
        _logger.LogInformation("Configuration mapping started.");

        var env     = ConfigurationTableMapping.NormaliseEnvironment(input.Environment);
        var port    = input.Port.Trim();
        var version = input.Version.Trim();

        var databases      = new List<DatabaseConfigEntry>();
        var settingsMaster = new List<SettingsMasterEntry>();
        var settingsDetail = new List<SettingEntry>();
        var paramsMaster   = new List<ParamsMasterEntry>();
        var paramsSettings = new List<ParameterEntry>();
        var issues         = new List<ValidationIssue>(parseResult.Issues);

        // Track seen heads to produce exactly one master record per head
        var seenSettingsHeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenParamsHeads   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in parseResult.Sections)
        {
            switch (section.Kind)
            {
                case SectionKind.Settings:
                    MapSettingsSection(section, port, version, env,
                        seenSettingsHeads, settingsMaster, settingsDetail, issues);
                    break;

                case SectionKind.Params:
                    MapParamsSection(section, port, version, env,
                        seenParamsHeads, paramsMaster, paramsSettings, issues);
                    break;

                case SectionKind.Database:
                    MapDatabaseSection(section, port, version, env, databases, issues);
                    break;

                default:
                    // Unknown sections already warned during parsing
                    break;
            }
        }

        _logger.LogInformation(
            "Mapping completed. Databases={Db}, SettingsMaster={SM}, SettingsDetails={SD}, " +
            "ParamsMaster={PM}, ParamsSettings={PS}",
            databases.Count, settingsMaster.Count, settingsDetail.Count,
            paramsMaster.Count, paramsSettings.Count);

        return new NormalisedConfiguration
        {
            Port               = port,
            Version            = version,
            Environment        = env,
            FileName           = input.FileName,
            IncludeTransaction = input.IncludeTransaction,
            Databases          = databases,
            SettingsMaster     = settingsMaster,
            SettingsDetails    = settingsDetail,
            ParamsMaster       = paramsMaster,
            ParamsSettings     = paramsSettings,
            ParseIssues        = issues,
        };
    }

    // ── Settings mapping ──────────────────────────────────────────────────

    private static void MapSettingsSection(
        ParsedSection section,
        string port, string version, string env,
        HashSet<string> seenHeads,
        List<SettingsMasterEntry> masters,
        List<SettingEntry> details,
        List<ValidationIssue> issues)
    {
        var head = section.Name;

        if (seenHeads.Add(head))
        {
            masters.Add(new SettingsMasterEntry
            {
                SettingHead  = head,
                SettingsType = "S",
                RecordStatus = 1,
                Port         = port,
                Version      = version,
                Environment  = env,
            });
        }
        else
        {
            issues.Add(ValidationIssue.Warn("DUPLICATE_SETTINGS_HEAD",
                $"Settings section '{head}' appears more than once. A single master row will be generated.",
                head));
        }

        foreach (var entry in section.Entries)
        {
            details.Add(new SettingEntry
            {
                SettingsHead     = head,
                MemberName       = entry.Key,
                MemberValue      = entry.Value,
                MemberDescription = null,
                MemberDataType   = entry.DataType,
                RecordStatus     = 1,
                Aui              = null,
                Port             = port,
                Version          = version,
                Environment      = env,
            });
        }
    }

    // ── Params mapping ────────────────────────────────────────────────────

    private static void MapParamsSection(
        ParsedSection section,
        string port, string version, string env,
        HashSet<string> seenHeads,
        List<ParamsMasterEntry> masters,
        List<ParameterEntry> settings,
        List<ValidationIssue> issues)
    {
        var head = section.Name;

        // Derive a SettingHead association: strip known param prefixes to find the base
        var settingHead = DeriveSettingHeadFromParamsHead(head);

        if (seenHeads.Add(head))
        {
            masters.Add(new ParamsMasterEntry
            {
                ParamsHead   = head,
                SettingHead  = settingHead,
                RecordStatus = 1,
                Port         = port,
                Version      = version,
                Environment  = env,
            });
        }
        else
        {
            issues.Add(ValidationIssue.Warn("DUPLICATE_PARAMS_HEAD",
                $"Params section '{head}' appears more than once. A single master row will be generated.",
                head));
        }

        foreach (var entry in section.Entries)
        {
            settings.Add(new ParameterEntry
            {
                ParamsHead       = head,
                ParentHead       = null,
                MemberName       = entry.Key,
                MemberValue      = entry.Value,
                MemberDescription = null,
                MemberDataType   = entry.DataType,
                RecordStatus     = 1,
                Aui              = null,
                Port             = port,
                Version          = version,
                Environment      = env,
            });
        }
    }

    // ── Database mapping ──────────────────────────────────────────────────

    private static void MapDatabaseSection(
        ParsedSection section,
        string port, string version, string env,
        List<DatabaseConfigEntry> databases,
        List<ValidationIssue> issues)
    {
        // Build a lookup from entry keys
        var lookup = section.Entries
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        // DataBaseType: int, default 0
        int dbType = 0;
        if (lookup.TryGetValue("DataBaseType", out var dbTypeStr) &&
            int.TryParse(dbTypeStr, out var dbTypeParsed))
            dbType = dbTypeParsed;

        // ActiveStatus / ReadEnable / WriteEnable: int, default 1
        int activeStatus = TryParseInt(lookup, "ActiveStatus", 1);
        int readEnable   = TryParseInt(lookup, "ReadEnable",   1);
        int writeEnable  = TryParseInt(lookup, "WriteEnable",  1);

        // DateTime fields
        DateTime? begin = TryParseDateTime(lookup, "ActivePeriodBegin", issues, section.Name);
        DateTime? end   = TryParseDateTime(lookup, "ActivePeriodEnd",   issues, section.Name);

        databases.Add(new DatabaseConfigEntry
        {
            DataBaseType       = dbType,
            Description        = GetOrNull(lookup, "Description"),
            UserName           = GetOrNull(lookup, "UserName"),
            Password           = GetOrNull(lookup, "Password"),
            DataBaseName       = GetOrNull(lookup, "DataBaseName")
                               ?? GetOrNull(lookup, "Database")
                               ?? GetOrNull(lookup, "InitialCatalog"),
            Server             = GetOrNull(lookup, "Server")
                               ?? GetOrNull(lookup, "DataSource")
                               ?? GetOrNull(lookup, "Host"),
            Provider           = GetOrNull(lookup, "Provider"),
            ActiveStatus       = activeStatus,
            ReadEnable         = readEnable,
            WriteEnable        = writeEnable,
            Aui                = GetOrNull(lookup, "AUI"),
            RecordStatus       = 1,
            Port               = port,
            Version            = version,
            Environment        = env,
            ActivePeriodBegin  = begin,
            ActivePeriodEnd    = end,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string DeriveSettingHeadFromParamsHead(string paramsHead)
    {
        // e.g. HtlGeneral → Hotel, HtlParams → Hotel, Params → General
        if (paramsHead.StartsWith("Htl", StringComparison.OrdinalIgnoreCase))
            return "Hotel";
        if (paramsHead.StartsWith("Param", StringComparison.OrdinalIgnoreCase))
            return "General";
        return paramsHead;
    }

    private static string? GetOrNull(Dictionary<string, string?> lookup, string key) =>
        lookup.TryGetValue(key, out var v) ? v : null;

    private static int TryParseInt(Dictionary<string, string?> lookup, string key, int defaultValue)
    {
        if (lookup.TryGetValue(key, out var s) && int.TryParse(s, out var v))
            return v;
        return defaultValue;
    }

    private static DateTime? TryParseDateTime(
        Dictionary<string, string?> lookup,
        string key,
        List<ValidationIssue> issues,
        string context)
    {
        if (!lookup.TryGetValue(key, out var s) || string.IsNullOrWhiteSpace(s))
            return null;

        if (DateTime.TryParse(s, out var dt))
            return dt;

        issues.Add(ValidationIssue.Warn("INVALID_DATETIME",
            $"Could not parse '{key}' value '{s}' as a datetime in section '{context}'. NULL will be used.",
            $"{context}.{key}"));
        return null;
    }
}
