using System;
using System.Collections.Generic;
using Arshu.App.Json;
using Arshu.Common;
using Assembler.Common;
using Assembler.Model;
using Assembler.Loader;
using Assembler.Interface;

namespace Assembler.Engine.PreProcess;

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
    /// <param name="loader">ILoaderPreProcess instance providing preprocessed templates</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced using preprocessed structures</returns>
    public string MergeTemplates(string appSite, string appFile, string? appView, ILoaderPreProcess loader, bool enableJsonProcessing = true)
    {
        Logger.Debug($"MergeTemplates called: appSite={appSite}, appFile={appFile}, appView={appView ?? "null"}, enableJson={enableJsonProcessing}", "EnginePreProcess");

        if (loader == null)
        {
            Logger.Warn("No loader provided", "EnginePreProcess");
            return "";
        }

        // Get main template using ILoaderPreProcess (includes AppView fallback and SearchAppSites logic)
        var mainPreprocessed = loader.GetTemplateHtml(appSite, appFile, appView, AppViewPrefix);
        if (mainPreprocessed == null)
        {
            Logger.Warn($"Main template not found for appSite={appSite}, appFile={appFile}", "EnginePreProcess");
            return "";
        }

        Logger.Debug($"Main template found, original size: {mainPreprocessed.OriginalContent.Length}", "EnginePreProcess");

        // Start with original content
        var contentHtml = mainPreprocessed.OriginalContent;

        // Merge JSON into main template first using loader's centralized method (for consistency with other engines)
        if (enableJsonProcessing)
        {
            contentHtml = loader.MergeHtmlWithJson(contentHtml, appSite, appFile);
            Logger.Debug($"After main template JSON merge: {contentHtml.Length} chars", "EnginePreProcess");
        }

        // Apply ALL replacement mappings from ALL templates using loader's method
        contentHtml = loader.ApplyAllReplacementMappings(contentHtml, appSite, mainPreprocessed, appView, AppViewPrefix, enableJsonProcessing);

        Logger.Debug($"MergeTemplates complete: output size={contentHtml.Length}", "EnginePreProcess");

        return contentHtml;
    }

    #endregion
}