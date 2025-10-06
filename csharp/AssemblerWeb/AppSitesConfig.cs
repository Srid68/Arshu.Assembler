using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssemblerWeb
{
    public static class AppSitesConfig
    {
        /// <summary>
        /// Discovers AppSites by scanning the AppSites folder and generates appsites.csv
        /// </summary>
        private static void GenerateAppSitesCsv(string wwwrootPath)
        {
            var appSitesPath = Path.Combine(wwwrootPath, "AppSites");
            var appDataPath = Path.Combine(wwwrootPath, "App_Data");
            var csvFilePath = Path.Combine(appDataPath, "appsites.csv");

            if (!Directory.Exists(appSitesPath))
            {
                throw new DirectoryNotFoundException($"AppSites directory not found: {appSitesPath}");
            }

            // Ensure App_Data directory exists
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            // Get all directories in AppSites folder
            var appSites = Directory.GetDirectories(appSitesPath)
                .Select(dir => Path.GetFileName(dir))
                .Where(dirName => !string.IsNullOrEmpty(dirName))
                .OrderBy(name => name)
                .ToList();

            // Add Index as it's a valid AppSite
            if (!appSites.Contains("Index"))
            {
                appSites.Add("Index");
            }

            // Write as CSV (comma-delimited)
            var csv = string.Join(",", appSites);
            File.WriteAllText(csvFilePath, csv);

            Console.WriteLine($"[AppSitesConfig] Generated appsites.csv with {appSites.Count} AppSites");
        }

        /// <summary>
        /// Loads AppSites from appsites.csv, generates it if it doesn't exist
        /// </summary>
        public static HashSet<string> LoadAppSites(string wwwrootPath)
        {
            var appDataPath = Path.Combine(wwwrootPath, "App_Data");
            var csvFilePath = Path.Combine(appDataPath, "appsites.csv");

            // Generate appsites.csv if it doesn't exist
            if (!File.Exists(csvFilePath))
            {
                Console.WriteLine($"[AppSitesConfig] appsites.csv not found, generating...");
                GenerateAppSitesCsv(wwwrootPath);
            }

            // Read and parse CSV
            var csv = File.ReadAllText(csvFilePath).Trim();

            if (string.IsNullOrWhiteSpace(csv))
            {
                throw new InvalidOperationException("appsites.csv is empty");
            }

            var appSites = csv.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (appSites.Count == 0)
            {
                throw new InvalidOperationException("No AppSites found in appsites.csv");
            }

            Console.WriteLine($"[AppSitesConfig] Loaded {appSites.Count} AppSites from appsites.csv");

            return new HashSet<string>(appSites, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reloads AppSites by regenerating appsites.csv from the file system
        /// </summary>
        public static HashSet<string> ReloadAppSites(string wwwrootPath)
        {
            Console.WriteLine($"[AppSitesConfig] Reloading AppSites...");
            GenerateAppSitesCsv(wwwrootPath);
            return LoadAppSites(wwwrootPath);
        }
    }
}
