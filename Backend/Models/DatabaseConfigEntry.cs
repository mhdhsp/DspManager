namespace HotelConfigAnalyser.Models;

/// <summary>
/// Normalised representation of one row destined for *_htl_databases.
/// Auto-increment column (DataBase_ID) is intentionally absent.
/// Sensitive fields (UserName, Password) must never be logged.
/// </summary>
public sealed class DatabaseConfigEntry
{
    public int DataBaseType { get; init; }
    public string? Description { get; init; }

    // ── SENSITIVE — never log these fields ───────────────────────────────
    public string? UserName { get; init; }
    public string? Password { get; init; }
    // ─────────────────────────────────────────────────────────────────────

    public string? DataBaseName { get; init; }
    public string? Server { get; init; }
    public string? Provider { get; init; }

    public int ActiveStatus { get; init; } = 1;
    public int ReadEnable { get; init; } = 1;
    public int WriteEnable { get; init; } = 1;

    public string? Aui { get; init; }
    public int RecordStatus { get; init; } = 1;

    public string Port { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;

    public DateTime? ActivePeriodBegin { get; init; }
    public DateTime? ActivePeriodEnd { get; init; }
}
