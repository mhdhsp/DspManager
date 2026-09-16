using System.Text.Json;
using HotelConfigAnalyser.Models;
using HotelConfigAnalyser.Schema;
using Microsoft.Extensions.Logging;

namespace HotelConfigAnalyser.Services;

/// <summary>
/// Parses raw JSON text into a structured intermediate representation.
///
/// Supported JSON structures:
///
///   1. DSP grouped-database format:
///      "Databases": { "DataBases": [ { "DataBase": [...], "Type": "1", "Description": "..." } ] }
///
///   2. Settings wrapper pattern:
///      "Settings": { "GenSettings": { ... }, "EmailSettings": { ... } }
///      → Each child object becomes its own section named "GenSettings", "EmailSettings" etc.
///
///   3. Params nested pattern:
///      "Params": { "Hotel": { "ChannelCodes": "...", "HtlGeneral": { ... }, "ZealConnect": { ... } } }
///      → "Hotel" is the top-level Params group, its scalar keys are direct entries,
///        its object children are sub-groups (HtlGeneral, ZealConnect)
///
///   4. Flat sections: "HtlGeneral": { "Decimals": "2", ... }
///
///   5. AppSettings / Configuration wrapper unwrapping
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

        var issues   = new List<ValidationIssue>();
        var sections = new List<ParsedSection>();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            issues.Add(ValidationIssue.Error("EMPTY_JSON",
                "The uploaded file is empty or contains only whitespace."));
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
                $"Invalid JSON format: {ex.Message} (line {ex.LineNumber}, " +
                $"position {ex.BytePositionInLine})."));
            return new ParseResult(sections, issues);
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Error("JSON_NOT_OBJECT",
                $"Root JSON element must be an object but found: {doc.RootElement.ValueKind}."));
            return new ParseResult(sections, issues);
        }

        var workingRoot = UnwrapIfNeeded(doc.RootElement, issues);

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
            var name = property.Name;
            var val  = property.Value;

            // ── Databases (DSP grouped format or flat array / object) ──────
            if (TableSchema.DatabaseSectionKeys.Contains(name))
            {
                ParseDatabaseSection(name, val, sections, issues);
                continue;
            }

            if (val.ValueKind == JsonValueKind.Array)
            {
                // Top-level array that isn't a known DB key — still treat as DBs
                ParseDatabaseSection(name, val, sections, issues);
                continue;
            }

            if (val.ValueKind != JsonValueKind.Object)
            {
                issues.Add(ValidationIssue.Warn("UNMAPPABLE_SECTION",
                    $"Section '{name}' is a scalar and cannot be mapped. It will be skipped.", name));
                continue;
            }

            // ── Settings wrapper: "Settings": { "GenSettings": {...}, ... } ─
            if (name.Equals("Settings", StringComparison.OrdinalIgnoreCase))
            {
                ParseSettingsWrapper(val, sections, issues);
                continue;
            }

            // ── Params wrapper: "Params": { "Hotel": { "HtlGeneral":{}, ... } } ─
            if (name.Equals("Params", StringComparison.OrdinalIgnoreCase))
            {
                ParseParamsWrapper(val, sections, issues);
                continue;
            }

            // ── Flat Params prefixed sections (HtlGeneral, HtlParams, ...) ──
            bool isParamsSection = TableSchema.ParamsSectionPrefixes
                .Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (isParamsSection)
            {
                ParseFlatSection(name, val, SectionKind.Params, sections, issues);
                continue;
            }

            // ── Default: Settings section ─────────────────────────────────
            ParseFlatSection(name, val, SectionKind.Settings, sections, issues);
        }

        if (topLevelCount == 0)
            issues.Add(ValidationIssue.Error("EMPTY_CONFIGURATION",
                "The JSON object contains no properties. Nothing to process."));

        _logger.LogInformation("Parsing complete. Sections={Count}, Issues={Issues}",
            sections.Count, issues.Count);

        return new ParseResult(sections, issues);
    }

    // ── Unwrap envelope ───────────────────────────────────────────────────

    private static JsonElement UnwrapIfNeeded(JsonElement root, List<ValidationIssue> issues)
    {
        foreach (var wrapper in new[] { "AppSettings", "Configuration" })
        {
            if (root.TryGetProperty(wrapper, out var inner) &&
                inner.ValueKind == JsonValueKind.Object)
            {
                issues.Add(ValidationIssue.Warn("JSON_UNWRAPPED",
                    $"Detected '{wrapper}' wrapper — descending into it for processing."));
                return inner;
            }
        }
        return root;
    }

    // ── Database section ──────────────────────────────────────────────────

    /// <summary>
    /// Handles three database formats:
    ///
    /// A) DSP grouped format:
    ///    { "DataBases": [ { "DataBase": [...], "Type": "1", "Description": "Master" } ] }
    ///
    /// B) Simple array:
    ///    [ { "Server": "...", "DataBaseName": "..." } ]
    ///
    /// C) Flat object:
    ///    { "Server": "...", "DataBaseName": "..." }
    /// </summary>
    private static void ParseDatabaseSection(
        string sectionName,
        JsonElement element,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        // Format A: outer object that contains a "DataBases" array
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("DataBases", out var dataBases) &&
            dataBases.ValueKind == JsonValueKind.Array)
        {
            ParseDspGroupedDatabases(dataBases, sections, issues);
            return;
        }

        // Format A variant: the element IS the object with "DataBases" already resolved
        // (when the top-level key is "Databases" and value is the grouped object)
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Check all children — if any is an array called DataBases, recurse
            foreach (var child in element.EnumerateObject())
            {
                if (child.Name.Equals("DataBases", StringComparison.OrdinalIgnoreCase) &&
                    child.Value.ValueKind == JsonValueKind.Array)
                {
                    ParseDspGroupedDatabases(child.Value, sections, issues);
                    return;
                }
            }

            // Format C: simple flat object
            if (HasOnlyScalarValues(element))
            {
                var entries = ExtractLeafEntries(sectionName, element, issues);
                if (entries.Count > 0)
                    sections.Add(new ParsedSection(sectionName, SectionKind.Database, entries));
            }
            else
            {
                // Each child is a separate DB entry
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        var entries = ExtractLeafEntries(prop.Name, prop.Value, issues);
                        if (entries.Count > 0)
                            sections.Add(new ParsedSection(prop.Name, SectionKind.Database, entries));
                    }
                }
            }
            return;
        }

        // Format B: simple array
        if (element.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var entries = ExtractLeafEntries($"{sectionName}[{idx}]", item, issues);
                    if (entries.Count > 0)
                        sections.Add(new ParsedSection(sectionName, SectionKind.Database, entries));
                }
                idx++;
            }
        }
    }

    /// <summary>
    /// Parses the DSP grouped databases format:
    /// [ { "DataBase": [ {...}, {...} ], "Type": "1", "Description": "Master Databases" } ]
    ///
    /// For each group, every entry in "DataBase" gets Type and Description merged in.
    /// Each individual DataBase entry becomes one DatabaseConfigEntry row.
    /// </summary>
    private static void ParseDspGroupedDatabases(
        JsonElement dataBases,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        foreach (var group in dataBases.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object) continue;

            // Extract group-level metadata
            string type        = SafeGetStringValue(group.TryGetProperty("Type",        out var t) ? t : default) ?? "0";
            string description = SafeGetStringValue(group.TryGetProperty("Description", out var d) ? d : default) ?? "";

            // Get the DataBase array within this group
            if (!group.TryGetProperty("DataBase", out var dataBaseArray) ||
                dataBaseArray.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var dbEntry in dataBaseArray.EnumerateArray())
            {
                if (dbEntry.ValueKind != JsonValueKind.Object) continue;

                // Extract per-entry fields
                var entries = new List<ParsedEntry>();

                // Merge Type and Description from the group
                entries.Add(new ParsedEntry("DataBaseType", type, "number"));
                entries.Add(new ParsedEntry("Description",  description, "string"));

                // Extract all scalar fields from the DataBase object
                foreach (var prop in dbEntry.EnumerateObject())
                {
                    var v = prop.Value;
                    string? strVal;
                    string  dt = InferDataType(v);

                    if (v.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        // Nested inside a DB entry — serialise
                        strVal = v.GetRawText();
                        issues.Add(ValidationIssue.Warn("DB_NESTED_VALUE",
                            $"Database entry field '{prop.Name}' is a complex type and has been serialised.",
                            prop.Name));
                    }
                    else
                    {
                        strVal = SafeGetStringValue(v);
                    }

                    // Map JSON field names → expected column names
                    var mappedKey = prop.Name switch
                    {
                        "DatabaseName" => "DataBaseName",
                        _              => prop.Name,
                    };

                    entries.Add(new ParsedEntry(mappedKey, strVal, dt));
                }

                sections.Add(new ParsedSection("Databases", SectionKind.Database, entries));
            }
        }
    }

    // ── Settings wrapper ──────────────────────────────────────────────────

    /// <summary>
    /// Handles: "Settings": { "GenSettings": { ... }, "EmailSettings": { ... } }
    /// Each child object becomes its own section with the child name as SettingsHead.
    /// Scalar children directly under Settings become a "Settings" catch-all section.
    /// </summary>
    private static void ParseSettingsWrapper(
        JsonElement element,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        var directScalars = new List<ParsedEntry>();

        foreach (var child in element.EnumerateObject())
        {
            if (child.Value.ValueKind == JsonValueKind.Object)
            {
                // Each sub-object → its own settings section with its own name as head
                var childEntries = ExtractLeafEntries(child.Name, child.Value, issues);
                if (childEntries.Count > 0)
                    sections.Add(new ParsedSection(child.Name, SectionKind.Settings, childEntries));
            }
            else if (child.Value.ValueKind != JsonValueKind.Array)
            {
                // Scalar directly under Settings
                directScalars.Add(new ParsedEntry(
                    child.Name,
                    SafeGetStringValue(child.Value),
                    InferDataType(child.Value)));
            }
        }

        if (directScalars.Count > 0)
            sections.Add(new ParsedSection("Settings", SectionKind.Settings, directScalars));
    }

    // ── Params wrapper ────────────────────────────────────────────────────

    /// <summary>
    /// Handles the DSP Params structure:
    ///
    /// "Params": {
    ///   "Hotel": {
    ///     "ChannelCodes": "DY,GT,...",       ← scalar → goes into ParamsHead=Hotel
    ///     "HtlGeneral":  { "Decimals": "2" }, ← sub-object → ParamsHead=HtlGeneral, SettingHead=Hotel
    ///     "ZealConnect": { "Enabled": "true" } ← sub-object → ParamsHead=ZealConnect, SettingHead=Hotel
    ///   },
    ///   "Airline": {
    ///     "General": { ... },                 ← sub-object → ParamsHead=Airline.General
    ///     "Galileo": { ... }
    ///   },
    ///   "Utility": {
    ///     "General": { "IsGulfWebConnect": "false" }
    ///   },
    ///   "Insurance": { ... }                  ← flat params section
    /// }
    ///
    /// Rules derived from sample inserts:
    ///   - Each top-level child of Params ("Hotel", "Airline", etc.) is a logical group.
    ///   - If that child has sub-objects, those sub-objects become ParamsHead entries
    ///     with SettingHead = parent name.
    ///   - Scalar values directly under a child go into the parent as ParamsHead entries.
    /// </summary>
    private static void ParseParamsWrapper(
        JsonElement element,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        foreach (var group in element.EnumerateObject())
        {
            var groupName = group.Name;
            var groupVal  = group.Value;

            if (groupVal.ValueKind != JsonValueKind.Object) continue;

            // Collect scalar entries directly on the group (e.g. Hotel.ChannelCodes)
            var directEntries = new List<ParsedEntry>();
            var childObjects  = new List<(string Name, JsonElement Value)>();

            foreach (var member in groupVal.EnumerateObject())
            {
                if (member.Value.ValueKind == JsonValueKind.Object)
                {
                    childObjects.Add((member.Name, member.Value));
                }
                else if (member.Value.ValueKind != JsonValueKind.Array)
                {
                    directEntries.Add(new ParsedEntry(
                        member.Name,
                        SafeGetStringValue(member.Value),
                        InferDataType(member.Value)));
                }
                else
                {
                    // Array value — serialise
                    directEntries.Add(new ParsedEntry(
                        member.Name,
                        member.Value.GetRawText(),
                        "array"));
                }
            }

            // Direct scalars under the group → ParamsHead = groupName, SettingHead = groupName
            if (directEntries.Count > 0)
                sections.Add(new ParsedSection(groupName, SectionKind.Params, directEntries));

            // Child sub-objects → each becomes ParamsHead = childName, SettingHead = groupName
            // We encode the SettingHead into the section name with a '|' separator
            // so the mapper can decode it without extra fields on ParsedSection.
            foreach (var (childName, childVal) in childObjects)
            {
                var childEntries = ExtractLeafEntries(childName, childVal, issues);
                if (childEntries.Count > 0)
                {
                    // Encode parent context into name: "Hotel|HtlGeneral"
                    var encodedName = $"{groupName}|{childName}";
                    sections.Add(new ParsedSection(encodedName, SectionKind.Params, childEntries));
                }
            }
        }
    }

    // ── Flat section (Settings or Params) ─────────────────────────────────

    private static void ParseFlatSection(
        string sectionName,
        JsonElement element,
        SectionKind kind,
        List<ParsedSection> sections,
        List<ValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(ValidationIssue.Warn("SECTION_NOT_OBJECT",
                $"Section '{sectionName}' expected an object but found {element.ValueKind}. Skipped.",
                sectionName));
            return;
        }

        if (!element.EnumerateObject().Any())
        {
            issues.Add(ValidationIssue.Warn("EMPTY_SECTION",
                $"Section '{sectionName}' is empty.", sectionName));
            return;
        }

        var entries = ExtractLeafEntries(sectionName, element, issues);
        if (entries.Count > 0)
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
            var key = prop.Name;
            var val = prop.Value;

            if (!seen.Add(key))
            {
                issues.Add(ValidationIssue.Warn("DUPLICATE_KEY",
                    $"Duplicate key '{key}' in section '{sectionName}'. Only first occurrence kept.",
                    $"{sectionName}.{key}"));
                continue;
            }

            string? strVal;
            var dt = InferDataType(val);

            switch (val.ValueKind)
            {
                case JsonValueKind.Object:
                    strVal = val.GetRawText();
                    issues.Add(ValidationIssue.Warn("NESTED_OBJECT_SERIALISED",
                        $"'{sectionName}.{key}' is a nested object and has been serialised as a JSON string.",
                        $"{sectionName}.{key}"));
                    break;

                case JsonValueKind.Array:
                    strVal = val.GetRawText();
                    issues.Add(ValidationIssue.Warn("ARRAY_VALUE_SERIALISED",
                        $"'{sectionName}.{key}' is an array and has been serialised.",
                        $"{sectionName}.{key}"));
                    break;

                case JsonValueKind.Null:
                    strVal = null;
                    break;

                default:
                    strVal = SafeGetStringValue(val);
                    break;
            }

            entries.Add(new ParsedEntry(key, strVal, dt));
        }

        return entries;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool HasOnlyScalarValues(JsonElement obj)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
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
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True   => "bool",
            JsonValueKind.False  => "bool",
            JsonValueKind.Null   => "null",
            JsonValueKind.Array  => "array",
            JsonValueKind.Object => "object",
            _                    => "string",
        };
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

public enum SectionKind { Settings, Params, Database, Unknown }

public sealed record ParsedEntry(string Key, string? Value, string DataType);

public sealed record ParsedSection(
    string Name,
    SectionKind Kind,
    IReadOnlyList<ParsedEntry> Entries);

public sealed record ParseResult(
    IReadOnlyList<ParsedSection> Sections,
    IReadOnlyList<ValidationIssue> Issues);
