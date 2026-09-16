namespace HotelConfigAnalyser.Schema;

/// <summary>
/// Centralised column length constraints mirroring the physical MySQL schema.
/// Used by the validator so length limits are not duplicated in business logic.
/// </summary>
public static class TableSchema
{
    // ── *_htl_settings_details ────────────────────────────────────────────

    public static class SettingsDetails
    {
        public const int MemberNameMaxLength        = 50;
        public const int MemberValueMaxLength       = 1000;
        public const int MemberDescriptionMaxLength = 200;
        public const int MemberDataTypeMaxLength    = 10;
        public const int SettingsHeadMaxLength      = 50;
        public const int AuiMaxLength               = 45;
        public const int PortMaxLength              = 45;
        public const int VersionMaxLength           = 5;
        public const int EnvironmentMaxLength       = 20;
    }

    // ── *_htl_settings_master ─────────────────────────────────────────────

    public static class SettingsMaster
    {
        public const int SettingHeadMaxLength   = 50;
        public const int SettingsTypeMaxLength  = 5;
        public const int PortMaxLength          = 45;
        public const int VersionMaxLength       = 5;
        public const int EnvironmentMaxLength   = 20;
    }

    // ── *_htl_params_master ───────────────────────────────────────────────

    public static class ParamsMaster
    {
        public const int ParamsHeadMaxLength    = 50;
        public const int SettingHeadMaxLength   = 50;
        public const int PortMaxLength          = 45;
        public const int VersionMaxLength       = 5;
        public const int EnvironmentMaxLength   = 20;
    }

    // ── *_htl_params_settings ─────────────────────────────────────────────

    public static class ParamsSettings
    {
        public const int MemberNameMaxLength        = 50;
        public const int MemberValueMaxLength       = 200;
        public const int MemberDescriptionMaxLength = 200;
        public const int MemberDataTypeMaxLength    = 10;
        public const int ParamsHeadMaxLength        = 45;
        public const int AuiMaxLength               = 45;
        public const int PortMaxLength              = 45;
        public const int VersionMaxLength           = 5;
        public const int EnvironmentMaxLength       = 20;
        public const int ParentHeadMaxLength        = 50;
    }

    // ── *_htl_databases ───────────────────────────────────────────────────

    public static class Databases
    {
        public const int DescriptionMaxLength  = 45;
        public const int UserNameMaxLength     = 45;
        public const int PasswordMaxLength     = 45;
        public const int DataBaseNameMaxLength = 45;
        public const int ServerMaxLength       = 250;
        public const int ProviderMaxLength     = 45;
        public const int AuiMaxLength          = 45;
        public const int PortMaxLength         = 45;
        public const int VersionMaxLength      = 5;
        public const int EnvironmentMaxLength  = 20;
    }

    // ── Shared auto-increment column names (must never appear in INSERTs) ─

    public static readonly IReadOnlySet<string> AutoIncrementColumns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DataBase_ID",
            "Settings_Details_ID",
            "SettingsMasterID",
            "ParamsMasterID",
            "Params_Hotel_ID",
        };

    // ── JSON section classification helpers ───────────────────────────────

    /// <summary>
    /// Top-level JSON keys that indicate database connection entries.
    /// </summary>
    public static readonly IReadOnlySet<string> DatabaseSectionKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Databases",
            "ConnectionStrings",
            "DbConnections",
            "DatabaseConnections",
        };

    /// <summary>
    /// Top-level JSON section name prefixes that indicate flat parameter sections
    /// (e.g. HtlGeneral, HtlParams when they appear at the top level rather than
    /// nested inside a "Params" wrapper).
    /// Note: "Params" itself is handled as a wrapper by the parser and is NOT in this list.
    /// </summary>
    public static readonly IReadOnlyList<string> ParamsSectionPrefixes =
    [
        "HtlGeneral",
        "HtlParams",
        "Htl",
    ];
}
