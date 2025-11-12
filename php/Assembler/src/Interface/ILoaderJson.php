<?php

namespace Assembler\Interface;

use Assembler\App\Json\JsonObject;
use Assembler\Model\PreprocessedTemplate;

/// <summary>
/// Generic loader interface that provides template extraction (HTML and JSON)
/// and JSON merging with inheritance support for clean architecture
/// Equivalent to C# ILoaderJson<TTemplate>
/// In PHP, getTemplateHtml returns mixed (string|PreprocessedTemplate|null) depending on implementation
/// </summary>
interface ILoaderJson
{
    // Loading Related Methods

    /// <summary>
    /// Gets the search AppSites for template fallback resolution
    /// Comma-delimited string of AppSite names
    /// </summary>
    public function getSearchAppSites(): string;

    /// <summary>
    /// Checks if a template exists
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name</param>
    /// <returns>True if template exists, false otherwise</returns>
    public function hasTemplate(string $appSite, string $templateName): bool;

    /// <summary>
    /// Gets all templates as a serialized JSON string for client-side template engine
    /// This is specifically for /api/templates endpoint
    /// </summary>
    /// <returns>Serialized JSON string containing all templates</returns>
    public function getAllTemplatesJson(): string;

    /// <summary>
    /// Clears the template cache (for testing/hot reload)
    /// </summary>
    public static function clearCache(): void;

    // Merging Related Methods

    /// <summary>
    /// Gets a template by appSite and name with optional AppView fallback
    /// Returns mixed - either string (for LoaderNormalJson) or PreprocessedTemplate (for LoaderPreProcessJson) or null
    /// Searches in SearchAppSites if not found in primary appSite
    /// </summary>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (e.g., "Header", "Index")</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="appViewPrefix">Optional AppView prefix for fallback logic</param>
    /// <returns>Template content (string or PreprocessedTemplate) or null if not found</returns>
    public function getTemplateHtml(string $appSite, string $templateName, ?string $appView = null, ?string $appViewPrefix = null): mixed;

    /// <summary>
    /// Merges HTML string with JSON data using inheritance-aware JSON retrieval
    /// This centralizes JSON merging logic in the loader for clean architecture
    /// </summary>
    /// <param name="html">The HTML string content to merge</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateName">The template name (for retrieving JSON data)</param>
    /// <returns>HTML string with JSON data merged, or original HTML if no JSON exists</returns>
    public function mergeHtmlWithJson(string $html, string $appSite, string $templateName): string;

    /// <summary>
    /// Applies all replacement mappings from all templates to the given content
    /// This is the core PreProcess engine logic - loader applies all mappings internally
    /// Only applicable for PreprocessedTemplate type implementations
    /// </summary>
    /// <param name="content">The content HTML to apply replacements to</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="mainTemplate">The main template (for first-pass JSON placeholder logic)</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="appViewPrefix">Optional AppView prefix for fallback logic</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>Content with all replacement mappings applied</returns>
    public function applyAllReplacementMappings(string $content, string $appSite, mixed $mainTemplate, ?string $appView, ?string $appViewPrefix, bool $enableJsonProcessing): string;
}
