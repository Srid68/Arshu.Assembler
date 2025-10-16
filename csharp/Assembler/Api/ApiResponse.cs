using Assembler.Model;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Assembler.Api
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

        public string SerializeToJson(bool indented = false)
        {
            var sb = new StringBuilder();
            int indent = 0;
            
            AppendLine(sb, "{", ref indent, indented, true);

            // Serialize Templates dictionary
            Append(sb, "\"Templates\":", indent, indented);
            SerializeDictionary(sb, Templates, SerializeTemplateData, indent, indented);
            AppendLine(sb, ",", ref indent, indented, false);

            // Serialize PreProcessTemplates dictionary  
            Append(sb, "\"PreProcessTemplates\":", indent, indented);
            SerializeDictionary(sb, PreProcessTemplates, SerializePreProcessMetadata, indent, indented);
            AppendLine(sb, ",", ref indent, indented, false);

            // Serialize AppSite
            Append(sb, "\"AppSite\":\"", indent, indented);
            sb.Append(EscapeJsonString(AppSite));
            sb.Append("\"");

            // Serialize AppFile if not null
            if (AppFile != null)
            {
                AppendLine(sb, ",", ref indent, indented, false);
                Append(sb, "\"AppFile\":\"", indent, indented);
                sb.Append(EscapeJsonString(AppFile));
                sb.Append("\"");
            }

            // Serialize AppView if not null
            if (AppView != null)
            {
                AppendLine(sb, ",", ref indent, indented, false);
                Append(sb, "\"AppView\":\"", indent, indented);
                sb.Append(EscapeJsonString(AppView));
                sb.Append("\"");
            }

            // Serialize ServerTimeMs
            AppendLine(sb, ",", ref indent, indented, false);
            Append(sb, "\"ServerTimeMs\":", indent, indented);
            sb.Append(ServerTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Merged Html
            AppendLine(sb, ",", ref indent, indented, false);
            Append(sb, "\"Html\":\"", indent, indented);
            sb.Append(EscapeHtmlString(Html));
            sb.Append("\"");

            //Merge Time
            AppendLine(sb, ",", ref indent, indented, false);
            Append(sb, "\"EngineTimeMs\":", indent, indented);
            sb.Append(EngineTimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

            AppendLine(sb, "", ref indent, indented, false);
            Append(sb, "}", indent, indented);
            
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string text, int indent, bool indented)
        {
            if (indented && text.Length > 0)
            {
                sb.Append(new string(' ', indent * 2));
            }
            sb.Append(text);
        }

        private static void AppendLine(StringBuilder sb, string text, ref int indent, bool indented, bool incrementIndent)
        {
            if (incrementIndent) indent++;
            sb.Append(text);
            if (indented)
            {
                sb.Append('\n');
            }
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

        private static void SerializeDictionary<T>(StringBuilder sb, Dictionary<string, T> dict, System.Action<StringBuilder, T, int, bool> serializeValue, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                    if (indented) sb.Append('\n');
                }
                
                if (indented) sb.Append(new string(' ', (indent + 1) * 2));
                sb.Append("\"");
                sb.Append(EscapeJsonString(kvp.Key));
                sb.Append("\":");
                if (indented) sb.Append(' ');
                
                serializeValue(sb, kvp.Value, indent + 1, indented);
                first = false;
            }
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', indent * 2));
            }
            sb.Append('}');
        }
        
        private static void SerializeTemplateData(StringBuilder sb, TemplateData data, int indent, bool indented)
        {
            sb.Append('{');
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', indent * 2));
            }
            sb.Append("\"Html\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(data.Html));
            sb.Append("\"");
            sb.Append(',');
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', indent * 2));
            }
            sb.Append("\"Json\":");
            if (indented) sb.Append(' ');
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
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        private static void SerializePreProcessMetadata(StringBuilder sb, PreProcessTemplateMetadata metadata, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            // OriginalContent
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"OriginalContent\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeHtmlString(metadata.OriginalContent));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            // Placeholders
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Placeholders\":");
            if (indented) sb.Append(' ');
            SerializePlaceholdersList(sb, metadata.Placeholders, indent, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            // SlottedTemplates
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"SlottedTemplates\":");
            if (indented) sb.Append(' ');
            SerializeSlottedTemplatesList(sb, metadata.SlottedTemplates, indent, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            // JsonData
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"JsonData\":");
            if (indented) sb.Append(' ');
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
            if (indented) sb.Append('\n');
            
            // JsonPlaceholders
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"JsonPlaceholders\":");
            if (indented) sb.Append(' ');
            SerializeJsonPlaceholdersList(sb, metadata.JsonPlaceholders, indent, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            // ReplacementMappings
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"ReplacementMappings\":");
            if (indented) sb.Append(' ');
            SerializeReplacementMappingsList(sb, metadata.ReplacementMappings, indent, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            // Boolean properties
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasPlaceholders\":");
            if (indented) sb.Append(' ');
            sb.Append(metadata.HasPlaceholders.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasSlottedTemplates\":");
            if (indented) sb.Append(' ');
            sb.Append(metadata.HasSlottedTemplates.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasJsonData\":");
            if (indented) sb.Append(' ');
            sb.Append(metadata.HasJsonData.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasJsonPlaceholders\":");
            if (indented) sb.Append(' ');
            sb.Append(metadata.HasJsonPlaceholders.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasReplacementMappings\":");
            if (indented) sb.Append(' ');
            sb.Append(metadata.HasReplacementMappings.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"RequiresProcessing\":");
            if (indented) sb.Append(' ');
            sb.Append(metadata.RequiresProcessing.ToString().ToLower());
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        private static void SerializePlaceholdersList(StringBuilder sb, List<TemplatePlaceholder> placeholders, int indent, bool indented)
        {
            sb.Append('[');
            if (indented && placeholders.Count > 0) sb.Append('\n');
            
            for (int i = 0; i < placeholders.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                    if (indented) sb.Append('\n');
                }
                if (indented) sb.Append(new string(' ', indent * 2));
                SerializePlaceholder(sb, placeholders[i], indent, indented);
            }
            
            if (indented && placeholders.Count > 0)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append(']');
        }

        private static void SerializePlaceholder(StringBuilder sb, TemplatePlaceholder placeholder, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Name\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(placeholder.Name));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"StartIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(placeholder.StartIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"EndIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(placeholder.EndIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"FullMatch\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(placeholder.FullMatch));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"TemplateKey\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(placeholder.TemplateKey));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"JsonData\":");
            if (indented) sb.Append(' ');
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
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"NestedPlaceholders\":");
            if (indented) sb.Append(' ');
            SerializePlaceholdersList(sb, placeholder.NestedPlaceholders, indent + 1, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"NestedSlots\":");
            if (indented) sb.Append(' ');
            SerializeSlotPlaceholdersList(sb, placeholder.NestedSlots, indent + 1, indented);
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        private static void SerializeSlotPlaceholdersList(StringBuilder sb, List<SlotPlaceholder> slots, int indent, bool indented)
        {
            sb.Append('[');
            if (indented && slots.Count > 0) sb.Append('\n');
            
            for (int i = 0; i < slots.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                    if (indented) sb.Append('\n');
                }
                if (indented) sb.Append(new string(' ', indent * 2));
                SerializeSlotPlaceholder(sb, slots[i], indent, indented);
            }
            
            if (indented && slots.Count > 0)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append(']');
        }

        private static void SerializeSlotPlaceholder(StringBuilder sb, SlotPlaceholder slot, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Number\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(slot.Number));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"StartIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(slot.StartIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"EndIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(slot.EndIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Content\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(slot.Content));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"SlotKey\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(slot.SlotKey));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"OpenTag\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(slot.OpenTag));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"CloseTag\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(slot.CloseTag));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"NestedSlots\":");
            if (indented) sb.Append(' ');
            SerializeSlotPlaceholdersList(sb, slot.NestedSlots, indent + 1, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"NestedPlaceholders\":");
            if (indented) sb.Append(' ');
            SerializePlaceholdersList(sb, slot.NestedPlaceholders, indent + 1, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"NestedSlottedTemplates\":");
            if (indented) sb.Append(' ');
            SerializeSlottedTemplatesList(sb, slot.NestedSlottedTemplates, indent + 1, indented);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasNestedPlaceholders\":");
            if (indented) sb.Append(' ');
            sb.Append(slot.HasNestedPlaceholders.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"HasNestedSlottedTemplates\":");
            if (indented) sb.Append(' ');
            sb.Append(slot.HasNestedSlottedTemplates.ToString().ToLower());
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"RequiresNestedProcessing\":");
            if (indented) sb.Append(' ');
            sb.Append(slot.RequiresNestedProcessing.ToString().ToLower());
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        private static void SerializeSlottedTemplatesList(StringBuilder sb, List<SlottedTemplate> templates, int indent, bool indented)
        {
            sb.Append('[');
            if (indented && templates.Count > 0) sb.Append('\n');
            
            for (int i = 0; i < templates.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                    if (indented) sb.Append('\n');
                }
                if (indented) sb.Append(new string(' ', indent * 2));
                SerializeSlottedTemplate(sb, templates[i], indent, indented);
            }
            
            if (indented && templates.Count > 0)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append(']');
        }

        private static void SerializeSlottedTemplate(StringBuilder sb, SlottedTemplate template, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Name\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(template.Name));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"StartIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(template.StartIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"EndIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(template.EndIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"FullMatch\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(template.FullMatch));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"TemplateKey\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(template.TemplateKey));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Slots\":");
            if (indented) sb.Append(' ');
            SerializeSlotPlaceholdersList(sb, template.Slots, indent + 1, indented);
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        private static void SerializeJsonPlaceholdersList(StringBuilder sb, List<JsonPlaceholder> placeholders, int indent, bool indented)
        {
            sb.Append('[');
            if (indented && placeholders.Count > 0) sb.Append('\n');
            
            for (int i = 0; i < placeholders.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                    if (indented) sb.Append('\n');
                }
                if (indented) sb.Append(new string(' ', indent * 2));
                SerializeJsonPlaceholder(sb, placeholders[i], indent, indented);
            }
            
            if (indented && placeholders.Count > 0)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append(']');
        }

        private static void SerializeJsonPlaceholder(StringBuilder sb, JsonPlaceholder placeholder, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Key\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(placeholder.Key));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Placeholder\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(placeholder.Placeholder));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Value\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeJsonString(placeholder.Value));
            sb.Append("\"");
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        private static void SerializeReplacementMappingsList(StringBuilder sb, List<ReplacementMapping> mappings, int indent, bool indented)
        {
            sb.Append('[');
            if (indented && mappings.Count > 0) sb.Append('\n');
            
            for (int i = 0; i < mappings.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                    if (indented) sb.Append('\n');
                }
                if (indented) sb.Append(new string(' ', indent * 2));
                SerializeReplacementMapping(sb, mappings[i], indent, indented);
            }
            
            if (indented && mappings.Count > 0)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append(']');
        }

        private static void SerializeReplacementMapping(StringBuilder sb, ReplacementMapping mapping, int indent, bool indented)
        {
            sb.Append('{');
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"StartIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(mapping.StartIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"EndIndex\":");
            if (indented) sb.Append(' ');
            sb.Append(mapping.EndIndex);
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"OriginalText\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeHtmlString(mapping.OriginalText));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"ReplacementText\":");
            if (indented) sb.Append(' ');
            sb.Append("\"");
            sb.Append(EscapeHtmlString(mapping.ReplacementText));
            sb.Append("\"");
            sb.Append(",");
            if (indented) sb.Append('\n');
            
            if (indented) sb.Append(new string(' ', indent * 2));
            sb.Append("\"Type\":");
            if (indented) sb.Append(' ');
            sb.Append((int)mapping.Type);
            
            if (indented)
            {
                sb.Append('\n');
                sb.Append(new string(' ', (indent - 1) * 2));
            }
            sb.Append('}');
        }

        // New manual serialization methods for PreprocessedSiteTemplates
        public static string SerializePreprocessedSiteTemplates(PreprocessedSiteTemplates templates, bool indented = false)
        {
            var sb = new StringBuilder();
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

        private static void SerializeTemplatesDictionary(StringBuilder sb, Dictionary<string, PreprocessedTemplate> templates)
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

        private static void SerializePreprocessedTemplate(StringBuilder sb, PreprocessedTemplate template)
        {
            sb.Append('{');

            // OriginalContent
            sb.Append("\"originalContent\":\"");
            sb.Append(EscapeJsonString(template.OriginalContent));
            sb.Append("\",");

            // Placeholders
            sb.Append("\"placeholders\":");
            SerializePlaceholdersList(sb, template.Placeholders, 0, false);
            sb.Append(",");

            // SlottedTemplates
            sb.Append("\"slottedTemplates\":");
            SerializeSlottedTemplatesList(sb, template.SlottedTemplates, 0, false);
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
            SerializeJsonPlaceholdersList(sb, template.JsonPlaceholders, 0, false);
            sb.Append(",");

            // ReplacementMappings
            sb.Append("\"replacementMappings\":");
            SerializeReplacementMappingsList(sb, template.ReplacementMappings, 0, false);
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

        // Create summary from PreprocessedSiteTemplates
        public static PreprocessedSummary CreatePreprocessedSummary(PreprocessedSiteTemplates siteTemplates)
        {
            return new PreprocessedSummary
            {
                SiteName = siteTemplates.SiteName,
                TemplatesRequiringProcessing = siteTemplates.Templates.Values.Count(t => t.RequiresProcessing),
                TemplatesWithJsonData = siteTemplates.Templates.Values.Count(t => t.HasJsonData),
                TemplatesWithPlaceholders = siteTemplates.Templates.Values.Count(t => t.HasPlaceholders),
                TotalTemplates = siteTemplates.Templates.Count
            };
        }

        // New manual serialization method for PreprocessedSummary
        public static string SerializePreprocessedSummary(PreprocessedSummary summary, bool indented = false)
        {
            var sb = new StringBuilder();
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
