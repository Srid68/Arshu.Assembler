using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Arshu.Common;
using Assembler.Common;

namespace Assembler.Loader;
// Always use Base JsonObject/JsonArray types for consistent processing
/// <summary>
/// Handles loading and caching of HTML templates from the file system
/// </summary>
public static class LoaderNormal
{
    #region Loading Templates

    private static readonly ConcurrentDictionary<string, Dictionary<string, (string html, string? json)>> _htmlTemplatesCache = new();

    /// <summary>
    /// Loads HTML files and corresponding JSON files from the specified application site directory, caching the output per appSite
    /// </summary>
    /// <param name="rootDirPath">Root directory path containing AppSites folder</param>
    /// <param name="appSite">Primary AppSite name to load</param>
    /// <param name="searchAppSites">Comma-delimited string of AppSite names to search for fallback templates (can be empty string)</param>
    public static Dictionary<string, (string html, string? json)> LoadGetTemplateFiles(string rootDirPath, string appSite, string searchAppSites)
    {
        Logger.Debug($"LoadGetTemplateFiles called for appSite: {appSite}, searchAppSites: {searchAppSites}", "LoaderNormal");

        var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite + "|" + searchAppSites;
        if (_htmlTemplatesCache.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug($"Returning cached templates for {appSite} ({cached.Count} templates)", "LoaderNormal");
            return cached;
        }

        // Load templates from primary appSite
        var result = LoadTemplatesFromSingleAppSite(rootDirPath, appSite);

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
                    if (!result.ContainsKey(kvp.Key))
                    {
                        result[kvp.Key] = kvp.Value;
                        Logger.Debug($"Added fallback template '{kvp.Key}' from '{searchAppSite}'", "LoaderNormal");
                    }
                }
            }
        }

        _htmlTemplatesCache.TryAdd(cacheKey, result);
        return result;
    }

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

    /// <summary>
    /// Clear all cached templates (useful for testing or when templates change)
    /// </summary>
    public static void ClearCache()
    {
        _htmlTemplatesCache.Clear();
    }

    #endregion
}