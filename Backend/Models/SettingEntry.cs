namespace HotelConfigAnalyser.Models;

/// <summary>
/// Normalised representation of one row destined for *_htl_settings_details.
/// Auto-increment column (Settings_Details_ID) is intentionally absent.
/// </summary>
public sealed class SettingEntry
{
    public string SettingsHead { get; init; } = string.Empty;
    public string MemberName { get; init; } = string.Empty;

    /// <summary>
    /// String representation of the value.
    /// NULL is represented by a null reference here; the SQL generator emits SQL NULL.
    /// </summary>
    public string? MemberValue { get; init; }

    public string? MemberDescription { get; init; }
    public string? MemberDataType { get; init; }

    /// <summary>Always 1 for INSERT generation.</summary>
    public int RecordStatus { get; init; } = 1;

    public string? Aui { get; init; }

    // ── runtime context injected by the mapper ────────────────────────────
    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}
