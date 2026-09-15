namespace HotelConfigAnalyser.Models;

/// <summary>Per-table row count and validity status shown in the analysis UI.</summary>
public sealed class TableAnalysisSummary
{
    public string TableName { get; init; } = string.Empty;
    public int RecordCount { get; init; }
    public bool HasErrors { get; init; }
    public string Status => HasErrors ? "Has errors" : "Valid";
}
