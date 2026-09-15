namespace HotelConfigAnalyser.Models;

/// <summary>
/// Complete analysis result returned by POST /api/configuration/analyze.
/// Contains all information required by the UI to display the summary,
/// validation issues, and to later request SQL generation.
/// </summary>
public sealed class AnalysisResult
{
    // ── Input echo ────────────────────────────────────────────────────────
    public string FileName { get; init; } = string.Empty;
    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public bool IncludeTransaction { get; init; } = true;

    // ── Row counts ────────────────────────────────────────────────────────
    public int DatabasesCount { get; init; }
    public int SettingsMasterCount { get; init; }
    public int SettingsDetailsCount { get; init; }
    public int ParamsMasterCount { get; init; }
    public int ParamsSettingsCount { get; init; }

    // ── Validation ────────────────────────────────────────────────────────
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];
    public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);

    /// <summary>
    /// True when there are no blocking errors.
    /// The UI enables "Generate SQL" only when this is true.
    /// </summary>
    public bool CanGenerateSql => ErrorCount == 0;

    // ── Per-table summaries ───────────────────────────────────────────────
    public IReadOnlyList<TableAnalysisSummary> TableSummaries { get; init; } = [];

    // ── Preview data (non-sensitive snippet of settings heads found) ──────
    public IReadOnlyList<string> SettingsHeadsFound { get; init; } = [];
    public IReadOnlyList<string> ParamsHeadsFound { get; init; } = [];

    // ── Internal: the normalised model needed by the SQL generator ─────────
    // Serialised to JSON and sent back to the frontend as an opaque token,
    // then echoed to /generate-sql.  No sensitive fields are exposed here
    // beyond what is already in the config; the download warning covers this.
    public NormalisedConfiguration? NormalisedConfig { get; init; }
}
