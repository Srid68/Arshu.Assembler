using System.Collections.Generic;

namespace Assembler.Interface;

/// <summary>
/// Loader interface specifically for EngineNormal
/// Provides raw HTML/JSON template access for Normal engine's runtime merging approach
/// Independent interface to ensure no coupling with other engine implementations
/// </summary>
public interface ILoaderNormal
{
    #region Loading Related

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    string SearchAppSites { get; }

    /// <summary>
    /// Loads all templates from the specified directory
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
    string? GetTemplateHtml(string appSite, string templateName, string? appView = null, string? appViewPrefix = null);

    /// <summary>
    /// Merges HTML string with JSON data for the specified template
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
    /// Engines cannot access raw JSON - all JSON retrieval and merging happens inside the loader
    /// </summary>
    /// <param name="html">The HTML string content to merge</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (for retrieving JSON data)</param>
    /// <returns>HTML string with JSON data merged, or original HTML if no JSON exists</returns>
    string MergeHtmlWithJson(string html, string appSite, string templateName);

    #endregion
}
