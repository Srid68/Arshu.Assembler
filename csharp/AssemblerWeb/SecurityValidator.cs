using Assembler.Config;
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

        /// <summary>
        /// Gets the valid AppSites from TemplateConfig. Throws if not loaded.
        /// </summary>
        public static HashSet<string> GetValidAppSites()
        {
            return ConfigUtil.GetAppSites();
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
