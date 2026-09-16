using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Converts a <see cref="ParseResult"/> and user inputs into a
/// <see cref="NormalisedConfiguration"/>.
///
/// Key mapping rules derived from actual DSP insert patterns:
///
///   RecordStatus = 0  (active record — matches sample inserts)
///
///   Settings sections:
///     SettingsType = 'S' for normal settings (GenSettings, EmailSettings, etc.)
///     SettingsType = 'P' for Hotel/params-linked settings (when section name = "Hotel")
///
///   Params sections (encoded as "SettingHead|ParamsHead" from parser):
///     e.g. "Hotel|HtlGeneral"  → ParamsHead = HtlGeneral, SettingHead = Hotel
///          "Hotel|ZealConnect" → ParamsHead = ZealConnect, SettingHead = Hotel
///          "Hotel"             → ParamsHead = Hotel,       SettingHead = Hotel
///
///   Database sections:
///     DataBaseType comes from the "DataBaseType" entry in the parsed section.
///     ActiveStatus / ReadEnable / WriteEnable from JSON booleans or integers.
/// </summary>
public sealed class ConfigurationMapper
{
    private readonly ILogger<ConfigurationMapper> _logger;

    public ConfigurationMapper(ILogger<ConfigurationMapper> logger)
    {
        _logger = logger;
    }

    public NormalisedConfiguration Map(ParseResult parseResult, ConfigurationInput input)
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
            }
        }

        _logger.LogInformation(
            "Mapping complete. DB={Db} SM={SM} SD={SD} PM={PM} PS={PS}",
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

        // "Hotel" under Settings is type 'P' (params-linked); all others are 'S'
        var settingsType = head.Equals("Hotel", StringComparison.OrdinalIgnoreCase) ? "P" : "S";

        if (seenHeads.Add(head))
        {
            masters.Add(new SettingsMasterEntry
            {
                SettingHead  = head,
                SettingsType = settingsType,
                RecordStatus = 0,   // 0 = active in DSP schema
                Port         = port,
                Version      = version,
                Environment  = env,
            });
        }
        else
        {
            issues.Add(ValidationIssue.Warn("DUPLICATE_SETTINGS_HEAD",
                $"Settings section '{head}' appears more than once. Single master row generated.",
                head));
        }

        foreach (var entry in section.Entries)
        {
            details.Add(new SettingEntry
            {
                SettingsHead      = head,
                MemberName        = entry.Key,
                MemberValue       = entry.Value,
                MemberDescription = null,
                MemberDataType    = entry.DataType,
                RecordStatus      = 0,
                Aui               = null,
                Port              = port,
                Version           = version,
                Environment       = env,
            });
        }
    }

    // ── Params mapping ────────────────────────────────────────────────────

    /// <summary>
    /// Handles two section name formats:
    ///
    ///   1. "Hotel"             → plain params section, ParamsHead=Hotel, SettingHead=Hotel
    ///   2. "Hotel|HtlGeneral"  → child section, ParamsHead=HtlGeneral, SettingHead=Hotel, ParentHead=Hotel
    ///   3. "Airline|Galileo"   → child section, ParamsHead=Galileo, SettingHead=Airline (or "General")
    ///
    /// The '|' encoding is set by the parser in ParseParamsWrapper.
    /// </summary>
    private static void MapParamsSection(
        ParsedSection section,
        string port, string version, string env,
        HashSet<string> seenHeads,
        List<ParamsMasterEntry> masters,
        List<ParameterEntry> settings,
        List<ValidationIssue> issues)
    {
        string paramsHead;
        string settingHead;
        string? parentHead;

        var pipeIdx = section.Name.IndexOf('|');
        if (pipeIdx >= 0)
        {
            // Encoded child: "SettingHead|ParamsHead"
            settingHead = section.Name[..pipeIdx];
            paramsHead  = section.Name[(pipeIdx + 1)..];
            parentHead  = settingHead;
        }
        else
        {
            // Top-level group — ParamsHead and SettingHead are the same
            paramsHead  = section.Name;
            settingHead = DeriveSettingHeadFromParamsHead(section.Name);
            parentHead  = null;
        }

        if (seenHeads.Add(paramsHead))
        {
            masters.Add(new ParamsMasterEntry
            {
                ParamsHead   = paramsHead,
                SettingHead  = settingHead,
                RecordStatus = 0,
                Port         = port,
                Version      = version,
                Environment  = env,
            });
        }
        else
        {
            issues.Add(ValidationIssue.Warn("DUPLICATE_PARAMS_HEAD",
                $"Params section '{paramsHead}' appears more than once. Single master row generated.",
                paramsHead));
        }

        foreach (var entry in section.Entries)
        {
            settings.Add(new ParameterEntry
            {
                ParamsHead        = paramsHead,
                ParentHead        = parentHead,
                MemberName        = entry.Key,
                MemberValue       = entry.Value,
                MemberDescription = null,
                MemberDataType    = entry.DataType,
                RecordStatus      = 0,
                Aui               = null,
                Port              = port,
                Version           = version,
                Environment       = env,
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
        var lookup = section.Entries
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

        // DataBaseType: int
        int dbType = TryParseIntOrBool(lookup, "DataBaseType", 0);

        // ActiveStatus / ReadEnable / WriteEnable: support both bool strings and ints
        int activeStatus = TryParseIntOrBool(lookup, "ActiveStatus", 1);
        int readEnable   = TryParseIntOrBool(lookup, "ReadEnable",   1);
        int writeEnable  = TryParseIntOrBool(lookup, "WriteEnable",  1);

        DateTime? begin = TryParseDateTime(lookup, "ActivePeriodBegin", issues, section.Name);
        DateTime? end   = TryParseDateTime(lookup, "ActivePeriodEnd",   issues, section.Name);

        databases.Add(new DatabaseConfigEntry
        {
            DataBaseType      = dbType,
            Description       = GetOrNull(lookup, "Description"),
            UserName          = GetOrNull(lookup, "UserName"),
            Password          = GetOrNull(lookup, "Password"),
            DataBaseName      = GetOrNull(lookup, "DataBaseName")
                                ?? GetOrNull(lookup, "DatabaseName")
                                ?? GetOrNull(lookup, "Database"),
            Server            = GetOrNull(lookup, "Server")
                                ?? GetOrNull(lookup, "DataSource")
                                ?? GetOrNull(lookup, "Host"),
            Provider          = GetOrNull(lookup, "Provider"),
            ActiveStatus      = activeStatus,
            ReadEnable        = readEnable,
            WriteEnable       = writeEnable,
            Aui               = GetOrNull(lookup, "AUI"),
            RecordStatus      = 0,   // 0 = active in DSP schema
            Port              = port,
            Version           = version,
            Environment       = env,
            ActivePeriodBegin = begin,
            ActivePeriodEnd   = end,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// For a plain params section (no parent), derive the SettingHead:
    ///   Hotel → Hotel (top-level hotel params group)
    ///   HtlGeneral / HtlParams → Hotel
    ///   Airline / Insurance / etc. → same as ParamsHead
    /// </summary>
    private static string DeriveSettingHeadFromParamsHead(string paramsHead)
    {
        if (paramsHead.Equals("Hotel", StringComparison.OrdinalIgnoreCase))
            return "Hotel";
        if (paramsHead.StartsWith("Htl", StringComparison.OrdinalIgnoreCase))
            return "Hotel";
        return paramsHead;
    }

    private static string? GetOrNull(Dictionary<string, string?> lookup, string key) =>
        lookup.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Parses a value that can be an integer ("1"), a boolean string ("true"/"false"),
    /// or already absent.  "true" → 1, "false" → 0.
    /// </summary>
    private static int TryParseIntOrBool(Dictionary<string, string?> lookup, string key, int defaultValue)
    {
        if (!lookup.TryGetValue(key, out var s) || s is null) return defaultValue;
        if (int.TryParse(s, out var iv)) return iv;
        if (s.Equals("true",  StringComparison.OrdinalIgnoreCase)) return 1;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0;
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
            $"Could not parse '{key}' = '{s}' as a datetime in '{context}'. NULL will be used.",
            $"{context}.{key}"));
        return null;
    }
}
