using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Arshu.App.Json;
using Arshu.Common;
using Assembler.Common;
using Assembler.Interface;

namespace Assembler.Loader.NormalJson;

/// <summary>
/// Loader that implements ILoaderJson<string> for Normal engine
/// Loads templates with JsonObject for type safety
/// </summary>
public class LoaderNormalJson : ILoaderJson<string>
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, (string html, JsonObject? json)>> _htmlTemplatesCache = new();
    private Dictionary<string, (string html, JsonObject? json)> _templates;
    private Dictionary<string, string> _parentMap;
    private string _appSite;

    #region Constructor

    /// <summary>
    /// Creates a new loader instance (use Load() method to load templates)
    /// </summary>
    public LoaderNormalJson()
    {
        _templates = new Dictionary<string, (string html, JsonObject? json)>();
        _parentMap = new Dictionary<string, string>();
        _appSite = string.Empty;
        SearchAppSites = string.Empty;
    }

    /// <summary>
    /// Convenience constructor that automatically loads templates
    /// </summary>
    /// <param name="rootDirPath">Root directory path containing AppSites folder</param>
    /// <param name="appSites">Primary AppSite name to load</param>
    /// <param name="searchAppSites">Comma-delimited string of AppSite names to search for fallback templates (can be empty string)</param>
    public LoaderNormalJson(string rootDirPath, string appSites, string searchAppSites) : this()
    {
        Load(rootDirPath, appSites, searchAppSites);
    }

    #endregion

    #region ILoaderJson Interface

    #region Loading Related

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    public string SearchAppSites { get; private set; }

    /// <summary>
    /// Loads and caches all templates from the specified directory
    /// Returns true if loading succeeded, false otherwise
    /// </summary>
    public bool Load(string rootDirPath, string appSite, string searchAppSites)
    {
        try
        {
            SearchAppSites = searchAppSites;
            _appSite = appSite;

            // Load templates from primary appSite
            _templates = LoadGetTemplateFiles(rootDirPath, appSite);

            // Load templates from searchAppSites for fallback
            if (!string.IsNullOrEmpty(SearchAppSites))
            {
                var searchAppSitesArray = SearchAppSites.Split(',');
                for (int i = 0; i < searchAppSitesArray.Length; i++)
                {
                    var searchAppSiteItem = searchAppSitesArray[i].Trim();
                    if (string.IsNullOrEmpty(searchAppSiteItem))
                        continue;

                    var searchTemplates = LoadGetTemplateFiles(rootDirPath, searchAppSiteItem);
                    foreach (var kvp in searchTemplates)
                    {
                        // Only add if not already present (primary appSite takes precedence)
                        if (!_templates.ContainsKey(kvp.Key))
                        {
                            _templates[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            // Build parent-child relationship map for JSON inheritance
            _parentMap = BuildParentMap();
            Logger.Debug($"Built parent map with {_parentMap.Count} relationships for JSON inheritance", "LoaderNormalJson");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load templates: {ex.Message}", "LoaderNormalJson");
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

    /// <summary>
    /// Gets all templates as a serialized JSON string for client-side template engine
    /// This is specifically for /api/templates endpoint
    /// Returns a JSON string - templates remain immutable (no references to internal state)
    /// </summary>
    public string GetAllTemplatesJson()
    {
        var templatesData = new Dictionary<string, object>();

        foreach (var kvp in _templates)
        {
            var templateKey = kvp.Key;
            var template = kvp.Value;

            // Create a serializable structure for this template
            var templateData = new Dictionary<string, object?>
            {
                ["html"] = template.html,
                ["json"] = template.json // JsonObject is already serializable
            };

            templatesData[templateKey] = templateData;
        }

        // Serialize to JSON string using Arshu.App.Json
        return Arshu.App.Json.JsonConverter.SerializeObject(templatesData);
    }

    #endregion

    #region Merging Related

    /// <summary>
    /// Gets a template's HTML content by appSite and name with AppView fallback
    /// Returns HTML only (no JSON)
    /// </summary>
    public string? GetTemplateHtml(string appSite, string templateName, string? appView = null, string? appViewPrefix = null)
    {
        var template = GetTemplateInternal(appSite, templateName, appView, appViewPrefix);
        return template?.html;
    }

    /// <summary>
    /// Merges HTML string with JSON data using inheritance-aware JSON retrieval
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// </summary>
    public string MergeHtmlWithJson(string html, string appSite, string templateName)
    {
        Logger.Debug($"MergeHtmlWithJson called: appSite={appSite}, templateName={templateName}", "LoaderNormalJson");

        if (string.IsNullOrEmpty(html))
            return html;

        // Get JSON with inheritance resolution
        var jsonData = GetTemplateJsonWithInheritance(appSite, templateName);

        if (jsonData == null)
        {
            Logger.Debug($"No JSON data found for {templateName}, returning original HTML", "LoaderNormalJson");
            return html;
        }

        var jsonKeys = string.Join(", ", jsonData.Keys);
        Logger.Debug($"Merging HTML with JSON for {templateName} (keys: {jsonKeys})", "LoaderNormalJson");
        return JsonMergeUtil.MergeTemplateWithJson(html, jsonData);
    }

    #endregion

    #endregion

    #region To Check

    /// <summary>
    /// Gets all templates for API serialization
    /// Returns a NEW dictionary with copies of template data - does not expose internal state
    /// This is for API endpoints that need to build complex responses
    /// </summary>
    public Dictionary<string, (string html, string? jsonString)> GetAllTemplatesForSerialization()
    {
        var result = new Dictionary<string, (string html, string? jsonString)>();

        foreach (var kvp in _templates)
        {
            var html = kvp.Value.html;
            var jsonString = kvp.Value.json != null
                ? Arshu.App.Json.JsonConverter.SerializeObject(kvp.Value.json)
                : null;

            result[kvp.Key] = (html, jsonString);
        }

        return result;
    }

    /// <summary>
    /// Applies all replacement mappings from all templates to the given content
    /// This method is NOT supported for Normal loaders - only for PreProcess architecture
    /// Normal loaders use on-demand template loading during the engine merge process
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown - method not supported for Normal loaders</exception>
    public string ApplyAllReplacementMappings(string content, string appSite, string? mainTemplate, string? appView, string? appViewPrefix, bool enableJsonProcessing)
    {
        throw new NotSupportedException("ApplyAllReplacementMappings is only supported for PreProcess loaders. NormalJson loader uses on-demand template loading during the engine merge process.");
    }

    #endregion

    #region Loading Templates (Private)

    /// <summary>
    /// Internal helper method with AppView fallback logic and SearchAppSites support
    /// This implements the template resolution strategy used by the engines
    /// </summary>
    private (string html, JsonObject? json)? GetTemplateInternal(string appSite, string templateName, string? appView, string? appViewPrefix)
    {
        // Try AppView fallback first if provided
        if (!string.IsNullOrEmpty(appView) && !string.IsNullOrEmpty(appViewPrefix) &&
            templateName.Contains(appViewPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var appKey = CommonUtil.ReplaceCaseInsensitive(templateName, appViewPrefix, appView);
            var fallbackKey = $"{appSite.ToLowerInvariant()}_{appKey.ToLowerInvariant()}";

            if (_templates.TryGetValue(fallbackKey, out var fallbackTemplate))
                return fallbackTemplate;
        }

        // Try primary template key
        var primaryKey = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        if (_templates.TryGetValue(primaryKey, out var template))
            return template;

        // Search in SearchAppSites as fallback
        if (!string.IsNullOrEmpty(SearchAppSites))
        {
            var searchAppSitesArray = SearchAppSites.Split(',');
            for (int i = 0; i < searchAppSitesArray.Length; i++)
            {
                var searchAppSite = searchAppSitesArray[i].Trim();
                if (string.IsNullOrEmpty(searchAppSite))
                    continue;

                var searchKey = $"{searchAppSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
                if (_templates.TryGetValue(searchKey, out var searchTemplate))
                {
                    Logger.Debug($"Template '{templateName}' not found in '{appSite}', using fallback from '{searchAppSite}'", "LoaderNormalJson");
                    return searchTemplate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Loads HTML files and corresponding JSON files from a single application site
    /// JSON is parsed to JsonObject immediately for type safety
    /// </summary>
    private static Dictionary<string, (string html, JsonObject? json)> LoadGetTemplateFiles(string rootDirPath, string appSite)
    {
        Logger.Debug($"LoadGetTemplateFiles called for appSite: {appSite}", "LoaderNormalJson");

        var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite;
        if (_htmlTemplatesCache.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug($"Returning cached templates for {appSite} ({cached.Count} templates)", "LoaderNormalJson");
            return cached;
        }

        var result = new Dictionary<string, (string html, JsonObject? json)>();
        var appSitesPath = Path.Combine(rootDirPath, "AppSites", appSite);

        if (!Directory.Exists(appSitesPath))
        {
            Logger.Warn($"AppSites directory not found: {appSitesPath}", "LoaderNormalJson");
            _htmlTemplatesCache.TryAdd(cacheKey, result);
            return result;
        }

        Logger.Debug($"Loading templates from: {appSitesPath}", "LoaderNormalJson");

        foreach (var file in Directory.GetFiles(appSitesPath, "*.html", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var key = ($"{appSite.ToLowerInvariant()}_{fileName.ToLowerInvariant()}");
            var htmlContent = CommonUtil.NormalizeFileContent(File.ReadAllText(file));

            Logger.Debug($"Loading template: {key} (html size: {htmlContent.Length})", "LoaderNormalJson");

            // Find and parse JSON file to JsonObject
            var jsonFile = Path.ChangeExtension(file, ".json");
            JsonObject? jsonObject = null;

            // Try exact match first
            if (File.Exists(jsonFile))
            {
                var jsonContent = CommonUtil.NormalizeFileContent(File.ReadAllText(jsonFile));
                if (!string.IsNullOrEmpty(jsonContent))
                {
                    try
                    {
                        jsonObject = JsonConverter.ParseJsonString(jsonContent);
                        Logger.Debug($"Found and parsed JSON file for {key} (size: {jsonContent.Length})", "LoaderNormalJson");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to parse JSON for {key}: {ex.Message}", "LoaderNormalJson");
                    }
                }
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
                        var jsonContent = CommonUtil.NormalizeFileContent(File.ReadAllText(matchingJson));
                        if (!string.IsNullOrEmpty(jsonContent))
                        {
                            try
                            {
                                jsonObject = JsonConverter.ParseJsonString(jsonContent);
                                Logger.Debug($"Found and parsed JSON file (case-insensitive) for {key} (size: {jsonContent.Length})", "LoaderNormalJson");
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to parse JSON for {key}: {ex.Message}", "LoaderNormalJson");
                            }
                        }
                    }
                }
            }

            result[key] = (htmlContent, jsonObject);
        }

        Logger.Debug($"Loaded {result.Count} templates for {appSite}", "LoaderNormalJson");

        _htmlTemplatesCache.TryAdd(cacheKey, result);
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

        Logger.Debug($"Building parent map for appSite: {_appSite}", "LoaderNormalJson");

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
                        Logger.Debug($"Parent relationship: {childTemplateKey} -> parent: {templateKey}", "LoaderNormalJson");
                    }
                }

                searchPos = closeStart + 2;
            }
        }

        Logger.Debug($"Built parent map with {parentMap.Count} relationships", "LoaderNormalJson");
        return parentMap;
    }

    /// <summary>
    /// Gets parsed JSON with inheritance resolution
    /// Resolves keys ending with # by searching up the parent tree
    /// </summary>
    private JsonObject? GetTemplateJsonWithInheritance(string appSite, string templateName)
    {
        var templateKey = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        Logger.Debug($"GetTemplateJsonWithInheritance: templateKey={templateKey}", "LoaderNormalJson");

        var template = GetTemplateInternal(appSite, templateName, null, null);
        if (template?.json == null)
        {
            Logger.Debug($"No JSON found for templateKey={templateKey}", "LoaderNormalJson");
            return null;
        }

        var jsonObj = template.Value.json;
        var rawKeys = string.Join(", ", jsonObj.Keys);
        Logger.Debug($"Raw JSON keys for {templateKey}: {rawKeys}", "LoaderNormalJson");

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
                Logger.Debug($"Found inheritance key: {key}, defaultValue={strValue}, resolving for actualKey={actualKey}", "LoaderNormalJson");
                var resolvedValue = ResolveJsonKeyWithInheritance(actualKey, strValue, templateKey);
                if (resolvedValue != null)
                {
                    resolvedJson[actualKey] = resolvedValue;
                    Logger.Debug($"Resolved inherited key {key} -> {actualKey} = {resolvedValue}", "LoaderNormalJson");
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
        Logger.Debug($"Resolving inherited key: {actualKey} for template {currentTemplateKey}", "LoaderNormalJson");

        // Search up the parent tree for the key
        var inheritedValue = SearchParentTreeForKey(actualKey, currentTemplateKey);

        if (inheritedValue != null)
        {
            Logger.Debug($"Found inherited value for {actualKey}: {inheritedValue}", "LoaderNormalJson");
            return inheritedValue;
        }

        // If not found in parents, use the default value
        Logger.Debug($"No inherited value found for {actualKey}, using default: {defaultValue}", "LoaderNormalJson");
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
            Logger.Debug($"No parent found for {currentTemplateKey}", "LoaderNormalJson");
            return null;
        }

        Logger.Debug($"Checking parent {parentKey} for key {key}", "LoaderNormalJson");

        // Get parent's template
        if (!_templates.TryGetValue(parentKey, out var parentTemplate))
        {
            Logger.Debug($"Parent template {parentKey} not found in templates", "LoaderNormalJson");
            return null;
        }

        if (parentTemplate.json == null)
        {
            Logger.Debug($"Parent template {parentKey} has no JSON data, searching further up", "LoaderNormalJson");
            // Parent has no JSON, search further up the tree
            return SearchParentTreeForKey(key, parentKey);
        }

        // Look for the key (case-insensitive)
        foreach (var kvp in parentTemplate.json)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value is string strValue)
                {
                    Logger.Debug($"Found key {key} in parent {parentKey}: {strValue}", "LoaderNormalJson");
                    return strValue;
                }
            }
        }

        Logger.Debug($"Key {key} not found in parent {parentKey}, searching further up", "LoaderNormalJson");
        // Not found in this parent, search further up the tree
        return SearchParentTreeForKey(key, parentKey);
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
