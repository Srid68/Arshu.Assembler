using Arshu.App.Json;
using Arshu.Common;
using Assembler.Common;
using Assembler.Interface;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Assembler.Loader.Normal;

/// <summary>
/// Loader implementation for EngineNormal
/// Handles loading and caching of HTML and JSON templates from the file system
/// Templates are kept immutable within the loader
/// </summary>
public class LoaderNormal : ILoaderNormal
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, (string html, string? json)>> _htmlTemplatesCache = new();
    private Dictionary<string, (string html, string? json)> _templates;
    private Dictionary<string, string> _parentMap;
    private string _appSite;

    #region Constructor

    /// <summary>
    /// Creates a new LoaderNormal instance (use Load() method to load templates)
    /// </summary>
    public LoaderNormal()
    {
        _templates = new Dictionary<string, (string html, string? json)>();
        _parentMap = new Dictionary<string, string>();
        SearchAppSites = string.Empty;
        _appSite = string.Empty;
    }

    /// <summary>
    /// Convenience constructor that automatically loads templates
    /// </summary>
    /// <param name="rootDirPath">Root directory path containing AppSites folder</param>
    /// <param name="appSite">Primary AppSite name to load</param>
    /// <param name="searchAppSites">Comma-delimited string of AppSite names for fallback templates</param>
    public LoaderNormal(string rootDirPath, string appSite, string searchAppSites) : this()
    {
        Load(rootDirPath, appSite, searchAppSites);
    }

    #endregion

    #region ILoaderNormal Interface

    #region Loading Related

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// </summary>
    public string SearchAppSites { get; private set; }

    /// <summary>
    /// Loads all templates from the specified directory
    /// Returns true if loading succeeded, false otherwise
    /// </summary>
    public bool Load(string rootDirPath, string appSite, string searchAppSites)
    {
        try
        {
            Logger.Debug($"Load called for appSite: {appSite}, searchAppSites: {searchAppSites}", "LoaderNormal");

            _appSite = appSite;
            SearchAppSites = searchAppSites;

            var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite + "|" + searchAppSites;
            if (_htmlTemplatesCache.TryGetValue(cacheKey, out var cached))
            {
                Logger.Debug($"Returning cached templates for {appSite} ({cached.Count} templates)", "LoaderNormal");
                _templates = cached;
                return true;
            }

            // Load templates from primary appSite
            _templates = LoadTemplatesFromSingleAppSite(rootDirPath, appSite);

            // Load templates from searchAppSites for fallback
            if (!string.IsNullOrEmpty(searchAppSites))
            {
                var searchAppSitesArray = searchAppSites.Split(',');
                for (int i = 0; i < searchAppSitesArray.Length; i++)
                {
                    var searchAppSite = searchAppSitesArray[i].Trim();
                    if (string.IsNullOrEmpty(searchAppSite))
                        continue;

                    var searchTemplates = LoadTemplatesFromSingleAppSite(rootDirPath, searchAppSite);
                    foreach (var kvp in searchTemplates)
                    {
                        // Only add if not already present (primary appSite takes precedence)
                        if (!_templates.ContainsKey(kvp.Key))
                        {
                            _templates[kvp.Key] = kvp.Value;
                            Logger.Debug($"Added fallback template '{kvp.Key}' from '{searchAppSite}'", "LoaderNormal");
                        }
                    }
                }
            }

            _htmlTemplatesCache.TryAdd(cacheKey, _templates);

            // Build parent-child relationship map for JSON inheritance
            _parentMap = BuildParentMap();
            Logger.Debug($"Built parent map with {_parentMap.Count} relationships for JSON inheritance", "LoaderNormal");

            Logger.Info($"Loaded {_templates.Count} templates for {appSite}", "LoaderNormal");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load templates: {ex.Message}", "LoaderNormal");
            return false;
        }
    }

    /// <summary>
    /// Checks if a template exists
    /// </summary>
    public bool HasTemplate(string appSite, string templateName)
    {
        var key = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        return _templates.ContainsKey(key);
    }

    /// <summary>
    /// Clears the template cache
    /// </summary>
    public void ClearCache()
    {
        _htmlTemplatesCache.Clear();
    }

    #endregion

    #region Merging Related

    /// <summary>
    /// Gets a template's HTML content by appSite and name with AppView fallback support
    /// </summary>
    public string? GetTemplateHtml(string appSite, string templateName, string? appView = null, string? appViewPrefix = null)
    {
        // Try AppView fallback first if provided
        if (!string.IsNullOrEmpty(appView) && !string.IsNullOrEmpty(appViewPrefix) &&
            templateName.Contains(appViewPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var appKey = CommonUtil.ReplaceCaseInsensitive(templateName, appViewPrefix, appView);
            var fallbackKey = $"{appSite.ToLowerInvariant()}_{appKey.ToLowerInvariant()}";

            if (_templates.TryGetValue(fallbackKey, out var appViewTemplate))
            {
                return appViewTemplate.html;
            }
        }

        // Try primary template key
        var key = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        if (_templates.TryGetValue(key, out var template))
        {
            return template.html;
        }

        // Try searchAppSites fallback
        if (!string.IsNullOrEmpty(SearchAppSites))
        {
            var searchAppSitesArray = SearchAppSites.Split(',');
            for (int i = 0; i < searchAppSitesArray.Length; i++)
            {
                var searchAppSite = searchAppSitesArray[i].Trim();
                if (string.IsNullOrEmpty(searchAppSite))
                    continue;

                var searchKey = $"{searchAppSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
                if (_templates.TryGetValue(searchKey, out var fallbackTemplate))
                {
                    Logger.Debug($"Template '{templateName}' not found in '{appSite}', using fallback from '{searchAppSite}'", "LoaderNormal");
                    return fallbackTemplate.html;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Merges HTML string with JSON data for the specified template
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
    /// Engines cannot access raw JSON - all JSON retrieval and merging happens inside the loader
    /// </summary>
    public string MergeHtmlWithJson(string html, string appSite, string templateName)
    {
        Logger.Debug($"MergeHtmlWithJson called: appSite={appSite}, templateName={templateName}", "LoaderNormal");

        if (string.IsNullOrEmpty(html))
            return html;

        // Get JSON with inheritance resolution
        var jsonData = GetTemplateJsonWithInheritance(appSite, templateName);

        if (jsonData == null)
        {
            Logger.Debug($"No JSON data found for {templateName}, returning original HTML", "LoaderNormal");
            return html;
        }

        var jsonKeys = string.Join(", ", jsonData.Keys);
        Logger.Debug($"Merging HTML with JSON for {templateName} (keys: {jsonKeys})", "LoaderNormal");
        return JsonMergeUtil.MergeTemplateWithJson(html, jsonData);
    }

    #endregion

    #endregion

    #region Loading Templates (Private)

    /// <summary>
    /// Loads templates from a single AppSite without caching or fallback logic
    /// </summary>
    private static Dictionary<string, (string html, string? json)> LoadTemplatesFromSingleAppSite(string rootDirPath, string appSite)
    {
        var result = new Dictionary<string, (string html, string? json)>();
        var appSitesPath = Path.Combine(rootDirPath, "AppSites", appSite);

        if (!Directory.Exists(appSitesPath))
        {
            Logger.Warn($"AppSites directory not found: {appSitesPath}", "LoaderNormal");
            return result;
        }

        Logger.Debug($"Loading templates from: {appSitesPath}", "LoaderNormal");

        foreach (var file in Directory.GetFiles(appSitesPath, "*.html", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var key = ($"{appSite.ToLowerInvariant()}_{fileName.ToLowerInvariant()}");
            var htmlContent = CommonUtil.NormalizeFileContent(File.ReadAllText(file));

            Logger.Debug($"Loading template: {key} (html size: {htmlContent.Length})", "LoaderNormal");

            // Find JSON file case-insensitively
            var jsonFile = Path.ChangeExtension(file, ".json");
            string? jsonContent = null;

            // Try exact match first
            if (File.Exists(jsonFile))
            {
                jsonContent = CommonUtil.NormalizeFileContent(File.ReadAllText(jsonFile));
                Logger.Debug($"Found JSON file for {key} (size: {jsonContent.Length})", "LoaderNormal");
            }
            else
            {
                // Try case-insensitive search in the same directory
                var directory = Path.GetDirectoryName(file);
                var baseFileName = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(directory))
                {
                    var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
                    string? matchingJson = null;
                    for (int i = 0; i < jsonFiles.Length; i++)
                    {
                        if (string.Equals(Path.GetFileNameWithoutExtension(jsonFiles[i]), baseFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            matchingJson = jsonFiles[i];
                            break;
                        }
                    }

                    if (matchingJson != null)
                    {
                        jsonContent = CommonUtil.NormalizeFileContent(File.ReadAllText(matchingJson));
                        Logger.Debug($"Found JSON file (case-insensitive) for {key} (size: {jsonContent.Length})", "LoaderNormal");
                    }
                }
            }

            result[key] = (htmlContent, jsonContent);
        }

        Logger.Debug($"Loaded {result.Count} templates for {appSite}", "LoaderNormal");
        return result;
    }

    #endregion

    #region JSON Inheritance Support (Private)

    // NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
    // Each loader/engine pair is independent to allow individual evolution without shared dependencies.
    // DO NOT extract these to shared utilities - that would create tight coupling.

    /// <summary>
    /// Builds a parent-child relationship map by analyzing template placeholders
    /// Tracks which template is the parent of another based on {{TemplateName}} references
    /// </summary>
    private Dictionary<string, string> BuildParentMap()
    {
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Logger.Debug($"Building parent map for appSite: {_appSite}", "LoaderNormal");

        foreach (var kvp in _templates)
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
                    var childTemplateKey = $"{_appSite.ToLowerInvariant()}_{placeholderName.ToLowerInvariant()}";

                    if (!parentMap.ContainsKey(childTemplateKey))
                    {
                        parentMap[childTemplateKey] = templateKey;
                        Logger.Debug($"Parent relationship: {childTemplateKey} -> parent: {templateKey}", "LoaderNormal");
                    }
                }

                searchPos = closeStart + 2;
            }
        }

        Logger.Debug($"Built parent map with {parentMap.Count} relationships", "LoaderNormal");
        return parentMap;
    }

    /// <summary>
    /// Gets parsed JSON with inheritance resolution
    /// Resolves keys ending with # by searching up the parent tree
    /// </summary>
    private JsonObject? GetTemplateJsonWithInheritance(string appSite, string templateName)
    {
        var templateKey = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        Logger.Debug($"GetTemplateJsonWithInheritance: templateKey={templateKey}", "LoaderNormal");

        // Try to get JSON from primary appSite template
        string? jsonContent = null;
        if (_templates.TryGetValue(templateKey, out var template))
        {
            jsonContent = template.json;
        }
        else
        {
            // Try searchAppSites fallback
            if (!string.IsNullOrEmpty(SearchAppSites))
            {
                var searchAppSitesArray = SearchAppSites.Split(',');
                for (int i = 0; i < searchAppSitesArray.Length; i++)
                {
                    var searchAppSite = searchAppSitesArray[i].Trim();
                    if (string.IsNullOrEmpty(searchAppSite))
                        continue;

                    var searchKey = $"{searchAppSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
                    if (_templates.TryGetValue(searchKey, out var fallbackTemplate))
                    {
                        jsonContent = fallbackTemplate.json;
                        templateKey = searchKey; // Update key for inheritance resolution
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(jsonContent))
        {
            Logger.Debug($"No JSON found for templateKey={templateKey}", "LoaderNormal");
            return null;
        }

        // Parse JSON string
        JsonObject jsonObj;
        try
        {
            jsonObj = JsonConverter.ParseJsonString(jsonContent);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to parse JSON for {templateKey}: {ex.Message}", "LoaderNormal");
            return null;
        }

        var rawKeys = string.Join(", ", jsonObj.Keys);
        Logger.Debug($"Raw JSON keys for {templateKey}: {rawKeys}", "LoaderNormal");

        var resolvedJson = new JsonObject();

        // Process each JSON key and resolve inheritance
        foreach (var kvp in jsonObj)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            // Check if this is an inheritable key (ends with #)
            if (key.EndsWith("#") && value is string strValue)
            {
                // Resolve inherited value
                var actualKey = key.Substring(0, key.Length - 1);
                Logger.Debug($"Found inheritance key: {key}, defaultValue={strValue}, resolving for actualKey={actualKey}", "LoaderNormal");
                var resolvedValue = ResolveJsonKeyWithInheritance(actualKey, strValue, templateKey);
                if (resolvedValue != null)
                {
                    resolvedJson[actualKey] = resolvedValue;
                    Logger.Debug($"Resolved inherited key {key} -> {actualKey} = {resolvedValue}", "LoaderNormal");
                    continue;
                }
            }

            // Normal key - keep as is
            resolvedJson[key] = value;
        }

        return resolvedJson;
    }

    /// <summary>
    /// Resolves a JSON key by searching up the parent tree
    /// </summary>
    private string? ResolveJsonKeyWithInheritance(string actualKey, string defaultValue, string currentTemplateKey)
    {
        Logger.Debug($"Resolving inherited key: {actualKey} for template {currentTemplateKey}", "LoaderNormal");

        // Search up the parent tree for the key
        var inheritedValue = SearchParentTreeForKey(actualKey, currentTemplateKey);

        if (inheritedValue != null)
        {
            Logger.Debug($"Found inherited value for {actualKey}: {inheritedValue}", "LoaderNormal");
            return inheritedValue;
        }

        // If not found in parents, use the default value
        Logger.Debug($"No inherited value found for {actualKey}, using default: {defaultValue}", "LoaderNormal");
        return defaultValue;
    }

    /// <summary>
    /// Searches up the parent tree to find a JSON key value
    /// </summary>
    private string? SearchParentTreeForKey(string key, string currentTemplateKey)
    {
        // Get parent template key
        if (!_parentMap.TryGetValue(currentTemplateKey, out var parentKey))
        {
            Logger.Debug($"No parent found for {currentTemplateKey}", "LoaderNormal");
            return null;
        }

        Logger.Debug($"Checking parent {parentKey} for key {key}", "LoaderNormal");

        // Get parent's template
        if (!_templates.TryGetValue(parentKey, out var parentTemplate))
        {
            Logger.Debug($"Parent template {parentKey} not found in templates", "LoaderNormal");
            return null;
        }

        if (string.IsNullOrEmpty(parentTemplate.json))
        {
            Logger.Debug($"Parent template {parentKey} has no JSON data, searching further up", "LoaderNormal");
            // Parent has no JSON, search further up the tree
            return SearchParentTreeForKey(key, parentKey);
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
                        Logger.Debug($"Found key {key} in parent {parentKey}: {strValue}", "LoaderNormal");
                        return strValue;
                    }
                }
            }

            Logger.Debug($"Key {key} not found in parent {parentKey}, searching further up", "LoaderNormal");
            // Not found in this parent, search further up the tree
            return SearchParentTreeForKey(key, parentKey);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error parsing JSON for parent {parentKey}: {ex.Message}", "LoaderNormal");
            return null;
        }
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

    #endregion
}
