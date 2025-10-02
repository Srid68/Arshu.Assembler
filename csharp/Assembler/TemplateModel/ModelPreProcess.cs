using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Arshu.App.Json;

namespace Assembler.TemplateModel;

// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;
using JsonArray = Arshu.App.Json.JsonArray;

/// <summary>
/// Contains preprocessed templates for a site with efficient lookup structures
/// </summary>
public class PreprocessedSiteTemplates
{
    public string SiteName { get; set; } = string.Empty;
    public Dictionary<string, PreprocessedTemplate> Templates { get; set; } = new();
    public Dictionary<string, string> RawTemplates { get; set; } = new();
    public HashSet<string> TemplateKeys { get; set; } = new();

    // ...existing code...
}

/// <summary>
/// Summary object for template statistics
/// </summary>
public class PreprocessedSummary
{
    public string SiteName { get; set; } = string.Empty;
    public int TemplatesRequiringProcessing { get; set; }
    public int TemplatesWithJsonData { get; set; }
    public int TemplatesWithPlaceholders { get; set; }
    public int TotalTemplates { get; set; }
}

/// <summary>
/// Represents a preprocessed template with parsed structure for efficient merging
/// </summary>
public class PreprocessedTemplate
{
    /// <summary>
    /// The original unprocessed template content
    /// </summary>
    public string OriginalContent { get; set; } = string.Empty;

    /// <summary>
    /// List of placeholders found in the template
    /// </summary>
    public List<TemplatePlaceholder> Placeholders { get; set; } = new();

    /// <summary>
    /// List of slotted templates found in the template
    /// </summary>
    public List<SlottedTemplate> SlottedTemplates { get; set; } = new();

    /// <summary>
    /// Raw JSON data parsed from the JSON file. This is only the parsed data structure,
    /// actual JSON merging happens in the engine.
    /// </summary>
    public JsonObject? JsonData { get; set; }

    /// <summary>
    /// Preprocessed JSON placeholders for direct replacement ({{$key}} patterns)
    /// </summary>
    public List<JsonPlaceholder> JsonPlaceholders { get; set; } = new();

    /// <summary>
    /// Complete replacement mappings for direct string replacement (no substring processing needed)
    /// </summary>
    public List<ReplacementMapping> ReplacementMappings { get; set; } = new();
    
    // Helper properties to check template state (included in JSON serialization)
    [JsonPropertyName("hasPlaceholders")]
    public bool HasPlaceholders => Placeholders.Any();
    
    [JsonPropertyName("hasSlottedTemplates")]
    public bool HasSlottedTemplates => SlottedTemplates.Any();
    
    [JsonPropertyName("hasJsonData")]
    public bool HasJsonData => JsonData != null && JsonData.Any();
    
    [JsonPropertyName("hasJsonPlaceholders")]
    public bool HasJsonPlaceholders => JsonPlaceholders.Any();
    
    [JsonPropertyName("hasReplacementMappings")]
    public bool HasReplacementMappings => ReplacementMappings.Any();
    
    [JsonPropertyName("requiresProcessing")]
    public bool RequiresProcessing => HasPlaceholders || HasSlottedTemplates || HasJsonData || HasJsonPlaceholders || HasReplacementMappings;
}

/// <summary>
/// Represents a JSON placeholder like {{$key}} with precomputed replacement value
/// </summary>
public class JsonPlaceholder
{
    public string Key { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Represents a pre-computed replacement for ultra-fast template merging
/// </summary>
public class ReplacementMapping
{
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string ReplacementText { get; set; } = string.Empty;
    public ReplacementType Type { get; set; }
}

/// <summary>
/// Types of template replacements
/// </summary>
public enum ReplacementType
{
    JsonPlaceholder,
    SimpleTemplate,
    SlottedTemplate
}

/// <summary>
/// Represents a simple placeholder like {{ComponentName}}
/// </summary>
public class TemplatePlaceholder
{
    public string Name { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string FullMatch { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public JsonObject? JsonData { get; set; }
    public List<TemplatePlaceholder> NestedPlaceholders { get; set; } = new();
    public List<SlotPlaceholder> NestedSlots { get; set; } = new();
}

/// <summary>
/// Represents a slotted template like {{#TemplateName}} ... {{/TemplateName}}
/// </summary>
public class SlottedTemplate
{
    public string Name { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string FullMatch { get; set; } = string.Empty;
    public string InnerContent { get; set; } = string.Empty;
    public List<SlotPlaceholder> Slots { get; set; } = new();
    public string TemplateKey { get; set; } = string.Empty;
    public JsonObject? JsonData { get; set; }
}

/// <summary>
/// Represents a slot within a slotted template like {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}}
/// </summary>
public class SlotPlaceholder
{
    public List<SlotPlaceholder> NestedSlots { get; set; } = new();
    public string Number { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string SlotKey { get; set; } = string.Empty;
    public string OpenTag { get; set; } = string.Empty;
    public string CloseTag { get; set; } = string.Empty;
    
    // Nested parsed structures for recursive template processing
    public List<TemplatePlaceholder> NestedPlaceholders { get; set; } = new();
    public List<SlottedTemplate> NestedSlottedTemplates { get; set; } = new();
    
    // Helper properties
    public bool HasNestedPlaceholders => NestedPlaceholders.Any();
    public bool HasNestedSlottedTemplates => NestedSlottedTemplates.Any();
    public bool RequiresNestedProcessing => HasNestedPlaceholders || HasNestedSlottedTemplates;
}

