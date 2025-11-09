using Arshu.App.Json;
using Arshu.Common;
using Assembler.Common;
using Assembler.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Assembler.Loader;

using JsonArray = Arshu.App.Json.JsonArray;
// Always use Base JsonObject/JsonArray types for consistent processing
using JsonObject = Arshu.App.Json.JsonObject;

/// <summary>
/// Handles loading and caching of HTML templates from the file system
/// </summary>
public static class LoaderPreProcess
{
    #region Loading Templates

    private static readonly ConcurrentDictionary<string, PreprocessedSiteTemplates> _preprocessedTemplatesCache = new();

    /// <summary>
    /// Loads and preprocesses HTML files from the specified application site directory into structured templates, caching the output per appSite and rootDirName
    /// </summary>
    /// <param name="rootDirPath">The root directory path</param>
    /// <param name="appSite">Primary AppSite name to load</param>
    /// <param name="searchAppSites">Comma-delimited string of AppSite names to search for fallback templates (can be empty string)</param>
    /// <returns>PreprocessedSiteTemplates containing structured template data</returns>
    public static PreprocessedSiteTemplates LoadProcessGetTemplateFiles(string rootDirPath, string appSite, string searchAppSites)
    {
        Logger.Debug($"LoadProcessGetTemplateFiles called for appSite: {appSite}, searchAppSites: {searchAppSites}", "LoaderPreProcess");

        var cacheKey = Path.GetDirectoryName(rootDirPath) + "|" + appSite + "|" + searchAppSites;
        if (_preprocessedTemplatesCache.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug($"Returning cached templates for {appSite} ({cached.Templates.Count} templates)", "LoaderPreProcess");
            return cached;
        }

        // Load templates from primary appSite
        var result = LoadTemplatesFromSingleAppSite(rootDirPath, appSite);

        // Load templates from searchAppSites for fallback
        if (!string.IsNullOrEmpty(searchAppSites))
        {
            var searchAppSitesArray = searchAppSites.Split(',');
            for (int i = 0; i < searchAppSitesArray.Length; i++)
            {
                var searchAppSite = searchAppSitesArray[i].Trim();
                if (string.IsNullOrEmpty(searchAppSite))
                    continue;

                var searchResult = LoadTemplatesFromSingleAppSite(rootDirPath, searchAppSite);

                // Merge templates (primary appSite takes precedence)
                foreach (var kvp in searchResult.Templates)
                {
                    if (!result.Templates.ContainsKey(kvp.Key))
                    {
                        result.Templates[kvp.Key] = kvp.Value;
                        result.RawTemplates[kvp.Key] = searchResult.RawTemplates[kvp.Key];
                        result.TemplateKeys.Add(kvp.Key);
                        Logger.Debug($"Added fallback template '{kvp.Key}' from '{searchAppSite}'", "LoaderPreProcess");
                    }
                }
            }
        }

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        CreateAllReplacementMappingsForSite(result, appSite);

        Logger.Debug($"Created all replacement mappings for {appSite}", "LoaderPreProcess");

        _preprocessedTemplatesCache.TryAdd(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Loads templates from a single AppSite without caching or fallback logic
    /// </summary>
    private static PreprocessedSiteTemplates LoadTemplatesFromSingleAppSite(string rootDirPath, string appSite)
    {
        var result = new PreprocessedSiteTemplates
        {
            SiteName = appSite
        };

        var appSitesPath = Path.Combine(rootDirPath, "AppSites", appSite);

        if (!Directory.Exists(appSitesPath))
        {
            Logger.Warn($"AppSites directory not found: {appSitesPath}", "LoaderPreProcess");
            return result;
        }

        Logger.Debug($"Loading templates from: {appSitesPath}", "LoaderPreProcess");

        foreach (var file in Directory.GetFiles(appSitesPath, "*.html", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var key = ($"{appSite.ToLowerInvariant()}_{fileName.ToLowerInvariant()}");
            var content = CommonUtil.NormalizeFileContent(File.ReadAllText(file));

            Logger.Debug($"Loading template: {key} (size: {content.Length})", "LoaderPreProcess");

            // Find JSON file case-insensitively
            var jsonFile = Path.ChangeExtension(file, ".json");
            string? jsonContent = null;

            // Try exact match first
            if (File.Exists(jsonFile))
            {
                jsonContent = CommonUtil.NormalizeFileContent(File.ReadAllText(jsonFile));
                Logger.Debug($"Found JSON file for {key} (size: {jsonContent.Length})", "LoaderPreProcess");
            }
            else
            {
                // Try case-insensitive search in the same directory
                var directory = Path.GetDirectoryName(file);
                var baseFileName = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(directory))
                {
                    var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
                    string? matchingJson = null;
                    for (int i = 0; i < jsonFiles.Length; i++)
                    {
                        if (string.Equals(Path.GetFileNameWithoutExtension(jsonFiles[i]), baseFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            matchingJson = jsonFiles[i];
                            break;
                        }
                    }

                    if (matchingJson != null)
                    {
                        jsonContent = CommonUtil.NormalizeFileContent(File.ReadAllText(matchingJson));
                        Logger.Debug($"Found JSON file (case-insensitive) for {key} (size: {jsonContent.Length})", "LoaderPreProcess");
                    }
                }
            }

            // Store raw template for backward compatibility
            result.RawTemplates[key] = content;
            result.TemplateKeys.Add(key);

            // Preprocess the template with JSON data
            var preprocessed = PreprocessTemplate(content, jsonContent, appSite, key);
            result.Templates[key] = preprocessed;

            Logger.Debug($"Preprocessed {key}: {preprocessed.ReplacementMappings.Count} replacements, {preprocessed.SlottedTemplates.Count} slotted, {preprocessed.Placeholders.Count} placeholders", "LoaderPreProcess");
        }

        Logger.Debug($"Loaded {result.Templates.Count} templates for {appSite}", "LoaderPreProcess");
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
        // Phase 0: Build parent map and resolve JSON inheritance BEFORE creating any mappings
        Logger.Debug($"Creating replacement mappings for {appSite} - Phase 0: JSON inheritance", "LoaderPreProcess");
        var parentMap = BuildParentMapForPreProcess(siteTemplates, appSite);
        ResolveJsonInheritanceForAllTemplates(siteTemplates, parentMap);

        // After resolving inheritance, recreate JSON placeholder mappings with the resolved values
        RecreateJsonPlaceholderMappingsAfterInheritance(siteTemplates);

        Logger.Debug($"Creating replacement mappings for {appSite} - Phase 1: JSON arrays", "LoaderPreProcess");
        // Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
        foreach (var templateKvp in siteTemplates.Templates)
        {
            var template = templateKvp.Value;
            // Create replacement mappings for JSON array blocks (including negative blocks)
            CreateJsonArrayReplacementMappings(template, template.OriginalContent);
        }

        Logger.Debug($"Creating replacement mappings for {appSite} - Phase 2: Simple placeholders", "LoaderPreProcess");
        // Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
        foreach (var templateKvp in siteTemplates.Templates)
        {
            var template = templateKvp.Value;
            // Create replacement mappings for simple placeholders
            CreatePlaceholderReplacementMappings(template, siteTemplates.Templates, appSite);
        }

        Logger.Debug($"Creating replacement mappings for {appSite} - Phase 3: Slotted templates", "LoaderPreProcess");
        // Phase 3: Create slotted template replacement mappings (may depend on other templates)
        foreach (var templateKvp in siteTemplates.Templates)
        {
            var template = templateKvp.Value;
            // Create replacement mappings for slotted templates
            CreateSlottedTemplateReplacementMappings(template, siteTemplates.Templates, appSite);
        }

        // Log summary of all replacement mappings
        int totalMappings = 0;
        foreach (var template in siteTemplates.Templates.Values)
        {
            totalMappings += template.ReplacementMappings.Count;
        }
        Logger.Info($"Total replacement mappings created for {appSite}: {totalMappings}", "LoaderPreProcess");
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
            // FIRST: Try current appSite
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{placeholder.TemplateKey}";
            PreprocessedTemplate? targetTemplate = null;

            if (allTemplates.TryGetValue(targetTemplateKey, out targetTemplate))
            {
                // Found in current appSite
            }
            else
            {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                var searchKey = $"_{placeholder.TemplateKey}";
                foreach (var kvp in allTemplates)
                {
                    if (kvp.Key.EndsWith(searchKey, StringComparison.OrdinalIgnoreCase))
                    {
                        targetTemplate = kvp.Value;
                        Logger.Debug($"Template '{placeholder.TemplateKey}' not found as '{targetTemplateKey}', using fallback from '{kvp.Key}'", "LoaderPreProcess");
                        break;
                    }
                }
            }

            if (targetTemplate != null)
            {
                // Start with the target template's original content
                var processedTemplate = targetTemplate.OriginalContent;

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context (e.g., header.json for Header component)
                var jsonMappings = new List<ReplacementMapping>();
                for (int i = 0; i < targetTemplate.ReplacementMappings.Count; i++)
                {
                    if (targetTemplate.ReplacementMappings[i].Type == ReplacementType.JsonPlaceholder)
                    {
                        jsonMappings.Add(targetTemplate.ReplacementMappings[i]);
                    }
                }
                Logger.Debug($"Applying {jsonMappings.Count} JSON mappings to {targetTemplateKey}", "LoaderPreProcess");
                Logger.Debug($"Before replacements, template size: {processedTemplate.Length}", "LoaderPreProcess");
                foreach (var jsonMapping in jsonMappings)
                {
                    var before = processedTemplate.Length;
                    Logger.Debug($"  Replacing placeholder (original size: {jsonMapping.OriginalText.Length}, replacement size: {jsonMapping.ReplacementText.Length})", "LoaderPreProcess");
                    processedTemplate = processedTemplate.Replace(jsonMapping.OriginalText, jsonMapping.ReplacementText);
                    var after = processedTemplate.Length;
                    Logger.Debug($"    Size changed from {before} to {after} (diff: {after - before})", "LoaderPreProcess");
                }
                Logger.Debug($"After replacements, template size: {processedTemplate.Length}", "LoaderPreProcess");


                // Create the replacement mapping
                Logger.Debug($"Creating replacement mapping: {placeholder.FullMatch} -> processed template (size: {processedTemplate.Length})", "LoaderPreProcess");
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

            // FIRST: Try current appSite
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{slottedTemplate.TemplateKey}";
            PreprocessedTemplate? targetTemplate = null;

            if (allTemplates.TryGetValue(targetTemplateKey, out targetTemplate))
            {
                // Found in current appSite
            }
            else
            {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                var searchKey = $"_{slottedTemplate.TemplateKey}";
                foreach (var kvp in allTemplates)
                {
                    if (kvp.Key.EndsWith(searchKey, StringComparison.OrdinalIgnoreCase))
                    {
                        targetTemplate = kvp.Value;
                        Logger.Debug($"Slotted template '{slottedTemplate.TemplateKey}' not found as '{targetTemplateKey}', using fallback from '{kvp.Key}'", "LoaderPreProcess");
                        break;
                    }
                }
            }

            if (targetTemplate != null)
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
                if (slottedTemplate.Slots.Count == 0)
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
                processedTemplate = CommonUtil.RemoveRemainingSlotPlaceholders(processedTemplate);

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
            // FIRST: Try current appSite
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{nestedSlottedTemplate.TemplateKey}";
            PreprocessedTemplate? targetTemplate = null;

            if (allTemplates.TryGetValue(targetTemplateKey, out targetTemplate))
            {
                // Found in current appSite
            }
            else
            {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                var searchKey = $"_{nestedSlottedTemplate.TemplateKey}";
                foreach (var kvp in allTemplates)
                {
                    if (kvp.Key.EndsWith(searchKey, StringComparison.OrdinalIgnoreCase))
                    {
                        targetTemplate = kvp.Value;
                        Logger.Debug($"Nested slotted template '{nestedSlottedTemplate.TemplateKey}' not found as '{targetTemplateKey}', using fallback from '{kvp.Key}'", "LoaderPreProcess");
                        break;
                    }
                }
            }

            if (targetTemplate != null)
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
                processedTemplate = CommonUtil.RemoveRemainingSlotPlaceholders(processedTemplate);

                // Replace in result
                result = result.Replace(nestedSlottedTemplate.FullMatch, processedTemplate);
            }
        }

        // Process nested simple placeholders
        foreach (var nestedPlaceholder in slot.NestedPlaceholders)
        {
            // FIRST: Try current appSite
            var targetTemplateKey = $"{appSite.ToLowerInvariant()}_{nestedPlaceholder.TemplateKey}";
            PreprocessedTemplate? targetTemplate = null;

            if (allTemplates.TryGetValue(targetTemplateKey, out targetTemplate))
            {
                // Found in current appSite
            }
            else
            {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                var searchKey = $"_{nestedPlaceholder.TemplateKey}";
                foreach (var kvp in allTemplates)
                {
                    if (kvp.Key.EndsWith(searchKey, StringComparison.OrdinalIgnoreCase))
                    {
                        targetTemplate = kvp.Value;
                        Logger.Debug($"Nested placeholder '{nestedPlaceholder.TemplateKey}' not found as '{targetTemplateKey}', using fallback from '{kvp.Key}'", "LoaderPreProcess");
                        break;
                    }
                }
            }

            if (targetTemplate != null)
            {
                // Start with the target template's original content
                var processedTemplate = targetTemplate.OriginalContent;

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context
                for (int i = 0; i < targetTemplate.ReplacementMappings.Count; i++)
                {
                    var jsonMapping = targetTemplate.ReplacementMappings[i];
                    if (jsonMapping.Type != ReplacementType.JsonPlaceholder)
                        continue;

                    processedTemplate = processedTemplate.Replace(jsonMapping.OriginalText, jsonMapping.ReplacementText);
                }

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
            if (string.IsNullOrEmpty(templateName) || !CommonUtil.IsAlphaNumeric(templateName))
            {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            var closeTag = "{{/" + templateName + "}}";
            var closeStart = CommonUtil.FindMatchingCloseTag(content, openEnd + 2, "{{#" + templateName + "}}", closeTag);
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

        var content = slot.Content;

        // Parse simple placeholders like {{ComponentName}} using IndexOf
        var searchPos = 0;
        while (searchPos < content.Length)
        {
            var openStart = content.IndexOf("{{", searchPos);
            if (openStart == -1) break;

            // Skip if it's a special placeholder
            if (openStart + 2 < content.Length && (content[openStart + 2] == '#' || content[openStart + 2] == '@' || content[openStart + 2] == '$' || content[openStart + 2] == '/'))
            {
                searchPos = openStart + 2;
                continue;
            }

            var closeStart = content.IndexOf("}}", openStart + 2);
            if (closeStart == -1) break;

            var templateName = content.Substring(openStart + 2, closeStart - openStart - 2).Trim();
            if (!string.IsNullOrEmpty(templateName) && CommonUtil.IsAlphaNumeric(templateName))
            {
                var templateKey = templateName.ToLowerInvariant();

                slot.NestedPlaceholders.Add(new TemplatePlaceholder
                {
                    Name = templateName,
                    StartIndex = openStart,
                    EndIndex = closeStart + 2,
                    FullMatch = content.Substring(openStart, closeStart + 2 - openStart),
                    TemplateKey = templateKey,
                    JsonData = jsonData
                });
            }

            searchPos = closeStart + 2;
        }

        // Parse slotted templates like {{#TemplateName}} ... {{/TemplateName}} using IndexOf
        searchPos = 0;
        while (searchPos < content.Length)
        {
            var openStart = content.IndexOf("{{#", searchPos);
            if (openStart == -1) break;

            var openEnd = content.IndexOf("}}", openStart + 3);
            if (openEnd == -1) break;

            var templateName = content.Substring(openStart + 3, openEnd - openStart - 3).Trim();
            if (string.IsNullOrEmpty(templateName) || !CommonUtil.IsAlphaNumeric(templateName))
            {
                searchPos = openStart + 1;
                continue;
            }

            var closeTag = "{{/" + templateName + "}}";
            var openTag = "{{#" + templateName + "}}";

            var closeStart = CommonUtil.FindMatchingCloseTag(content, openEnd + 2, openTag, closeTag);
            if (closeStart == -1)
            {
                searchPos = openStart + 1;
                continue;
            }

            var innerContent = content.Substring(openEnd + 2, closeStart - openEnd - 2);
            var templateKey = templateName.ToLowerInvariant();

            var nestedSlottedTemplate = new SlottedTemplate
            {
                Name = templateName,
                StartIndex = openStart,
                EndIndex = closeStart + closeTag.Length,
                FullMatch = content.Substring(openStart, closeStart + closeTag.Length - openStart),
                InnerContent = innerContent,
                TemplateKey = templateKey,
                JsonData = jsonData
            };

            // Parse slots within this nested slotted template
            ParseSlots(innerContent, nestedSlottedTemplate, appSite);

            slot.NestedSlottedTemplates.Add(nestedSlottedTemplate);
            searchPos = closeStart + closeTag.Length;
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
            if (string.IsNullOrEmpty(placeholderName) || !CommonUtil.IsAlphaNumeric(placeholderName))
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
                        string valueStr = kvp.Value switch
                        {
                            bool b => b ? "true" : "false",
                            null => string.Empty,
                            _ => kvp.Value.ToString() ?? string.Empty
                        };
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
                // Only handle {{$key}} pattern - removed backward compatibility
                var placeholder = "{{$" + kvp.Key + "}}";

                if (content.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    // Create replacement mapping for direct replacement
                    template.ReplacementMappings.Add(new ReplacementMapping
                    {
                        OriginalText = placeholder,
                        ReplacementText = stringValue,
                        Type = ReplacementType.JsonPlaceholder
                    });

                    // Also create JsonPlaceholder (avoid duplicates)
                    bool placeholderExists = false;
                    for (int i = 0; i < template.JsonPlaceholders.Count; i++)
                    {
                        if (template.JsonPlaceholders[i].Placeholder == placeholder)
                        {
                            placeholderExists = true;
                            break;
                        }
                    }
                    if (!placeholderExists)
                    {
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

    #region JSON Inheritance Support
    // NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
    // Each loader/engine pair is independent to allow individual evolution without shared dependencies.
    // DO NOT extract these to shared utilities - that would create tight coupling.

    /// <summary>
    /// Builds a parent-child relationship map by analyzing template placeholders
    /// Tracks which template is the parent of another based on {{TemplateName}} references
    /// </summary>
    private static Dictionary<string, string> BuildParentMapForPreProcess(PreprocessedSiteTemplates siteTemplates, string appSite)
    {
        var parentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Logger.Debug($"Building parent map for appSite: {appSite}", "LoaderPreProcess");

        foreach (var kvp in siteTemplates.Templates)
        {
            var templateKey = kvp.Key;
            var template = kvp.Value;

            // Find all {{TemplateName}} placeholders in this template
            foreach (var placeholder in template.Placeholders)
            {
                var placeholderName = placeholder.Name;

                // This template (templateKey) is the parent of the placeholder template
                var childTemplateKey = $"{appSite.ToLowerInvariant()}_{placeholderName.ToLowerInvariant()}";

                if (!parentMap.ContainsKey(childTemplateKey))
                {
                    parentMap[childTemplateKey] = templateKey;
                    Logger.Debug($"Parent relationship: {childTemplateKey} -> parent: {templateKey}", "LoaderPreProcess");
                }
            }

            // Also check slotted templates
            foreach (var slottedTemplate in template.SlottedTemplates)
            {
                var templateName = slottedTemplate.Name;
                var childTemplateKey = $"{appSite.ToLowerInvariant()}_{templateName.ToLowerInvariant()}";

                if (!parentMap.ContainsKey(childTemplateKey))
                {
                    parentMap[childTemplateKey] = templateKey;
                    Logger.Debug($"Parent relationship (slotted): {childTemplateKey} -> parent: {templateKey}", "LoaderPreProcess");
                }
            }
        }

        Logger.Debug($"Built parent map with {parentMap.Count} relationships", "LoaderPreProcess");
        return parentMap;
    }

    /// <summary>
    /// Resolves JSON inheritance for all templates by modifying their JsonData in place
    /// </summary>
    private static void ResolveJsonInheritanceForAllTemplates(PreprocessedSiteTemplates siteTemplates, Dictionary<string, string> parentMap)
    {
        foreach (var kvp in siteTemplates.Templates)
        {
            var templateKey = kvp.Key;
            var template = kvp.Value;

            if (template.JsonData == null)
                continue;

            // Resolve inheritance for this template
            var resolvedJson = new JsonObject();
            bool hasInheritance = false;

            foreach (var jsonKvp in template.JsonData)
            {
                var key = jsonKvp.Key;
                var value = jsonKvp.Value;

                // Check if this is an inheritable key (ends with #)
                if (key.EndsWith("#") && value is string strValue)
                {
                    hasInheritance = true;
                    var actualKey = key.Substring(0, key.Length - 1);
                    var resolvedValue = SearchParentTreeForKeyPreProcess(actualKey, templateKey, siteTemplates.Templates, parentMap);

                    if (resolvedValue != null)
                    {
                        resolvedJson[actualKey] = resolvedValue;
                        Logger.Debug($"Resolved inherited key {key} -> {actualKey} = {resolvedValue} for template {templateKey}", "LoaderPreProcess");
                    }
                    else
                    {
                        // Use default value if not found in parents
                        resolvedJson[actualKey] = strValue;
                        Logger.Debug($"No inherited value found for {actualKey}, using default: {strValue}", "LoaderPreProcess");
                    }
                }
                else
                {
                    // Normal key - keep as is
                    resolvedJson[key] = value;
                }
            }

            // Replace JsonData with resolved version if any inheritance was found
            if (hasInheritance)
            {
                template.JsonData = resolvedJson;
                Logger.Debug($"Updated JsonData for template {templateKey} with resolved inheritance", "LoaderPreProcess");
            }
        }
    }

    /// <summary>
    /// Recreates JSON placeholder replacement mappings after inheritance resolution
    /// This is needed because mappings were created during preprocessing before inheritance was resolved
    /// </summary>
    private static void RecreateJsonPlaceholderMappingsAfterInheritance(PreprocessedSiteTemplates siteTemplates)
    {
        foreach (var kvp in siteTemplates.Templates)
        {
            var template = kvp.Value;

            if (template.JsonData == null)
                continue;

            // Remove old JSON placeholder mappings (both simple placeholders AND array blocks use JsonPlaceholder type)
            var newMappings = new List<ReplacementMapping>();
            for (int i = 0; i < template.ReplacementMappings.Count; i++)
            {
                if (template.ReplacementMappings[i].Type != ReplacementType.JsonPlaceholder)
                {
                    newMappings.Add(template.ReplacementMappings[i]);
                }
            }

            template.ReplacementMappings = newMappings;

            // Recreate JSON array block mappings FIRST (they may contain simple placeholders)
            CreateJsonArrayReplacementMappings(template, template.OriginalContent);

            // Then recreate simple JSON placeholder mappings from the resolved JsonData
            CreateJsonPlaceholderReplacementMappings(template, template.OriginalContent);

            Logger.Debug($"Recreated JSON placeholder and array mappings for template {kvp.Key} after inheritance resolution", "LoaderPreProcess");
        }
    }

    /// <summary>
    /// Searches up the parent tree to find a JSON key value
    /// </summary>
    private static string? SearchParentTreeForKeyPreProcess(string key, string currentTemplateKey, Dictionary<string, PreprocessedTemplate> allTemplates, Dictionary<string, string> parentMap)
    {
        // Get parent template key
        if (!parentMap.TryGetValue(currentTemplateKey, out var parentKey))
        {
            Logger.Debug($"No parent found for {currentTemplateKey}", "LoaderPreProcess");
            return null;
        }

        Logger.Debug($"Checking parent {parentKey} for key {key}", "LoaderPreProcess");

        // Get parent's template
        if (!allTemplates.TryGetValue(parentKey, out var parentTemplate))
        {
            Logger.Debug($"Parent template {parentKey} not found in templates", "LoaderPreProcess");
            return null;
        }

        if (parentTemplate.JsonData == null)
        {
            Logger.Debug($"Parent template {parentKey} has no JSON data, searching further up", "LoaderPreProcess");
            // Parent has no JSON, search further up the tree
            return SearchParentTreeForKeyPreProcess(key, parentKey, allTemplates, parentMap);
        }

        // Look for the key (case-insensitive)
        foreach (var kvp in parentTemplate.JsonData)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value is string strValue)
                {
                    Logger.Debug($"Found key {key} in parent {parentKey}: {strValue}", "LoaderPreProcess");
                    return strValue;
                }
            }
        }

        Logger.Debug($"Key {key} not found in parent {parentKey}, searching further up", "LoaderPreProcess");
        // Not found in this parent, search further up the tree
        return SearchParentTreeForKeyPreProcess(key, parentKey, allTemplates, parentMap);
    }

    #endregion
}