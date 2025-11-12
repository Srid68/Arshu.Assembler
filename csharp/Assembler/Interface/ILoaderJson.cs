namespace Assembler.Interface;

using Arshu.App.Json;
using System.Collections.Generic;

/// <summary>
/// Generic loader interface that provides template extraction (HTML and JSON)
/// and JSON merging with inheritance support for clean architecture
/// </summary>
public interface ILoaderJson<TTemplate>
{
    #region Loading Related

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    string SearchAppSites { get; }

    /// <summary>
    /// Loads and caches all templates from the specified directory
    /// Returns true if loading succeeded, false otherwise
    /// Templates are kept internal to the loader for immutability
    /// </summary>
    /// <param name="rootDirPath">Root directory path containing AppSites folder</param>
    /// <param name="appSite">Primary AppSite name to load</param>
    /// <param name="searchAppSites">Comma-delimited string of AppSite names for fallback templates</param>
    /// <returns>True if templates loaded successfully, false otherwise</returns>
    bool Load(string rootDirPath, string appSite, string searchAppSites);

    /// <summary>
    /// Checks if a template exists
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name</param>
    /// <returns>True if template exists, false otherwise</returns>
    bool HasTemplate(string appSite, string templateName);

    /// <summary>
    /// Gets all templates as a serialized JSON string for client-side template engine
    /// This is specifically for /api/templates endpoint
    /// Returns a JSON string - templates remain immutable (no references to internal state)
    /// </summary>
    /// <returns>Serialized JSON string containing all templates</returns>
    string GetAllTemplatesJson();

    /// <summary>
    /// Clears the template cache (for testing/hot reload)
    /// </summary>
    void ClearCache();

    #endregion

    #region Merging Related

    /// <summary>
    /// Gets a template's HTML content by appSite and name with optional AppView fallback
    /// Returns raw HTML only (no JSON merged)
    /// Searches in SearchAppSites if not found in primary appSite
    /// Templates stay immutable inside the loader
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (e.g., "Header", "Index")</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="appViewPrefix">Optional AppView prefix for fallback logic</param>
    /// <returns>Template HTML content or null if not found</returns>
    TTemplate? GetTemplateHtml(string appSite, string templateName, string? appView = null, string? appViewPrefix = null);

    /// <summary>
    /// Merges HTML string with JSON data using inheritance-aware JSON retrieval
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
    /// Engines cannot access raw JSON - all JSON retrieval and merging happens inside the loader
    /// Note: This method always works with string HTML, independent of TTemplate type
    /// </summary>
    /// <param name="html">The HTML string content to merge</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (for retrieving JSON data)</param>
    /// <returns>HTML string with JSON data merged, or original HTML if no JSON exists</returns>
    string MergeHtmlWithJson(string html, string appSite, string templateName);

    /// <summary>
    /// Applies all replacement mappings from all templates to the given content
    /// This is the core PreProcess engine logic - loader applies all mappings internally
    /// Engines call this method without needing direct access to template dictionary
    /// Only applicable for PreprocessedTemplate type (TTemplate = PreprocessedTemplate)
    /// </summary>
    /// <param name="content">The content HTML to apply replacements to</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="mainTemplate">The main template (for first-pass JSON placeholder logic)</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="appViewPrefix">Optional AppView prefix for fallback logic</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>Content with all replacement mappings applied</returns>
    string ApplyAllReplacementMappings(string content, string appSite, TTemplate? mainTemplate, string? appView, string? appViewPrefix, bool enableJsonProcessing);

    #endregion
}
