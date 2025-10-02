using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Arshu.App.Json;
using Assembler.TemplateCommon;
using Assembler.TemplateModel;

namespace Assembler.TemplateLoader;

// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;
using JsonArray = Arshu.App.Json.JsonArray;

/// <summary>
/// Handles loading and caching of HTML templates from the file system
/// </summary>
public static class LoaderPreProcess
{
    #region Loading Templates

    private static readonly Dictionary<string, PreprocessedSiteTemplates> _preprocessedTemplatesCache = new();

    /// <summary>
    /// Loads and preprocesses HTML files from the specified application site directory into structured templates, caching the output per appSite and rootDirName
    /// </summary>
    /// <param name="rootDirName">The root directory name (e.g., "wwwroot")</param>
    /// <param name="appSite">The application site name</param>
    /// <returns>PreprocessedSiteTemplates containing structured template data</returns>
    public static PreprocessedSiteTemplates LoadProcessGetTemplateFiles(string rootDirPath, string appSite)
    {
        var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite;
        if (_preprocessedTemplatesCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var result = new PreprocessedSiteTemplates
        {
            SiteName = appSite
        };

        var appSitesPath = Path.Combine(rootDirPath, "AppSites", appSite);

        if (!Directory.Exists(appSitesPath))
        {
            _preprocessedTemplatesCache[cacheKey] = result;
            return result;
        }

        foreach (var file in Directory.GetFiles(appSitesPath, "*.html", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var key = ($"{appSite.ToLowerInvariant()}_{fileName.ToLowerInvariant()}");
            var content = File.ReadAllText(file);

            // Look for corresponding JSON file
            var jsonFile = Path.ChangeExtension(file, ".json");
            string? jsonContent = null;
            if (File.Exists(jsonFile))
            {
                jsonContent = File.ReadAllText(jsonFile);
            }

            // Store raw template for backward compatibility
            result.RawTemplates[key] = content;
            result.TemplateKeys.Add(key);

            // Preprocess the template with JSON data
            var preprocessed = PreprocessTemplate(content, jsonContent, appSite, key);
            result.Templates[key] = preprocessed;
        }

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        CreateAllReplacementMappingsForSite(result, appSite);

        _preprocessedTemplatesCache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Clear all cached templates (useful for testing or when templates change)
    /// </summary>
    public static void ClearCache()
    {
        _preprocessedTemplatesCache.Clear();
    }

    #endregion

    #region PreProcess and Mapping

    /// <summary>
    /// Creates a preprocessed template by parsing its structure and any associated JSON data.
    /// This method handles parsing and JSON preprocessing, leaving only merging to the template engine.
    /// </summary>
    /// <param name="content">The template content to parse</param>
    /// <param name="jsonContent">The JSON content to parse (optional)</param>
    /// <param name="appSite">The application site name</param>
    /// <param name="templateKey">The template key for reference</param>
    /// <returns>PreprocessedTemplate containing parsed structure and preprocessed JSON</returns>
    private static PreprocessedTemplate PreprocessTemplate(string content, string? jsonContent, string appSite, string templateKey)
    {
        var template = new PreprocessedTemplate
        {
            OriginalContent = content
        };

        if (string.IsNullOrEmpty(content))
            return template;

        // Parse JSON data into a structure
        if (!string.IsNullOrEmpty(jsonContent))
        {
            template.JsonData = PreprocessJsonData(jsonContent);
        }

        // Parse template structure
        ParseSlottedTemplates(content, appSite, template);
        ParsePlaceholderTemplates(content, appSite, template);

        // Preprocess JSON templates - analyze and prepare JSON placeholders and blocks
        if (template.HasJsonData)
        {
            PreprocessJsonTemplates(template);
        }

        return template;
    }

    /// <summary>
    /// Preprocesses JSON data into a JsonObject structure for efficient template merging
    /// </summary>
    /// <param name="jsonContent">The JSON content to preprocess</param>
    /// <returns>JsonObject containing preprocessed JSON data</returns>
    public static JsonObject PreprocessJsonData(string jsonContent)
    {
        return JsonConverter.ParseJsonString(jsonContent);
    }

    /// <summary>
    /// Creates ALL replacement mappings for all templates after they are loaded
    /// This ensures the PreProcess engine only does merging, no processing logic
    /// Critical architectural method - moves ALL processing from engine to loader
    /// </summary>
    /// <param name="siteTemplates">All templates for the site</param>
    /// <param name="appSite">The application site name</param>
    private static void CreateAllReplacementMappingsForSite(PreprocessedSiteTemplates siteTemplates, string appSite)
    {
        // Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
        foreach (var templateKvp in siteTemplates.Templates)
        {
            var template = templateKvp.Value;
            // Create replacement mappings for JSON array blocks (including negative blocks)
            CreateJsonArrayReplacementMappings(template, template.OriginalContent);
        }

        // Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
        foreach (var templateKvp in siteTemplates.Templates)
        {
            var template = templateKvp.Value;
            // Create replacement mappings for simple placeholders
            CreatePlaceholderReplacementMappings(template, siteTemplates.Templates, appSite);
        }

        // Phase 3: Create slotted template replacement mappings (may depend on other templates)
        foreach (var templateKvp in siteTemplates.Templates)
        {
            var template = templateKvp.Value;
            // Create replacement mappings for slotted templates
            CreateSlottedTemplateReplacementMappings(template, siteTemplates.Templates, appSite);
        }
    }

    /// <summary>
    /// Creates replacement mappings for simple placeholders ({{templatename}})
    /// This moves ALL placeholder processing logic from PreProcess engine to TemplateLoader
    /// </summary>
    private static void CreatePlaceholderReplacementMappings(PreprocessedTemplate template, Dictionary<string, PreprocessedTemplate> allTemplates, string appSite)
    {
        if (!template.HasPlaceholders)
            return;

        var content = template.OriginalContent;

        foreach (var placeholder in template.Placeholders)
        {
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{placeholder.TemplateKey}";
            if (allTemplates.TryGetValue(targetTemplateKey, out var targetTemplate))
            {
                // Use the target template's original content without applying other replacement mappings
                // This prevents circular dependencies and infinite recursion
                var processedTemplate = targetTemplate.OriginalContent;

                // Create the replacement mapping
                template.ReplacementMappings.Add(new ReplacementMapping
                {
                    OriginalText = placeholder.FullMatch,
                    ReplacementText = processedTemplate,
                    Type = ReplacementType.SimpleTemplate
                });
            }
        }
    }

    /// <summary>
    /// Creates replacement mappings for slotted templates ({{#templatename}}...{{/templatename}})
    /// This moves ALL slotted template processing logic from PreProcess engine to TemplateLoader
    /// </summary>
    private static void CreateSlottedTemplateReplacementMappings(PreprocessedTemplate template, Dictionary<string, PreprocessedTemplate> allTemplates, string appSite)
    {
        if (!template.HasSlottedTemplates)
            return;

        foreach (var slottedTemplate in template.SlottedTemplates)
        {
            // Use the pre-parsed FullMatch to ensure exact text matching
            var fullMatch = slottedTemplate.FullMatch;

            // Get the target template using appsite_templatename format
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{slottedTemplate.TemplateKey}";

            if (allTemplates.TryGetValue(targetTemplateKey, out var targetTemplate))
            {
                // Start with the target template's original content
                var processedTemplate = targetTemplate.OriginalContent;

                // Process slots using the pre-parsed slot data
                foreach (var slot in slottedTemplate.Slots)
                {
                    var processedSlotContent = ProcessSlotContentForReplacementMapping(slot, allTemplates, appSite);
                    processedTemplate = processedTemplate.Replace(slot.SlotKey, processedSlotContent);
                }

                // Handle default slot if no explicit slots
                if (!slottedTemplate.Slots.Any())
                {
                    // Use the pre-parsed InnerContent instead of recalculating
                    var actualInnerContent = slottedTemplate.InnerContent;

                    if (!string.IsNullOrWhiteSpace(actualInnerContent))
                    {
                        var defaultSlotKey = "{{$HTMLPLACEHOLDER}}";
                        if (processedTemplate.Contains(defaultSlotKey))
                        {
                            processedTemplate = processedTemplate.Replace(defaultSlotKey, actualInnerContent.Trim());
                        }
                    }
                }

                // Remove any remaining slot placeholders
                processedTemplate = TemplateUtils.RemoveRemainingSlotPlaceholders(processedTemplate);

                // Create the replacement mapping using the exact FullMatch text
                template.ReplacementMappings.Add(new ReplacementMapping
                {
                    OriginalText = fullMatch,
                    ReplacementText = processedTemplate,
                    Type = ReplacementType.SlottedTemplate
                });
            }
        }
    }

    /// <summary>
    /// Processes slot content for creating replacement mappings
    /// This recursively processes nested templates and placeholders
    /// </summary>
    private static string ProcessSlotContentForReplacementMapping(SlotPlaceholder slot, Dictionary<string, PreprocessedTemplate> allTemplates, string appSite)
    {
        var result = slot.Content;

        // Process nested slotted templates
        foreach (var nestedSlottedTemplate in slot.NestedSlottedTemplates)
        {
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{nestedSlottedTemplate.TemplateKey}";
            if (allTemplates.TryGetValue(targetTemplateKey, out var targetTemplate))
            {
                // Use the target template's original content without applying replacement mappings
                // This prevents circular dependencies during replacement mapping creation
                var processedTemplate = targetTemplate.OriginalContent;

                // Process nested slots
                foreach (var nestedSlot in nestedSlottedTemplate.Slots)
                {
                    var processedNestedSlotContent = ProcessSlotContentForReplacementMapping(nestedSlot, allTemplates, appSite);
                    processedTemplate = processedTemplate.Replace(nestedSlot.SlotKey, processedNestedSlotContent);
                }

                // Remove remaining slot placeholders
                processedTemplate = TemplateUtils.RemoveRemainingSlotPlaceholders(processedTemplate);

                // Replace in result
                result = result.Replace(nestedSlottedTemplate.FullMatch, processedTemplate);
            }
        }

        // Process nested simple placeholders
        foreach (var nestedPlaceholder in slot.NestedPlaceholders)
        {
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{nestedPlaceholder.TemplateKey}";
            if (allTemplates.TryGetValue(targetTemplateKey, out var targetTemplate))
            {
                // Use the target template's original content
                var processedTemplate = targetTemplate.OriginalContent;

                // Replace in result
                result = result.Replace(nestedPlaceholder.FullMatch, processedTemplate);
            }
        }

        return result;
    }

    #endregion

    #region Slot Processing

    /// <summary>
    /// IndexOf-based version: Parses slotted templates in the content and adds them to the preprocessed template
    /// </summary>
    private static void ParseSlottedTemplates(string content, string appSite, PreprocessedTemplate template)
    {
        var searchPos = 0;

        while (searchPos < content.Length)
        {
            // Look for opening tag {{#
            var openStart = content.IndexOf("{{#", searchPos);
            if (openStart == -1) break;

            // Find the end of the template name
            var openEnd = content.IndexOf("}}", openStart + 3);
            if (openEnd == -1) break;

            // Extract template name
            var templateName = content.Substring(openStart + 3, openEnd - openStart - 3).Trim();
            if (string.IsNullOrEmpty(templateName) || !TemplateUtils.IsAlphaNumeric(templateName))
            {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            var closeTag = "{{/" + templateName + "}}";
            var closeStart = TemplateUtils.FindMatchingCloseTag(content, openEnd + 2, "{{#" + templateName + "}}", closeTag);
            if (closeStart == -1)
            {
                searchPos = openStart + 1;
                continue;
            }

            // Extract inner content
            var innerStart = openEnd + 2;
            var innerContent = content.Substring(innerStart, closeStart - innerStart);
            var fullMatch = content.Substring(openStart, closeStart + closeTag.Length - openStart);

            // Create slotted template structure
            var slottedTemplate = new SlottedTemplate
            {
                Name = templateName,
                StartIndex = openStart,
                EndIndex = closeStart + closeTag.Length,
                FullMatch = fullMatch,
                InnerContent = innerContent,
                TemplateKey = templateName.ToLowerInvariant() // Simple template name since appSite is passed as parameter
            };

            // Parse slots within this slotted template using IndexOf
            ParseSlots(innerContent, slottedTemplate, appSite);

            template.SlottedTemplates.Add(slottedTemplate);
            searchPos = closeStart + closeTag.Length;
        }
    }

    /// <summary>
    /// IndexOf-based version: Parses slots within a slotted template
    /// </summary>
    private static void ParseSlots(string innerContent, SlottedTemplate slottedTemplate, string appSite)
    {
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

            var closeStart = TemplateUtils.FindMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag);
            if (closeStart == -1)
            {
                searchPos = slotStart + 1;
                continue;
            }

            // Extract slot content
            var slotContent = innerContent.Substring(slotOpenEnd, closeStart - slotOpenEnd);

            // Generate slot key
            var slotKey = string.IsNullOrEmpty(slotNum) ? "{{$HTMLPLACEHOLDER}}" : $"{{{{$HTMLPLACEHOLDER{slotNum}}}}}";

            // Create slot structure
            var slot = new SlotPlaceholder
            {
                Number = slotNum,
                StartIndex = slotStart,
                EndIndex = closeStart + closeTag.Length,
                Content = slotContent,
                SlotKey = slotKey,
                OpenTag = openTag,
                CloseTag = closeTag
            };

            // Parse nested templates within the slot content
            ParseNestedTemplatesInSlot(slot, slottedTemplate.JsonData, appSite);

            slottedTemplate.Slots.Add(slot);
            searchPos = closeStart + closeTag.Length;
        }
    }

    /// <summary>
    /// Parses nested templates within slot content (simple version without recursion)
    /// </summary>
    private static void ParseNestedTemplatesInSlot(SlotPlaceholder slot, JsonObject? jsonData, string appSite)
    {
        if (string.IsNullOrEmpty(slot.Content))
            return;

        // Parse simple placeholders like {{ComponentName}}
        var placeholderRegex = new Regex(@"\{\{([^#/@}]+)\}\}", RegexOptions.IgnoreCase);
        var placeholderMatches = placeholderRegex.Matches(slot.Content);

        foreach (Match match in placeholderMatches)
        {
            var templateName = match.Groups[1].Value.Trim();
            var templateKey = templateName.ToLowerInvariant(); // Simple template name since appSite is passed as parameter

            slot.NestedPlaceholders.Add(new TemplatePlaceholder
            {
                Name = templateName,
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length,
                FullMatch = match.Value,
                TemplateKey = templateKey,
                JsonData = jsonData
            });
        }

        // Parse slotted templates like {{#TemplateName}} ... {{/TemplateName}}
        var slottedRegex = new Regex(@"\{\{#([^}]+)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var slottedMatches = slottedRegex.Matches(slot.Content);

        foreach (Match match in slottedMatches)
        {
            var templateName = match.Groups[1].Value.Trim();
            var innerContent = match.Groups[2].Value;
            var templateKey = templateName.ToLowerInvariant(); // Simple template name since appSite is passed as parameter

            var nestedSlottedTemplate = new SlottedTemplate
            {
                Name = templateName,
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length,
                FullMatch = match.Value,
                InnerContent = innerContent,
                TemplateKey = templateKey,
                JsonData = jsonData
            };

            // Parse slots within this nested slotted template
            ParseSlots(innerContent, nestedSlottedTemplate, appSite);

            slot.NestedSlottedTemplates.Add(nestedSlottedTemplate);
        }
    }

    #endregion

    #region PlaceHolder Processing

    /// <summary>
    /// IndexOf-based version: Parses simple placeholders in the content and adds them to the preprocessed template
    /// </summary>
    private static void ParsePlaceholderTemplates(string content, string appSite, PreprocessedTemplate template)
    {
        var searchPos = 0;

        while (searchPos < content.Length)
        {
            // Look for opening placeholder {{
            var openStart = content.IndexOf("{{", searchPos);
            if (openStart == -1) break;

            // Make sure it's not a slotted template or special placeholder
            if (openStart + 2 < content.Length && (content[openStart + 2] == '#' || content[openStart + 2] == '@' || content[openStart + 2] == '$' || content[openStart + 2] == '/'))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            var closeStart = content.IndexOf("}}", openStart + 2);
            if (closeStart == -1) break;

            // Extract placeholder name
            var placeholderName = content.Substring(openStart + 2, closeStart - openStart - 2).Trim();
            if (string.IsNullOrEmpty(placeholderName) || !TemplateUtils.IsAlphaNumeric(placeholderName))
            {
                searchPos = openStart + 2;
                continue;
            }

            // Create placeholder structure
            var placeholder = new TemplatePlaceholder
            {
                Name = placeholderName,
                StartIndex = openStart,
                EndIndex = closeStart + 2,
                FullMatch = content.Substring(openStart, closeStart + 2 - openStart),
                TemplateKey = placeholderName.ToLowerInvariant() // Simple template name since appSite is passed as parameter
            };

            template.Placeholders.Add(placeholder);
            searchPos = closeStart + 2;
        }
    }

    #endregion

    #region Json Processing

    /// <summary>
    /// Preprocesses JSON templates by creating complete replacement mappings
    /// This creates structured data that the PreProcess engine can apply directly without any processing
    /// </summary>
    /// <param name="template">The template to preprocess JSON for</param>
    private static void PreprocessJsonTemplates(PreprocessedTemplate template)
    {
        if (template.JsonData == null)
            return;

        var content = template.OriginalContent;

        // Step 1: Create replacement mappings for JSON array blocks
        CreateJsonArrayReplacementMappings(template, content);

        // Step 2: Create replacement mappings for JSON placeholders  
        CreateJsonPlaceholderReplacementMappings(template, content);

        // Note: No processing here - only creating replacement mappings for the PreProcess engine
    }

    /// <summary>
    /// Creates replacement mappings for JSON array blocks ({{@array}}...{{/array}} patterns)
    /// This creates direct string replacements without any processing logic
    /// </summary>
    private static void CreateJsonArrayReplacementMappings(PreprocessedTemplate template, string content)
    {
        if (template.JsonData == null) return;

        foreach (var jsonKvp in template.JsonData)
        {
            if (jsonKvp.Value is JsonArray dataList)
            {
                var jsonKey = jsonKvp.Key;

                // Try to find a matching template block for this JSON array
                var keyNorm = jsonKey.ToLowerInvariant();
                var possibleTags = new[] { jsonKey, keyNorm, keyNorm.TrimEnd('s'), keyNorm + "s" };

                foreach (var tag in possibleTags)
                {
                    string blockStartTag = "{{@" + tag + "}}";
                    string blockEndTag = "{{/" + tag + "}}";

                    int startIdx = content.IndexOf(blockStartTag, StringComparison.OrdinalIgnoreCase);
                    if (startIdx != -1)
                    {
                        int searchFrom = startIdx + blockStartTag.Length;
                        int endIdx = content.IndexOf(blockEndTag, searchFrom, StringComparison.OrdinalIgnoreCase);

                        if (endIdx != -1 && endIdx > startIdx)
                        {
                            // Found a valid block - extract content and process it completely
                            string blockContent = content.Substring(startIdx + blockStartTag.Length, endIdx - (startIdx + blockStartTag.Length));
                            string fullBlock = content.Substring(startIdx, endIdx + blockEndTag.Length - startIdx);

                            // Process the array content completely here
                            string processedArrayContent = ProcessArrayBlockContentSafely(blockContent, dataList);

                            // Create direct replacement mapping
                            template.ReplacementMappings.Add(new ReplacementMapping
                            {
                                StartIndex = startIdx,
                                EndIndex = endIdx + blockEndTag.Length,
                                OriginalText = fullBlock,
                                ReplacementText = processedArrayContent,
                                Type = ReplacementType.JsonPlaceholder
                            });

                            // Handle empty array blocks with safety checks
                            string emptyBlockStart = "{{^" + tag + "}}";
                            string emptyBlockEnd = "{{/" + tag + "}}";
                            int emptyStartIdx = content.IndexOf(emptyBlockStart, StringComparison.OrdinalIgnoreCase);
                            if (emptyStartIdx != -1)
                            {
                                // Search for the closing tag AFTER the opening tag to avoid ambiguity
                                int emptySearchFrom = emptyStartIdx + emptyBlockStart.Length;
                                int emptyEndIdx = content.IndexOf(emptyBlockEnd, emptySearchFrom, StringComparison.OrdinalIgnoreCase);

                                if (emptyEndIdx != -1 && emptyEndIdx > emptyStartIdx + emptyBlockStart.Length)
                                {
                                    int contentStart = emptyStartIdx + emptyBlockStart.Length;
                                    int contentLength = emptyEndIdx - contentStart;

                                    // Additional safety check for valid length
                                    if (contentLength >= 0 && contentStart + contentLength <= content.Length)
                                    {
                                        string emptyBlockContent = content.Substring(contentStart, contentLength);
                                        string fullEmptyBlock = content.Substring(emptyStartIdx, emptyEndIdx + emptyBlockEnd.Length - emptyStartIdx);
                                        string emptyReplacement = dataList.Count == 0 ? emptyBlockContent : "";

                                        template.ReplacementMappings.Add(new ReplacementMapping
                                        {
                                            StartIndex = emptyStartIdx,
                                            EndIndex = emptyEndIdx + emptyBlockEnd.Length,
                                            OriginalText = fullEmptyBlock,
                                            ReplacementText = emptyReplacement,
                                            Type = ReplacementType.JsonPlaceholder
                                        });
                                    }
                                }
                            }

                            break; // Process only the first matching template for this JSON key
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Safely processes array block content by iterating through JSON array data and replacing placeholders
    /// This method handles all processing logic safely without causing substring errors
    /// </summary>
    /// <param name="blockContent">The array block content to process</param>
    /// <param name="arrayData">The JSON array data</param>
    /// <returns>Fully processed content ready for direct replacement</returns>
    private static string ProcessArrayBlockContentSafely(string blockContent, JsonArray arrayData)
    {
        try
        {
            string mergedBlock = "";

            // Process each item in the array data
            foreach (var item in arrayData)
            {
                if (item is JsonObject jsonItem)
                {
                    string itemBlock = blockContent;

                    // Replace all placeholders for this item
                    foreach (var kvp in jsonItem)
                    {
                        string placeholder = "{{$" + kvp.Key + "}}";
                        string valueStr = kvp.Value?.ToString() ?? "";
                        itemBlock = ReplaceAllCaseInsensitive(itemBlock, placeholder, valueStr);
                    }

                    // Handle conditional blocks for this item safely
                    itemBlock = ProcessConditionalBlocksSafely(itemBlock, jsonItem);

                    mergedBlock += itemBlock;
                }
            }

            return mergedBlock;
        }
        catch (Exception)
        {
            // If processing fails, return original content
            return blockContent;
        }
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
    /// Safely processes conditional blocks without causing substring errors
    /// </summary>
    private static string ProcessConditionalBlocksSafely(string content, JsonObject jsonItem)
    {
        try
        {
            string result = content;

            // Find all conditional keys in the content
            var conditionalKeys = FindConditionalKeysInContent(result);

            foreach (var condKey in conditionalKeys)
            {
                bool condValue = GetConditionValue(jsonItem, condKey);
                result = ProcessConditionalBlockSafely(result, condKey, condValue);
            }

            return result;
        }
        catch (Exception)
        {
            // If processing fails, return original content
            return content;
        }
    }

    /// <summary>
    /// Helper method to find conditional keys in content
    /// </summary>
    private static HashSet<string> FindConditionalKeysInContent(string content)
    {
        var conditionalKeys = new HashSet<string>();
        int condIdx = 0;

        while (true)
        {
            int condStart = content.IndexOf("{{@", condIdx, StringComparison.OrdinalIgnoreCase);
            if (condStart == -1) break;
            int condEnd = content.IndexOf("}}", condStart, StringComparison.OrdinalIgnoreCase);
            if (condEnd == -1) break;
            string condKey = content.Substring(condStart + 3, condEnd - (condStart + 3)).Trim();
            conditionalKeys.Add(condKey);
            condIdx = condEnd + 2;
        }

        return conditionalKeys;
    }

    /// <summary>
    /// Helper method to get condition value from item data
    /// </summary>
    private static bool GetConditionValue(JsonObject item, string condKey)
    {
        // First try exact match
        if (item.ContainsKey(condKey))
        {
            var condObj = item[condKey];
            if (condObj != null)
            {
                if (condObj is bool boolValue)
                    return boolValue;
                else if (condObj is string strValue && bool.TryParse(strValue, out bool sb))
                    return sb;
                else if (condObj is int intValue)
                    return intValue != 0;
                else if (condObj is long longValue)
                    return longValue != 0;
                else if (condObj is double doubleValue)
                    return doubleValue != 0.0;
                else if (condObj is decimal decimalValue)
                    return decimalValue != 0m;
            }
        }

        // If exact match fails, try case-insensitive match
        foreach (var kvp in item)
        {
            if (string.Equals(kvp.Key, condKey, StringComparison.OrdinalIgnoreCase))
            {
                var condObjCaseInsensitive = kvp.Value;
                if (condObjCaseInsensitive != null)
                {
                    if (condObjCaseInsensitive is bool boolValue)
                        return boolValue;
                    else if (condObjCaseInsensitive is string strValue && bool.TryParse(strValue, out bool sb))
                        return sb;
                    else if (condObjCaseInsensitive is int intValue)
                        return intValue != 0;
                    else if (condObjCaseInsensitive is long longValue)
                        return longValue != 0;
                    else if (condObjCaseInsensitive is double doubleValue)
                        return doubleValue != 0.0;
                    else if (condObjCaseInsensitive is decimal decimalValue)
                        return decimalValue != 0m;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Safely processes a single conditional block without causing substring errors
    /// </summary>
    private static string ProcessConditionalBlockSafely(string input, string key, bool condition)
    {
        try
        {
            // Support both space variants: {{ /Key}} and {{/Key}}
            var conditionTags = new[]
            {
                ("{{@" + key + "}}", "{{ /" + key + "}}"),
                ("{{@" + key + "}}", "{{/" + key + "}}")
            };

            foreach (var (condStart, condEnd) in conditionTags)
            {
                int startIdx = input.IndexOf(condStart, StringComparison.OrdinalIgnoreCase);
                int endIdx = input.IndexOf(condEnd, StringComparison.OrdinalIgnoreCase);

                while (startIdx != -1 && endIdx != -1)
                {
                    // Safety check to prevent negative length
                    int contentStart = startIdx + condStart.Length;
                    if (endIdx > contentStart)
                    {
                        string content = input.Substring(contentStart, endIdx - contentStart);
                        input = condition
                            ? input.Substring(0, startIdx) + content + input.Substring(endIdx + condEnd.Length)
                            : input.Substring(0, startIdx) + input.Substring(endIdx + condEnd.Length);
                    }
                    else
                    {
                        // Malformed conditional block - skip it
                        break;
                    }

                    startIdx = input.IndexOf(condStart, StringComparison.OrdinalIgnoreCase);
                    endIdx = input.IndexOf(condEnd, StringComparison.OrdinalIgnoreCase);
                }
            }

            return input;
        }
        catch (Exception)
        {
            // If processing fails, return original input
            return input;
        }
    }

    /// <summary>
    /// Creates replacement mappings for JSON placeholders ({{$key}} patterns)
    /// This creates direct string replacements without any processing logic
    /// </summary>
    private static void CreateJsonPlaceholderReplacementMappings(PreprocessedTemplate template, string content)
    {
        if (template.JsonData == null) return;

        foreach (var kvp in template.JsonData)
        {
            if (kvp.Value is string stringValue)
            {
                // Handle both {{$key}} and {{key}} patterns
                var placeholders = new[]
                {
                    "{{$" + kvp.Key + "}}",
                    "{{" + kvp.Key + "}}"
                };

                foreach (var placeholder in placeholders)
                {
                    if (content.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                    {
                        // Create replacement mapping for direct replacement
                        template.ReplacementMappings.Add(new ReplacementMapping
                        {
                            OriginalText = placeholder,
                            ReplacementText = stringValue,
                            Type = ReplacementType.JsonPlaceholder
                        });

                        // Also create JsonPlaceholder for backward compatibility
                        template.JsonPlaceholders.Add(new JsonPlaceholder
                        {
                            Key = kvp.Key,
                            Placeholder = placeholder,
                            Value = stringValue
                        });
                    }
                }
            }
        }
    }

    #endregion
}