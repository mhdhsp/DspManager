namespace HotelConfigAnalyser.Models;

/// <summary>
/// Normalised representation of one row destined for *_htl_settings_master.
/// Auto-increment column (SettingsMasterID) is intentionally absent.
/// One master entry is generated per unique SettingsHead.
/// </summary>
public sealed class SettingsMasterEntry
{
    public string SettingHead { get; init; } = string.Empty;

    /// <summary>
    /// "S" by default.  Override with JSON metadata if present.
    /// Maximum 5 characters (VARCHAR(5)).
    /// </summary>
    public string SettingsType { get; init; } = "S";

    public int RecordStatus { get; init; } = 0;

    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}
