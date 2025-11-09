using System;
using System.Collections.Generic;
using Arshu.App.Json;
using Arshu.Common;
using Assembler.Common;
using Assembler.Model;
using Assembler.Loader;

namespace Assembler.Engine;

// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;
using JsonArray = Arshu.App.Json.JsonArray;

/// <summary>
/// PreProcess JSON template engine implementation that only does merging using preprocessed data structures with JsonObject
/// All parsing and JSON processing is done by LoaderPreProcessJson, this engine only handles merging
/// Uses ILoader<PreprocessedTemplate> for consistency with NormalJson architecture
/// </summary>
public class EnginePreProcessJson
{
    #region Merge Templates

    public string AppViewPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Merges templates using preprocessed data structures with JsonObject via ILoader
    /// This method only does merging using preprocessed data structures - no loading or parsing
    /// </summary>
    /// <param name="appSite">The application site name for template key generation</param>
    /// <param name="appFile">The application file name</param>
    /// <param name="appView">The application view name (optional)</param>
    /// <param name="loader">ILoader providing preprocessed templates</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced using preprocessed structures</returns>
    public string MergeTemplates(string appSite, string appFile, string? appView, ILoader<PreprocessedTemplate> loader, bool enableJsonProcessing = true)
    {
        Logger.Debug($"MergeTemplates called: appSite={appSite}, appFile={appFile}, appView={appView ?? "null"}, enableJson={enableJsonProcessing}", "EnginePreProcessJson");

        if (loader == null)
        {
            Logger.Warn("No loader provided", "EnginePreProcessJson");
            return "";
        }

        // Get all templates (needed for PreProcess engine to apply replacement mappings from all templates)
        var preprocessedTemplates = (loader as LoaderPreProcessJson)?.AllTemplates;
        if (preprocessedTemplates == null || preprocessedTemplates.Count == 0)
        {
            Logger.Warn("No preprocessed templates available", "EnginePreProcessJson");
            return "";
        }

        Logger.Debug($"Using {preprocessedTemplates.Count} preprocessed templates", "EnginePreProcessJson");

        // Use ILoader to retrieve the main template
        var mainPreprocessed = loader.GetTemplateHtml(appSite, appFile, appView, AppViewPrefix);
        if (mainPreprocessed == null)
        {
            Logger.Warn($"Main template not found for appSite={appSite}, appFile={appFile}", "EnginePreProcessJson");
            return "";
        }

        Logger.Debug($"Main template found, original size: {mainPreprocessed.OriginalContent.Length}", "EnginePreProcessJson");

        // Start with original content
        var contentHtml = mainPreprocessed.OriginalContent;

        // Merge JSON into main template first using loader's centralized method
        if (enableJsonProcessing)
        {
            contentHtml = loader.MergeHtmlWithJson(contentHtml, appSite, appFile);
            Logger.Debug($"After main template JSON merge: {contentHtml.Length} chars", "EnginePreProcessJson");
        }

        // Apply ALL replacement mappings from ALL templates (LoaderPreProcessJson creates structure, engine does merging)
        contentHtml = ApplyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed, loader, appSite);

        Logger.Debug($"MergeTemplates complete: output size={contentHtml.Length}", "EnginePreProcessJson");

        return contentHtml;
    }

    /// <summary>
    /// Retrieves a template from the preprocessed templates dictionary based on various scenarios including AppView fallback logic
    /// This is a helper method for internal use
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
            var appKey = CommonUtil.ReplaceCaseInsensitive(templateName, viewPrefix, appView);
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
    /// Applies all replacement mappings from all templates with JSON merging done by engine
    /// </summary>
    private string ApplyTemplateReplacements(string content, Dictionary<string, PreprocessedTemplate> preprocessedTemplates, bool enableJsonProcessing, string? appView, PreprocessedTemplate? mainTemplate, ILoader<PreprocessedTemplate> loader, string appSite)
    {
        var result = content;

        Logger.Debug($"Starting ApplyTemplateReplacements, initial size: {content.Length}", "EnginePreProcessJson");

        // Apply replacement mappings from all templates in multiple passes until no more changes
        string previous;
        int maxPasses = 10; // Prevent infinite loops
        int currentPass = 0;

        do
        {
            previous = result;
            currentPass++;

            Logger.Debug($"Replacement pass {currentPass}, current size: {result.Length}", "EnginePreProcessJson");

            int slottedCount = 0, simpleCount = 0, jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template
            if (mainTemplate != null && currentPass == 1 && enableJsonProcessing)
            {
                for (int i = 0; i < mainTemplate.ReplacementMappings.Count; i++)
                {
                    var mapping = mainTemplate.ReplacementMappings[i];
                    if (mapping.Type != ReplacementType.JsonPlaceholder)
                        continue;

                    if (result.Contains(mapping.OriginalText))
                    {
                        Logger.Debug($"Applying main template JSON placeholder: {mapping.OriginalText} -> {mapping.ReplacementText}", "EnginePreProcessJson");
                        result = result.Replace(mapping.OriginalText, mapping.ReplacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            // Apply replacement mappings from all templates
            foreach (var template in preprocessedTemplates.Values)
            {
                // Apply slotted template mappings - engine retrieves and merges JSON
                for (int i = 0; i < template.ReplacementMappings.Count; i++)
                {
                    var mapping = template.ReplacementMappings[i];
                    if (mapping.Type != ReplacementType.SlottedTemplate)
                        continue;

                    if (result.Contains(mapping.OriginalText))
                    {
                        // Get replacement text and merge JSON using loader's centralized method
                        var replacementText = mapping.ReplacementText;
                        if (enableJsonProcessing && !string.IsNullOrEmpty(mapping.TargetTemplateName))
                        {
                            replacementText = loader.MergeHtmlWithJson(replacementText, appSite, mapping.TargetTemplateName);
                            Logger.Debug($"After merging JSON for slotted template {mapping.TargetTemplateName}: {replacementText.Length} chars", "EnginePreProcessJson");
                        }

                        Logger.Debug($"Applying slotted template: {mapping.OriginalText.Substring(0, Math.Min(50, mapping.OriginalText.Length))}... -> {replacementText.Length} chars", "EnginePreProcessJson");
                        result = result.Replace(mapping.OriginalText, replacementText);
                        slottedCount++;
                    }
                }

                // Apply simple template mappings (components) - engine retrieves and merges JSON
                for (int j = 0; j < template.ReplacementMappings.Count; j++)
                {
                    var mapping = template.ReplacementMappings[j];
                    if (mapping.Type != ReplacementType.SimpleTemplate)
                        continue;

                    if (result.Contains(mapping.OriginalText))
                    {
                        // Get replacement text and merge JSON using loader's centralized method
                        var replacementText = mapping.ReplacementText;

                        // Handle AppView logic if needed
                        if (!string.IsNullOrEmpty(appView) && !string.IsNullOrEmpty(mapping.TargetTemplateName))
                        {
                            var appViewTemplate = GetTemplate(appSite, mapping.TargetTemplateName, preprocessedTemplates, appView, AppViewPrefix, useAppViewFallback: true);
                            if (appViewTemplate != null)
                            {
                                replacementText = appViewTemplate.OriginalContent;
                            }
                        }

                        // Merge JSON using loader's centralized method
                        if (enableJsonProcessing && !string.IsNullOrEmpty(mapping.TargetTemplateName))
                        {
                            replacementText = loader.MergeHtmlWithJson(replacementText, appSite, mapping.TargetTemplateName);
                            Logger.Debug($"After merging JSON for simple template {mapping.TargetTemplateName}: {replacementText.Length} chars", "EnginePreProcessJson");
                        }

                        Logger.Debug($"Applying simple template: {mapping.OriginalText} -> {replacementText.Length} chars", "EnginePreProcessJson");
                        result = result.Replace(mapping.OriginalText, replacementText);
                        simpleCount++;
                    }
                }
            }

            Logger.Debug($"Pass {currentPass} applied: {jsonPlaceholderCount} main JSON placeholders, {slottedCount} slotted, {simpleCount} simple", "EnginePreProcessJson");

        } while (result != previous && currentPass < maxPasses);

        Logger.Debug($"Replacement complete after {currentPass} passes, final size: {result.Length}", "EnginePreProcessJson");

        return result;
    }

    #endregion
}
