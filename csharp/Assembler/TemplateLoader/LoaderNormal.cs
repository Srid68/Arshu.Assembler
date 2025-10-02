using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Arshu.App.Json;
using Assembler.TemplateCommon;

namespace Assembler.TemplateLoader;

// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;
using JsonArray = Arshu.App.Json.JsonArray;

/// <summary>
/// Handles loading and caching of HTML templates from the file system
/// </summary>
public static class LoaderNormal
{
    #region Loading Templates
    
    private static readonly Dictionary<string, Dictionary<string, (string html, string? json)>> _htmlTemplatesCache = new();

    /// <summary>
    /// Loads HTML files and corresponding JSON files from the specified application site directory, caching the output per appSite
    /// </summary>
    public static Dictionary<string, (string html, string? json)> LoadGetTemplateFiles(string rootDirPath, string appSite)
    {
        var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite;
        if (_htmlTemplatesCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = new Dictionary<string, (string html, string? json)>();
        var appSitesPath = Path.Combine(rootDirPath, "AppSites", appSite);
        if (!Directory.Exists(appSitesPath)) return result;

        foreach (var file in Directory.GetFiles(appSitesPath, "*.html", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var key = ($"{appSite.ToLowerInvariant()}_{fileName.ToLowerInvariant()}");
            var htmlContent = File.ReadAllText(file);
            var jsonFile = Path.ChangeExtension(file, ".json");
            string? jsonContent = null;
            if (File.Exists(jsonFile))
                jsonContent = File.ReadAllText(jsonFile);
            result[key] = (htmlContent, jsonContent);
        }
        _htmlTemplatesCache[cacheKey] = result;
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