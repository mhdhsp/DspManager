namespace HotelConfigAnalyser.Models;

/// <summary>
/// Request body for POST /api/configuration/generate-sql.
/// The client sends back the normalised configuration that was returned
/// by the analyse endpoint, plus the user's transaction preference.
/// </summary>
public sealed class GenerateSqlRequest
{
    public NormalisedConfiguration Config { get; init; } = new();
}
