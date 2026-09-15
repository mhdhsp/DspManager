namespace HotelConfigAnalyser.Models;

/// <summary>
/// Normalised representation of one row destined for *_htl_params_settings.
/// Auto-increment column (Params_Hotel_ID) is intentionally absent.
/// </summary>
public sealed class ParameterEntry
{
    public string ParamsHead { get; init; } = string.Empty;

    /// <summary>
    /// Parent section name when params are nested inside a settings section.
    /// Null when the params section is a direct top-level key.
    /// </summary>
    public string? ParentHead { get; init; }

    public string MemberName { get; init; } = string.Empty;

    /// <summary>NULL is represented by null; SQL generator emits SQL NULL.</summary>
    public string? MemberValue { get; init; }

    public string? MemberDescription { get; init; }
    public string? MemberDataType { get; init; }

    public int RecordStatus { get; init; } = 1;

    public string? Aui { get; init; }

    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}
