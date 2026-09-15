namespace HotelConfigAnalyser.Models;

/// <summary>
/// The central normalised representation of an entire hotel configuration file.
/// All downstream steps (validation, SQL generation, future DB diff) operate
/// exclusively on this model — never on raw JSON.
/// </summary>
public sealed record NormalisedConfiguration
{
    // ── User inputs echoed through ────────────────────────────────────────
    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public bool IncludeTransaction { get; init; } = true;

    // ── Normalised data rows ──────────────────────────────────────────────
    public IReadOnlyList<DatabaseConfigEntry> Databases { get; init; } = [];
    public IReadOnlyList<SettingsMasterEntry> SettingsMaster { get; init; } = [];
    public IReadOnlyList<SettingEntry> SettingsDetails { get; init; } = [];
    public IReadOnlyList<ParamsMasterEntry> ParamsMaster { get; init; } = [];
    public IReadOnlyList<ParameterEntry> ParamsSettings { get; init; } = [];

    // ── Issues discovered during parsing & mapping ────────────────────────
    public IReadOnlyList<ValidationIssue> ParseIssues { get; init; } = [];
}
