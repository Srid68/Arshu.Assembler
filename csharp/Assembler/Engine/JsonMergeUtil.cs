using System;
using System.Collections.Generic;
using Arshu.App.Json;

namespace Assembler.Engine;

/// <summary>
/// Shared JSON merging utilities for all loaders
/// Moved from EngineNormal to centralize JSON processing logic
/// </summary>
public static class JsonMergeUtil
{
    /// <summary>
    /// Merges HTML template with JSON data using placeholder replacement
    /// Handles:
    /// - Array blocks: {{@array}}...{{/array}}
    /// - Conditional blocks: {{@condition}}...{{/condition}}
    /// - Empty array blocks: {{^array}}...{{/array}}
    /// - Simple placeholders: {{$key}}
    /// </summary>
    /// <param name="template">The HTML template content</param>
    /// <param name="jsonObject">The parsed JsonObject</param>
    /// <returns>Merged HTML with JSON data populated</returns>
    public static string MergeTemplateWithJson(string template, JsonObject jsonObject)
    {
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

        // Process JSON arrays - match JSON array keys to template blocks
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
                                        string valueStr = kvp.Value switch
                                        {
                                            bool b => b ? "true" : "false",
                                            null => string.Empty,
                                            _ => kvp.Value.ToString() ?? string.Empty
                                        };
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
            string? valueStr = kvp.Value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                int i => i.ToString(),
                double d => d.ToString(),
                _ => kvp.Value?.ToString()
            };

            if (valueStr != null)
            {
                string placeholder = "{{$" + kvp.Key + "}}";
                result = ReplaceAllCaseInsensitive(result, placeholder, valueStr);
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
}
