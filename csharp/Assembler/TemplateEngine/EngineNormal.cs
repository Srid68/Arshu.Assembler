using System;
using System.Collections.Generic;
using System.Linq;
using Arshu.App.Json;
using Assembler.TemplateCommon;

namespace Assembler.TemplateEngine;

// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;
using JsonArray = Arshu.App.Json.JsonArray;

/// <summary>
/// IndexOf-based template engine implementation for improved performance
/// </summary>
public class EngineNormal 
{
    #region Merge Templates

    public string AppViewPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Merges templates by replacing placeholders with corresponding HTML
    /// This is a hybrid method that processes both slotted templates and simple placeholders
    /// JSON files with matching names are automatically merged with HTML templates before processing
    /// </summary>
    /// <param name="appSite">The application site name for template key generation</param>
    /// <param name="appView">The application view name (optional)</param>
    /// <param name="appFile">The application file name</param>
    /// <param name="templates">Dictionary of available templates, where value is tuple of (HTML content, JSON content or null)</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced</returns>
    public string MergeTemplates(string appSite, string appFile, string? appView, Dictionary<string, (string html, string? json)> templates, bool enableJsonProcessing = true)
    {
        if (templates == null || templates.Count == 0)
            return "";

        // Direct dictionary lookup for main template
    string mainTemplateKey = appSite.ToLowerInvariant() + "_" + appFile.ToLowerInvariant();
        (string html, string? json) mainTemplate;
        if (!templates.TryGetValue(mainTemplateKey, out mainTemplate))
        {
            // AppView fallback logic
            if (!string.IsNullOrEmpty(appView) && !string.IsNullOrEmpty(AppViewPrefix) && appFile.Contains(AppViewPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var appKey = TemplateUtils.ReplaceCaseInsensitive(appFile, AppViewPrefix, appView);
                var fallbackTemplateKey = appSite.ToLowerInvariant() + "_" + appKey.ToLowerInvariant();
                if (!templates.TryGetValue(fallbackTemplateKey, out mainTemplate))
                    return string.Empty;
            }
            else
            {
                return string.Empty;
            }
        }

        var contentHtml = mainTemplate.html;
        if (enableJsonProcessing && !string.IsNullOrEmpty(mainTemplate.json))
        {
            contentHtml = MergeTemplateWithJson(contentHtml, mainTemplate.json);
        }

        // Pre-merge all templates and JSON values
        var mergedTemplates = new Dictionary<string, string>(templates.Count);
        var allJsonValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in templates)
        {
            var htmlContent = kvp.Value.html;
            var jsonContent = kvp.Value.json;
            if (enableJsonProcessing && !string.IsNullOrEmpty(jsonContent))
            {
                htmlContent = MergeTemplateWithJson(htmlContent, jsonContent);
                try
                {
                    var jsonObj = JsonConverter.ParseJsonString(jsonContent);
                    foreach (var jsonKvp in jsonObj)
                    {
                        if (jsonKvp.Value is string s)
                        {
                            allJsonValues[jsonKvp.Key] = s;
                        }
                    }
                }
                catch { }
            }
            mergedTemplates[kvp.Key] = htmlContent;
        }

        // Simple loop like Go implementation - avoid StringBuilder overhead
        string previous;
        int maxPasses = 10;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            previous = contentHtml;
            contentHtml = MergeTemplateSlots(contentHtml, appSite, appView, mergedTemplates);
            contentHtml = ReplaceTemplatePlaceholdersWithJson(contentHtml, appSite, mergedTemplates, allJsonValues, appView);
            if (contentHtml == previous) break;
        }
        return contentHtml;

    }

    /// <summary>
    /// Retrieves template HTML from the merged templates dictionary (optimized version)
    /// </summary>
    private string? GetTemplate(string appSite, string templateName, Dictionary<string, string> mergedTemplates, string? appView = null, bool useAppViewFallback = true)
    {
        if (mergedTemplates == null || mergedTemplates.Count == 0)
            return null;

        var primaryTemplateKey = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if (useAppViewFallback && !string.IsNullOrEmpty(appView) && !string.IsNullOrEmpty(AppViewPrefix) &&
            templateName.Contains(AppViewPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Direct replacement: Replace the AppViewPrefix with the AppView value
            var appKey = TemplateUtils.ReplaceCaseInsensitive(templateName, AppViewPrefix, appView);
            var fallbackTemplateKey = $"{appSite.ToLowerInvariant()}_{appKey.ToLowerInvariant()}";
            if (mergedTemplates.TryGetValue(fallbackTemplateKey, out var fallbackTemplate))
            {
                return fallbackTemplate;
            }
        }
        
        // SECOND: If no AppView-specific template found, try primary template
        if (mergedTemplates.TryGetValue(primaryTemplateKey, out var primaryTemplate))
        {
            return primaryTemplate;
        }
        
        return null;
    }

    // New helper: replaces placeholders using both templates and JSON values
    private string ReplaceTemplatePlaceholdersWithJson(string html, string appSite, Dictionary<string, string> htmlFiles, Dictionary<string, string> jsonValues, string? appView = null)
    {
        var result = html;
        var searchPos = 0;

        while (searchPos < result.Length)
        {
            // Look for opening placeholder {{
            var openStart = result.IndexOf("{{", searchPos);
            if (openStart == -1) break;

            // Make sure it's not a slotted template or special placeholder
            if (openStart + 2 < result.Length && (result[openStart + 2] == '#' || result[openStart + 2] == '@' || result[openStart + 2] == '$' || result[openStart + 2] == '/'))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            var closeStart = result.IndexOf("}}", openStart + 2);
            if (closeStart == -1) break;

            // Extract placeholder name
            var placeholderName = result.Substring(openStart + 2, closeStart - openStart - 2).Trim();
            if (string.IsNullOrEmpty(placeholderName) || !TemplateUtils.IsAlphaNumeric(placeholderName))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Look up replacement in templates - use GetTemplate method for optimized lookup
            var templateContent = GetTemplate(appSite, placeholderName, htmlFiles, appView, useAppViewFallback: true);

            string? processedReplacement = null;

            if (!string.IsNullOrEmpty(templateContent))
            {
                processedReplacement = ReplaceTemplatePlaceholdersWithJson(templateContent, appSite, htmlFiles, jsonValues ?? new Dictionary<string, string>(), appView);
            }
            // If template not found, try JSON value
            else if (jsonValues != null && jsonValues.TryGetValue(placeholderName, out var jsonValue))
            {
                processedReplacement = jsonValue;
            }

            if (processedReplacement != null)
            {
                var placeholder = result.Substring(openStart, closeStart + 2 - openStart);
                result = result.Replace(placeholder, processedReplacement);
                searchPos = openStart + processedReplacement.Length;
            }
            else
            {
                searchPos = closeStart + 2;
            }
        }

        return result;
    }

    #endregion

    #region Slot Processing

    /// <summary>
    /// IndexOf-based version: Recursively merges a slotted template (e.g., center.html, columns.html) with content.html
    /// Slot patterns in content.html: {{#TemplateName}} ... {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}} ... {{/TemplateName}}
    /// </summary>
    /// <param name="contentHtml">The content HTML containing slot patterns</param>
    /// <param name="appSite">The application site name for template key generation</param>
    /// <param name="templates">Dictionary of available templates</param>
    /// <returns>Merged HTML with slots filled</returns>
    private string MergeTemplateSlots(string contentHtml, string appSite, string? appView, Dictionary<string, string> templates)
    {
        if (string.IsNullOrEmpty(contentHtml) || templates == null || templates.Count == 0)
            return contentHtml;

        string previous;
        do
        {
            previous = contentHtml;
            contentHtml = ProcessTemplateSlots(contentHtml, appSite, appView, templates);
        } while (contentHtml != previous);
        return contentHtml;
    }

    /// <summary>
    /// Helper method to process slotted templates using IndexOf
    /// </summary>
    private string ProcessTemplateSlots(string contentHtml, string appSite, string? appView, Dictionary<string, string> templates)
    {
        var result = contentHtml;
        var searchPos = 0;

        while (searchPos < result.Length)
        {
            // Look for opening tag {{#
            var openStart = result.IndexOf("{{#", searchPos);
            if (openStart == -1) break;

            // Find the end of the template name
            var openEnd = result.IndexOf("}}", openStart + 3);
            if (openEnd == -1) break;

            // Extract template name
            var templateName = result.Substring(openStart + 3, openEnd - openStart - 3).Trim();
            if (string.IsNullOrEmpty(templateName) || !TemplateUtils.IsAlphaNumeric(templateName))
            {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            var closeTag = "{{/" + templateName + "}}";
            var closeStart = TemplateUtils.FindMatchingCloseTag(result, openEnd + 2, "{{#" + templateName + "}}", closeTag);
            if (closeStart == -1)
            {
                searchPos = openStart + 1;
                continue;
            }

            // Extract inner content
            var innerStart = openEnd + 2;
            var innerContent = result.Substring(innerStart, closeStart - innerStart);

            // Process the template replacement using the optimized GetTemplate method
            var templateHtml = GetTemplate(appSite, templateName, templates, appView, useAppViewFallback: true);

            if (!string.IsNullOrEmpty(templateHtml))
            {
                // Extract slot contents
                var slotContents = ExtractSlotContents(innerContent, appSite, appView, templates);

                // Replace slots in template
                var processedTemplate = templateHtml;
                foreach (var kvp in slotContents)
                {
                    processedTemplate = processedTemplate.Replace(kvp.Key, kvp.Value);
                }

                // Remove any remaining slot placeholders
                processedTemplate = TemplateUtils.RemoveRemainingSlotPlaceholders(processedTemplate);

                // Replace the entire slotted section
                var fullMatch = result.Substring(openStart, closeStart + closeTag.Length - openStart);
                result = result.Replace(fullMatch, processedTemplate);
                searchPos = openStart + processedTemplate.Length;
            }
            else
            {
                searchPos = openStart + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Extract slot contents using IndexOf approach
    /// </summary>
    private Dictionary<string, string> ExtractSlotContents(string innerContent, string appSite, string? appView, Dictionary<string, string> templates)
    {
        var slotContents = new Dictionary<string, string>();
        var searchPos = 0;

        while (searchPos < innerContent.Length)
        {
            // Look for slot start {{@HTMLPLACEHOLDER
            var slotStart = innerContent.IndexOf("{{@HTMLPLACEHOLDER", searchPos);
            if (slotStart == -1) break;

            // Find the number (if any) and closing }}
            var afterPlaceholder = slotStart + 18; // Length of "{{@HTMLPLACEHOLDER"
            var slotNum = "";
            var pos = afterPlaceholder;

            // Extract slot number
            while (pos < innerContent.Length && char.IsDigit(innerContent[pos]))
            {
                slotNum += innerContent[pos];
                pos++;
            }

            // Check for closing }}
            if (pos + 1 >= innerContent.Length || innerContent.Substring(pos, 2) != "}}")
            {
                searchPos = slotStart + 1;
                continue;
            }

            var slotOpenEnd = pos + 2;

            // Find matching closing tag
            var closeTag = string.IsNullOrEmpty(slotNum) ? "{{/HTMLPLACEHOLDER}}" : $"{{{{/HTMLPLACEHOLDER{slotNum}}}}}";
            var openTag = string.IsNullOrEmpty(slotNum) ? "{{@HTMLPLACEHOLDER}}" : $"{{{{@HTMLPLACEHOLDER{slotNum}}}}}";

            var closeStart = TemplateUtils.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag);
            if (closeStart == -1)
            {
                searchPos = slotStart + 1;
                continue;
            }

            // Extract slot content
            var slotContent = innerContent.Substring(slotOpenEnd, closeStart - slotOpenEnd);

            // Generate slot key
            var slotKey = string.IsNullOrEmpty(slotNum) ? "{{$HTMLPLACEHOLDER}}" : $"{{{{$HTMLPLACEHOLDER{slotNum}}}}}";

            // FIXED: Process both slotted templates AND simple placeholders in slot content
            // This enables proper nested template processing to match the preprocessing implementation
            var recursiveResult = MergeTemplateSlots(slotContent, appSite, appView, templates);
            recursiveResult = ReplaceTemplatePlaceholders(recursiveResult, appSite, appView, templates);
            slotContents[slotKey] = recursiveResult;

            searchPos = closeStart + closeTag.Length;
        }

        return slotContents;
    }

    #endregion

    #region PlaceHolder Processing

    /// <summary>
    /// Helper method to process simple placeholders only (without slotted template processing)
    /// </summary>
    private string ReplaceTemplatePlaceholders(string html, string appSite, string? appView, Dictionary<string, string> htmlFiles)
    {
        var result = html;
        var searchPos = 0;

        // Try to get JSON values from the main template if available
        Dictionary<string, string>? jsonValues = null;
        if (htmlFiles.TryGetValue("__json_values__", out var jsonRaw) && !string.IsNullOrEmpty(jsonRaw))
        {
            // Parse as key=value pairs separated by newlines (custom format for this fix)
            jsonValues = jsonRaw.Split('\n')
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        }

        while (searchPos < result.Length)
        {
            // Look for opening placeholder {{
            var openStart = result.IndexOf("{{", searchPos);
            if (openStart == -1) break;

            // Make sure it's not a slotted template or special placeholder
            if (openStart + 2 < result.Length && (result[openStart + 2] == '#' || result[openStart + 2] == '@' || result[openStart + 2] == '$' || result[openStart + 2] == '/'))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            var closeStart = result.IndexOf("}}", openStart + 2);
            if (closeStart == -1) break;

            // Extract placeholder name
            var placeholderName = result.Substring(openStart + 2, closeStart - openStart - 2).Trim();
            if (string.IsNullOrEmpty(placeholderName) || !TemplateUtils.IsAlphaNumeric(placeholderName))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Look up replacement in templates using the optimized GetTemplate method
            var templateContent2 = GetTemplate(appSite, placeholderName, htmlFiles, appView, useAppViewFallback: true);

            string? processedReplacement = null;
            if (!string.IsNullOrEmpty(templateContent2))
            {
                processedReplacement = ReplaceTemplatePlaceholders(templateContent2, appSite, appView, htmlFiles);
            }
            // If not found, try JSON value
            else if (jsonValues != null && jsonValues.TryGetValue(placeholderName, out var jsonValue))
            {
                processedReplacement = jsonValue;
            }

            if (processedReplacement != null)
            {
                var placeholder = result.Substring(openStart, closeStart + 2 - openStart);
                result = result.Replace(placeholder, processedReplacement);
                searchPos = openStart + processedReplacement.Length;
            }
            else
            {
                searchPos = closeStart + 2;
            }
        }

        return result;
    }

    #endregion

    #region Json Processing

    /// <summary>
    /// Merges HTML template with JSON data using placeholder replacement
    /// </summary>
    /// <param name="template">The HTML template content</param>
    /// <param name="jsonText">The JSON data as string</param>
    /// <returns>Merged HTML with JSON data populated</returns>
    private static string MergeTemplateWithJson(string template, string jsonText)
    {
        // Parse JSON using JsonConverter
        var jsonObject = JsonConverter.ParseJsonString(jsonText);
        
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Convert JsonObject to dictionary
        foreach (var kvp in jsonObject)
        {
            if (kvp.Value is JsonArray jsonArray)
            {
                // Convert JsonArray to List<Dictionary<string, object?>>
                var arr = new List<Dictionary<string, object?>>();
                foreach (var item in jsonArray)
                {
                    if (item is JsonObject jsonObj)
                    {
                        var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var subKvp in jsonObj)
                        {
                            obj[subKvp.Key] = subKvp.Value;
                        }
                        arr.Add(obj);
                    }
                    else
                    {
                        // Handle array of simple values
                        var simpleObj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        simpleObj["Value"] = item;
                        arr.Add(simpleObj);
                    }
                }
                dict[kvp.Key] = arr;
            }
            else
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        // Advanced merge logic for block and conditional patterns
        string result = template;

        // Instead of finding all array tags first, directly match JSON array keys to template blocks
        foreach (var jsonKey in dict.Keys)
        {
            if (dict[jsonKey] is List<Dictionary<string, object?>> dataList)
            {
                // Try to find a matching template block for this JSON array
                var keyNorm = jsonKey.ToLowerInvariant();

                // Look for possible template tags that match this JSON key
                var possibleTags = new[] { jsonKey, jsonKey.ToLowerInvariant(), keyNorm.TrimEnd('s'), keyNorm + "s" };

                foreach (var tag in possibleTags)
                {
                    string blockStartTag = "{{@" + tag + "}}";
                    string blockEndTag = "{{/" + tag + "}}";

                    int startIdx = result.IndexOf(blockStartTag, StringComparison.OrdinalIgnoreCase);
                    if (startIdx != -1)
                    {
                        int searchFrom = startIdx + blockStartTag.Length;
                        int endIdx = result.IndexOf(blockEndTag, searchFrom, StringComparison.OrdinalIgnoreCase);

                        if (endIdx != -1 && endIdx > startIdx)
                        {
                            // Found a valid block - process it
                            int contentStartIdx = startIdx + blockStartTag.Length;
                            if (contentStartIdx <= endIdx)
                            {
                                string blockContent = result.Substring(contentStartIdx, endIdx - contentStartIdx);
                                string mergedBlock = "";

                                // Find all conditional blocks in the template block (e.g., {{@Key}}...{{/Key}})
                                var conditionalKeys = new HashSet<string>();
                                int condIdx = 0;
                                while (true)
                                {
                                    int condStart = blockContent.IndexOf("{{@", condIdx, StringComparison.OrdinalIgnoreCase);
                                    if (condStart == -1) break;
                                    int condEnd = blockContent.IndexOf("}}", condStart, StringComparison.OrdinalIgnoreCase);
                                    if (condEnd == -1) break;
                                    string condKey = blockContent.Substring(condStart + 3, condEnd - (condStart + 3)).Trim();
                                    conditionalKeys.Add(condKey);
                                    condIdx = condEnd + 2;
                                }

                                foreach (var item in dataList)
                                {
                                    string itemBlock = blockContent;

                                    // Replace all placeholders dynamically
                                    foreach (var kvp in item)
                                    {
                                        string placeholder = "{{$" + kvp.Key + "}}";
                                        string valueStr = kvp.Value != null ? kvp.Value.ToString() ?? string.Empty : string.Empty;
                                        itemBlock = ReplaceAllCaseInsensitive(itemBlock, placeholder, valueStr);
                                    }

                                    // Handle all conditional blocks dynamically
                                    foreach (var condKey in conditionalKeys)
                                    {
                                        bool condValue = false;
                                        if (item.TryGetValue(condKey, out var condObj) && condObj != null)
                                        {
                                            if (condObj is bool b)
                                                condValue = b;
                                            else if (condObj is string s && bool.TryParse(s, out bool sb))
                                                condValue = sb;
                                            else if (condObj is int i)
                                                condValue = i != 0;
                                        }
                                        itemBlock = HandleConditional(itemBlock, condKey, condValue);
                                    }
                                    mergedBlock += itemBlock;
                                }

                                // Replace block in result
                                result = result.Substring(0, startIdx) + mergedBlock + result.Substring(endIdx + blockEndTag.Length);
                                break; // Process only the first matching template for this JSON key
                            }
                        }
                    }
                }
            }
        }

        // Handle {{^ArrayName}} block if array is empty (dynamic detection)
        foreach (var key in dict.Keys)
        {
            string emptyBlockStart = "{{^" + key + "}}";
            string emptyBlockEnd = "{{/" + key + "}}";
            int emptyStartIdx = result.IndexOf(emptyBlockStart, StringComparison.OrdinalIgnoreCase);
            int emptyEndIdx = result.IndexOf(emptyBlockEnd, StringComparison.OrdinalIgnoreCase);
            if (emptyStartIdx != -1 && emptyEndIdx != -1 && dict[key] is List<Dictionary<string, object?>> l)
            {
                bool isEmpty = l.Count == 0;
                string emptyContent = result.Substring(emptyStartIdx + emptyBlockStart.Length, emptyEndIdx - (emptyStartIdx + emptyBlockStart.Length));
                result = isEmpty
                    ? result.Substring(0, emptyStartIdx) + emptyContent + result.Substring(emptyEndIdx + emptyBlockEnd.Length)
                    : result.Substring(0, emptyStartIdx) + result.Substring(emptyEndIdx + emptyBlockEnd.Length);
            }
        }

        // Replace remaining simple placeholders
        foreach (var kvp in dict)
        {
            if (kvp.Value is string s)
            {
                string placeholder = "{{$" + kvp.Key + "}}";
                result = ReplaceAllCaseInsensitive(result, placeholder, s);
            }
        }

        return result;
    }

    /// <summary>
    /// Helper: Replace all case-insensitive occurrences
    /// </summary>
    private static string ReplaceAllCaseInsensitive(string input, string search, string replacement)
    {
        int idx = 0;
        while (true)
        {
            int found = input.IndexOf(search, idx, StringComparison.OrdinalIgnoreCase);
            if (found == -1) break;
            input = input.Substring(0, found) + replacement + input.Substring(found + search.Length);
            idx = found + replacement.Length;
        }
        return input;
    }

    /// <summary>
    /// Helper: Handle conditional blocks like {{@Selected}}...{{/Selected}}
    /// </summary>
    private static string HandleConditional(string input, string key, bool condition)
    {
        // Support spaces inside block tags, e.g. {{@Selected}} ... {{ /Selected}}
        string condStart = "{{@" + key + "}}";
        string condEnd = "{{ /" + key + "}}";
        int startIdx = input.IndexOf(condStart, StringComparison.OrdinalIgnoreCase);
        int endIdx = input.IndexOf(condEnd, StringComparison.OrdinalIgnoreCase);
        while (startIdx != -1 && endIdx != -1)
        {
            string content = input.Substring(startIdx + condStart.Length, endIdx - (startIdx + condStart.Length));
            input = condition
                ? input.Substring(0, startIdx) + content + input.Substring(endIdx + condEnd.Length)
                : input.Substring(0, startIdx) + input.Substring(endIdx + condEnd.Length);
            startIdx = input.IndexOf(condStart, StringComparison.OrdinalIgnoreCase);
            endIdx = input.IndexOf(condEnd, StringComparison.OrdinalIgnoreCase);
        }
        // Also handle without space: {{/Selected}}
        condEnd = "{{/" + key + "}}";
        startIdx = input.IndexOf(condStart, StringComparison.OrdinalIgnoreCase);
        endIdx = input.IndexOf(condEnd, StringComparison.OrdinalIgnoreCase);
        while (startIdx != -1 && endIdx != -1)
        {
            string content = input.Substring(startIdx + condStart.Length, endIdx - (startIdx + condStart.Length));
            input = condition
                ? input.Substring(0, startIdx) + content + input.Substring(endIdx + condEnd.Length)
                : input.Substring(0, startIdx) + input.Substring(endIdx + condEnd.Length);
            startIdx = input.IndexOf(condStart, StringComparison.OrdinalIgnoreCase);
            endIdx = input.IndexOf(condEnd, StringComparison.OrdinalIgnoreCase);
        }
        return input;
    }

    #endregion
}