using System;
using System.Collections.Generic;
using System.Linq;
using Arshu.App.Json;
using Assembler.TemplateCommon;
using Assembler.TemplateModel;

namespace Assembler.TemplateEngine;

// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;
using JsonArray = Arshu.App.Json.JsonArray;

/// <summary>
/// PreProcess template engine implementation that only does merging using preprocessed data structures
/// All parsing is done by TemplateLoader, this engine only handles merging
/// </summary>
public class EnginePreProcess 
{
    #region Merge Templates

    public string AppViewPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Merges templates using preprocessed data structures
    /// This method only does merging using preprocessed data structures - no loading or parsing
    /// </summary>
    /// <param name="appSite">The application site name for template key generation</param>
    /// <param name="appFile">The application file name</param>
    /// <param name="appView">The application view name (optional)</param>
    /// <param name="preprocessedTemplates">Dictionary of preprocessed templates for this specific appSite</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced using preprocessed structures</returns>
    public string MergeTemplates(string appSite, string appFile, string? appView, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, bool enableJsonProcessing = true)
    {
        if (preprocessedTemplates == null || preprocessedTemplates.Count == 0)
            return "";

        // Use the new GetTemplate method to retrieve the main template
        var mainPreprocessed = GetTemplate(appSite, appFile, preprocessedTemplates, appView, AppViewPrefix, useAppViewFallback: true);
        if (mainPreprocessed == null)
            return "";

        // Start with original content
        var contentHtml = mainPreprocessed.OriginalContent;

        // Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
        contentHtml = ApplyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView);

        return contentHtml;
    }

    /// <summary>
    /// Retrieves a template from the preprocessed templates dictionary based on various scenarios including AppView fallback logic
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (can be appFile or placeholderName)</param>
    /// <param name="preprocessedTemplates">Dictionary of preprocessed templates</param>
    /// <param name="appView">The application view name (optional)</param>
    /// <param name="appViewPrefix">The application view prefix (optional, uses instance property if not provided)</param>
    /// <param name="useAppViewFallback">Whether to apply AppView fallback logic</param>
    /// <returns>The template's original content if found, null otherwise</returns>
    private PreprocessedTemplate? GetTemplate(string appSite, string templateName, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, string? appView = null, string? appViewPrefix = null, bool useAppViewFallback = true)
    {
        if (preprocessedTemplates == null || preprocessedTemplates.Count == 0)
            return null;

        var viewPrefix = appViewPrefix ?? AppViewPrefix;
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if (useAppViewFallback && !string.IsNullOrEmpty(appView) && !string.IsNullOrEmpty(viewPrefix) && templateName.Contains(viewPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Direct replacement: Replace the AppViewPrefix with the AppView value
            // For example: Html3AContent with AppViewPrefix=Html3A and AppView=html3B becomes html3BContent
            var appKey = TemplateUtils.ReplaceCaseInsensitive(templateName, viewPrefix, appView);
            var fallbackTemplateKey = $"{appSite.ToLowerInvariant()}_{appKey.ToLowerInvariant()}";
            if (preprocessedTemplates.TryGetValue(fallbackTemplateKey, out var fallbackTemplate))
            {
                return fallbackTemplate; // Found AppView-specific template, use it
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        var primaryTemplateKey = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";
        if (preprocessedTemplates.TryGetValue(primaryTemplateKey, out var primaryTemplate))
        {
            return primaryTemplate;
        }

        return null;
    }


    #endregion

    #region Apply PreProcess Structure

    /// <summary>
    /// Applies all replacement mappings from all templates - NO processing logic, only direct replacements
    /// </summary>
    private string ApplyTemplateReplacements(string content, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, bool enableJsonProcessing, string? appView)
    {
        var result = content;

        // Apply replacement mappings from all templates in multiple passes until no more changes
        string previous;
        int maxPasses = 10; // Prevent infinite loops
        int currentPass = 0;
        
        do
        {
            previous = result;
            currentPass++;
            
            // Apply replacement mappings from all templates
            foreach (var template in preprocessedTemplates.Values)
            {
                // CRITICAL: Apply slotted template mappings FIRST (before JSON processing changes the content)
                foreach (var mapping in template.ReplacementMappings.Where(m => m.Type == ReplacementType.SlottedTemplate))
                {
                    if (result.Contains(mapping.OriginalText))
                    {
                        result = result.Replace(mapping.OriginalText, mapping.ReplacementText);
                    }
                }
                
                // Then apply other replacement mappings (simple templates) with AppView logic
                foreach (var mapping in template.ReplacementMappings.Where(m => m.Type == ReplacementType.SimpleTemplate))
                {
                    if (result.Contains(mapping.OriginalText))
                    {
                        // Apply AppView logic before replacement
                        var replacementText = ApplyAppViewLogicToReplacement(mapping.OriginalText, mapping.ReplacementText, preprocessedTemplates, appView);
                        result = result.Replace(mapping.OriginalText, replacementText);
                    }
                }
                
                // Apply JSON replacement mappings only if JSON processing is enabled
                if (enableJsonProcessing)
                {
                    foreach (var mapping in template.ReplacementMappings.Where(m => m.Type == ReplacementType.JsonPlaceholder))
                    {
                        if (result.Contains(mapping.OriginalText))
                        {
                            result = result.Replace(mapping.OriginalText, mapping.ReplacementText);
                        }
                    }
                }
                
                // Apply JSON placeholders if JSON processing is enabled (LAST)
                if (enableJsonProcessing)
                {
                    foreach (var placeholder in template.JsonPlaceholders)
                    {
                        result = ReplaceAllCaseInsensitive(result, placeholder.Placeholder, placeholder.Value);
                    }
                }
            }
            
        } while (result != previous && currentPass < maxPasses);

        return result;
    }

    /// <summary>
    /// Helper method to replace all case-insensitive occurrences
    /// </summary>
    private static string ReplaceAllCaseInsensitive(string input, string search, string replacement)
    {
        int idx = 0;
        while (true)
        {
            int found = input.IndexOf(search, idx, StringComparison.OrdinalIgnoreCase);
            if (found == -1) break;
            input = input.Substring(0, found) + replacement + input.Substring(found + search.Length);
            idx = found + replacement.Length;
        }
        return input;
    }

    /// <summary>
    /// Applies AppView fallback logic to template replacement text using the centralized GetTemplate method
    /// </summary>
    private string ApplyAppViewLogicToReplacement(string originalText, string replacementText, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, string? appView)
    {
        // Check if the original text is a placeholder that should use AppView fallback logic
        // Extract placeholder name from {{PlaceholderName}} format
        var placeholderName = ExtractPlaceholderName(originalText);
        if (string.IsNullOrEmpty(placeholderName))
            return replacementText;

        // Use the centralized GetTemplate method for consistent AppView logic
        // First get the appSite from the template key pattern
        var sampleKey = preprocessedTemplates.Keys.FirstOrDefault();
        if (string.IsNullOrEmpty(sampleKey))
            return replacementText;
            
        var parts = sampleKey.Split('_');
        if (parts.Length < 2)
            return replacementText;
            
        var appSite = parts[0]; // Extract appSite from the key pattern
        
        var template = GetTemplate(appSite, placeholderName, preprocessedTemplates, appView, AppViewPrefix, useAppViewFallback: true);
        
        return template?.OriginalContent ?? replacementText;
    }

    /// <summary>
    /// Extracts placeholder name from {{PlaceholderName}} format
    /// </summary>
    private static string ExtractPlaceholderName(string originalText)
    {
        if (string.IsNullOrEmpty(originalText) || !originalText.StartsWith("{{") || !originalText.EndsWith("}}"))
            return string.Empty;
        
        return originalText.Substring(2, originalText.Length - 4).Trim();
    }

    #endregion
}