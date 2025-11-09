using System;
using System.Collections.Generic;
using System.IO;

namespace Assembler.Config;

public class Scenario
{
    public string AppSite { get; set; }
    public string AppFile { get; set; }
    public string AppView { get; set; }

    public Scenario(string appSite, string appFile, string appView)
    {
        AppSite = appSite;
        AppFile = appFile;
        AppView = appView;
    }

    public override string ToString()
    {
        return $"{AppSite}:{AppFile}:{AppView}";
    }
}

public static class ConfigUtil
{    
    public static string DefaultAppFile = "Index";

    private static HashSet<string>? _cachedAppSites = null;
    private static List<Scenario>? _cachedScenarios = null;
    private static readonly object _cacheLock = new object();
    private static string? _wwwrootPath = null;

    /// <summary>
    /// Extracts unique AppSites from scenarios
    /// </summary>
    private static HashSet<string> ExtractAppSitesFromScenarios(List<Scenario> scenarios)
    {
        var appSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < scenarios.Count; i++)
        {
            var appSite = scenarios[i].AppSite;
            if (!string.IsNullOrWhiteSpace(appSite))
            {
                appSites.Add(appSite);
            }
        }

        Console.WriteLine($"[ConfigUtil] Extracted {appSites.Count} AppSites from folder scan");

        return appSites;
    }

    /// <summary>
    /// Discovers scenarios by scanning AppSites folder structure
    /// </summary>
    private static List<Scenario> LoadScenariosInternal(string wwwrootPath)
    {
        var appSitesPath = Path.Combine(wwwrootPath, "AppSites");

        if (!Directory.Exists(appSitesPath))
        {
            throw new DirectoryNotFoundException($"AppSites directory not found: {appSitesPath}");
        }

        var scenarios = new List<Scenario>();

        // Get all directories in AppSites folder
        var directories = Directory.GetDirectories(appSitesPath);
        var appSiteDirs = new List<string>();

        for (int i = 0; i < directories.Length; i++)
        {
            var dirName = Path.GetFileName(directories[i]);
            if (!string.IsNullOrEmpty(dirName))
            {
                appSiteDirs.Add(dirName);
            }
        }

        // Sort alphabetically
        appSiteDirs.Sort();

        for (int i = 0; i < appSiteDirs.Count; i++)
        {
            var appSite = appSiteDirs[i];

            // Get all HTML files in the appSite directory (top level only)
            var appSiteDir = Path.Combine(appSitesPath, appSite);
            var htmlFiles = Directory.GetFiles(appSiteDir, "*.html", SearchOption.TopDirectoryOnly);

            // If no HTML files found, use DefaultAppFile
            if (htmlFiles.Length == 0)
            {
                htmlFiles = new string[] { DefaultAppFile };
            }

            for (int j = 0; j < htmlFiles.Length; j++)
            {
                var appFile = htmlFiles[j] == DefaultAppFile ? DefaultAppFile : Path.GetFileNameWithoutExtension(htmlFiles[j]);

                // Check for Views folder
                var viewsPath = Path.Combine(appSitesPath, appSite, "Views");
                var viewDirs = new List<string>();

                if (Directory.Exists(viewsPath))
                {
                    // Get all subdirectories in Views folder
                    var viewDirectories = Directory.GetDirectories(viewsPath);
                    for (int k = 0; k < viewDirectories.Length; k++)
                    {
                        var viewDirName = Path.GetFileName(viewDirectories[k]);
                        if (!string.IsNullOrEmpty(viewDirName))
                        {
                            viewDirs.Add(viewDirName);
                        }
                    }
                }

                // Only add empty AppView scenario if no specific Views exist
                if (viewDirs.Count == 0)
                {
                    scenarios.Add(new Scenario(appSite, appFile, ""));
                }
                else
                {
                    // Add specific view scenarios
                    for (int k = 0; k < viewDirs.Count; k++)
                    {
                        scenarios.Add(new Scenario(appSite, appFile, viewDirs[k]));
                    }
                }
            }
        }

        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException("No scenarios found in AppSites folder");
        }

        Console.WriteLine($"[ConfigUtil] Loaded {scenarios.Count} scenarios from AppSites folder");

        return scenarios;
    }

    /// <summary>
    /// Loads AppSites from wwwroot path and caches them. Call this during startup.
    /// </summary>
    public static void Load(string wwwrootPath)
    {
        lock (_cacheLock)
        {
            _wwwrootPath = wwwrootPath;
            _cachedScenarios = LoadScenariosInternal(wwwrootPath);
            _cachedAppSites = ExtractAppSitesFromScenarios(_cachedScenarios);
        }
    }

    /// <summary>
    /// Reloads AppSites and Scenarios from the stored wwwroot path. Throws if not loaded.
    /// </summary>
    public static void Reload()
    {
        lock (_cacheLock)
        {
            if (_wwwrootPath == null)
                throw new InvalidOperationException("ConfigUtil not loaded. Call Load(wwwrootPath) first.");

            _cachedScenarios = LoadScenariosInternal(_wwwrootPath);
            _cachedAppSites = ExtractAppSitesFromScenarios(_cachedScenarios);
        }
    }

    /// <summary>
    /// Gets the cached AppSites. Throws if not loaded.
    /// </summary>
    public static HashSet<string> GetAppSites()
    {
        if (_cachedAppSites == null)
            throw new InvalidOperationException("AppSitesConfig not loaded. Call Load(wwwrootPath) first.");

        return _cachedAppSites;
    }

    /// <summary>
    /// Gets the cached Scenarios. Throws if not loaded.
    /// </summary>
    public static List<Scenario> GetScenarios()
    {
        if (_cachedScenarios == null)
            throw new InvalidOperationException("AppSitesConfig not loaded. Call Load(wwwrootPath) first.");

        return _cachedScenarios;
    }

    /// <summary>
    /// Filters scenarios by appSite
    /// </summary>
    public static List<Scenario> FilterByAppSite(List<Scenario> scenarios, string appSiteFilter)
    {
        if (string.IsNullOrEmpty(appSiteFilter))
        {
            return scenarios;
        }

        var filtered = new List<Scenario>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            if (scenarios[i].AppSite.Equals(appSiteFilter, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(scenarios[i]);
            }
        }

        return filtered;
    }

    /// <summary>
    /// Gets the AppFile for a given AppSite and AppView.
    /// Returns DefaultAppFile ("Index") if no matching scenario is found.
    /// </summary>
    /// <param name="appSite">The AppSite name</param>
    /// <param name="appView">The AppView name (can be empty string)</param>
    /// <returns>AppFile name or DefaultAppFile if not found</returns>
    public static string GetAppFile(string appSite, string appView = "")
    {
        var scenarios = GetScenarios();

        for (int i = 0; i < scenarios.Count; i++)
        {
            if (scenarios[i].AppSite.Equals(appSite, StringComparison.OrdinalIgnoreCase) &&
                scenarios[i].AppView.Equals(appView ?? "", StringComparison.OrdinalIgnoreCase))
            {
                return scenarios[i].AppFile;
            }
        }

        // Return DefaultAppFile if no matching scenario found
        return DefaultAppFile;
    }
}
