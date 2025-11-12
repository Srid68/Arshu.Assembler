using Arshu.Common;
using Assembler.Common;
using Assembler.Interface;
using System.Collections.Generic;

namespace Assembler.Engine.NormalJson;

// Always use Base JsonObject/JsonArray types for consistent processing
/// <summary>
/// NEW Normal engine that uses ILoaderJson interface and JsonObject
/// Based on EngineNormal but with improved architecture and type safety
/// </summary>
public class EngineNormalJson
{
    #region Merge Templates

    public string AppViewPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Merges templates by replacing placeholders with corresponding HTML
    /// This is a hybrid method that processes both slotted templates and simple placeholders
    /// JSON files with matching names are automatically merged with HTML templates before processing
    /// </summary>
    /// <param name="appSite">The application site name for template key generation</param>
    /// <param name="appView">The application view name (optional)</param>
    /// <param name="appFile">The application file name</param>
    /// <param name="loader">ILoaderJson instance providing templates and JSON merging</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>HTML with placeholders replaced</returns>
    public string MergeTemplates(string appSite, string appFile, string? appView, ILoaderJson<string> loader, bool enableJsonProcessing = true)
    {
        Logger.Debug($"MergeTemplates called: appSite={appSite}, appFile={appFile}, appView={appView ?? "null"}, enableJson={enableJsonProcessing}", "EngineNormalJson");

        if (loader == null)
        {
            Logger.Warn("No loader provided", "EngineNormalJson");
            return "";
        }

        // Get main template using ILoaderJson (includes AppView fallback logic)
        var contentHtml = loader.GetTemplateHtml(appSite, appFile, appView, AppViewPrefix);
        if (string.IsNullOrEmpty(contentHtml))
        {
            Logger.Warn($"Main template not found for appSite={appSite}, appFile={appFile}", "EngineNormalJson");
            return string.Empty;
        }

        Logger.Debug($"Main template found, html size: {contentHtml.Length}", "EngineNormalJson");

        // Merge main template with JSON data using loader's centralized method
        if (enableJsonProcessing)
        {
            contentHtml = loader.MergeHtmlWithJson(contentHtml, appSite, appFile);
            Logger.Debug($"After main JSON merge: {contentHtml.Length} chars", "EngineNormalJson");
        }

        // Simple loop like Go implementation - avoid StringBuilder overhead
        // Templates are now loaded on-demand via GetTemplate method
        string previous;
        int maxPasses = 10;
        int actualPasses = 0;
        for (int pass = 0; pass < maxPasses; pass++)
        {
            previous = contentHtml;
            actualPasses = pass + 1;

            Logger.Debug($"Pass {actualPasses}, current size: {contentHtml.Length}", "EngineNormalJson");

            contentHtml = MergeTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing);
            Logger.Debug($"After slot merge: {contentHtml.Length} chars", "EngineNormalJson");

            contentHtml = ReplaceTemplatePlaceholders(contentHtml, appSite, appView, loader, enableJsonProcessing);
            Logger.Debug($"After placeholder replacement: {contentHtml.Length} chars", "EngineNormalJson");

            if (contentHtml == previous)
            {
                Logger.Debug($"No changes in pass {actualPasses}, stopping", "EngineNormalJson");
                break;
            }
        }

        Logger.Debug($"MergeTemplates complete after {actualPasses} passes: output size={contentHtml.Length}", "EngineNormalJson");
        return contentHtml;

    }

    #endregion

    #region Get Template (Private)

    /// <summary>
    /// Gets a template with on-demand loading and JSON merging from ILoaderJson
    /// </summary>
    private string? GetTemplateWithJson(string appSite, string templateName, ILoaderJson<string> loader, string? appView, bool enableJsonProcessing)
    {
        // Get HTML template
        var html = loader.GetTemplateHtml(appSite, templateName, appView, AppViewPrefix);
        if (string.IsNullOrEmpty(html))
            return null;

        Logger.Debug($"GetTemplateWithJson: template={templateName}, html size={html.Length}", "EngineNormalJson");

        // Merge with JSON if enabled using loader's centralized method
        if (enableJsonProcessing)
        {
            var originalSize = html.Length;
            html = loader.MergeHtmlWithJson(html, appSite, templateName);
            Logger.Debug($"After JSON merge for {templateName}: size {originalSize} -> {html.Length}", "EngineNormalJson");
        }

        return html;
    }

    #endregion

    #region Slot Processing (Private)

    /// <summary>
    /// IndexOf-based version: Recursively merges a slotted template (e.g., center.html, columns.html) with content.html
    /// Slot patterns in content.html: {{#TemplateName}} ... {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}} ... {{/TemplateName}}
    /// </summary>
    /// <param name="contentHtml">The content HTML containing slot patterns</param>
    /// <param name="appSite">The application site name for template key generation</param>
    /// <param name="appView">Optional AppView for fallback logic</param>
    /// <param name="loader">ILoaderJson instance for on-demand template loading</param>
    /// <param name="enableJsonProcessing">Whether to enable JSON data processing</param>
    /// <returns>Merged HTML with slots filled</returns>
    private string MergeTemplateSlots(string contentHtml, string appSite, string? appView, ILoaderJson<string> loader, bool enableJsonProcessing)
    {
        if (string.IsNullOrEmpty(contentHtml))
            return contentHtml;

        string previous;
        do
        {
            previous = contentHtml;
            contentHtml = ProcessTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing);
        } while (contentHtml != previous);
        return contentHtml;
    }

    /// <summary>
    /// Helper method to process slotted templates using IndexOf
    /// </summary>
    private string ProcessTemplateSlots(string contentHtml, string appSite, string? appView, ILoaderJson<string> loader, bool enableJsonProcessing)
    {
        var result = contentHtml;
        var searchPos = 0;

        while (searchPos < result.Length)
        {
            // Look for opening tag {{#
            var openStart = result.IndexOf("{{#", searchPos);
            if (openStart == -1) break;

            // Find the end of the template name
            var openEnd = result.IndexOf("}}", openStart + 3);
            if (openEnd == -1) break;

            // Extract template name
            var templateName = result.Substring(openStart + 3, openEnd - openStart - 3).Trim();
            if (string.IsNullOrEmpty(templateName) || !CommonUtil.IsAlphaNumeric(templateName))
            {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            var closeTag = "{{/" + templateName + "}}";
            var closeStart = CommonUtil.FindMatchingCloseTag(result, openEnd + 2, "{{#" + templateName + "}}", closeTag);
            if (closeStart == -1)
            {
                searchPos = openStart + 1;
                continue;
            }

            // Extract inner content
            var innerStart = openEnd + 2;
            var innerContent = result.Substring(innerStart, closeStart - innerStart);

            // Load template with JSON on-demand
            var templateHtml = GetTemplateWithJson(appSite, templateName, loader, appView, enableJsonProcessing);

            if (!string.IsNullOrEmpty(templateHtml))
            {
                // Extract slot contents
                var slotContents = ExtractSlotContents(innerContent, appSite, appView, loader, enableJsonProcessing);

                // Replace slots in template
                var processedTemplate = templateHtml;
                foreach (var kvp in slotContents)
                {
                    processedTemplate = processedTemplate.Replace(kvp.Key, kvp.Value);
                }

                // Remove any remaining slot placeholders
                processedTemplate = CommonUtil.RemoveRemainingSlotPlaceholders(processedTemplate);

                // Replace the entire slotted section
                var fullMatch = result.Substring(openStart, closeStart + closeTag.Length - openStart);
                result = result.Replace(fullMatch, processedTemplate);
                searchPos = openStart + processedTemplate.Length;
            }
            else
            {
                searchPos = openStart + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Extract slot contents using IndexOf approach
    /// </summary>
    private Dictionary<string, string> ExtractSlotContents(string innerContent, string appSite, string? appView, ILoaderJson<string> loader, bool enableJsonProcessing)
    {
        var slotContents = new Dictionary<string, string>();
        var searchPos = 0;

        while (searchPos < innerContent.Length)
        {
            // Look for slot start {{@HTMLPLACEHOLDER
            var slotStart = innerContent.IndexOf("{{@HTMLPLACEHOLDER", searchPos);
            if (slotStart == -1) break;

            // Find the number (if any) and closing }}
            var afterPlaceholder = slotStart + 18; // Length of "{{@HTMLPLACEHOLDER"
            var slotNum = "";
            var pos = afterPlaceholder;

            // Extract slot number
            while (pos < innerContent.Length && char.IsDigit(innerContent[pos]))
            {
                slotNum += innerContent[pos];
                pos++;
            }

            // Check for closing }}
            if (pos + 1 >= innerContent.Length || innerContent.Substring(pos, 2) != "}}")
            {
                searchPos = slotStart + 1;
                continue;
            }

            var slotOpenEnd = pos + 2;

            // Find matching closing tag
            var closeTag = string.IsNullOrEmpty(slotNum) ? "{{/HTMLPLACEHOLDER}}" : $"{{{{/HTMLPLACEHOLDER{slotNum}}}}}";
            var openTag = string.IsNullOrEmpty(slotNum) ? "{{@HTMLPLACEHOLDER}}" : $"{{{{@HTMLPLACEHOLDER{slotNum}}}}}";

            var closeStart = CommonUtil.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag);
            if (closeStart == -1)
            {
                searchPos = slotStart + 1;
                continue;
            }

            // Extract slot content
            var slotContent = innerContent.Substring(slotOpenEnd, closeStart - slotOpenEnd);

            // Generate slot key
            var slotKey = string.IsNullOrEmpty(slotNum) ? "{{$HTMLPLACEHOLDER}}" : $"{{{{$HTMLPLACEHOLDER{slotNum}}}}}";

            // Process both slotted templates AND simple placeholders in slot content
            // This enables proper nested template processing to match the preprocessing implementation
            var recursiveResult = MergeTemplateSlots(slotContent, appSite, appView, loader, enableJsonProcessing);
            recursiveResult = ReplaceTemplatePlaceholders(recursiveResult, appSite, appView, loader, enableJsonProcessing);
            slotContents[slotKey] = recursiveResult;

            searchPos = closeStart + closeTag.Length;
        }

        return slotContents;
    }

    #endregion

    #region PlaceHolder Processing (Private)

    /// <summary>
    /// Helper method to process simple placeholders only (without slotted template processing)
    /// </summary>
    private string ReplaceTemplatePlaceholders(string html, string appSite, string? appView, ILoaderJson<string> loader, bool enableJsonProcessing)
    {
        var result = html;
        var searchPos = 0;

        while (searchPos < result.Length)
        {
            // Look for opening placeholder {{
            var openStart = result.IndexOf("{{", searchPos);
            if (openStart == -1) break;

            // Make sure it's not a slotted template or special placeholder
            if (openStart + 2 < result.Length && (result[openStart + 2] == '#' || result[openStart + 2] == '@' || result[openStart + 2] == '$' || result[openStart + 2] == '/'))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            var closeStart = result.IndexOf("}}", openStart + 2);
            if (closeStart == -1) break;

            // Extract placeholder name
            var placeholderName = result.Substring(openStart + 2, closeStart - openStart - 2).Trim();
            if (string.IsNullOrEmpty(placeholderName) || !CommonUtil.IsAlphaNumeric(placeholderName))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Load template with JSON on-demand
            var templateContent = GetTemplateWithJson(appSite, placeholderName, loader, appView, enableJsonProcessing);

            if (!string.IsNullOrEmpty(templateContent))
            {
                // Recursively process the loaded template
                var processedReplacement = ReplaceTemplatePlaceholders(templateContent, appSite, appView, loader, enableJsonProcessing);
                var placeholder = result.Substring(openStart, closeStart + 2 - openStart);
                result = result.Replace(placeholder, processedReplacement);
                searchPos = openStart + processedReplacement.Length;
            }
            else
            {
                searchPos = closeStart + 2;
            }
        }

        return result;
    }

    #endregion
}
