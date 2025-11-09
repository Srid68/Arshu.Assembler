<?php

namespace Assembler\Loader;

use Assembler\App\Json\JsonObject;

/// <summary>
/// Generic loader interface that provides template extraction (HTML and JSON)
/// and JSON merging with inheritance support for clean architecture
/// </summary>
interface ILoader
{
    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    public function getSearchAppSites(): string;

    /// <summary>
    /// Gets a template's HTML content by appSite and name with optional AppView fallback
    /// Returns raw HTML only (no JSON merged)
    /// Searches in SearchAppSites if not found in primary appSite
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (e.g., "Header", "Index")</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="appViewPrefix">Optional AppView prefix for fallback logic</param>
    /// <returns>Template HTML content or null if not found</returns>
    public function getTemplateHtml(string $appSite, string $templateName, ?string $appView = null, ?string $appViewPrefix = null): ?string;

    /// <summary>
    /// Gets parsed JSON data for a template
    /// Returns null if no JSON file exists for the template
    /// Searches in SearchAppSites if not found in primary appSite
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name</param>
    /// <returns>Parsed JsonObject or null if no JSON file exists</returns>
    public function getTemplateJson(string $appSite, string $templateName): ?JsonObject;

    /// <summary>
    /// Merges HTML string with JSON data using inheritance-aware JSON retrieval
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// The loader internally retrieves JSON (with inheritance resolution) and merges it with HTML
    /// Note: This method always works with string HTML, independent of TTemplate type
    /// </summary>
    /// <param name="html">The HTML string content to merge</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (for retrieving JSON data)</param>
    /// <returns>HTML string with JSON data merged, or original HTML if no JSON exists</returns>
    public function mergeHtmlWithJson(string $html, string $appSite, string $templateName): string;

    /// <summary>
    /// Checks if a template exists
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name</param>
    /// <returns>True if template exists, false otherwise</returns>
    public function hasTemplate(string $appSite, string $templateName): bool;

    /// <summary>
    /// Clears the template cache (for testing/hot reload)
    /// </summary>
    public static function clearCache(): void;
}
