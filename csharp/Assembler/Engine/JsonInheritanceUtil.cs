using System;
using System.Collections.Generic;
using Arshu.App.Json;
using Arshu.Common;

namespace Assembler.Engine;

using JsonObject = Arshu.App.Json.JsonObject;

/// <summary>
/// Utility class for resolving JSON key inheritance from parent templates
/// Inheritance follows a tree structure: parent -> child, not siblings
/// </summary>
public static class JsonInheritanceUtil
{
    /// <summary>
    /// Resolves a JSON key with inheritance support
    /// If the key ends with #, searches up the parent tree for the key without #
    /// </summary>
    /// <param name="jsonKey">The JSON key (may end with #)</param>
    /// <param name="currentTemplateKey">Current template key (e.g., "jsonruleflow1a_languagelinks")</param>
    /// <param name="allTemplates">Dictionary of all templates with their JSON data</param>
    /// <param name="parentMap">Map of child template key to parent template key</param>
    /// <returns>Resolved value or the default value after # or empty string</returns>
    public static string? ResolveJsonKeyWithInheritance(
        string jsonKey,
        string currentValue,
        string currentTemplateKey,
        Dictionary<string, (string html, string? json)> allTemplates,
        Dictionary<string, string> parentMap)
    {
        // If key doesn't end with #, no inheritance - return current value
        if (!jsonKey.EndsWith("#"))
        {
            return currentValue;
        }

        // Extract the actual key name without the # suffix
        var actualKey = jsonKey.Substring(0, jsonKey.Length - 1);

        Logger.Debug($"Resolving inherited key: {jsonKey} -> {actualKey} for template {currentTemplateKey}", "JsonInheritance");

        // Search up the parent tree for the key
        var inheritedValue = SearchParentTreeForKey(actualKey, currentTemplateKey, allTemplates, parentMap);

        if (inheritedValue != null)
        {
            Logger.Debug($"Found inherited value for {actualKey}: {inheritedValue}", "JsonInheritance");
            return inheritedValue;
        }

        // If not found in parents, use the current value as default
        Logger.Debug($"No inherited value found for {actualKey}, using default: {currentValue}", "JsonInheritance");
        return currentValue;
    }

    /// <summary>
    /// Searches up the parent tree to find a JSON key value
    /// </summary>
    private static string? SearchParentTreeForKey(
        string key,
        string currentTemplateKey,
        Dictionary<string, (string html, string? json)> allTemplates,
        Dictionary<string, string> parentMap)
    {
        // Get parent template key
        if (!parentMap.TryGetValue(currentTemplateKey, out var parentKey))
        {
            Logger.Debug($"No parent found for {currentTemplateKey}", "JsonInheritance");
            return null;
        }

        Logger.Debug($"Checking parent {parentKey} for key {key}", "JsonInheritance");

        // Get parent's JSON data
        if (!allTemplates.TryGetValue(parentKey, out var parentTemplate))
        {
            Logger.Debug($"Parent template {parentKey} not found in allTemplates", "JsonInheritance");
            return null;
        }

        if (string.IsNullOrEmpty(parentTemplate.json))
        {
            Logger.Debug($"Parent template {parentKey} has no JSON data, searching further up", "JsonInheritance");
            // Parent has no JSON, search further up the tree
            return SearchParentTreeForKey(key, parentKey, allTemplates, parentMap);
        }

        // Parse parent's JSON
        try
        {
            var parentJsonObj = JsonConverter.ParseJsonString(parentTemplate.json);

            // Look for the key (case-insensitive)
            foreach (var kvp in parentJsonObj)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (kvp.Value is string strValue)
                    {
                        Logger.Debug($"Found key {key} in parent {parentKey}: {strValue}", "JsonInheritance");
                        return strValue;
                    }
                }
            }

            Logger.Debug($"Key {key} not found in parent {parentKey}, searching further up", "JsonInheritance");
            // Not found in this parent, search further up the tree
            return SearchParentTreeForKey(key, parentKey, allTemplates, parentMap);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error parsing JSON for parent {parentKey}: {ex.Message}", "JsonInheritance");
            return null;
        }
    }

    /// <summary>
    /// Builds a parent map from template structure by analyzing placeholders
    /// This tracks which template is the parent of another based on {{TemplateName}} references
    /// </summary>
    public static Dictionary<string, string> BuildParentMap(
        string appSite,
        Dictionary<string, (string html, string? json)> allTemplates)
    {
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Logger.Debug($"Building parent map for appSite: {appSite}", "JsonInheritance");

        foreach (var kvp in allTemplates)
        {
            var templateKey = kvp.Key;
            var html = kvp.Value.html;

            // Find all {{TemplateName}} placeholders in this template
            var searchPos = 0;
            while (searchPos < html.Length)
            {
                var openStart = html.IndexOf("{{", searchPos);
                if (openStart == -1) break;

                // Skip special placeholders (#, @, $, /)
                if (openStart + 2 < html.Length &&
                    (html[openStart + 2] == '#' || html[openStart + 2] == '@' ||
                     html[openStart + 2] == '$' || html[openStart + 2] == '/'))
                {
                    searchPos = openStart + 2;
                    continue;
                }

                var closeStart = html.IndexOf("}}", openStart + 2);
                if (closeStart == -1) break;

                var placeholderName = html.Substring(openStart + 2, closeStart - openStart - 2).Trim();

                // Check if this is a valid alphanumeric template name
                if (!string.IsNullOrEmpty(placeholderName) && IsAlphaNumeric(placeholderName))
                {
                    // This template (templateKey) is the parent of the placeholder template
                    var childTemplateKey = $"{appSite.ToLowerInvariant()}_{placeholderName.ToLowerInvariant()}";

                    if (!parentMap.ContainsKey(childTemplateKey))
                    {
                        parentMap[childTemplateKey] = templateKey;
                        Logger.Debug($"Parent relationship: {childTemplateKey} -> parent: {templateKey}", "JsonInheritance");
                    }
                }

                searchPos = closeStart + 2;
            }
        }

        Logger.Debug($"Built parent map with {parentMap.Count} relationships", "JsonInheritance");
        return parentMap;
    }

    private static bool IsAlphaNumeric(string str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            if (!char.IsLetterOrDigit(str[i]))
                return false;
        }
        return true;
    }
}
