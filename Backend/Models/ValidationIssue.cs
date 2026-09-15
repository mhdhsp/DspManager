using System.Text.Json.Serialization;

namespace HotelConfigAnalyser.Models;

/// <summary>Severity of a validation finding.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueSeverity
{
    Warning,
    Error
}

/// <summary>
/// A single validation finding produced during parsing, mapping, or schema validation.
/// Errors block SQL generation; warnings do not.
/// </summary>
public sealed class ValidationIssue
{
    public IssueSeverity Severity { get; init; }

    /// <summary>Short machine-readable code (e.g. "MISSING_PORT", "DUPLICATE_SETTING").</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Human-readable description shown in the analysis UI.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Section / key path that caused the issue (e.g. "Hotel.Region").</summary>
    public string? Context { get; init; }

    // ── factory helpers ──────────────────────────────────────────────────────

    public static ValidationIssue Error(string code, string message, string? context = null) =>
        new() { Severity = IssueSeverity.Error, Code = code, Message = message, Context = context };

    public static ValidationIssue Warn(string code, string message, string? context = null) =>
        new() { Severity = IssueSeverity.Warning, Code = code, Message = message, Context = context };
}
