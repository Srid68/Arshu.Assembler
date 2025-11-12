// PreProcess template loader for Node.js - handles loading and preprocessing of HTML templates

import fs from 'fs';
import path from 'path';
import { JsonConverter } from '../../app/jsonConverter.js';
import { isAlphaNumeric, findMatchingCloseTag, removeRemainingSlotPlaceholders, normalizeFileContent, replaceCaseInsensitive } from '../../common/commonUtil.js';
import { Logger } from '../../../../Arshu/src/common/Logger.js';
import {
    PreprocessedSiteTemplates,
    PreprocessedTemplate,
    TemplatePlaceholder,
    SlottedTemplate,
    SlotPlaceholder,
    JsonPlaceholder,
    ReplacementMapping,
    ReplacementType
} from '../../model/modelPreProcess.js';

// Module-level cache for performance (shared across all instances)
const _preprocessedTemplatesCache = new Map();

export class LoaderPreProcess {
    #rootDirPath;
    #appSite;
    #preprocessedSiteTemplates;

    /**
     * Creates a new LoaderPreProcess instance and loads templates
     * Implements ILoaderPreProcess interface
     * @param {string} rootDirPath - Root directory path
     * @param {string} appSite - Primary AppSite name to load
     * @param {string} searchAppSites - Comma-delimited string of AppSite names for fallback templates
     */
    constructor(rootDirPath, appSite, searchAppSites = '') {
        this.#rootDirPath = rootDirPath || '';
        this.#appSite = appSite || '';
        this.searchAppSites = searchAppSites || '';
        this.#preprocessedSiteTemplates = null;

        // Auto-load if parameters are provided (matching C# pattern)
        if (rootDirPath && appSite !== undefined) {
            this.load(rootDirPath, appSite, searchAppSites);
        }
    }

    /**
     * ILoaderPreProcess: load
     * Loads and preprocesses all templates from the specified directory
     * @param {string} rootDirPath - Root directory path containing AppSites folder
     * @param {string} appSite - Primary AppSite name to load
     * @param {string} searchAppSites - Comma-delimited string of AppSite names for fallback templates
     * @returns {boolean} True if templates loaded successfully
     */
    load(rootDirPath, appSite, searchAppSites) {
        if (rootDirPath) this.#rootDirPath = rootDirPath;
        if (appSite) this.#appSite = appSite;
        if (typeof searchAppSites === 'string') this.searchAppSites = searchAppSites;

        Logger.debug(`Load called for appSite: ${this.#appSite}, searchAppSites: ${this.searchAppSites}`, 'LoaderPreProcess');

        const cacheKey = `${path.dirname(this.#rootDirPath)}|${this.#appSite}|${this.searchAppSites}`;

        if (_preprocessedTemplatesCache.has(cacheKey)) {
            this.#preprocessedSiteTemplates = _preprocessedTemplatesCache.get(cacheKey);
            Logger.debug(`Returning cached templates for ${this.#appSite} (${this.#preprocessedSiteTemplates.templates.size} templates)`, 'LoaderPreProcess');
            return true;
        }

        // Load templates from primary appSite
        const result = this.#loadTemplatesFromSingleAppSite(this.#rootDirPath, this.#appSite);

        // Load templates from searchAppSites for fallback
        if (this.searchAppSites) {
            const searchAppSitesArray = this.searchAppSites.split(',');
            for (const searchAppSite of searchAppSitesArray) {
                const trimmedSearchAppSite = searchAppSite.trim();
                if (!trimmedSearchAppSite) {
                    continue;
                }

                const searchResult = this.#loadTemplatesFromSingleAppSite(this.#rootDirPath, trimmedSearchAppSite);

                // Merge templates (primary appSite takes precedence)
                for (const [key, value] of searchResult.templates) {
                    if (!result.templates.has(key)) {
                        result.templates.set(key, value);
                        result.rawTemplates.set(key, searchResult.rawTemplates.get(key));
                        result.templateKeys.add(key);
                        Logger.debug(`Added fallback template '${key}' from '${trimmedSearchAppSite}'`, 'LoaderPreProcess');
                    }
                }
            }
        }

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        this.#createAllReplacementMappingsForSite(result, this.#appSite);

        Logger.debug(`Created all replacement mappings for ${this.#appSite}`, 'LoaderPreProcess');

        this.#preprocessedSiteTemplates = result;
        _preprocessedTemplatesCache.set(cacheKey, result);
        return true;
    }

    /**
     * ILoaderPreProcess: hasTemplate
     * Checks if a template exists
     * @param {string} appSite - The application site name
     * @param {string} templateName - The template name
     * @returns {boolean} True if template exists
     */
    hasTemplate(appSite, templateName) {
        if (!this.#preprocessedSiteTemplates) return false;
        const key = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        return this.#preprocessedSiteTemplates.templates.has(key);
    }

    /**
     * ILoaderPreProcess: clearCache (static)
     * Clears the template cache
     */
    static clearCache() {
        _preprocessedTemplatesCache.clear();
    }

    /**
     * ILoaderPreProcess: clearCache (instance)
     * Clears the template cache
     */
    clearCache() {
        _preprocessedTemplatesCache.clear();
    }

    /**
     * ILoaderPreProcess: getTemplateHtml
     * Gets a preprocessed template by appSite and name with optional AppView fallback
     * @param {string} appSite - The application site name
     * @param {string} templateName - The template name
     * @param {string|null} appView - Optional AppView for fallback logic
     * @param {string|null} appViewPrefix - Optional AppView prefix for fallback logic
     * @returns {PreprocessedTemplate|null} PreprocessedTemplate or null if not found
     */
    getTemplateHtml(appSite, templateName, appView = null, appViewPrefix = null) {
        if (!this.#preprocessedSiteTemplates) return null;
        return this.#getTemplate(appSite, templateName, appView, appViewPrefix, true);
    }

    /**
     * ILoaderPreProcess: mergeHtmlWithJson
     * Merges HTML string with JSON data for the specified template
     * @param {string} html - The HTML string content to merge
     * @param {string} appSite - The application site name
     * @param {string} templateName - The template name
     * @returns {string} HTML string with JSON data merged
     */
    mergeHtmlWithJson(html, appSite, templateName) {
        // PreProcess loader doesn't have separate JSON files - JSON is already baked into replacement mappings
        // Return HTML as-is (JSON merging happens via replacement mappings)
        return html;
    }

    /**
     * ILoaderPreProcess: applyAllReplacementMappings
     * Applies all replacement mappings from all templates to the given content
     * This is the core PreProcess engine logic moved into the loader
     * @param {string} content - The content HTML to apply replacements to
     * @param {string} appSite - The application site name
     * @param {PreprocessedTemplate|null} mainTemplate - The main template
     * @param {string|null} appView - Optional AppView for fallback logic
     * @param {string|null} appViewPrefix - Optional AppView prefix for fallback logic
     * @param {boolean} enableJsonProcessing - Whether to enable JSON data processing
     * @returns {string} Content with all replacement mappings applied
     */
    applyAllReplacementMappings(content, appSite, mainTemplate, appView, appViewPrefix, enableJsonProcessing) {
        if (!this.#preprocessedSiteTemplates) return content;

        let result = content;
        Logger.debug(`Starting ApplyAllReplacementMappings, initial size: ${content.length}`, 'LoaderPreProcess');

        const maxPasses = 10;
        let currentPass = 0;

        do {
            const previous = result;
            currentPass++;
            Logger.debug(`Replacement pass ${currentPass}, current size: ${result.length}`, 'LoaderPreProcess');

            let slottedCount = 0;
            let simpleCount = 0;
            let jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template
            if (mainTemplate && currentPass === 1 && enableJsonProcessing) {
                for (const mapping of mainTemplate.replacementMappings) {
                    if ((mapping.type === 0 || mapping.type === ReplacementType.JsonPlaceholder) && result.includes(mapping.originalText)) {
                        Logger.debug(`Applying main template JSON placeholder (original size: ${mapping.originalText.length}, replacement size: ${mapping.replacementText.length})`, 'LoaderPreProcess');
                        result = result.split(mapping.originalText).join(mapping.replacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            // Apply replacement mappings from all templates
            for (const template of this.#preprocessedSiteTemplates.templates.values()) {
                // Apply slotted template mappings FIRST
                for (const mapping of template.replacementMappings) {
                    if ((mapping.type === 2 || mapping.type === ReplacementType.SlottedTemplate) && result.includes(mapping.originalText)) {
                        result = result.split(mapping.originalText).join(mapping.replacementText);
                        slottedCount++;
                    }
                }

                // Then apply simple template mappings with AppView logic
                for (const mapping of template.replacementMappings) {
                    if ((mapping.type === 1 || mapping.type === ReplacementType.SimpleTemplate) && result.includes(mapping.originalText)) {
                        let replacementText = mapping.replacementText;

                        // Apply AppView logic if appView is provided
                        if (appView) {
                            replacementText = this.#applyAppViewLogicToReplacement(mapping.originalText, replacementText, appView, appViewPrefix || '');
                        }

                        Logger.debug(`Applying simple template: ${mapping.originalText} -> replacement text (first 200 chars): ${replacementText.substring(0, Math.min(200, replacementText.length))}`, 'LoaderPreProcess');
                        result = result.split(mapping.originalText).join(replacementText);
                        simpleCount++;
                    }
                }
            }

            Logger.debug(`Pass ${currentPass} applied: ${jsonPlaceholderCount} main JSON placeholders, ${slottedCount} slotted, ${simpleCount} simple`, 'LoaderPreProcess');

            if (result === previous || currentPass >= maxPasses) {
                break;
            }
        } while (true);

        Logger.debug(`Replacement complete after ${currentPass} passes, final size: ${result.length}`, 'LoaderPreProcess');
        return result;
    }

    /**
     * Gets a template from the preprocessed templates with AppView fallback logic
     * @param {string} appSite - The application site name
     * @param {string} templateName - The template name
     * @param {string|null} appView - Optional AppView for fallback logic
     * @param {string|null} appViewPrefix - Optional AppView prefix for fallback logic
     * @param {boolean} useAppViewFallback - Whether to use AppView fallback
     * @returns {PreprocessedTemplate|null} Template or null
     */
    #getTemplate(appSite, templateName, appView, appViewPrefix, useAppViewFallback) {
        if (!this.#preprocessedSiteTemplates) return null;

        const viewPrefix = appViewPrefix || '';

        // FIRST: Check for AppView-specific template
        if (useAppViewFallback && appView && viewPrefix && templateName.toLowerCase().includes(viewPrefix.toLowerCase())) {
            const appKey = replaceCaseInsensitive(templateName, viewPrefix, appView);
            const fallbackTemplateKey = `${appSite.toLowerCase()}_${appKey.toLowerCase()}`;
            if (this.#preprocessedSiteTemplates.templates.has(fallbackTemplateKey)) {
                return this.#preprocessedSiteTemplates.templates.get(fallbackTemplateKey);
            }
        }

        // SECOND: Try primary template
        const primaryTemplateKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        if (this.#preprocessedSiteTemplates.templates.has(primaryTemplateKey)) {
            return this.#preprocessedSiteTemplates.templates.get(primaryTemplateKey);
        }

        // THIRD: Search in SearchAppSites
        if (this.searchAppSites) {
            for (const searchAppSite of this.searchAppSites.split(',')) {
                const site = searchAppSite.trim();
                if (!site) continue;

                const searchKey = `${site.toLowerCase()}_${templateName.toLowerCase()}`;
                if (this.#preprocessedSiteTemplates.templates.has(searchKey)) {
                    Logger.debug(`Template '${templateName}' not found in '${appSite}', using fallback from '${site}'`, 'LoaderPreProcess');
                    return this.#preprocessedSiteTemplates.templates.get(searchKey);
                }
            }
        }

        return null;
    }

    /**
     * Applies AppView fallback logic to template replacement text
     * @param {string} originalText - Original placeholder text
     * @param {string} replacementText - Current replacement text
     * @param {string} appView - The application view name
     * @param {string} appViewPrefix - The application view prefix
     * @returns {string} Updated replacement text with AppView logic applied
     */
    #applyAppViewLogicToReplacement(originalText, replacementText, appView, appViewPrefix) {
        if (!appView) return replacementText;

        // Extract placeholder name from {{PlaceholderName}} format
        const placeholderName = this.#extractPlaceholderName(originalText);
        if (!placeholderName) return replacementText;

        // Get the appSite from the template key pattern
        const sampleKey = this.#preprocessedSiteTemplates.templates.keys().next().value;
        if (!sampleKey) return replacementText;

        const parts = sampleKey.split('_');
        if (parts.length < 2) return replacementText;

        const appSite = parts[0];

        // Try to find AppView-specific template
        const appViewTemplate = this.#getTemplate(appSite, placeholderName, appView, appViewPrefix, true);
        if (!appViewTemplate) return replacementText;

        // Apply JSON placeholder replacements before returning
        let processedContent = appViewTemplate.originalContent;
        for (const mapping of appViewTemplate.replacementMappings) {
            if ((mapping.type === 0 || mapping.type === ReplacementType.JsonPlaceholder) && processedContent.includes(mapping.originalText)) {
                processedContent = processedContent.split(mapping.originalText).join(mapping.replacementText);
            }
        }

        return processedContent;
    }

    /**
     * Extracts placeholder name from {{PlaceholderName}} format
     * @param {string} originalText - Original placeholder text
     * @returns {string} Extracted placeholder name or empty string
     */
    #extractPlaceholderName(originalText) {
        if (!originalText || !originalText.startsWith('{{') || !originalText.endsWith('}}')) {
            return '';
        }
        return originalText.substring(2, originalText.length - 2).trim();
    }

    /**
     * Loads templates from a single AppSite without caching or fallback logic
     * @param {string} rootDirPath - Root directory path
     * @param {string} appSite - Application site name
     * @returns {PreprocessedSiteTemplates} PreprocessedSiteTemplates containing structured template data
     */
    #loadTemplatesFromSingleAppSite(rootDirPath, appSite) {
        const result = new PreprocessedSiteTemplates();
        result.siteName = appSite;

        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);

        if (!fs.existsSync(appSitesPath) || !fs.statSync(appSitesPath).isDirectory()) {
            Logger.warn(`AppSites directory not found: ${appSitesPath}`, 'LoaderPreProcess');
            return result;
        }

        Logger.debug(`Loading templates from: ${appSitesPath}`, 'LoaderPreProcess');

        // Recursively find all HTML files
        this.#walkDirectory(appSitesPath, (filePath, stats) => {
            if (stats.isFile() && path.extname(filePath) === '.html') {
                const fileName = path.basename(filePath, '.html');
                const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;

                const content = normalizeFileContent(fs.readFileSync(filePath, 'utf8'));
                Logger.debug(`Loading template: ${key} (size: ${content.length})`, 'LoaderPreProcess');

                // Find JSON file case-insensitively
                const jsonFile = filePath.replace('.html', '.json');
                let jsonContent = null;

                if (fs.existsSync(jsonFile)) {
                    jsonContent = normalizeFileContent(fs.readFileSync(jsonFile, 'utf8'));
                    Logger.debug(`Found JSON file for ${key} (size: ${jsonContent.length})`, 'LoaderPreProcess');
                } else {
                    // Try case-insensitive search in the same directory
                    const dir = path.dirname(filePath);
                    const baseName = path.basename(filePath, '.html').toLowerCase();
                    const entries = fs.readdirSync(dir);

                    for (const entry of entries) {
                        const entryPath = path.join(dir, entry);
                        if (fs.statSync(entryPath).isFile() && path.extname(entry).toLowerCase() === '.json') {
                            const entryBase = path.basename(entry, path.extname(entry)).toLowerCase();
                            if (entryBase === baseName) {
                                jsonContent = normalizeFileContent(fs.readFileSync(entryPath, 'utf8'));
                                Logger.debug(`Found JSON file (case-insensitive) for ${key} (size: ${jsonContent.length})`, 'LoaderPreProcess');
                                break;
                            }
                        }
                    }
                }

                // Store raw template for backward compatibility
                result.rawTemplates.set(key, content);
                result.templateKeys.add(key);

                // Preprocess the template with JSON data
                const preprocessed = this.#preprocessTemplate(content, jsonContent, appSite, key);
                result.templates.set(key, preprocessed);

                Logger.debug(`Preprocessed ${key}: ${preprocessed.replacementMappings.length} replacements, ${preprocessed.slottedTemplates.length} slotted, ${preprocessed.placeholders.length} placeholders`, 'LoaderPreProcess');
            }
        });

        Logger.debug(`Loaded ${result.templates.size} templates for ${appSite}`, 'LoaderPreProcess');
        return result;
    }

    /**
     * Preprocesses JSON data into a JsonObject structure for efficient template merging
     * @param {string} jsonContent - The JSON content to preprocess
     * @returns {JsonObject} JsonObject containing preprocessed JSON data
     */
    preprocessJsonData(jsonContent) {
        const parsed = JsonConverter.parseJsonString(jsonContent);
        return this.#normalizeJsonStructure(parsed);
    }

    #normalizeJsonStructure(value) {
        if (!value) return value;

        const ctorName = value?.constructor?.name;

        if (ctorName === 'JsonObject') {
            const normalized = new value.constructor();
            for (const [key, subValue] of value) {
                const normalizedKey = key.endsWith('#') ? key.slice(0, -1) : key;
                normalized.set(normalizedKey, this.#normalizeJsonStructure(subValue));
            }
            return normalized;
        }

        if (ctorName === 'JsonArray') {
            const normalizedArray = new value.constructor();
            for (const item of value) {
                normalizedArray.push(this.#normalizeJsonStructure(item));
            }
            return normalizedArray;
        }

        return value;
    }

    /**
     * Creates a preprocessed template by parsing its structure and any associated JSON data.
     * This method handles parsing and JSON preprocessing, leaving only merging to the template engine.
     * @param {string} content - The template content to parse
     * @param {string|null} jsonContent - The JSON content to parse (optional)
     * @param {string} appSite - The application site name
     * @param {string} templateKey - The template key for reference
     * @returns {PreprocessedTemplate} PreprocessedTemplate containing parsed structure and preprocessed JSON
     */
    #preprocessTemplate(content, jsonContent, appSite, templateKey) {
        const template = new PreprocessedTemplate();
        template.originalContent = content;

        if (!content) {
            return template;
        }

        // Parse JSON data into a structure
        if (jsonContent) {
            template.jsonData = this.preprocessJsonData(jsonContent);
        }

        // Parse template structure
        this.#parseSlottedTemplates(content, appSite, template);
        this.#parsePlaceholderTemplates(content, appSite, template);

        // Preprocess JSON templates - analyze and prepare JSON placeholders and blocks
        if (template.hasJsonData) {
            this.#preprocessJsonTemplates(template);
        }

        return template;
    }

    /**
     * Creates ALL replacement mappings for all templates after they are loaded
     * This ensures the PreProcess engine only does merging, no processing logic
     * Critical architectural method - moves ALL processing from engine to loader
     * @param {PreprocessedSiteTemplates} siteTemplates - All templates for the site
     * @param {string} appSite - The application site name
     */
    #createAllReplacementMappingsForSite(siteTemplates, appSite) {
        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 1: JSON arrays`, 'LoaderPreProcess');
        // Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
        for (const template of siteTemplates.templates.values()) {
            // Create replacement mappings for JSON array blocks (including negative blocks)
            this.#createJsonArrayReplacementMappings(template, template.originalContent);
        }

        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 2: Simple placeholders`, 'LoaderPreProcess');
        // Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
        for (const template of siteTemplates.templates.values()) {
            // Create replacement mappings for simple placeholders
            this.#createPlaceholderReplacementMappings(template, siteTemplates.templates, appSite);
        }

        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 3: Slotted templates`, 'LoaderPreProcess');
        // Phase 3: Create slotted template replacement mappings (may depend on other templates)
        for (const template of siteTemplates.templates.values()) {
            // Create replacement mappings for slotted templates
            this.#createSlottedTemplateReplacementMappings(template, siteTemplates.templates, appSite);
        }

        // Log summary of all replacement mappings
        let totalMappings = 0;
        for (const template of siteTemplates.templates.values()) {
            totalMappings += template.replacementMappings.length;
        }
        Logger.debug(`Total replacement mappings created for ${appSite}: ${totalMappings}`, 'LoaderPreProcess');
    }

    /**
     * IndexOf-based version: Parses slotted templates in the content and adds them to the preprocessed template
     * @param {string} content - Template content
     * @param {string} appSite - Application site
     * @param {PreprocessedTemplate} template - Template to populate
     */
    #parseSlottedTemplates(content, appSite, template) {
        let searchPos = 0;

        while (searchPos < content.length) {
            // Look for opening tag {{#
            const openStart = content.indexOf('{{#', searchPos);
            if (openStart === -1) break;

            // Find the end of the template name
            const openEnd = content.indexOf('}}', openStart + 3);
            if (openEnd === -1) break;

            // Extract template name
            const templateName = content.substring(openStart + 3, openEnd).trim();
            if (!templateName || !isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            const closeTag = `{{/${templateName}}}`;
            const closeStart = findMatchingCloseTag(
                content, 
                openEnd + 2, 
                `{{#${templateName}}}`, 
                closeTag
            );
            if (closeStart === -1) {
                searchPos = openStart + 1;
                continue;
            }

            // Extract inner content
            const innerStart = openEnd + 2;
            const innerContent = content.substring(innerStart, closeStart);
            const fullMatch = content.substring(openStart, closeStart + closeTag.length);

            // Create slotted template structure
            const slottedTemplate = new SlottedTemplate();
            slottedTemplate.name = templateName;
            slottedTemplate.startIndex = openStart;
            slottedTemplate.endIndex = closeStart + closeTag.length;
            slottedTemplate.fullMatch = fullMatch;
            slottedTemplate.innerContent = innerContent;
            slottedTemplate.templateKey = templateName.toLowerCase(); // Simple template name since appSite is passed as parameter

            // Parse slots within the slotted template
            this.#parseSlots(innerContent, slottedTemplate, appSite);

            template.slottedTemplates.push(slottedTemplate);
            searchPos = closeStart + closeTag.length;
        }
    }

    /**
     * Parses placeholder templates in the content and adds them to the preprocessed template
     * @param {string} content - Template content
     * @param {string} appSite - Application site
     * @param {PreprocessedTemplate} template - Template to populate
     */
    #parsePlaceholderTemplates(content, appSite, template) {
        let searchPos = 0;

        while (searchPos < content.length) {
            // Look for opening placeholder {{
            const openStart = content.indexOf('{{', searchPos);
            if (openStart === -1) break;

            // Make sure it's not a slotted template, conditional, or special placeholder
            if (openStart + 2 < content.length && 
                ['#', '@', '$', '/'].includes(content[openStart + 2])) {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            const closeStart = content.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            // Extract placeholder name
            const placeholderName = content.substring(openStart + 2, closeStart).trim();
            if (!placeholderName || !isAlphaNumeric(placeholderName)) {
                searchPos = openStart + 2;
                continue;
            }

            const fullMatch = content.substring(openStart, closeStart + 2);

            // Create placeholder structure
            const placeholder = new TemplatePlaceholder();
            placeholder.name = placeholderName;
            placeholder.startIndex = openStart;
            placeholder.endIndex = closeStart + 2;
            placeholder.fullMatch = fullMatch;
            placeholder.templateKey = placeholderName.toLowerCase();

            template.placeholders.push(placeholder);
            searchPos = closeStart + 2;
        }
    }

    /**
     * Parses slots within a slotted template
     * @param {string} innerContent - Inner content of slotted template
     * @param {SlottedTemplate} slottedTemplate - Slotted template to populate
     * @param {string} appSite - Application site
     */
    #parseSlots(innerContent, slottedTemplate, appSite) {
        let searchPos = 0;

        while (searchPos < innerContent.length) {
            const placeholderStart = innerContent.indexOf('{{@HTMLPLACEHOLDER', searchPos);
            if (placeholderStart === -1) break;

            const afterPlaceholder = placeholderStart + 18;
            let pos = afterPlaceholder;

            // Skip digits to find the number
            let placeholderNumber = '';
            while (pos < innerContent.length && /\d/.test(innerContent[pos])) {
                placeholderNumber += innerContent[pos];
                pos++;
            }

            if (pos + 1 < innerContent.length && innerContent.substring(pos, pos + 2) === '}}') {
                const placeholderEnd = pos + 2;
                const placeholder = innerContent.substring(placeholderStart, placeholderEnd);
                
                // Find the closing tag - handle both numbered and unnumbered placeholders
                const closingTag = placeholderNumber ? `{{/HTMLPLACEHOLDER${placeholderNumber}}}` : `{{/HTMLPLACEHOLDER}}`;
                const openTag = placeholder;
                
                const closingStart = findMatchingCloseTag(
                    innerContent,
                    placeholderEnd,
                    openTag,
                    closingTag
                );
                
                if (closingStart !== -1) {
                    const slotContent = innerContent.substring(placeholderEnd, closingStart);
                    
                    // Generate slot key - handle both numbered and unnumbered placeholders
                    const slotKey = placeholderNumber ? `{{$HTMLPLACEHOLDER${placeholderNumber}}}` : `{{$HTMLPLACEHOLDER}}`;
                    
                    // Create slot structure
                    const slot = new SlotPlaceholder();
                    slot.number = placeholderNumber;
                    slot.startIndex = placeholderStart;
                    slot.endIndex = closingStart + closingTag.length;
                    slot.content = slotContent;
                    slot.slotKey = slotKey;
                    slot.openTag = openTag;
                    slot.closeTag = closingTag;

                    // Parse nested templates within the slot content
                    this.#parseNestedTemplatesInSlot(slot, slottedTemplate.jsonData, appSite);

                    slottedTemplate.slots.push(slot);
                    searchPos = closingStart + closingTag.length;
                } else {
                    searchPos = placeholderEnd;
                }
            } else {
                searchPos = placeholderStart + 1;
            }
        }
    }

    /**
     * Parses nested templates within slot content
     * @param {SlotPlaceholder} slot - Slot to parse nested templates in
     * @param {JsonObject|null} jsonData - JSON data for the slot
     * @param {string} appSite - Application site
     */
    #parseNestedTemplatesInSlot(slot, jsonData, appSite) {
        if (!slot.content) {
            return;
        }

        // Parse simple placeholders like {{ComponentName}}
        const placeholderRegex = /\{\{([^#/@}]+)\}\}/gi;
        let placeholderMatch;
        while ((placeholderMatch = placeholderRegex.exec(slot.content)) !== null) {
            const templateName = placeholderMatch[1].trim();
            const templateKey = templateName.toLowerCase(); // Simple template name since appSite is passed as parameter

            const placeholder = new TemplatePlaceholder();
            placeholder.name = templateName;
            placeholder.startIndex = placeholderMatch.index;
            placeholder.endIndex = placeholderMatch.index + placeholderMatch[0].length;
            placeholder.fullMatch = placeholderMatch[0];
            placeholder.templateKey = templateKey;
            placeholder.jsonData = jsonData;

            slot.nestedPlaceholders.push(placeholder);
        }

        // Parse slotted templates like {{#TemplateName}} ... {{/TemplateName}}
        let searchPos = 0;
        while (searchPos < slot.content.length) {
            // Look for opening tag {{#
            const openStart = slot.content.indexOf('{{#', searchPos);
            if (openStart === -1) break;

            // Find the end of the template name
            const openEnd = slot.content.indexOf('}}', openStart + 3);
            if (openEnd === -1) break;

            // Extract template name
            const templateName = slot.content.substring(openStart + 3, openEnd).trim();
            if (!templateName || !isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            const closeTag = `{{/${templateName}}}`;
            const closeStart = findMatchingCloseTag(
                slot.content, 
                openEnd + 2, 
                `{{#${templateName}}}`, 
                closeTag
            );
            if (closeStart === -1) {
                searchPos = openStart + 1;
                continue;
            }

            // Extract inner content
            const innerStart = openEnd + 2;
            const innerContent = slot.content.substring(innerStart, closeStart);
            const fullMatch = slot.content.substring(openStart, closeStart + closeTag.length);

            // Create nested slotted template structure
            const nestedSlottedTemplate = new SlottedTemplate();
            nestedSlottedTemplate.name = templateName;
            nestedSlottedTemplate.startIndex = openStart;
            nestedSlottedTemplate.endIndex = closeStart + closeTag.length;
            nestedSlottedTemplate.fullMatch = fullMatch;
            nestedSlottedTemplate.innerContent = innerContent;
            nestedSlottedTemplate.templateKey = templateName.toLowerCase(); // Simple template name since appSite is passed as parameter
            nestedSlottedTemplate.jsonData = jsonData;

            // Parse slots within this nested slotted template
            this.#parseSlots(innerContent, nestedSlottedTemplate, appSite);

            slot.nestedSlottedTemplates.push(nestedSlottedTemplate);
            searchPos = closeStart + closeTag.length;
        }
    }

    /**
     * Preprocesses JSON templates by creating complete replacement mappings
     * This creates structured data that the PreProcess engine can apply directly without any processing
     * @param {PreprocessedTemplate} template - Template to process
     */
    #preprocessJsonTemplates(template) {
        if (!template.jsonData) return;

        const content = template.originalContent;

        // Step 1: Create replacement mappings for JSON array blocks
        this.#createJsonArrayReplacementMappings(template, content);        // Step 2: Create replacement mappings for JSON placeholders
        this.#createJsonPlaceholderReplacementMappings(template, content);

        // Note: This matches C# PreprocessJsonTemplates which calls CreateJsonArrayReplacementMappings twice
    }

    /**
     * Creates replacement mappings for JSON placeholders ({{$key}} patterns)
     * This creates direct string replacements without any processing logic
     * @param {PreprocessedTemplate} template - Template to process
     * @param {string} content - Template content
     */
    #createJsonPlaceholderReplacementMappings(template, content) {
        if (!template.jsonData) return;

        for (const [key, value] of template.jsonData) {
            if (typeof value === 'string') {
                // Handle both {{$key}} and {{key}} patterns
                const placeholders = [
                    `{{$${key}}}`,
                    `{{${key}}}`
                ];

                for (const placeholder of placeholders) {
                    if (content.toLowerCase().includes(placeholder.toLowerCase())) {
                        // Create replacement mapping for direct replacement
                        const mapping = new ReplacementMapping();
                        mapping.startIndex = 0;
                        mapping.endIndex = 0;
                        mapping.originalText = placeholder;
                        mapping.replacementText = value;
                        mapping.type = ReplacementType.JsonPlaceholder;

                        template.replacementMappings.push(mapping);

                        // Also create JsonPlaceholder for backward compatibility (avoid duplicates)
                        if (!template.jsonPlaceholders.some(p => p.placeholder === placeholder)) {
                            const jsonPlaceholder = new JsonPlaceholder(key, placeholder, value);
                            template.jsonPlaceholders.push(jsonPlaceholder);
                        }
                    }
                }
            }
        }
    }

    /**
     * Creates replacement mappings for simple placeholders ({{templatename}})
     * This moves ALL placeholder processing logic from PreProcess engine to TemplateLoader
     * @param {PreprocessedTemplate} template - Template to process
     * @param {Map<string, PreprocessedTemplate>} allTemplates - All templates
     * @param {string} appSite - Application site
     */
    #createPlaceholderReplacementMappings(template, allTemplates, appSite) {
        if (!template.hasPlaceholders) return;

        for (const placeholder of template.placeholders) {
            // FIRST: Try current appSite
            const targetTemplateKey = `${appSite.toLowerCase()}_${placeholder.templateKey}`;
            let targetTemplate = allTemplates.get(targetTemplateKey);

            if (!targetTemplate) {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                const searchKey = `_${placeholder.templateKey}`;
                for (const [kvpKey, kvpValue] of allTemplates) {
                    if (kvpKey.toLowerCase().endsWith(searchKey.toLowerCase())) {
                        targetTemplate = kvpValue;
                        Logger.debug(`Template '${placeholder.templateKey}' not found as '${targetTemplateKey}', using fallback from '${kvpKey}'`, 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if (targetTemplate) {
                // Start with the target template's original content
                let processedTemplate = targetTemplate.originalContent;

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context (e.g., header.json for Header component)
                const jsonMappings = targetTemplate.replacementMappings.filter(m => m.type === ReplacementType.JsonPlaceholder);
                Logger.debug(`Applying ${jsonMappings.length} JSON mappings to ${targetTemplateKey}`, 'LoaderPreProcess');
                Logger.debug(`Before replacements, template size: ${processedTemplate.length}`, 'LoaderPreProcess');
                for (const jsonMapping of jsonMappings) {
                    const before = processedTemplate.length;
                    Logger.debug(`  Replacing placeholder (original size: ${jsonMapping.originalText.length}, replacement size: ${jsonMapping.replacementText.length})`, 'LoaderPreProcess');
                    processedTemplate = processedTemplate.replace(new RegExp(this.#escapeRegExp(jsonMapping.originalText), 'g'), jsonMapping.replacementText);
                    const after = processedTemplate.length;
                    Logger.debug(`    Size changed from ${before} to ${after} (diff: ${after - before})`, 'LoaderPreProcess');
                }
                Logger.debug(`After replacements, template size: ${processedTemplate.length}`, 'LoaderPreProcess');

                // Create the replacement mapping
                Logger.debug(`Creating replacement mapping: ${placeholder.fullMatch} -> processed template (size: ${processedTemplate.length})`, 'LoaderPreProcess');
                const mapping = new ReplacementMapping();
                mapping.originalText = placeholder.fullMatch;
                mapping.replacementText = processedTemplate;
                mapping.type = ReplacementType.SimpleTemplate;

                template.replacementMappings.push(mapping);
            }
        }
    }

    /**
     * Escapes special regex characters for use in RegExp constructor
     * @param {string} string - String to escape
     * @returns {string} Escaped string
     */
    #escapeRegExp(string) {
        return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    /**
     * Creates replacement mappings for slotted templates ({{#templatename}}...{{/templatename}})
     * This moves ALL slotted template processing logic from PreProcess engine to TemplateLoader
     * @param {PreprocessedTemplate} template - Template to process
     * @param {Map<string, PreprocessedTemplate>} allTemplates - All templates
     * @param {string} appSite - Application site
     */
    #createSlottedTemplateReplacementMappings(template, allTemplates, appSite) {
        if (!template.hasSlottedTemplates) return;

        for (const slottedTemplate of template.slottedTemplates) {
            // FIRST: Try current appSite
            const targetTemplateKey = `${appSite.toLowerCase()}_${slottedTemplate.templateKey}`;
            let targetTemplate = allTemplates.get(targetTemplateKey);

            if (!targetTemplate) {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                const searchKey = `_${slottedTemplate.templateKey}`;
                for (const [kvpKey, kvpValue] of allTemplates) {
                    if (kvpKey.toLowerCase().endsWith(searchKey.toLowerCase())) {
                        targetTemplate = kvpValue;
                        Logger.debug(`Slotted template '${slottedTemplate.templateKey}' not found as '${targetTemplateKey}', using fallback from '${kvpKey}'`, 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if (targetTemplate) {
                let processedTemplate = targetTemplate.originalContent;

                // Process slots in the target template
                for (const slot of slottedTemplate.slots) {
                    const processedSlotContent = this.#processSlotContentForReplacementMapping(slot, allTemplates, appSite);
                    processedTemplate = processedTemplate.replace(slot.slotKey, processedSlotContent);
                }

                // Handle default slot content when no explicit slots are declared
                if (slottedTemplate.slots.length === 0) {
                    const actualInnerContent = slottedTemplate.innerContent;
                    if (actualInnerContent && actualInnerContent.trim().length > 0) {
                        const defaultSlotKey = '{{$HTMLPLACEHOLDER}}';
                        if (processedTemplate.includes(defaultSlotKey)) {
                            processedTemplate = processedTemplate.replaceAll(defaultSlotKey, actualInnerContent.trim());
                        }
                    }
                }

                // Remove remaining slot placeholders
                processedTemplate = removeRemainingSlotPlaceholders(processedTemplate);

                // Create the replacement mapping
                const mapping = new ReplacementMapping();
                mapping.originalText = slottedTemplate.fullMatch;
                mapping.replacementText = processedTemplate;
                mapping.type = ReplacementType.SlottedTemplate;
                
                template.replacementMappings.push(mapping);
            }
        }
    }

    /**
     * Processes slot content for creating replacement mappings
     * This recursively processes nested templates and placeholders
     * @param {SlotPlaceholder} slot - Slot to process
     * @param {Map<string, PreprocessedTemplate>} allTemplates - All templates
     * @param {string} appSite - Application site
     * @returns {string} Processed slot content
     */
    #processSlotContentForReplacementMapping(slot, allTemplates, appSite) {
        let result = slot.content;

        // Process nested slotted templates recursively
        for (const nestedSlottedTemplate of slot.nestedSlottedTemplates) {
            // FIRST: Try current appSite
            const targetTemplateKey = `${appSite.toLowerCase()}_${nestedSlottedTemplate.templateKey}`;
            let targetTemplate = allTemplates.get(targetTemplateKey);

            if (!targetTemplate) {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                const searchKey = `_${nestedSlottedTemplate.templateKey}`;
                for (const [kvpKey, kvpValue] of allTemplates) {
                    if (kvpKey.toLowerCase().endsWith(searchKey.toLowerCase())) {
                        targetTemplate = kvpValue;
                        Logger.debug(`Nested slotted template '${nestedSlottedTemplate.templateKey}' not found as '${targetTemplateKey}', using fallback from '${kvpKey}'`, 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if (targetTemplate) {
                // Use the target template's original content without applying replacement mappings
                // This prevents circular dependencies during replacement mapping creation
                let processedTemplate = targetTemplate.originalContent;

                // Process nested slots recursively
                for (const nestedSlot of nestedSlottedTemplate.slots) {
                    const processedNestedSlotContent = this.#processSlotContentForReplacementMapping(nestedSlot, allTemplates, appSite);
                    processedTemplate = processedTemplate.replace(nestedSlot.slotKey, processedNestedSlotContent);
                }

                // Remove remaining slot placeholders
                processedTemplate = removeRemainingSlotPlaceholders(processedTemplate);

                // Replace in result
                result = result.replace(nestedSlottedTemplate.fullMatch, processedTemplate);
            }
        }

        // Process nested simple placeholders
        for (const nestedPlaceholder of slot.nestedPlaceholders) {
            // FIRST: Try current appSite
            const targetTemplateKey = `${appSite.toLowerCase()}_${nestedPlaceholder.templateKey}`;
            let targetTemplate = allTemplates.get(targetTemplateKey);

            if (!targetTemplate) {
                // SECOND: Search in all loaded templates (includes searchAppSites)
                const searchKey = `_${nestedPlaceholder.templateKey}`;
                for (const [kvpKey, kvpValue] of allTemplates) {
                    if (kvpKey.toLowerCase().endsWith(searchKey.toLowerCase())) {
                        targetTemplate = kvpValue;
                        Logger.debug(`Nested placeholder '${nestedPlaceholder.templateKey}' not found as '${targetTemplateKey}', using fallback from '${kvpKey}'`, 'LoaderPreProcess');
                        break;
                    }
                }
            }

            if (targetTemplate) {
                // Start with the target template's original content
                let processedTemplate = targetTemplate.originalContent;

                // CRITICAL FIX: Apply the target template's JSON placeholder replacements
                // This ensures nested components use their own JSON context
                for (const mapping of targetTemplate.replacementMappings) {
                    if (mapping.type === ReplacementType.JsonPlaceholder) {
                        processedTemplate = processedTemplate.replace(new RegExp(this.#escapeRegExp(mapping.originalText), 'g'), mapping.replacementText);
                    }
                }

                // Replace in result
                result = result.replace(nestedPlaceholder.fullMatch, processedTemplate);
            }
        }

        return result;
    }

    /**
     * Creates replacement mappings for JSON array blocks
     * @param {PreprocessedTemplate} template - Template to process
     * @param {string} content - Template content
     */
    #createJsonArrayReplacementMappings(template, content) {
        if (!template.jsonData) return;

        // First, create a case-insensitive lookup map for JSON keys
        const jsonKeyLookup = new Map();
        for (const [jsonKey, dataValue] of template.jsonData) {
            jsonKeyLookup.set(jsonKey.toLowerCase(), { originalKey: jsonKey, value: dataValue });
        }

        // Process JSON arrays for Mustache template blocks
        for (const [jsonKey, dataValue] of template.jsonData) {
            if (Array.isArray(dataValue)) {
                // Try to find a matching template block for this JSON array
                const keyNorm = jsonKey.toLowerCase();
                const possibleTags = [jsonKey, keyNorm, keyNorm.replace(/s$/, ''), keyNorm + 's'];

                for (const tag of possibleTags) {
                    const blockStartTag = `{{@${tag}}}`;
                    const blockEndTag = `{{/${tag}}}`;

                    const startIdx = content.toLowerCase().indexOf(blockStartTag.toLowerCase());
                    if (startIdx !== -1) {
                        const searchFrom = startIdx + blockStartTag.length;
                        const endIdx = content.toLowerCase().indexOf(blockEndTag.toLowerCase(), searchFrom);

                        if (endIdx !== -1 && endIdx > startIdx) {
                            // Found a valid block - extract content and process it completely
                            const blockContent = content.substring(startIdx + blockStartTag.length, endIdx);
                            const fullBlock = content.substring(startIdx, endIdx + blockEndTag.length);

                            // Process the array content completely here
                            const processedArrayContent = this.#processArrayBlockContent(blockContent, dataValue);

                            // Create direct replacement mapping
                            const mapping = new ReplacementMapping();
                            mapping.startIndex = startIdx;
                            mapping.endIndex = endIdx + blockEndTag.length;
                            mapping.originalText = fullBlock;
                            mapping.replacementText = processedArrayContent;
                            mapping.type = ReplacementType.JsonPlaceholder;
                            
                            template.replacementMappings.push(mapping);

                            // Handle empty array blocks ({{^tag}}...{{/tag}}) with case-insensitive matching
                            const emptyBlockStart = `{{^${tag}}}`;
                            const emptyBlockEnd = `{{/${tag}}}`;
                            const emptyStartIdx = content.toLowerCase().indexOf(emptyBlockStart.toLowerCase());
                            if (emptyStartIdx !== -1) {
                                const emptySearchFrom = emptyStartIdx + emptyBlockStart.length;
                                const emptyEndIdx = content.toLowerCase().indexOf(emptyBlockEnd.toLowerCase(), emptySearchFrom);

                                if (emptyEndIdx !== -1 && emptyEndIdx > emptyStartIdx + emptyBlockStart.length) {
                                    const contentStart = emptyStartIdx + emptyBlockStart.length;
                                    const contentLength = emptyEndIdx - contentStart;

                                    if (contentLength >= 0 && contentStart + contentLength <= content.length) {
                                        const emptyBlockContent = content.substring(contentStart, contentStart + contentLength);
                                        const fullEmptyBlock = content.substring(emptyStartIdx, emptyEndIdx + emptyBlockEnd.length);
                                        
                                        // Use the actual JSON data for this array (could be different case)
                                        const actualArrayData = jsonKeyLookup.get(tag.toLowerCase())?.value || dataValue;
                                        const emptyReplacement = (Array.isArray(actualArrayData) && actualArrayData.length === 0) ? emptyBlockContent : '';

                                        const emptyMapping = new ReplacementMapping();
                                        emptyMapping.startIndex = emptyStartIdx;
                                        emptyMapping.endIndex = emptyEndIdx + emptyBlockEnd.length;
                                        emptyMapping.originalText = fullEmptyBlock;
                                        emptyMapping.replacementText = emptyReplacement;
                                        emptyMapping.type = ReplacementType.JsonPlaceholder;
                                        
                                        template.replacementMappings.push(emptyMapping);
                                    }
                                }
                            }
                            break; // Found and processed the block, move to next array
                        }
                    }
                }
            }
        }
    }

    /**
     * Processes array block content by expanding template for each array item
     * @param {string} blockContent - Template content between {{@ArrayKey}} and {{/ArrayKey}}
     * @param {Array} dataArray - JSON array data
     * @returns {string} Processed array content
     */
    #processArrayBlockContent(blockContent, dataArray) {
        let result = '';

        for (let i = 0; i < dataArray.length; i++) {
            const item = dataArray[i];
            let itemTemplate = blockContent;

            // Process conditional blocks like {{@Selected}}...{{/Selected}} FIRST
            itemTemplate = this.#processConditionalBlocks(itemTemplate, item);

            // Replace {{$PropertyName}} with actual values AFTER conditional processing
            // Handle both Map (JsonObject) and plain object structures with case-insensitive matching
            if (item instanceof Map) {
                for (const [prop, val] of item) {
                    const placeholder = `{{$${prop}}}`;
                    const valueStr = this.#jsonValueToStringWithoutHtmlEscape(val);
                    itemTemplate = this.#replaceAllCaseInsensitive(itemTemplate, placeholder, valueStr);
                }
            } else {
                for (const [prop, val] of Object.entries(item)) {
                    const placeholder = `{{$${prop}}}`;
                    const valueStr = this.#jsonValueToStringWithoutHtmlEscape(val);
                    itemTemplate = this.#replaceAllCaseInsensitive(itemTemplate, placeholder, valueStr);
                }
            }

            result += itemTemplate;
        }

        return result;
    }

    /**
     * Converts JSON value to string without HTML escaping (like Go's SetEscapeHTML(false))
     * @param {*} value - JSON value to convert
     * @returns {string} String representation without HTML escaping
     */
    #jsonValueToStringWithoutHtmlEscape(value) {
        if (value === null || value === undefined) {
            return '';
        }

        // Convert to JSON string first
        let valueStr = JSON.stringify(value);

        // Remove quotes if it's a string
        if (typeof value === 'string') {
            valueStr = valueStr.slice(1, -1);
        }

        // Decode HTML entities that JSON.stringify escapes
        // This matches Go's SetEscapeHTML(false) behavior
        valueStr = valueStr
            .replace(/\\u003c/gi, '<')
            .replace(/\\u003e/gi, '>')
            .replace(/\\u0026/gi, '&')
            .replace(/\\u0027/gi, "'")
            .replace(/\\u0022/gi, '"');

        return valueStr;
    }

    /**
     * Helper method to recursively walk directory
     * @param {string} dir - Directory to walk
     * @param {Function} callback - Callback function for each file/directory
     */
    #walkDirectory(dir, callback) {
        const files = fs.readdirSync(dir);
        
        for (const file of files) {
            const filePath = path.join(dir, file);
            const stats = fs.statSync(filePath);
            
            callback(filePath, stats);
            
            if (stats.isDirectory()) {
                this.#walkDirectory(filePath, callback);
            }
        }
    }

    /**
     * Processes conditional blocks safely without causing errors
     * Ported from C# ProcessConditionalBlocksSafely method
     * @param {string} content - String content containing conditional blocks
     * @param {Object} jsonItem - JSON object with conditional values
     * @returns {string} Content with conditional blocks processed
     */
    #processConditionalBlocks(content, jsonItem) {
        try {
            let result = content;
            
            // Find all conditional keys in the content and process them
            const conditionalKeys = this.#findConditionalKeysInContent(result);
            
            for (const condKey of conditionalKeys) {
                const condValue = this.#getConditionValue(jsonItem, condKey);
                result = this.#processConditionalBlockSafely(result, condKey, condValue);
            }
            
            return result;
        } catch (error) {
            // If processing fails, return original content
            return content;
        }
    }

    /**
     * Find conditional keys in content (e.g., "Selected" from "{{@Selected}}")
     * @param {string} content - Content to search
     * @returns {Array<string>} Array of conditional keys found
     */
    #findConditionalKeysInContent(content) {
        const keys = new Set();
        const regex = /\{\{@(\w+)\}\}/gi;
        let match;
        
        while ((match = regex.exec(content)) !== null) {
            keys.add(match[1]);
        }
        
        return Array.from(keys);
    }

    /**
     * Get condition value from JSON item with case-insensitive matching
     * @param {Object} jsonItem - JSON object 
     * @param {string} key - Key to look for
     * @returns {boolean} Condition value
     */
    #getConditionValue(jsonItem, key) {
        // Handle both Map (JsonObject) and plain object structures
        let entries;
        if (jsonItem instanceof Map) {
            entries = Array.from(jsonItem.entries());
        } else {
            entries = Object.entries(jsonItem);
        }
        
        // Case-insensitive key matching
        for (const [prop, val] of entries) {
            if (prop.toLowerCase() === key.toLowerCase()) {
                // Convert to boolean - follow C# logic
                if (typeof val === 'boolean') return val;
                if (typeof val === 'string') return val.toLowerCase() === 'true';
                if (typeof val === 'number') return val !== 0;
                return !!val;
            }
        }
        
        return false;
    }

    /**
     * Helper method to replace all case-insensitive occurrences
     * @param {string} input - Input string
     * @param {string} search - Search string
     * @param {string} replacement - Replacement string
     * @returns {string} String with all occurrences replaced
     */
    #replaceAllCaseInsensitive(input, search, replacement) {
        let idx = 0;
        while (true) {
            const found = input.toLowerCase().indexOf(search.toLowerCase(), idx);
            if (found === -1) break;
            input = input.substring(0, found) + replacement + input.substring(found + search.length);
            idx = found + replacement.length;
        }
        return input;
    }

    /**
     * Safely processes a single conditional block without causing errors
     * Ported from C# ProcessConditionalBlockSafely method
     * @param {string} input - Input string
     * @param {string} key - Conditional key (e.g., "Selected")  
     * @param {boolean} condition - Whether to include or remove the conditional content
     * @returns {string} String with conditional block processed
     */
    #processConditionalBlockSafely(input, key, condition) {
        try {
            // Support both space variants: {{ /Key}} and {{/Key}}
            const conditionTags = [
                { start: `{{@${key}}}`, end: `{{ /${key}}}` },
                { start: `{{@${key}}}`, end: `{{/${key}}}` }
            ];

            for (const { start: condStart, end: condEnd } of conditionTags) {
                let startIdx = input.toLowerCase().indexOf(condStart.toLowerCase());
                let endIdx = input.toLowerCase().indexOf(condEnd.toLowerCase());

                while (startIdx !== -1 && endIdx !== -1) {
                    // Safety check to prevent negative length
                    const contentStart = startIdx + condStart.length;
                    if (endIdx > contentStart) {
                        const content = input.substring(contentStart, endIdx);
                        if (condition) {
                            input = input.substring(0, startIdx) + content + input.substring(endIdx + condEnd.length);
                        } else {
                            input = input.substring(0, startIdx) + input.substring(endIdx + condEnd.length);
                        }
                    } else {
                        // Malformed conditional block - skip it
                        break;
                    }

                    startIdx = input.toLowerCase().indexOf(condStart.toLowerCase());
                    endIdx = input.toLowerCase().indexOf(condEnd.toLowerCase());
                }
            }

            return input;
        } catch (error) {
            // If processing fails, return original input
            return input;
        }
    }
}
