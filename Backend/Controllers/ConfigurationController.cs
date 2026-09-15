using System.Text;
using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Services;
using Microsoft.AspNetCore.Mvc;

namespace HotelConfigAnalyser.Controllers;

/// <summary>
/// Exposes the configuration analysis and SQL generation pipeline via HTTP.
///
/// POST /api/configuration/analyze
///   Accepts a multipart/form-data upload (file + port + version + environment).
///   Returns an AnalysisResult containing counts, validation issues, and the
///   normalised configuration model needed by generate-sql.
///
/// POST /api/configuration/generate-sql
///   Accepts a GenerateSqlRequest (the normalised config echoed from analyse).
///   Returns the generated SQL as a downloadable .sql file.
///
/// Phase 1 constraints enforced here:
///   - No database connection.
///   - No SQL execution.
///   - File content is read into memory only; never written to disk.
///   - Passwords / secrets are never logged.
/// </summary>
[ApiController]
[Route("api/configuration")]
public sealed class ConfigurationController : ControllerBase
{
    private readonly ConfigurationAnalyzer _analyzer;
    private readonly SqlGenerator          _sqlGenerator;
    private readonly ILogger<ConfigurationController> _logger;

    // 10 MB upload limit — reasonable for JSON config files
    private const long MaxFileSize = 10 * 1024 * 1024;

    public ConfigurationController(
        ConfigurationAnalyzer analyzer,
        SqlGenerator sqlGenerator,
        ILogger<ConfigurationController> logger)
    {
        _analyzer     = analyzer;
        _sqlGenerator = sqlGenerator;
        _logger       = logger;
    }

    // ── POST /api/configuration/analyze ───────────────────────────────────

    /// <summary>
    /// Parses, normalises and validates an uploaded JSON configuration file.
    /// Returns an analysis summary with validation issues and table record counts.
    /// </summary>
    [HttpPost("analyze")]
    [RequestSizeLimit(MaxFileSize)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AnalysisResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze([FromForm] AnalyzeFormInput form)
    {
        // ── Validate form inputs before reading the file ──────────────────
        if (form.File is null || form.File.Length == 0)
            return BadRequest(Problem("A JSON configuration file is required.", title: "Missing file"));

        if (!form.File.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(Problem("Only .json files are accepted.", title: "Invalid file type"));

        if (form.File.Length > MaxFileSize)
            return BadRequest(Problem(
                $"File exceeds the maximum allowed size of {MaxFileSize / 1024 / 1024} MB.",
                title: "File too large"));

        // ── Read file content — never written to disk ─────────────────────
        string jsonContent;
        try
        {
            using var reader = new StreamReader(form.File.OpenReadStream(), Encoding.UTF8);
            jsonContent = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read uploaded file '{FileName}'.", form.File.FileName);
            return BadRequest(Problem("Could not read the uploaded file.", title: "File read error"));
        }

        var input = new ConfigurationInput
        {
            JsonContent        = jsonContent,
            FileName           = form.File.FileName,
            Port               = (form.Port ?? string.Empty).Trim(),
            Version            = (form.Version ?? string.Empty).Trim(),
            Environment        = (form.Environment ?? string.Empty).Trim().ToUpperInvariant(),
            IncludeTransaction = form.IncludeTransaction,
        };

        _logger.LogInformation(
            "Analyze request received. File='{File}', Port={Port}, Version={Version}, Environment={Env}",
            input.FileName, input.Port, input.Version, input.Environment);

        var result = _analyzer.Analyse(input);

        return Ok(result);
    }

    // ── POST /api/configuration/generate-sql ─────────────────────────────

    /// <summary>
    /// Generates a MySQL INSERT script from the previously analysed configuration.
    /// Returns the SQL as a downloadable .sql file.
    /// </summary>
    [HttpPost("generate-sql")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult GenerateSql([FromBody] GenerateSqlRequest request)
    {
        if (request?.Config is null)
            return BadRequest(Problem("Request body is required.", title: "Missing request body"));

        var config = request.Config;

        // Re-validate that we won't generate SQL for an invalid environment
        if (string.IsNullOrWhiteSpace(config.Environment) ||
            !HotelConfigAnalyser.Schema.ConfigurationTableMapping.ValidEnvironments.Contains(config.Environment))
        {
            return BadRequest(Problem(
                $"Cannot generate SQL: environment '{config.Environment}' is not valid.",
                title: "Invalid environment"));
        }

        if (string.IsNullOrWhiteSpace(config.Port))
            return BadRequest(Problem("Cannot generate SQL: Port is required.", title: "Missing Port"));

        if (string.IsNullOrWhiteSpace(config.Version))
            return BadRequest(Problem("Cannot generate SQL: Version is required.", title: "Missing Version"));

        _logger.LogInformation(
            "SQL generation request received. Environment={Env}, Port={Port}, Version={Version}",
            config.Environment, config.Port, config.Version);

        string sql = _sqlGenerator.Generate(config);

        // Build a safe download filename: hotel-config-BETA-80-v6.0.sql
        var safeEnv     = SanitiseFilenameSegment(config.Environment);
        var safePort    = SanitiseFilenameSegment(config.Port);
        var safeVersion = SanitiseFilenameSegment(config.Version);
        var filename    = $"hotel-config-{safeEnv}-{safePort}-v{safeVersion}.sql";

        _logger.LogInformation("SQL generation completed. DownloadFilename='{Filename}'", filename);

        var bytes = Encoding.UTF8.GetBytes(sql);
        return File(bytes, "application/sql", filename);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string SanitiseFilenameSegment(string value)
    {
        // Remove any character that is not alphanumeric, hyphen, underscore, or dot
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}

// ── Form binding model ────────────────────────────────────────────────────────

/// <summary>
/// Represents the multipart/form-data fields for the analyze endpoint.
/// Using a separate model keeps the controller action signature clean.
/// </summary>
public sealed class AnalyzeFormInput
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
    public IFormFile? File { get; set; }

    public string? Port { get; set; }
    public string? Version { get; set; }
    public string? Environment { get; set; }
    public bool IncludeTransaction { get; set; } = true;
}
