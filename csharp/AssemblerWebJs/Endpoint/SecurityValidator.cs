using Assembler.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssemblerWebJs
{
    /// <summary>
    /// Security validation helper for input validation and AppSites configuration
    /// </summary>
    public static class SecurityValidator
    {
        // Maximum parameter length to prevent DoS attacks
        private const int ParamMaxLength = 256;

        // Maximum content sizes to prevent DDOS attacks
        public const int MaxLogFileSize = 500 * 1024; // 500 KB per log file

        // Buffer allowance for output size validation (50 KB)
        public const int OutputSizeBuffer = 50 * 1024; // 50 KB buffer for dynamic content (performance reports, test results)

        // Valid engine types allowlist
        public static readonly HashSet<string> ValidEngineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Normal", "PreProcess", "NormalJson"
        };

        // Log entry validation pattern (allows timestamps, log levels, messages, stack traces)
        // Matches patterns like: [timestamp] LEVEL: message or similar structured log formats
        private static readonly System.Text.RegularExpressions.Regex LogEntryPattern =
            new System.Text.RegularExpressions.Regex(
                @"^[\[\]0-9:\-\s\.TZ]+\s*(DEBUG|INFO|WARN|ERROR|TRACE|FATAL)?:?\s*.+$",
                System.Text.RegularExpressions.RegexOptions.Multiline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

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

        /// <summary>
        /// Validates content size against maximum limit
        /// </summary>
        public static bool IsValidContentSize(string? content, int maxSize)
        {
            if (string.IsNullOrEmpty(content))
                return true;

            var contentSize = System.Text.Encoding.UTF8.GetByteCount(content);
            return contentSize <= maxSize;
        }

        /// <summary>
        /// Validates output size against template total size with fixed buffer
        /// </summary>
        public static bool IsValidOutputSizeWithBuffer(string? htmlContent, int templateTotalSize)
        {
            if (string.IsNullOrEmpty(htmlContent))
                return true;

            var outputSize = System.Text.Encoding.UTF8.GetByteCount(htmlContent);

            // Check against template size + buffer
            if (templateTotalSize > 0)
            {
                var maxAllowedSize = templateTotalSize + OutputSizeBuffer;
                return outputSize <= maxAllowedSize;
            }

            // If template size is unknown, reject (requires template size for validation)
            return false;
        }

        /// <summary>
        /// Validates log content format and size
        /// </summary>
        public static bool IsValidLogContent(string? logContent, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrEmpty(logContent))
            {
                errorMessage = "Log content is empty";
                return false;
            }

            // Check file size limit (500 KB per log file)
            if (!IsValidContentSize(logContent, MaxLogFileSize))
            {
                errorMessage = "Log file exceeds maximum size limit (500 KB)";
                return false;
            }

            // Split into lines for validation
            var lines = logContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Check if at least some lines match log pattern (allow some flexibility)
            // Require at least 50% of non-empty lines to match log pattern
            int validLines = 0;
            int totalLines = lines.Length;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Check if line matches log pattern or is a continuation line (stack trace, etc.)
                if (LogEntryPattern.IsMatch(line) || line.StartsWith("    at ") || line.StartsWith("\tat "))
                {
                    validLines++;
                }
            }

            // At least 50% of lines should match expected log format
            if (totalLines > 0 && ((double)validLines / totalLines) < 0.5)
            {
                errorMessage = "Log content does not match expected format";
                return false;
            }

            return true;
        }

    }
}
