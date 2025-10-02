using Assembler.TemplateModel;
using Arshu.App.FlexJson;
using System.Linq;
using Arshu.App.Json;

namespace AssemblerTest
{
    public static class PreprocessExtensions
    {
        // Extension methods for PreprocessedSiteTemplates
        public static string ToJson(this PreprocessedSiteTemplates siteTemplates, bool indented = true)
        {
            return SerializePreprocessedTemplates(siteTemplates, indented);
        }
        
        public static PreprocessedSummary CreateSummary(this PreprocessedSiteTemplates siteTemplates)
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
        
        public static string ToSummaryJson(this PreprocessedSiteTemplates siteTemplates, bool indented = true)
        {
            return SerializePreprocessedSummary(siteTemplates.CreateSummary(), indented);
        }
        
        // Serialization helpers (formerly in JsonConverter)
        public static string SerializePreprocessedTemplates(PreprocessedSiteTemplates templates, bool indented = true)
        {            
            // Always use custom JSON serialization for consistent camelCase and structure
            var jsonObject = ConvertPreprocessedTemplatesToJsonObject(templates);
            var json = FlexJsonUtil.SerializeFromJsonObject(jsonObject, indented);
            // Fix forward slash escaping to match Rust output
            return json.Replace("\\/", "/");
        }
        
        public static string SerializePreprocessedSummary(PreprocessedSummary summary, bool indented = true)
        {
            // Always use custom JSON serialization for consistent camelCase and structure
            var jsonObject = ConvertPreprocessedSummaryToJsonObject(summary);
            var json = FlexJsonUtil.SerializeFromJsonObject(jsonObject, indented);
            return json;
        }
        
        private static JsonObject ConvertPreprocessedTemplatesToJsonObject(PreprocessedSiteTemplates templates)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("siteName", templates.SiteName);
        
            var templatesObject = new JsonObject();
            foreach (var kvp in templates.Templates)
            {
                templatesObject.Add(kvp.Key, ConvertPreprocessedTemplateToJsonObject(kvp.Value));
            }
            jsonObject.Add("templates", templatesObject);
        
            var rawTemplatesObject = new JsonObject();
            foreach (var kvp in templates.RawTemplates)
            {
                rawTemplatesObject.Add(kvp.Key, kvp.Value);
            }
            jsonObject.Add("rawTemplates", rawTemplatesObject);
        
            var templateKeysArray = new JsonArray();
            foreach (var key in templates.TemplateKeys)
            {
                templateKeysArray.Add(key);
            }
            jsonObject.Add("templateKeys", templateKeysArray);
        
            return jsonObject;
        }
        
        private static JsonObject ConvertPreprocessedSummaryToJsonObject(PreprocessedSummary summary)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("siteName", summary.SiteName);
            jsonObject.Add("templatesRequiringProcessing", summary.TemplatesRequiringProcessing);
            jsonObject.Add("templatesWithJsonData", summary.TemplatesWithJsonData);
            jsonObject.Add("templatesWithPlaceholders", summary.TemplatesWithPlaceholders);
            jsonObject.Add("totalTemplates", summary.TotalTemplates);
            return jsonObject;
        }
        
        private static JsonObject ConvertPreprocessedTemplateToJsonObject(PreprocessedTemplate template)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("originalContent", template.OriginalContent);
        
            var placeholdersArray = new JsonArray();
            foreach (var placeholder in template.Placeholders)
            {
                placeholdersArray.Add(ConvertTemplatePlaceholderToJsonObject(placeholder));
            }
            jsonObject.Add("placeholders", placeholdersArray);
        
            var slottedTemplatesArray = new JsonArray();
            foreach (var slotted in template.SlottedTemplates)
            {
                slottedTemplatesArray.Add(ConvertSlottedTemplateToJsonObject(slotted));
            }
            jsonObject.Add("slottedTemplates", slottedTemplatesArray);
        
            if (template.JsonData != null)
            {
                jsonObject.Add("jsonData", template.JsonData);
            }
        
            var jsonPlaceholdersArray = new JsonArray();
            foreach (var jsonPlaceholder in template.JsonPlaceholders)
            {
                jsonPlaceholdersArray.Add(ConvertJsonPlaceholderToJsonObject(jsonPlaceholder));
            }
            jsonObject.Add("jsonPlaceholders", jsonPlaceholdersArray);
        
            var replacementMappingsArray = new JsonArray();
            foreach (var mapping in template.ReplacementMappings)
            {
                replacementMappingsArray.Add(ConvertReplacementMappingToJsonObject(mapping, template));
            }
            jsonObject.Add("replacementMappings", replacementMappingsArray);
        
            jsonObject.Add("hasPlaceholders", template.HasPlaceholders);
            jsonObject.Add("hasSlottedTemplates", template.HasSlottedTemplates);
            jsonObject.Add("hasJsonData", template.HasJsonData);
            jsonObject.Add("hasJsonPlaceholders", template.HasJsonPlaceholders);
            jsonObject.Add("hasReplacementMappings", template.HasReplacementMappings);
            jsonObject.Add("requiresProcessing", template.RequiresProcessing);
        
            return jsonObject;
        }
        
        private static JsonObject ConvertTemplatePlaceholderToJsonObject(TemplatePlaceholder placeholder)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("name", placeholder.Name);
            jsonObject.Add("startIndex", placeholder.StartIndex);
            jsonObject.Add("endIndex", placeholder.EndIndex);
            jsonObject.Add("fullMatch", placeholder.FullMatch);
            jsonObject.Add("templateKey", placeholder.TemplateKey);
            if (placeholder.JsonData != null)
            {
                jsonObject.Add("jsonData", placeholder.JsonData);
            }
        
            var nestedPlaceholdersArray = new JsonArray();
            foreach (var nested in placeholder.NestedPlaceholders)
            {
                nestedPlaceholdersArray.Add(ConvertTemplatePlaceholderToJsonObject(nested));
            }
            jsonObject.Add("nestedPlaceholders", nestedPlaceholdersArray);
        
            var nestedSlotsArray = new JsonArray();
            foreach (var slot in placeholder.NestedSlots)
            {
                nestedSlotsArray.Add(ConvertSlotPlaceholderToJsonObject(slot));
            }
            jsonObject.Add("nestedSlots", nestedSlotsArray);
        
            return jsonObject;
        }
        
        private static JsonObject ConvertSlottedTemplateToJsonObject(SlottedTemplate slotted)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("name", slotted.Name);
            jsonObject.Add("startIndex", slotted.StartIndex);
            jsonObject.Add("endIndex", slotted.EndIndex);
            jsonObject.Add("fullMatch", slotted.FullMatch);
            jsonObject.Add("innerContent", slotted.InnerContent);
            jsonObject.Add("templateKey", slotted.TemplateKey);
            if (slotted.JsonData != null)
            {
                jsonObject.Add("jsonData", slotted.JsonData);
            }
        
            var slotsArray = new JsonArray();
            foreach (var slot in slotted.Slots)
            {
                slotsArray.Add(ConvertSlotPlaceholderToJsonObject(slot));
            }
            jsonObject.Add("slots", slotsArray);
        
            return jsonObject;
        }
        
        private static JsonObject ConvertSlotPlaceholderToJsonObject(SlotPlaceholder slot)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("number", slot.Number);
            jsonObject.Add("startIndex", slot.StartIndex);
            jsonObject.Add("endIndex", slot.EndIndex);
            jsonObject.Add("content", slot.Content);
            jsonObject.Add("slotKey", slot.SlotKey);
            jsonObject.Add("openTag", slot.OpenTag);
            jsonObject.Add("closeTag", slot.CloseTag);
        
            var nestedSlotsArray = new JsonArray();
            foreach (var nested in slot.NestedSlots)
            {
                nestedSlotsArray.Add(ConvertSlotPlaceholderToJsonObject(nested));
            }
            jsonObject.Add("nestedSlots", nestedSlotsArray);
        
            var nestedPlaceholdersArray = new JsonArray();
            foreach (var placeholder in slot.NestedPlaceholders)
            {
                nestedPlaceholdersArray.Add(ConvertTemplatePlaceholderToJsonObject(placeholder));
            }
            jsonObject.Add("nestedPlaceholders", nestedPlaceholdersArray);
        
            var nestedSlottedTemplatesArray = new JsonArray();
            foreach (var slotted in slot.NestedSlottedTemplates)
            {
                nestedSlottedTemplatesArray.Add(ConvertSlottedTemplateToJsonObject(slotted));
            }
            jsonObject.Add("nestedSlottedTemplates", nestedSlottedTemplatesArray);
        
            return jsonObject;
        }
        
        private static JsonObject ConvertJsonPlaceholderToJsonObject(JsonPlaceholder jsonPlaceholder)
        {
            var jsonObject = new JsonObject();
            jsonObject.Add("key", jsonPlaceholder.Key);
            jsonObject.Add("placeholder", jsonPlaceholder.Placeholder);
            jsonObject.Add("value", jsonPlaceholder.Value);
            return jsonObject;
        }
        
        private static JsonObject ConvertReplacementMappingToJsonObject(ReplacementMapping mapping, PreprocessedTemplate template)
        {
            var placeholder = template.Placeholders.FirstOrDefault(p => p.FullMatch == mapping.OriginalText);
            var startIndex = placeholder?.StartIndex ?? mapping.StartIndex;
            var endIndex = placeholder?.EndIndex ?? mapping.EndIndex;
            
            var jsonObject = new JsonObject();
            jsonObject.Add("startIndex", startIndex);
            jsonObject.Add("endIndex", endIndex);
            jsonObject.Add("originalText", mapping.OriginalText);
            jsonObject.Add("replacementText", mapping.ReplacementText);
            jsonObject.Add("type", mapping.Type.ToString());
            return jsonObject;
        }
    }
}
