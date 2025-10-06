using Assembler.TemplateModel;
using System.Collections.Generic;

namespace Assembler.TemplateApi
{
    public class TemplateData
    {
        public string Html { get; set; } = string.Empty;
        public string? Json { get; set; }       
    }

    public class PreProcessTemplateMetadata
    {
        public string OriginalContent { get; set; } = string.Empty;
        public List<TemplatePlaceholder> Placeholders { get; set; } = new();
        public List<SlottedTemplate> SlottedTemplates { get; set; } = new();
        public object? JsonData { get; set; }
        public List<JsonPlaceholder> JsonPlaceholders { get; set; } = new();
        public List<ReplacementMapping> ReplacementMappings { get; set; } = new();
        public bool HasPlaceholders { get; set; }
        public bool HasSlottedTemplates { get; set; }
        public bool HasJsonData { get; set; }
        public bool HasJsonPlaceholders { get; set; }
        public bool HasReplacementMappings { get; set; }
        public bool RequiresProcessing { get; set; }        
    }

    public class ApiResponse
    {
        public Dictionary<string, TemplateData> Templates { get; set; } = new();
        public Dictionary<string, PreProcessTemplateMetadata> PreProcessTemplates { get; set; } = new();
        public string AppSite { get; set; } = string.Empty;
        public string? AppFile { get; set; }
        public string? AppView { get; set; }
        public double ServerTimeMs { get; set; } = 0;
        public string Html { get; set; } = string.Empty;
        public double EngineTimeMs { get; set; }

        static ApiResponse() { }

        public string SerializeToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');

            // Serialize Templates dictionary
            sb.Append("\"Templates\":");
            SerializeDictionary(sb, Templates, SerializeTemplateData);

            sb.Append(',');

            // Serialize PreProcessTemplates dictionary  
            sb.Append("\"PreProcessTemplates\":");
            SerializeDictionary(sb, PreProcessTemplates, SerializePreProcessMetadata);

            sb.Append(',');

            // Serialize AppSite
            sb.Append("\"AppSite\":\"");
            sb.Append(EscapeJsonString(AppSite));
            sb.Append("\"");

            // Serialize AppFile if not null
            if (AppFile != null)
            {
                sb.Append(",\"AppFile\":\"");
                sb.Append(EscapeJsonString(AppFile));
                sb.Append("\"");
            }

            // Serialize AppView if not null
            if (AppView != null)
            {
                sb.Append(",\"AppView\":\"");
                sb.Append(EscapeJsonString(AppView));
                sb.Append("\"");
            }

            // Serialize ServerTimeMs
            sb.Append(",\"ServerTimeMs\":");
            sb.Append(ServerTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

            //// Serialize ClientTime
            //sb.Append(",\"ClientTime\":\"");
            //sb.Append(EscapeJsonString(ClientTime));
            //sb.Append("\"");

            // Merged Html
            sb.Append(",\"Html\":\"");
            sb.Append(EscapeHtmlString(Html));
            sb.Append("\"");

            //Merge Time
            sb.Append(",\"EngineTimeMs\":");
            sb.Append(EngineTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

            sb.Append('}');
            return sb.ToString();
        }

        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            return input
                .Replace("\\", "\\\\")
                //.Replace("\"", "\\u0022")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("<", "\\u003C")
                .Replace(">", "\\u003E")
                .Replace("&", "\\u0026")
                .Replace("'", "\\u0027")
                .Replace("+", "\\u002B");
        }

        private static string EscapeHtmlString(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            return input
                .Replace("\\", "\\\\")
                //.Replace("\"", "\\u0022")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static void SerializeDictionary<T>(System.Text.StringBuilder sb, Dictionary<string, T> dict, System.Action<System.Text.StringBuilder, T> serializeValue)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                sb.Append("\"");
                sb.Append(EscapeJsonString(kvp.Key));
                sb.Append("\":");
                serializeValue(sb, kvp.Value);
                first = false;
            }
            sb.Append('}');
        }
        
        private static void SerializeTemplateData(System.Text.StringBuilder sb, TemplateData data)
        {
            sb.Append("{\"Html\":\"");
            sb.Append(EscapeJsonString(data.Html));
            sb.Append("\",\"Json\":");
            if (data.Json != null)
            {
                sb.Append("\"");
                sb.Append(EscapeJsonString(data.Json));
                sb.Append("\"");
            }
            else
            {
                sb.Append("null");
            }
            sb.Append('}');
        }

        private static void SerializePreProcessMetadata(System.Text.StringBuilder sb, PreProcessTemplateMetadata metadata)
        {
            sb.Append('{');
            
            // OriginalContent
            sb.Append("\"OriginalContent\":\"");
            sb.Append(EscapeHtmlString(metadata.OriginalContent));
            sb.Append("\",");
            
            // Placeholders
            sb.Append("\"Placeholders\":");
            SerializePlaceholdersList(sb, metadata.Placeholders);
            sb.Append(",");
            
            // SlottedTemplates
            sb.Append("\"SlottedTemplates\":");
            SerializeSlottedTemplatesList(sb, metadata.SlottedTemplates);
            sb.Append(",");
            
            // JsonData
            sb.Append("\"JsonData\":");
            if (metadata.JsonData != null)
            {
                // Safely serialize JsonData - if it's already a JSON string, don't double-escape
                var jsonDataStr = metadata.JsonData.ToString() ?? "";
                if (jsonDataStr.StartsWith("{") || jsonDataStr.StartsWith("["))
                {
                    // Appears to be JSON already, include as-is
                    sb.Append(jsonDataStr);
                }
                else
                {
                    // Treat as string value
                    sb.Append("\"");
                    sb.Append(EscapeJsonString(jsonDataStr));
                    sb.Append("\"");
                }
            }
            else
            {
                sb.Append("null");
            }
            sb.Append(",");
            
            // JsonPlaceholders
            sb.Append("\"JsonPlaceholders\":");
            SerializeJsonPlaceholdersList(sb, metadata.JsonPlaceholders);
            sb.Append(",");
            
            // ReplacementMappings
            sb.Append("\"ReplacementMappings\":");
            SerializeReplacementMappingsList(sb, metadata.ReplacementMappings);
            sb.Append(",");
            
            // Boolean properties
            sb.Append("\"HasPlaceholders\":");
            sb.Append(metadata.HasPlaceholders.ToString().ToLower());
            sb.Append(",\"HasSlottedTemplates\":");
            sb.Append(metadata.HasSlottedTemplates.ToString().ToLower());
            sb.Append(",\"HasJsonData\":");
            sb.Append(metadata.HasJsonData.ToString().ToLower());
            sb.Append(",\"HasJsonPlaceholders\":");
            sb.Append(metadata.HasJsonPlaceholders.ToString().ToLower());
            sb.Append(",\"HasReplacementMappings\":");
            sb.Append(metadata.HasReplacementMappings.ToString().ToLower());
            sb.Append(",\"RequiresProcessing\":");
            sb.Append(metadata.RequiresProcessing.ToString().ToLower());
            
            sb.Append('}');
        }

        private static void SerializePlaceholdersList(System.Text.StringBuilder sb, System.Collections.Generic.List<Assembler.TemplateModel.TemplatePlaceholder> placeholders)
        {
            sb.Append('[');
            for (int i = 0; i < placeholders.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializePlaceholder(sb, placeholders[i]);
            }
            sb.Append(']');
        }

        private static void SerializePlaceholder(System.Text.StringBuilder sb, Assembler.TemplateModel.TemplatePlaceholder placeholder)
        {
            sb.Append('{');
            sb.Append("\"Name\":\"");
            sb.Append(EscapeJsonString(placeholder.Name));
            sb.Append("\",\"StartIndex\":");
            sb.Append(placeholder.StartIndex);
            sb.Append(",\"EndIndex\":");
            sb.Append(placeholder.EndIndex);
            sb.Append(",\"FullMatch\":\"");
            sb.Append(EscapeJsonString(placeholder.FullMatch));
            sb.Append("\",\"TemplateKey\":\"");
            sb.Append(EscapeJsonString(placeholder.TemplateKey));
            sb.Append("\",\"JsonData\":");
            if (placeholder.JsonData != null)
            {
                sb.Append("\"");
                sb.Append(EscapeJsonString(placeholder.JsonData.ToString() ?? ""));
                sb.Append("\"");
            }
            else
            {
                sb.Append("null");
            }
            sb.Append(",\"NestedPlaceholders\":");
            SerializePlaceholdersList(sb, placeholder.NestedPlaceholders);
            sb.Append(",\"NestedSlots\":");
            SerializeSlotPlaceholdersList(sb, placeholder.NestedSlots);
            sb.Append('}');
        }

        private static void SerializeSlotPlaceholdersList(System.Text.StringBuilder sb, System.Collections.Generic.List<Assembler.TemplateModel.SlotPlaceholder> slots)
        {
            sb.Append('[');
            for (int i = 0; i < slots.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeSlotPlaceholder(sb, slots[i]);
            }
            sb.Append(']');
        }

        private static void SerializeSlotPlaceholder(System.Text.StringBuilder sb, Assembler.TemplateModel.SlotPlaceholder slot)
        {
            sb.Append('{');
            sb.Append("\"Number\":\"");
            sb.Append(EscapeJsonString(slot.Number));
            sb.Append("\",\"StartIndex\":");
            sb.Append(slot.StartIndex);
            sb.Append(",\"EndIndex\":");
            sb.Append(slot.EndIndex);
            sb.Append(",\"Content\":\"");
            sb.Append(EscapeJsonString(slot.Content));
            sb.Append("\",\"SlotKey\":\"");
            sb.Append(EscapeJsonString(slot.SlotKey));
            sb.Append("\",\"OpenTag\":\"");
            sb.Append(EscapeJsonString(slot.OpenTag));
            sb.Append("\",\"CloseTag\":\"");
            sb.Append(EscapeJsonString(slot.CloseTag));
            sb.Append("\",\"NestedSlots\":");
            SerializeSlotPlaceholdersList(sb, slot.NestedSlots);
            sb.Append(",\"NestedPlaceholders\":");
            SerializePlaceholdersList(sb, slot.NestedPlaceholders);
            sb.Append(",\"NestedSlottedTemplates\":");
            SerializeSlottedTemplatesList(sb, slot.NestedSlottedTemplates);
            sb.Append(",\"HasNestedPlaceholders\":");
            sb.Append(slot.HasNestedPlaceholders.ToString().ToLower());
            sb.Append(",\"HasNestedSlottedTemplates\":");
            sb.Append(slot.HasNestedSlottedTemplates.ToString().ToLower());
            sb.Append(",\"RequiresNestedProcessing\":");
            sb.Append(slot.RequiresNestedProcessing.ToString().ToLower());
            sb.Append('}');
        }

        private static void SerializeSlottedTemplatesList(System.Text.StringBuilder sb, System.Collections.Generic.List<Assembler.TemplateModel.SlottedTemplate> templates)
        {
            sb.Append('[');
            for (int i = 0; i < templates.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeSlottedTemplate(sb, templates[i]);
            }
            sb.Append(']');
        }

        private static void SerializeSlottedTemplate(System.Text.StringBuilder sb, Assembler.TemplateModel.SlottedTemplate template)
        {
            sb.Append('{');
            sb.Append("\"Name\":\"");
            sb.Append(EscapeJsonString(template.Name));
            sb.Append("\",\"StartIndex\":");
            sb.Append(template.StartIndex);
            sb.Append(",\"EndIndex\":");
            sb.Append(template.EndIndex);
            sb.Append(",\"FullMatch\":\"");
            sb.Append(EscapeJsonString(template.FullMatch));
            sb.Append("\",\"TemplateKey\":\"");
            sb.Append(EscapeJsonString(template.TemplateKey));
            sb.Append("\",\"Slots\":");
            SerializeSlotPlaceholdersList(sb, template.Slots);
            sb.Append('}');
        }

        private static void SerializeJsonPlaceholdersList(System.Text.StringBuilder sb, System.Collections.Generic.List<Assembler.TemplateModel.JsonPlaceholder> placeholders)
        {
            sb.Append('[');
            for (int i = 0; i < placeholders.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeJsonPlaceholder(sb, placeholders[i]);
            }
            sb.Append(']');
        }

        private static void SerializeJsonPlaceholder(System.Text.StringBuilder sb, Assembler.TemplateModel.JsonPlaceholder placeholder)
        {
            sb.Append('{');
            sb.Append("\"Key\":\"");
            sb.Append(EscapeJsonString(placeholder.Key));
            sb.Append("\",\"Placeholder\":\"");
            sb.Append(EscapeJsonString(placeholder.Placeholder));
            sb.Append("\",\"Value\":\"");
            sb.Append(EscapeJsonString(placeholder.Value));
            sb.Append("\"}");
        }

        private static void SerializeReplacementMappingsList(System.Text.StringBuilder sb, System.Collections.Generic.List<Assembler.TemplateModel.ReplacementMapping> mappings)
        {
            sb.Append('[');
            for (int i = 0; i < mappings.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeReplacementMapping(sb, mappings[i]);
            }
            sb.Append(']');
        }

        private static void SerializeReplacementMapping(System.Text.StringBuilder sb, Assembler.TemplateModel.ReplacementMapping mapping)
        {
            sb.Append('{');
            sb.Append("\"StartIndex\":");
            sb.Append(mapping.StartIndex);
            sb.Append(",\"EndIndex\":");
            sb.Append(mapping.EndIndex);
            sb.Append(",\"OriginalText\":\"");
            sb.Append(EscapeHtmlString(mapping.OriginalText));
            sb.Append("\",\"ReplacementText\":\"");
            sb.Append(EscapeHtmlString(mapping.ReplacementText));
            sb.Append("\",\"Type\":");
            sb.Append((int)mapping.Type);
            sb.Append('}');
        }

        // New manual serialization methods for PreprocessedSiteTemplates
        public static string SerializePreprocessedSiteTemplates(Assembler.TemplateModel.PreprocessedSiteTemplates templates, bool indented = false)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');

            // SiteName
            sb.Append("\"siteName\":\"");
            sb.Append(EscapeJsonString(templates.SiteName));
            sb.Append("\",");

            // Templates dictionary
            sb.Append("\"templates\":");
            SerializeTemplatesDictionary(sb, templates.Templates);
            sb.Append(",");

            // RawTemplates dictionary
            sb.Append("\"rawTemplates\":{");
            bool firstRaw = true;
            foreach (var kvp in templates.RawTemplates)
            {
                if (!firstRaw) sb.Append(',');
                sb.Append("\"");
                sb.Append(EscapeJsonString(kvp.Key));
                sb.Append("\":\"");
                sb.Append(EscapeJsonString(kvp.Value));
                sb.Append("\"");
                firstRaw = false;
            }
            sb.Append("},");

            // TemplateKeys array
            sb.Append("\"templateKeys\":[");
            bool firstKey = true;
            foreach (var key in templates.TemplateKeys)
            {
                if (!firstKey) sb.Append(',');
                sb.Append("\"");
                sb.Append(EscapeJsonString(key));
                sb.Append("\"");
                firstKey = false;
            }
            sb.Append("]");

            sb.Append('}');

            if (indented)
            {
                return FormatJson(sb.ToString());
            }
            return sb.ToString();
        }

        private static void SerializeTemplatesDictionary(System.Text.StringBuilder sb, Dictionary<string, Assembler.TemplateModel.PreprocessedTemplate> templates)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in templates)
            {
                if (!first) sb.Append(',');
                sb.Append("\"");
                sb.Append(EscapeJsonString(kvp.Key));
                sb.Append("\":");
                SerializePreprocessedTemplate(sb, kvp.Value);
                first = false;
            }
            sb.Append('}');
        }

        private static void SerializePreprocessedTemplate(System.Text.StringBuilder sb, Assembler.TemplateModel.PreprocessedTemplate template)
        {
            sb.Append('{');

            // OriginalContent
            sb.Append("\"originalContent\":\"");
            sb.Append(EscapeJsonString(template.OriginalContent));
            sb.Append("\",");

            // Placeholders
            sb.Append("\"placeholders\":");
            SerializePlaceholdersList(sb, template.Placeholders);
            sb.Append(",");

            // SlottedTemplates
            sb.Append("\"slottedTemplates\":");
            SerializeSlottedTemplatesList(sb, template.SlottedTemplates);
            sb.Append(",");

            // JsonData
            sb.Append("\"jsonData\":");
            if (template.JsonData != null)
            {
                var jsonDataStr = template.JsonData.ToString() ?? "";
                if (jsonDataStr.StartsWith("{") || jsonDataStr.StartsWith("["))
                {
                    sb.Append(jsonDataStr);
                }
                else
                {
                    sb.Append("\"");
                    sb.Append(EscapeJsonString(jsonDataStr));
                    sb.Append("\"");
                }
            }
            else
            {
                sb.Append("null");
            }
            sb.Append(",");

            // JsonPlaceholders
            sb.Append("\"jsonPlaceholders\":");
            SerializeJsonPlaceholdersList(sb, template.JsonPlaceholders);
            sb.Append(",");

            // ReplacementMappings
            sb.Append("\"replacementMappings\":");
            SerializeReplacementMappingsList(sb, template.ReplacementMappings);
            sb.Append(",");

            // Boolean properties
            sb.Append("\"hasPlaceholders\":");
            sb.Append(template.HasPlaceholders.ToString().ToLower());
            sb.Append(",\"hasSlottedTemplates\":");
            sb.Append(template.HasSlottedTemplates.ToString().ToLower());
            sb.Append(",\"hasJsonData\":");
            sb.Append(template.HasJsonData.ToString().ToLower());
            sb.Append(",\"hasJsonPlaceholders\":");
            sb.Append(template.HasJsonPlaceholders.ToString().ToLower());
            sb.Append(",\"hasReplacementMappings\":");
            sb.Append(template.HasReplacementMappings.ToString().ToLower());
            sb.Append(",\"requiresProcessing\":");
            sb.Append(template.RequiresProcessing.ToString().ToLower());

            sb.Append('}');
        }

        // New manual serialization method for PreprocessedSummary
        public static string SerializePreprocessedSummary(Assembler.TemplateModel.PreprocessedSummary summary, bool indented = false)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');

            sb.Append("\"siteName\":\"");
            sb.Append(EscapeJsonString(summary.SiteName));
            sb.Append("\",");

            sb.Append("\"templatesRequiringProcessing\":");
            sb.Append(summary.TemplatesRequiringProcessing);
            sb.Append(",");

            sb.Append("\"templatesWithJsonData\":");
            sb.Append(summary.TemplatesWithJsonData);
            sb.Append(",");

            sb.Append("\"templatesWithPlaceholders\":");
            sb.Append(summary.TemplatesWithPlaceholders);
            sb.Append(",");

            sb.Append("\"totalTemplates\":");
            sb.Append(summary.TotalTemplates);

            sb.Append('}');

            if (indented)
            {
                return FormatJson(sb.ToString());
            }
            return sb.ToString();
        }

        private static string FormatJson(string json)
        {
            // Simple JSON formatting for readability
            var formatted = json.Replace(",", ",\n  ")
                              .Replace("{", "{\n  ")
                              .Replace("}", "\n}")
                              .Replace("[", "[\n  ")
                              .Replace("]", "\n]");
            return formatted;
        }

    }
}
