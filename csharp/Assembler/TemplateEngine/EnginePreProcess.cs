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
        Logger.Info($"MergeTemplates called: appSite={appSite}, appFile={appFile}, appView={appView ?? "null"}, enableJson={enableJsonProcessing}", "EnginePreProcess");

        if (preprocessedTemplates == null || preprocessedTemplates.Count == 0)
        {
            Logger.Warn("No preprocessed templates available", "EnginePreProcess");
            return "";
        }

        Logger.Debug($"Using {preprocessedTemplates.Count} preprocessed templates", "EnginePreProcess");

        // Use the new GetTemplate method to retrieve the main template
        var mainPreprocessed = GetTemplate(appSite, appFile, preprocessedTemplates, appView, AppViewPrefix, useAppViewFallback: true);
        if (mainPreprocessed == null)
        {
            Logger.Warn($"Main template not found for appSite={appSite}, appFile={appFile}", "EnginePreProcess");
            return "";
        }

        Logger.Debug($"Main template found, original size: {mainPreprocessed.OriginalContent.Length}", "EnginePreProcess");

        // Start with original content
        var contentHtml = mainPreprocessed.OriginalContent;

        // Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
        contentHtml = ApplyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed);

        Logger.Info($"MergeTemplates complete: output size={contentHtml.Length}", "EnginePreProcess");

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
    private string ApplyTemplateReplacements(string content, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, bool enableJsonProcessing, string? appView, PreprocessedTemplate? mainTemplate = null)
    {
        var result = content;

        Logger.Debug($"Starting ApplyTemplateReplacements, initial size: {content.Length}", "EnginePreProcess");

        // Apply replacement mappings from all templates in multiple passes until no more changes
        string previous;
        int maxPasses = 10; // Prevent infinite loops
        int currentPass = 0;

        do
        {
            previous = result;
            currentPass++;

            Logger.Debug($"Replacement pass {currentPass}, current size: {result.Length}", "EnginePreProcess");

            int slottedCount = 0, simpleCount = 0, jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template (to avoid overwriting component content)
            if (mainTemplate != null && currentPass == 1 && enableJsonProcessing)
            {
                foreach (var mapping in mainTemplate.ReplacementMappings.Where(m => m.Type == ReplacementType.JsonPlaceholder))
                {
                    if (result.Contains(mapping.OriginalText))
                    {
                        Logger.Debug($"Applying main template JSON placeholder: {mapping.OriginalText} -> {mapping.ReplacementText}", "EnginePreProcess");
                        result = result.Replace(mapping.OriginalText, mapping.ReplacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            // Apply replacement mappings from all templates
            foreach (var template in preprocessedTemplates.Values)
            {
                // Apply slotted template mappings
                foreach (var mapping in template.ReplacementMappings.Where(m => m.Type == ReplacementType.SlottedTemplate))
                {
                    if (result.Contains(mapping.OriginalText))
                    {
                        Logger.Debug($"Applying slotted template: {mapping.OriginalText.Substring(0, Math.Min(50, mapping.OriginalText.Length))}... -> {mapping.ReplacementText.Length} chars", "EnginePreProcess");
                        result = result.Replace(mapping.OriginalText, mapping.ReplacementText);
                        slottedCount++;
                    }
                }

                // Apply simple template mappings (components) - replacement text already has JSON values baked in by LoaderPreProcess
                foreach (var mapping in template.ReplacementMappings.Where(m => m.Type == ReplacementType.SimpleTemplate))
                {
                    if (result.Contains(mapping.OriginalText))
                    {
                        // Apply AppView logic to handle runtime template selection with baked-in JSON values
                        var replacementText = ApplyAppViewLogicToReplacement(mapping.OriginalText, mapping.ReplacementText, preprocessedTemplates, appView);
                        Logger.Info($"Applying simple template: {mapping.OriginalText} -> replacement text (first 200 chars): {replacementText.Substring(0, Math.Min(200, replacementText.Length))}", "EnginePreProcess");
                        result = result.Replace(mapping.OriginalText, replacementText);
                        simpleCount++;
                    }
                }
            }

            Logger.Debug($"Pass {currentPass} applied: {jsonPlaceholderCount} main JSON placeholders, {slottedCount} slotted, {simpleCount} simple", "EnginePreProcess");

        } while (result != previous && currentPass < maxPasses);

        // All JSON replacements are handled in LoaderPreProcess during replacement mapping creation
        // The engine only does simple string replacements using pre-prepared mappings
        Logger.Info($"Replacement complete after {currentPass} passes, final size: {result.Length}", "EnginePreProcess");

        return result;
    }

    /// <summary>
    /// Applies AppView fallback logic to template replacement text using the centralized GetTemplate method
    /// </summary>
    private string ApplyAppViewLogicToReplacement(string originalText, string replacementText, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, string? appView)
    {
        // If no appView context, use the default replacement text (which already has JSON values baked in)
        if (string.IsNullOrEmpty(appView))
            return replacementText;

        // Extract placeholder name from {{PlaceholderName}} format
        var placeholderName = ExtractPlaceholderName(originalText);
        if (string.IsNullOrEmpty(placeholderName))
            return replacementText;

        // First get the appSite from the template key pattern
        var sampleKey = preprocessedTemplates.Keys.FirstOrDefault();
        if (string.IsNullOrEmpty(sampleKey))
            return replacementText;

        var parts = sampleKey.Split('_');
        if (parts.Length < 2)
            return replacementText;

        var appSite = parts[0]; // Extract appSite from the key pattern

        // Use GetTemplate to find the AppView-specific template variant
        var appViewTemplate = GetTemplate(appSite, placeholderName, preprocessedTemplates, appView, AppViewPrefix, useAppViewFallback: true);

        // If no AppView-specific template found, use the default replacement text
        if (appViewTemplate == null)
            return replacementText;

        // Return the AppView-specific template's original content (which already has JSON baked in by LoaderPreProcess)
        return appViewTemplate.OriginalContent;
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