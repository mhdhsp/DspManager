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
///   For each DataBaseType group (Types 1–20):
///     Rule A — If multiple entries and at least one has ActiveStatus=true:
///              → Insert ONLY the active entry(s). RecordStatus=0.
///     Rule B — Single entry with ActiveStatus=false:
///              → Insert it. ReadEnable=0, WriteEnable=0, RecordStatus=0.
///     Rule C — Multiple entries all with ActiveStatus=false:
///              → Insert ONLY the first. ReadEnable=0, WriteEnable=0, RecordStatus=1.
///   RecordStatus = 0 when ActiveStatus=1(true), = 1 when ActiveStatus=0(false).
///
/// TABLE 2 — Settings Master (exactly 4 rows, whitelist only):
///   GenSettings  → SettingsType='S'
///   EmailSettings → SettingsType='S'
///   TemplateSettings → SettingsType='S'
///   Hotel         → SettingsType='P'
///
/// TABLE 3 — Settings Details (whitelist only, Hotel excluded):
///   GenSettings, EmailSettings, TemplateSettings only.
///
/// TABLE 4 — Params Master (exactly 3 rows, whitelist only):
///   Hotel       → SettingHead='Hotel'
///   HtlGeneral  → SettingHead='Hotel'
///   ZealConnect → SettingHead='Hotel'
///
/// TABLE 5 — Params Settings (whitelist only):
///   ParamsHead=Hotel       → ChannelCodes entry, ParentHead=NULL
///   ParamsHead=HtlGeneral  → all entries from Params.Hotel.HtlGeneral, ParentHead='Hotel'
///   ParamsHead=ZealConnect → all entries from Params.Hotel.ZealConnect, ParentHead='Hotel'
/// </summary>
public sealed class ConfigurationMapper
{
    private readonly ILogger<ConfigurationMapper> _logger;

    // ── Whitelists ────────────────────────────────────────────────────────

    // Exactly these 4 settings heads are allowed into settings_master
    private static readonly IReadOnlySet<string> AllowedSettingsHeads =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GenSettings", "EmailSettings", "TemplateSettings", "Hotel"
        };

    // Settings details are inserted for these heads only (Hotel excluded)
    private static readonly IReadOnlySet<string> AllowedSettingsDetailHeads =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GenSettings", "EmailSettings", "TemplateSettings"
        };

    // Exactly these 3 params heads are allowed into params_master / params_settings
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

        // ── Group database sections by DataBaseType before mapping ────────
        // The parser creates one ParsedSection per DataBase entry.
        // We need to group them by type so we can apply the selection rules.
        var dbSections = parseResult.Sections
            .Where(s => s.Kind == SectionKind.Database)
            .ToList();

        MapDatabaseSections(dbSections, port, version, env, databases, issues);

        // ── Map settings and params sections ──────────────────────────────
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

    // ── Table 1: Database mapping (with selection rules) ──────────────────

    /// <summary>
    /// Groups all database ParsedSections by DataBaseType and applies
    /// the three-rule selection logic per group.
    /// </summary>
    private static void MapDatabaseSections(
        IReadOnlyList<ParsedSection> dbSections,
        string port, string version, string env,
        List<DatabaseConfigEntry> databases,
        List<ValidationIssue> issues)
    {
        // Group sections by DataBaseType value
        var groups = new Dictionary<string, List<ParsedSection>>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in dbSections)
        {
            var typeLookup = BuildLookup(section);
            var typeKey = typeLookup.TryGetValue("DataBaseType", out var t) ? (t ?? "0") : "0";

            if (!groups.TryGetValue(typeKey, out var list))
            {
                list = [];
                groups[typeKey] = list;
            }
            list.Add(section);
        }

        // Apply selection rules per group
        foreach (var (typeKey, group) in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            SelectAndMapDatabaseGroup(typeKey, group, port, version, env, databases, issues);
        }
    }

    /// <summary>
    /// Applies the 3 database selection rules for one DataBaseType group:
    ///
    ///   Rule A — ≥1 active entry exists → insert only active entries, RecordStatus=0
    ///   Rule B — single inactive entry → insert it, ReadEnable=0, WriteEnable=0, RecordStatus=0
    ///   Rule C — multiple entries, all inactive → insert first only, ReadEnable=0, WriteEnable=0, RecordStatus=1
    /// </summary>
    private static void SelectAndMapDatabaseGroup(
        string typeKey,
        List<ParsedSection> group,
        string port, string version, string env,
        List<DatabaseConfigEntry> databases,
        List<ValidationIssue> issues)
    {
        // Categorise entries by ActiveStatus
        var activeEntries   = group.Where(s => IsActive(s)).ToList();
        var inactiveEntries = group.Where(s => !IsActive(s)).ToList();

        if (activeEntries.Count > 0)
        {
            // Rule A: one or more active entries — insert all active ones
            foreach (var section in activeEntries)
                databases.Add(BuildDatabaseEntry(section, port, version, env,
                    overrideRecordStatus: 0, issues: issues));
        }
        else if (group.Count == 1)
        {
            // Rule B: single entry that is inactive
            databases.Add(BuildDatabaseEntry(group[0], port, version, env,
                overrideReadEnable: 0, overrideWriteEnable: 0, overrideRecordStatus: 0,
                issues: issues));
        }
        else
        {
            // Rule C: multiple entries, all inactive — take only the first
            databases.Add(BuildDatabaseEntry(group[0], port, version, env,
                overrideReadEnable: 0, overrideWriteEnable: 0, overrideRecordStatus: 1,
                issues: issues));

            if (group.Count > 1)
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
        if (v.Equals("true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("1")) return true;
        return false;
    }

    private static DatabaseConfigEntry BuildDatabaseEntry(
        ParsedSection section,
        string port, string version, string env,
        int? overrideReadEnable    = null,
        int? overrideWriteEnable   = null,
        int? overrideRecordStatus  = null,
        List<ValidationIssue>? issues = null)
    {
        var lookup = BuildLookup(section);

        int activeStatus = TryParseIntOrBool(lookup, "ActiveStatus", 1);
        int readEnable   = overrideReadEnable  ?? TryParseIntOrBool(lookup, "ReadEnable",  1);
        int writeEnable  = overrideWriteEnable ?? TryParseIntOrBool(lookup, "WriteEnable", 1);

        // RecordStatus rule: 0 when active, 1 when inactive — unless overridden by caller
        int recordStatus = overrideRecordStatus ?? (activeStatus == 1 ? 0 : 1);

        DateTime? begin = TryParseDateTime(lookup, "ActivePeriodBegin",
            issues ?? [], section.Name);
        DateTime? end   = TryParseDateTime(lookup, "ActivePeriodEnd",
            issues ?? [], section.Name);

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

    // ── Table 2 & 3: Settings mapping (whitelist) ─────────────────────────

    private static void MapSettingsSection(
        ParsedSection section,
        string port, string version, string env,
        HashSet<string> seenHeads,
        List<SettingsMasterEntry> masters,
        List<SettingEntry> details,
        List<ValidationIssue> issues)
    {
        var head = section.Name;

        // Only process sections that are in the whitelist
        if (!AllowedSettingsHeads.Contains(head))
        {
            issues.Add(ValidationIssue.Warn("SETTINGS_HEAD_SKIPPED",
                $"Settings section '{head}' is not in the allowed list " +
                $"(GenSettings, EmailSettings, TemplateSettings, Hotel) and will be skipped.",
                head));
            return;
        }

        // Table 2: Settings Master — one row per allowed head
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

        // Table 3: Settings Details — Hotel is excluded
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

    // ── Table 4 & 5: Params mapping (whitelist) ───────────────────────────

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
        string settingHead;
        string? parentHead;

        var pipeIdx = section.Name.IndexOf('|');
        if (pipeIdx >= 0)
        {
            settingHead = section.Name[..pipeIdx];
            paramsHead  = section.Name[(pipeIdx + 1)..];
            parentHead  = settingHead;
        }
        else
        {
            paramsHead  = section.Name;
            settingHead = paramsHead.Equals("Hotel", StringComparison.OrdinalIgnoreCase)
                ? "Hotel"
                : paramsHead;
            parentHead  = null;
        }

        // Only process params heads that are in the whitelist
        if (!AllowedParamsHeads.Contains(paramsHead))
        {
            issues.Add(ValidationIssue.Warn("PARAMS_HEAD_SKIPPED",
                $"Params section '{paramsHead}' is not in the allowed list " +
                $"(Hotel, HtlGeneral, ZealConnect) and will be skipped.",
                paramsHead));
            return;
        }

        // Table 4: Params Master
        if (seenHeads.Add(paramsHead))
        {
            masters.Add(new ParamsMasterEntry
            {
                ParamsHead   = paramsHead,
                SettingHead  = "Hotel",   // all 3 allowed heads belong to Hotel
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

        // Table 5: Params Settings
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
