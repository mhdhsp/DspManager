namespace HotelConfigAnalyser.Models;

/// <summary>
/// Normalised representation of one row destined for *_htl_params_master.
/// Auto-increment column (ParamsMasterID) is intentionally absent.
/// One master entry is generated per unique ParamsHead.
/// </summary>
public sealed class ParamsMasterEntry
{
    public string ParamsHead { get; init; } = string.Empty;

    /// <summary>
    /// Associated settings group.  Defaults to the nearest settings section or
    /// "General" when no association can be determined.
    /// </summary>
    public string SettingHead { get; init; } = "General";

    public int RecordStatus { get; init; } = 1;

    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
}
