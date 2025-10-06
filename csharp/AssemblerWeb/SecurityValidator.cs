using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssemblerWeb
{
    /// <summary>
    /// Security validation helper for input validation and AppSites configuration
    /// </summary>
    public static class SecurityValidator
    {
        // Maximum parameter length to prevent DoS attacks
        private const int ParamMaxLength = 256;

        // Valid engine types allowlist
        public static readonly HashSet<string> ValidEngineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Normal", "PreProcess"
        };

        // Cached ValidAppSites - loaded on first request
        private static HashSet<string>? _cachedValidAppSites = null;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Gets the valid AppSites from cache, or loads from appsites.csv (generating if needed)
        /// </summary>
        public static HashSet<string> GetValidAppSites(string wwwrootPath)
        {
            if (_cachedValidAppSites != null)
                return _cachedValidAppSites;

            lock (_cacheLock)
            {
                // Double-check after acquiring lock
                if (_cachedValidAppSites != null)
                    return _cachedValidAppSites;

                _cachedValidAppSites = AppSitesConfig.LoadAppSites(wwwrootPath);
                return _cachedValidAppSites;
            }
        }

        /// <summary>
        /// Clears the AppSites cache - useful for development/testing
        /// </summary>
        public static void ClearAppSitesCache()
        {
            lock (_cacheLock)
            {
                _cachedValidAppSites = null;
            }
        }

        /// <summary>
        /// Validates if a path component is safe (no traversal, invalid chars, or excessive length)
        /// </summary>
        public static bool IsValidPathComponent(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Check parameter length to prevent DoS
            if (value.Length > ParamMaxLength)
                return false;

            // Check for path traversal attempts
            if (value.Contains("..") || value.Contains("/") || value.Contains("\\"))
                return false;

            // Check for other suspicious characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (value.Any(c => invalidChars.Contains(c)))
                return false;

            return true;
        }
    }
}
