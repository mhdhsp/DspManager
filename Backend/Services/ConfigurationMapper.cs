using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Converts a <see cref="ParseResult"/> and user inputs into a
/// <see cref="NormalisedConfiguration"/>.
///
/// Mapping rules (per specification):
///
/// TABLE 1 — Databases
///   Group entries by DataBaseType. For each group:
///     Rule A — ≥1 active (ActiveStatus=true): insert only active entries, RecordStatus=0
///     Rule B — single inactive entry: insert it, ReadEnable=0, WriteEnable=0, RecordStatus=0
///     Rule C — multiple entries all inactive: insert first only, ReadEnable=0, WriteEnable=0, RecordStatus=1
///   AUI always NULL.
///
/// TABLE 2 — Settings Master (exactly 4 rows, whitelist):
///   GenSettings(S), EmailSettings(S), TemplateSettings(S), Hotel(P)
///
/// TABLE 3 — Settings Details (whitelist, Hotel excluded):
///   GenSettings, EmailSettings, TemplateSettings only.
///
/// TABLE 4 — Params Master (exactly 3 rows, whitelist):
///   Hotel, HtlGeneral, ZealConnect — all with SettingHead='Hotel'
///
/// TABLE 5 — Params Settings (whitelist):
///   Hotel → ChannelCodes entry, ParentHead=NULL
///   HtlGeneral / ZealConnect → all entries, ParentHead='Hotel'
/// </summary>
public sealed class ConfigurationMapper
{
    private readonly ILogger<ConfigurationMapper> _logger;

    // ── Whitelists ────────────────────────────────────────────────────────

    private static readonly IReadOnlySet<string> AllowedSettingsHeads =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GenSettings", "EmailSettings", "TemplateSettings", "Hotel"
        };

    private static readonly IReadOnlySet<string> AllowedSettingsDetailHeads =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GenSettings", "EmailSettings", "TemplateSettings"
        };

    private static readonly IReadOnlySet<string> AllowedParamsHeads =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Hotel", "HtlGeneral", "ZealConnect"
        };

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

        // ── Group database sections by DataBaseType then apply selection rules
        var dbSections = parseResult.Sections
            .Where(s => s.Kind == SectionKind.Database)
            .ToList();

        MapDatabaseSections(dbSections, port, version, env, databases, issues);

        // ── Map settings and params with whitelist filtering
        foreach (var section in parseResult.Sections.Where(s => s.Kind != SectionKind.Database))
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

    // ── TABLE 1: Databases ────────────────────────────────────────────────

    private static void MapDatabaseSections(
        IReadOnlyList<ParsedSection> dbSections,
        string port, string version, string env,
        List<DatabaseConfigEntry> databases,
        List<ValidationIssue> issues)
    {
        // Group by DataBaseType
        var groups = new Dictionary<string, List<ParsedSection>>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in dbSections)
        {
            var lookup  = BuildLookup(section);
            var typeKey = lookup.TryGetValue("DataBaseType", out var t) ? (t ?? "0") : "0";

            if (!groups.TryGetValue(typeKey, out var list))
            {
                list = [];
                groups[typeKey] = list;
            }
            list.Add(section);
        }

        // Apply selection rules per group, ordered by type number
        foreach (var (typeKey, group) in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            SelectAndMapDatabaseGroup(typeKey, group, port, version, env, databases, issues);
    }

    private static void SelectAndMapDatabaseGroup(
        string typeKey,
        List<ParsedSection> group,
        string port, string version, string env,
        List<DatabaseConfigEntry> databases,
        List<ValidationIssue> issues)
    {
        var activeEntries = group.Where(IsActive).ToList();

        if (activeEntries.Count > 0)
        {
            // Rule A: ≥1 active — insert all active ones, RecordStatus=0
            foreach (var s in activeEntries)
                databases.Add(BuildDatabaseEntry(s, port, version, env,
                    overrideRecordStatus: 0, issues: issues));
        }
        else if (group.Count == 1)
        {
            // Rule B: single inactive — insert it, ReadEnable=0, WriteEnable=0, RecordStatus=0
            databases.Add(BuildDatabaseEntry(group[0], port, version, env,
                overrideReadEnable: 0, overrideWriteEnable: 0, overrideRecordStatus: 0,
                issues: issues));
        }
        else
        {
            // Rule C: multiple all-inactive — take first only, RecordStatus=1
            databases.Add(BuildDatabaseEntry(group[0], port, version, env,
                overrideReadEnable: 0, overrideWriteEnable: 0, overrideRecordStatus: 1,
                issues: issues));

            issues.Add(ValidationIssue.Warn("DB_ALL_INACTIVE",
                $"DataBaseType {typeKey}: all {group.Count} entries are inactive. " +
                $"First entry selected (RecordStatus=1).",
                $"Database[Type={typeKey}]"));
        }
    }

    private static bool IsActive(ParsedSection section)
    {
        var lookup = BuildLookup(section);
        if (!lookup.TryGetValue("ActiveStatus", out var v) || v is null) return false;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
    }

    private static DatabaseConfigEntry BuildDatabaseEntry(
        ParsedSection section,
        string port, string version, string env,
        int? overrideReadEnable   = null,
        int? overrideWriteEnable  = null,
        int? overrideRecordStatus = null,
        List<ValidationIssue>? issues = null)
    {
        var lookup = BuildLookup(section);

        int activeStatus = TryParseIntOrBool(lookup, "ActiveStatus", 1);
        int readEnable   = overrideReadEnable  ?? TryParseIntOrBool(lookup, "ReadEnable",  1);
        int writeEnable  = overrideWriteEnable ?? TryParseIntOrBool(lookup, "WriteEnable", 1);
        int recordStatus = overrideRecordStatus ?? (activeStatus == 1 ? 0 : 1);

        DateTime? begin = TryParseDateTime(lookup, "ActivePeriodBegin", issues ?? [], section.Name);
        DateTime? end   = TryParseDateTime(lookup, "ActivePeriodEnd",   issues ?? [], section.Name);

        return new DatabaseConfigEntry
        {
            DataBaseType      = TryParseIntOrBool(lookup, "DataBaseType", 0),
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
            Aui               = null,   // always NULL per specification
            RecordStatus      = recordStatus,
            Port              = port,
            Version           = version,
            Environment       = env,
            ActivePeriodBegin = begin,
            ActivePeriodEnd   = end,
        };
    }

    // ── TABLE 2 & 3: Settings ─────────────────────────────────────────────

    private static void MapSettingsSection(
        ParsedSection section,
        string port, string version, string env,
        HashSet<string> seenHeads,
        List<SettingsMasterEntry> masters,
        List<SettingEntry> details,
        List<ValidationIssue> issues)
    {
        var head = section.Name;

        if (!AllowedSettingsHeads.Contains(head))
        {
            issues.Add(ValidationIssue.Warn("SETTINGS_HEAD_SKIPPED",
                $"Settings section '{head}' is not in the allowed list and will be skipped.",
                head));
            return;
        }

        var settingsType = head.Equals("Hotel", StringComparison.OrdinalIgnoreCase) ? "P" : "S";

        if (seenHeads.Add(head))
        {
            masters.Add(new SettingsMasterEntry
            {
                SettingHead  = head,
                SettingsType = settingsType,
                RecordStatus = 0,
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

        // Hotel is excluded from details
        if (!AllowedSettingsDetailHeads.Contains(head))
            return;

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

    // ── TABLE 4 & 5: Params ───────────────────────────────────────────────

    private static void MapParamsSection(
        ParsedSection section,
        string port, string version, string env,
        HashSet<string> seenHeads,
        List<ParamsMasterEntry> masters,
        List<ParameterEntry> settings,
        List<ValidationIssue> issues)
    {
        // Decode "SettingHead|ParamsHead" encoding from parser
        string paramsHead;
        string? parentHead;

        var pipeIdx = section.Name.IndexOf('|');
        if (pipeIdx >= 0)
        {
            paramsHead = section.Name[(pipeIdx + 1)..];
            parentHead = section.Name[..pipeIdx];
        }
        else
        {
            paramsHead = section.Name;
            parentHead = null;
        }

        if (!AllowedParamsHeads.Contains(paramsHead))
        {
            issues.Add(ValidationIssue.Warn("PARAMS_HEAD_SKIPPED",
                $"Params section '{paramsHead}' is not in the allowed list and will be skipped.",
                paramsHead));
            return;
        }

        if (seenHeads.Add(paramsHead))
        {
            masters.Add(new ParamsMasterEntry
            {
                ParamsHead   = paramsHead,
                SettingHead  = "Hotel",
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

    // ── Shared helpers ────────────────────────────────────────────────────

    private static Dictionary<string, string?> BuildLookup(ParsedSection section) =>
        section.Entries
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

    private static string? GetOrNull(Dictionary<string, string?> lookup, string key) =>
        lookup.TryGetValue(key, out var v) ? v : null;

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
