namespace HotelConfigAnalyser.Schema;

/// <summary>
/// Single source of truth for:
///  - The target database name
///  - The logical → physical table name mapping for each environment
///
/// No other class in the application should hard-code table names or
/// the database name.  Always resolve through this class.
/// </summary>
public static class ConfigurationTableMapping
{
    // ── Database ──────────────────────────────────────────────────────────

    public const string DatabaseName = "wccorpcanadaprdmasdb";

    // ── Logical table keys ────────────────────────────────────────────────

    public const string KeyDatabases = "databases";
    public const string KeySettingsMaster = "settings_master";
    public const string KeySettingsDetails = "settings_details";
    public const string KeyParamsMaster = "params_master";
    public const string KeyParamsSettings = "params_settings";

    // ── Table name maps ───────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> BetaTableNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [KeyDatabases]       = "set_htl_databases",
            [KeySettingsMaster]  = "set_htl_settings_master",
            [KeySettingsDetails] = "set_htl_settings_details",
            [KeyParamsMaster]    = "set_htl_params_master",
            [KeyParamsSettings]  = "set_htl_params_settings",
        };

    private static readonly IReadOnlyDictionary<string, string> LiveTableNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [KeyDatabases]       = "live_htl_databases",
            [KeySettingsMaster]  = "live_htl_settings_master",
            [KeySettingsDetails] = "live_htl_settings_details",
            [KeyParamsMaster]    = "live_htl_params_master",
            [KeyParamsSettings]  = "live_htl_params_settings",
        };

    // ── Valid environments ────────────────────────────────────────────────

    public static readonly IReadOnlySet<string> ValidEnvironments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BETA", "LIVE" };

    // ── Resolver ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the physical table name for the given logical key and environment.
    /// </summary>
    /// <param name="logicalKey">One of the Key* constants defined above.</param>
    /// <param name="environment">BETA or LIVE (case-insensitive).</param>
    public static string GetTableName(string logicalKey, string environment)
    {
        var map = string.Equals(environment, "LIVE", StringComparison.OrdinalIgnoreCase)
            ? LiveTableNames
            : BetaTableNames;

        if (!map.TryGetValue(logicalKey, out var name))
            throw new ArgumentException($"Unknown logical table key: '{logicalKey}'", nameof(logicalKey));

        return name;
    }

    /// <summary>Returns all five physical table names for the given environment, in SQL generation order.</summary>
    public static IEnumerable<(string LogicalKey, string PhysicalName)> GetAllTables(string environment)
    {
        // SQL generation order: databases → settings_master → settings_details → params_master → params_settings
        yield return (KeyDatabases,       GetTableName(KeyDatabases,       environment));
        yield return (KeySettingsMaster,  GetTableName(KeySettingsMaster,  environment));
        yield return (KeySettingsDetails, GetTableName(KeySettingsDetails, environment));
        yield return (KeyParamsMaster,    GetTableName(KeyParamsMaster,    environment));
        yield return (KeyParamsSettings,  GetTableName(KeyParamsSettings,  environment));
    }

    /// <summary>Normalises the environment string to uppercase BETA or LIVE.</summary>
    public static string NormaliseEnvironment(string environment) =>
        environment.Trim().ToUpperInvariant();
}
