using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Orchestrates the full analyse pipeline:
///   Parse → Map → Validate → Build AnalysisResult
///
/// This service owns the pipeline sequencing only.
/// It delegates each step to the dedicated service.
/// </summary>
public sealed class ConfigurationAnalyzer
{
    private readonly JsonConfigurationParser  _parser;
    private readonly ConfigurationMapper      _mapper;
    private readonly ConfigurationValidator   _validator;
    private readonly ILogger<ConfigurationAnalyzer> _logger;

    public ConfigurationAnalyzer(
        JsonConfigurationParser  parser,
        ConfigurationMapper      mapper,
        ConfigurationValidator   validator,
        ILogger<ConfigurationAnalyzer> logger)
    {
        _parser    = parser;
        _mapper    = mapper;
        _validator = validator;
        _logger    = logger;
    }

    public AnalysisResult Analyse(ConfigurationInput input)
    {
        _logger.LogInformation("Analysis started. File='{File}', Port={Port}, Version={Version}, Environment={Env}",
            input.FileName, input.Port, input.Version, input.Environment);

        // ── 1. Parse ──────────────────────────────────────────────────────
        var parseResult = _parser.Parse(input.JsonContent);

        // If JSON itself is broken, return immediately with the parse errors
        bool hasFatalParseError = parseResult.Issues.Any(i =>
            i.Severity == IssueSeverity.Error &&
            i.Code is "INVALID_JSON" or "EMPTY_JSON" or "JSON_NOT_OBJECT" or "EMPTY_CONFIGURATION");

        if (hasFatalParseError)
        {
            _logger.LogWarning("Analysis aborted: fatal parse errors found.");
            return BuildResult(input, new NormalisedConfiguration
            {
                Port        = input.Port.Trim(),
                Version     = input.Version.Trim(),
                Environment = ConfigurationTableMapping.NormaliseEnvironment(input.Environment),
                FileName    = input.FileName,
                ParseIssues = parseResult.Issues,
            }, parseResult.Issues);
        }

        // ── 2. Map ────────────────────────────────────────────────────────
        var normalisedConfig = _mapper.Map(parseResult, input);

        // ── 3. Validate ───────────────────────────────────────────────────
        var validationIssues = _validator.Validate(normalisedConfig);

        // Merge all issues: parse/map issues already on the config + new validation issues
        var allIssues = normalisedConfig.ParseIssues
            .Concat(validationIssues)
            .ToList();

        _logger.LogInformation("Analysis completed. Errors={E}, Warnings={W}",
            allIssues.Count(i => i.Severity == IssueSeverity.Error),
            allIssues.Count(i => i.Severity == IssueSeverity.Warning));

        return BuildResult(input, normalisedConfig, allIssues);
    }

    // ── Builder ───────────────────────────────────────────────────────────

    private static AnalysisResult BuildResult(
        ConfigurationInput input,
        NormalisedConfiguration config,
        IReadOnlyList<ValidationIssue> allIssues)
    {
        var env = ConfigurationTableMapping.NormaliseEnvironment(
            string.IsNullOrWhiteSpace(input.Environment) ? "BETA" : input.Environment);

        bool envIsValid = ConfigurationTableMapping.ValidEnvironments.Contains(env);

        var tableSummaries = new List<TableAnalysisSummary>();

        if (envIsValid)
        {
            bool dbErrors       = HasTableErrors(allIssues, "Database");
            bool smErrors       = HasTableErrors(allIssues, "SettingsMaster");
            bool sdErrors       = HasTableErrors(allIssues, "SettingsDetails");
            bool pmErrors       = HasTableErrors(allIssues, "ParamsMaster");
            bool psErrors       = HasTableErrors(allIssues, "ParamsSettings");

            tableSummaries.Add(new TableAnalysisSummary
            {
                TableName   = ConfigurationTableMapping.GetTableName(ConfigurationTableMapping.KeyDatabases,       env),
                RecordCount = config.Databases.Count,
                HasErrors   = dbErrors,
            });
            tableSummaries.Add(new TableAnalysisSummary
            {
                TableName   = ConfigurationTableMapping.GetTableName(ConfigurationTableMapping.KeySettingsMaster,  env),
                RecordCount = config.SettingsMaster.Count,
                HasErrors   = smErrors,
            });
            tableSummaries.Add(new TableAnalysisSummary
            {
                TableName   = ConfigurationTableMapping.GetTableName(ConfigurationTableMapping.KeySettingsDetails, env),
                RecordCount = config.SettingsDetails.Count,
                HasErrors   = sdErrors,
            });
            tableSummaries.Add(new TableAnalysisSummary
            {
                TableName   = ConfigurationTableMapping.GetTableName(ConfigurationTableMapping.KeyParamsMaster,    env),
                RecordCount = config.ParamsMaster.Count,
                HasErrors   = pmErrors,
            });
            tableSummaries.Add(new TableAnalysisSummary
            {
                TableName   = ConfigurationTableMapping.GetTableName(ConfigurationTableMapping.KeyParamsSettings,  env),
                RecordCount = config.ParamsSettings.Count,
                HasErrors   = psErrors,
            });
        }

        return new AnalysisResult
        {
            FileName              = input.FileName,
            Port                  = input.Port.Trim(),
            Version               = input.Version.Trim(),
            Environment           = env,
            IncludeTransaction    = input.IncludeTransaction,
            DatabasesCount        = config.Databases.Count,
            SettingsMasterCount   = config.SettingsMaster.Count,
            SettingsDetailsCount  = config.SettingsDetails.Count,
            ParamsMasterCount     = config.ParamsMaster.Count,
            ParamsSettingsCount   = config.ParamsSettings.Count,
            Issues                = allIssues,
            TableSummaries        = tableSummaries,
            SettingsHeadsFound    = config.SettingsMaster.Select(s => s.SettingHead).ToList(),
            ParamsHeadsFound      = config.ParamsMaster.Select(p => p.ParamsHead).ToList(),
            NormalisedConfig      = config,
        };
    }

    private static bool HasTableErrors(IReadOnlyList<ValidationIssue> issues, string tablePrefix) =>
        issues.Any(i =>
            i.Severity == IssueSeverity.Error &&
            (i.Context?.Contains(tablePrefix, StringComparison.OrdinalIgnoreCase) == true ||
             i.Code?.Contains(tablePrefix.Replace("Details","").Replace("Master",""),
                 StringComparison.OrdinalIgnoreCase) == true));
}
