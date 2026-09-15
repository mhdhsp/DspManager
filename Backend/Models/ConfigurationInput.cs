namespace HotelConfigAnalyser.Models;

/// <summary>
/// Raw user inputs received from the multipart form upload.
/// Owned by the controller layer; passed to the parser/analyzer pipeline.
/// </summary>
public sealed class ConfigurationInput
{
    /// <summary>Raw JSON text extracted from the uploaded file.</summary>
    public string JsonContent { get; init; } = string.Empty;

    /// <summary>Original filename, used in the SQL header comment and download name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Target port, treated as a VARCHAR in all generated SQL.</summary>
    public string Port { get; init; } = string.Empty;

    /// <summary>Configuration version string (e.g. "6.0").</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Target environment: BETA or LIVE (case-normalised before processing).</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>Whether to wrap the generated SQL in START TRANSACTION / COMMIT.</summary>
    public bool IncludeTransaction { get; init; } = true;
}
