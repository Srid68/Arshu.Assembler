using System;
using System.IO;
using System.Linq;

namespace Assembler.TemplateCommon;

/// <summary>
/// Shared utility methods for template processing
/// </summary>
public static class TemplateUtils
{
    public static (string assemblerWebDirPath, string projectDirectory) GetAssemblerWebDirPath()
    {
        string currentDirectory = Environment.CurrentDirectory;
        string projectDirectory = currentDirectory;
        DirectoryInfo currentDirInfo = new DirectoryInfo(currentDirectory);
        int idxBin = currentDirectory.IndexOf("bin");
        if (idxBin > -1)
        {
            projectDirectory = currentDirectory.Substring(0, idxBin);
        }
        else if (currentDirInfo.Name.EndsWith("AssemblerTest"))
        {
            projectDirectory = currentDirectory;
        }
        else if (currentDirInfo.Name.EndsWith("csharp"))
        {
            projectDirectory = Path.Combine(currentDirectory, "AssemblerTest");
        }
        else if (currentDirInfo.Name.StartsWith("Arshu.Assembler"))
        {
            projectDirectory = Path.Combine(currentDirectory, "csharp", "AssemblerTest");
        }
        string assemblerWebDirPath = string.Empty;
        if (!string.IsNullOrEmpty(projectDirectory))
        {
            DirectoryInfo projectDirInfo = new DirectoryInfo(projectDirectory);
            if (projectDirInfo.Parent != null)
            {
                string webDirPath = Path.Combine(projectDirInfo.Parent.FullName, "AssemblerWeb", "wwwroot");
                if (Directory.Exists(webDirPath))
                {
                    assemblerWebDirPath = webDirPath;
                }
            }
        }
        return (assemblerWebDirPath, projectDirectory);
    }

    /// <summary>
    /// Check if string contains only alphanumeric characters
    /// </summary>
    public static bool IsAlphaNumeric(string str)
    {
        return !string.IsNullOrEmpty(str) && str.All(c => char.IsLetterOrDigit(c));
    }

    /// <summary>
    /// Find matching closing tag with proper nesting support
    /// </summary>
    public static int FindMatchingCloseTag(string content, int startPos, string openTag, string closeTag)
    {
        var searchPos = startPos;
        var openCount = 1;

        while (searchPos < content.Length && openCount > 0)
        {
            var nextOpen = content.IndexOf(openTag, searchPos);
            var nextClose = content.IndexOf(closeTag, searchPos);

            if (nextClose == -1) return -1;

            if (nextOpen != -1 && nextOpen < nextClose)
            {
                openCount++;
                searchPos = nextOpen + openTag.Length;
            }
            else
            {
                openCount--;
                if (openCount == 0)
                {
                    return nextClose;
                }
                searchPos = nextClose + closeTag.Length;
            }
        }

        return -1;
    }

    /// <summary>
    /// Remove remaining slot placeholders
    /// </summary>
    public static string RemoveRemainingSlotPlaceholders(string html)
    {
        var result = html;
        var searchPos = 0;

        while (searchPos < result.Length)
        {
            var placeholderStart = result.IndexOf("{{$HTMLPLACEHOLDER", searchPos);
            if (placeholderStart == -1) break;

            var afterPlaceholder = placeholderStart + 18;
            var pos = afterPlaceholder;

            // Skip digits
            while (pos < result.Length && char.IsDigit(result[pos]))
            {
                pos++;
            }

            // Check for closing }}
            if (pos + 1 < result.Length && result.Substring(pos, 2) == "}}")
            {
                var placeholderEnd = pos + 2;
                var placeholder = result.Substring(placeholderStart, placeholderEnd - placeholderStart);
                result = result.Replace(placeholder, "");
                // Don't advance searchPos since we removed content
            }
            else
            {
                searchPos = placeholderStart + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
    /// </summary>
    public static string ReplaceCaseInsensitive(string text, string from, string to)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(from))
            return text;

        int index = text.IndexOf(from, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return text.Substring(0, index) + to + text.Substring(index + from.Length);
        }
        return text;
    }
}