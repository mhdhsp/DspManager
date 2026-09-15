using System.Text.Json;
using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Parses raw JSON text into a structured intermediate representation.
/// Produces a dictionary of section-name → section-data plus a list of
/// parse-time issues (warnings and errors).
///
/// Responsibilities:
///   - Validate that the text is well-formed JSON.
///   - Detect the top-level structure (flat object, AppSettings wrapper, etc.).
///   - Classify each top-level key as: Database | Params | Settings | Unknown.
///   - Extract key/value leaf pairs from each section.
///   - Detect duplicate keys within a section.
///   - Handle null, empty, unexpected types gracefully.
///   - Never throw; always return issues instead.
/// </summary>
public sealed class JsonConfigurationParser
{
    private readonly ILogger<JsonConfigurationParser> _logger;

    public JsonConfigurationParser(ILogger<JsonConfigurationParser> logger)
    {
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public ParseResult Parse(string jsonContent)
    {
        _logger.LogInformation("JSON parsing started. Content length: {Length}", jsonContent?.Length ?? 0);

        var issues = new List<ValidationIssue>();
        var sections = new List<ParsedSection>();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            issues.Add(ValidationIssue.Error("EMPTY_JSON", "The uploaded file is empty or contains only whitespace."));
            _logger.LogWarning("JSON parsing failed: empty content.");
            return new ParseResult(sections, issues);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling     = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            issues.Add(ValidationIssue.Error("INVALID_JSON",
                $"Invalid JSON format: {ex.Message} (line {ex.LineNumber}, position {ex.BytePositionInLine})."));
            _logger.LogWarning("JSON parsing failed: {Message}", ex.Message);
            return new ParseResult(sections, issues);
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Error("JSON_NOT_OBJECT",
                $"The root JSON element must be an object but found: {doc.RootElement.ValueKind}."));
            return new ParseResult(sections, issues);
        }

        var root = doc.RootElement;

        // Unwrap common wrapper keys so the rest of the pipeline sees flat sections
        var workingRoot = UnwrapIfNeeded(root, issues);

        if (workingRoot.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Error("JSON_UNWRAP_FAILED",
                "Could not resolve a usable configuration object from the JSON structure."));
            return new ParseResult(sections, issues);
        }

        int topLevelCount = 0;
        foreach (var property in workingRoot.EnumerateObject())
        {
            topLevelCount++;
            var sectionName = property.Name;
            var sectionKind = ClassifySection(sectionName, property.Value);

            switch (sectionKind)
            {
                case SectionKind.Database:
                    ParseDatabaseSection(sectionName, property.Value, sections, issues);
                    break;

                case SectionKind.Params:
                    ParseObjectSection(sectionName, property.Value, SectionKind.Params, sections, issues);
                    break;

                case SectionKind.Settings:
                    ParseObjectSection(sectionName, property.Value, SectionKind.Settings, sections, issues);
                    break;

                case SectionKind.Unknown:
                    issues.Add(ValidationIssue.Warn("UNMAPPABLE_SECTION",
                        $"Section '{sectionName}' could not be classified and will be skipped.",
                        sectionName));
                    break;
            }
        }

        if (topLevelCount == 0)
        {
            issues.Add(ValidationIssue.Error("EMPTY_CONFIGURATION",
                "The JSON object contains no properties. Nothing to process."));
        }

        _logger.LogInformation("JSON parsing completed. Sections found: {Count}, Issues: {Issues}",
            sections.Count, issues.Count);

        return new ParseResult(sections, issues);
    }

    // ── Section unwrapping ────────────────────────────────────────────────

    /// <summary>
    /// If the JSON is wrapped in a known envelope (AppSettings, Configuration),
    /// descend into it so the rest of the pipeline sees flat hotel config sections.
    /// </summary>
    private static JsonElement UnwrapIfNeeded(JsonElement root, List<ValidationIssue> issues)
    {
        // Pattern: { "AppSettings": { ... } }
        if (root.TryGetProperty("AppSettings", out var appSettings) &&
            appSettings.ValueKind == JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Warn("JSON_UNWRAPPED",
                "Detected 'AppSettings' wrapper — descending into AppSettings for processing."));
            return appSettings;
        }

        // Pattern: { "Configuration": { ... } }
        if (root.TryGetProperty("Configuration", out var config) &&
            config.ValueKind == JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Warn("JSON_UNWRAPPED",
                "Detected 'Configuration' wrapper — descending into Configuration for processing."));
            return config;
        }

        return root;
    }

    // ── Section classification ────────────────────────────────────────────

    private static SectionKind ClassifySection(string name, JsonElement value)
    {
        // Explicit database keys
        if (TableSchema.DatabaseSectionKeys.Contains(name))
            return SectionKind.Database;

        // Scalars at top level are not supported as sections
        if (value.ValueKind != JsonValueKind.Object && value.ValueKind != JsonValueKind.Array)
            return SectionKind.Unknown;

        // Params: key starts with a known params prefix
        foreach (var prefix in TableSchema.ParamsSectionPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return SectionKind.Params;
        }

        // Arrays of objects without database key — treat as database entries
        if (value.ValueKind == JsonValueKind.Array)
            return SectionKind.Database;

        // Default: settings
        return SectionKind.Settings;
    }

    // ── Database section parsing ──────────────────────────────────────────

    private static void ParseDatabaseSection(
        string sectionName,
        JsonElement element,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var entries = ExtractLeafEntries(sectionName, item, issues);
                    sections.Add(new ParsedSection(sectionName, SectionKind.Database, entries));
                }
                else
                {
                    issues.Add(ValidationIssue.Warn("DB_ARRAY_ITEM_NOT_OBJECT",
                        $"Item {index} in '{sectionName}' is not an object and will be skipped.",
                        sectionName));
                }
                index++;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            // ConnectionStrings-style: each property value might be a connection string or a nested object
            if (HasOnlyStringValues(element))
            {
                // Treat the whole object as one database entry
                var entries = ExtractLeafEntries(sectionName, element, issues);
                sections.Add(new ParsedSection(sectionName, SectionKind.Database, entries));
            }
            else
            {
                // Each child property is a separate database entry
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        var entries = ExtractLeafEntries(prop.Name, prop.Value, issues);
                        sections.Add(new ParsedSection(prop.Name, SectionKind.Database, entries));
                    }
                    else
                    {
                        // Single connection string value
                        var entries = new List<ParsedEntry>
                        {
                            new(prop.Name, SafeGetStringValue(prop.Value), InferDataType(prop.Value))
                        };
                        sections.Add(new ParsedSection(prop.Name, SectionKind.Database, entries));
                    }
                }
            }
        }
        else
        {
            issues.Add(ValidationIssue.Warn("DB_SECTION_UNEXPECTED_TYPE",
                $"Section '{sectionName}' has an unexpected type ({element.ValueKind}) and will be skipped.",
                sectionName));
        }
    }

    // ── Object section parsing (Settings / Params) ────────────────────────

    private static void ParseObjectSection(
        string sectionName,
        JsonElement element,
        SectionKind kind,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Warn("SECTION_NOT_OBJECT",
                $"Section '{sectionName}' is expected to be an object but found {element.ValueKind}. It will be skipped.",
                sectionName));
            return;
        }

        if (!element.EnumerateObject().Any())
        {
            issues.Add(ValidationIssue.Warn("EMPTY_SECTION",
                $"Section '{sectionName}' is empty and will produce no rows.",
                sectionName));
            return;
        }

        var entries = ExtractLeafEntries(sectionName, element, issues);
        sections.Add(new ParsedSection(sectionName, kind, entries));
    }

    // ── Leaf extraction ───────────────────────────────────────────────────

    private static List<ParsedEntry> ExtractLeafEntries(
        string sectionName,
        JsonElement obj,
        List<ValidationIssue> issues)
    {
        var entries = new List<ParsedEntry>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in obj.EnumerateObject())
        {
            var key   = prop.Name;
            var value = prop.Value;

            // Duplicate detection
            if (!seen.Add(key))
            {
                issues.Add(ValidationIssue.Warn("DUPLICATE_KEY",
                    $"Duplicate key '{key}' found in section '{sectionName}'. Only the first occurrence will be used.",
                    $"{sectionName}.{key}"));
                continue;
            }

            string? stringValue;
            var dataType = InferDataType(value);

            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    // Nested object: serialise as JSON string, warn
                    stringValue = value.GetRawText();
                    issues.Add(ValidationIssue.Warn("NESTED_OBJECT_SERIALISED",
                        $"'{sectionName}.{key}' is a nested object. Its value has been serialised as a JSON string.",
                        $"{sectionName}.{key}"));
                    break;

                case JsonValueKind.Array:
                    stringValue = value.GetRawText();
                    issues.Add(ValidationIssue.Warn("ARRAY_VALUE_SERIALISED",
                        $"'{sectionName}.{key}' is an array. Its value has been serialised as a JSON string.",
                        $"{sectionName}.{key}"));
                    break;

                case JsonValueKind.Null:
                    stringValue = null;
                    issues.Add(ValidationIssue.Warn("NULL_VALUE",
                        $"'{sectionName}.{key}' is null. SQL NULL will be generated.",
                        $"{sectionName}.{key}"));
                    break;

                default:
                    stringValue = SafeGetStringValue(value);
                    break;
            }

            entries.Add(new ParsedEntry(key, stringValue, dataType));
        }

        return entries;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool HasOnlyStringValues(JsonElement obj)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number
                or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                return false;
        }
        return true;
    }

    private static string? SafeGetStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Null   => null,
            _                    => element.GetRawText(),
        };
    }

    private static string InferDataType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String  => "string",
            JsonValueKind.Number  => "number",
            JsonValueKind.True    => "bool",
            JsonValueKind.False   => "bool",
            JsonValueKind.Null    => "null",
            JsonValueKind.Array   => "array",
            JsonValueKind.Object  => "object",
            _                     => "string",
        };
    }
}

// ── Supporting types (internal to the parser pipeline) ────────────────────────

public enum SectionKind { Settings, Params, Database, Unknown }

public sealed record ParsedEntry(string Key, string? Value, string DataType);

public sealed record ParsedSection(
    string Name,
    SectionKind Kind,
    IReadOnlyList<ParsedEntry> Entries);

public sealed record ParseResult(
    IReadOnlyList<ParsedSection> Sections,
    IReadOnlyList<ValidationIssue> Issues);
