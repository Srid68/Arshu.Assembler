using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assembler.TemplateCommon;

namespace Assembler.TemplateLoader;
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
    public static Dictionary<string, (string html, string? json)> LoadGetTemplateFiles(string rootDirPath, string appSite)
    {
        Logger.Debug($"LoadGetTemplateFiles called for appSite: {appSite}", "LoaderNormal");

        var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite;
        if (_htmlTemplatesCache.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug($"Returning cached templates for {appSite} ({cached.Count} templates)", "LoaderNormal");
            return cached;
        }

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
            var htmlContent = File.ReadAllText(file);

            Logger.Debug($"Loading template: {key} (html size: {htmlContent.Length})", "LoaderNormal");

            // Find JSON file case-insensitively
            var jsonFile = Path.ChangeExtension(file, ".json");
            string? jsonContent = null;

            // Try exact match first
            if (File.Exists(jsonFile))
            {
                jsonContent = File.ReadAllText(jsonFile);
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
                    var matchingJson = jsonFiles.FirstOrDefault(f =>
                        string.Equals(Path.GetFileNameWithoutExtension(f), baseFileName, StringComparison.OrdinalIgnoreCase));

                    if (matchingJson != null)
                    {
                        jsonContent = File.ReadAllText(matchingJson);
                        Logger.Debug($"Found JSON file (case-insensitive) for {key} (size: {jsonContent.Length})", "LoaderNormal");
                    }
                }
            }

            result[key] = (htmlContent, jsonContent);
        }

        Logger.Info($"Loaded {result.Count} templates for {appSite}", "LoaderNormal");

        _htmlTemplatesCache.TryAdd(cacheKey, result);
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