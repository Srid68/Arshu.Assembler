// PreProcess template loader for Node.js - handles loading and preprocessing of HTML templates

import fs from 'fs';
import path from 'path';
import { JsonConverter } from '../app/jsonConverter.js';
import { TemplateUtils } from '../templateCommon/templateUtils.js';
import {
    PreprocessedSiteTemplates,
    PreprocessedTemplate,
    TemplatePlaceholder,
    SlottedTemplate,
    SlotPlaceholder,
    JsonPlaceholder,
    ReplacementMapping,
    ReplacementType
} from '../templateModel/modelPreProcess.js';

export class LoaderPreProcess {
    static #preprocessedTemplatesCache = new Map();

    /**
     * Loads and preprocesses HTML files from the specified application site directory into structured templates, caching the output per appSite and rootDirName
     * @param {string} rootDirPath - Root directory path
     * @param {string} appSite - Application site name
     * @returns {PreprocessedSiteTemplates} PreprocessedSiteTemplates containing structured template data
     */
    static loadProcessGetTemplateFiles(rootDirPath, appSite) {
        const cacheKey = `${path.dirname(rootDirPath)}|${appSite}`;
        
        if (this.#preprocessedTemplatesCache.has(cacheKey)) {
            return this.#preprocessedTemplatesCache.get(cacheKey);
        }

        const result = new PreprocessedSiteTemplates();
        result.siteName = appSite;

        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);
        
        if (!fs.existsSync(appSitesPath) || !fs.statSync(appSitesPath).isDirectory()) {
            this.#preprocessedTemplatesCache.set(cacheKey, result);
            return result;
        }

        // Recursively find all HTML files
        this.#walkDirectory(appSitesPath, (filePath, stats) => {
            if (stats.isFile() && path.extname(filePath) === '.html') {
                const fileName = path.basename(filePath, '.html');
                const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;
                
                const content = fs.readFileSync(filePath, 'utf8');
                
                // Look for corresponding JSON file
                const jsonFile = filePath.replace('.html', '.json');
                let jsonContent = null;
                
                if (fs.existsSync(jsonFile)) {
                    jsonContent = fs.readFileSync(jsonFile, 'utf8');
                }

                // Store raw template for backward compatibility
                result.rawTemplates.set(key, content);
                result.templateKeys.add(key);

                // Preprocess the template with JSON data
                const preprocessed = this.#preprocessTemplate(content, jsonContent, appSite, key);
                result.templates.set(key, preprocessed);
            }
        });

        // CRITICAL: Create ALL replacement mappings after all templates are loaded
        // This ensures PreProcess engine does ONLY merging, no processing logic
        this.#createAllReplacementMappingsForSite(result, appSite);

        this.#preprocessedTemplatesCache.set(cacheKey, result);
        return result;
    }

    /**
     * Preprocesses JSON data into a JsonObject structure for efficient template merging
     * @param {string} jsonContent - The JSON content to preprocess
     * @returns {JsonObject} JsonObject containing preprocessed JSON data
     */
    static preprocessJsonData(jsonContent) {
        return JsonConverter.parseJsonString(jsonContent);
    }

    /**
     * Clear all cached templates (useful for testing or when templates change)
     */
    static clearCache() {
        this.#preprocessedTemplatesCache.clear();
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
    static #preprocessTemplate(content, jsonContent, appSite, templateKey) {
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
    static #createAllReplacementMappingsForSite(siteTemplates, appSite) {
        // Phase 1: Create JSON replacement mappings for all templates first (no dependencies)
        for (const template of siteTemplates.templates.values()) {
            // Create replacement mappings for JSON array blocks (including negative blocks)
            this.#createJsonArrayReplacementMappings(template, template.originalContent);
        }

        // Phase 2: Create simple template replacement mappings (may depend on JSON but not on slotted templates)
        for (const template of siteTemplates.templates.values()) {
            // Create replacement mappings for simple placeholders
            this.#createPlaceholderReplacementMappings(template, siteTemplates.templates, appSite);
        }

        // Phase 3: Create slotted template replacement mappings (may depend on other templates)
        for (const template of siteTemplates.templates.values()) {
            // Create replacement mappings for slotted templates
            this.#createSlottedTemplateReplacementMappings(template, siteTemplates.templates, appSite);
        }
    }

    /**
     * IndexOf-based version: Parses slotted templates in the content and adds them to the preprocessed template
     * @param {string} content - Template content
     * @param {string} appSite - Application site
     * @param {PreprocessedTemplate} template - Template to populate
     */
    static #parseSlottedTemplates(content, appSite, template) {
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
            if (!templateName || !TemplateUtils.isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            const closeTag = `{{/${templateName}}}`;
            const closeStart = TemplateUtils.findMatchingCloseTag(
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
    static #parsePlaceholderTemplates(content, appSite, template) {
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
            if (!placeholderName || !TemplateUtils.isAlphaNumeric(placeholderName)) {
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
    static #parseSlots(innerContent, slottedTemplate, appSite) {
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
                
                const closingStart = TemplateUtils.findMatchingCloseTag(
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
    static #parseNestedTemplatesInSlot(slot, jsonData, appSite) {
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
            if (!templateName || !TemplateUtils.isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            const closeTag = `{{/${templateName}}}`;
            const closeStart = TemplateUtils.findMatchingCloseTag(
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
    static #preprocessJsonTemplates(template) {
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
    static #createJsonPlaceholderReplacementMappings(template, content) {
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
                        mapping.type = ReplacementType.JSON_PLACEHOLDER;
                        
                        template.replacementMappings.push(mapping);

                        // Also create JsonPlaceholder for backward compatibility
                        const jsonPlaceholder = new JsonPlaceholder(key, placeholder, value);
                        template.jsonPlaceholders.push(jsonPlaceholder);
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
    static #createPlaceholderReplacementMappings(template, allTemplates, appSite) {
        if (!template.hasPlaceholders) return;

        for (const placeholder of template.placeholders) {
            const targetTemplateKey = `${appSite.toLowerCase()}_${placeholder.templateKey}`;
            const targetTemplate = allTemplates.get(targetTemplateKey);
            
            if (targetTemplate) {
                // Use the target template's original content without applying other replacement mappings
                // This prevents circular dependencies and infinite recursion
                const processedTemplate = targetTemplate.originalContent;

                // Create the replacement mapping
                const mapping = new ReplacementMapping();
                mapping.originalText = placeholder.fullMatch;
                mapping.replacementText = processedTemplate;
                mapping.type = ReplacementType.SIMPLE_TEMPLATE;
                
                template.replacementMappings.push(mapping);
            }
        }
    }

    /**
     * Creates replacement mappings for slotted templates ({{#templatename}}...{{/templatename}})
     * This moves ALL slotted template processing logic from PreProcess engine to TemplateLoader
     * @param {PreprocessedTemplate} template - Template to process
     * @param {Map<string, PreprocessedTemplate>} allTemplates - All templates
     * @param {string} appSite - Application site
     */
    static #createSlottedTemplateReplacementMappings(template, allTemplates, appSite) {
        if (!template.hasSlottedTemplates) return;

        for (const slottedTemplate of template.slottedTemplates) {
            const targetTemplateKey = `${appSite.toLowerCase()}_${slottedTemplate.templateKey}`;
            const targetTemplate = allTemplates.get(targetTemplateKey);
            
            if (targetTemplate) {
                let processedTemplate = targetTemplate.originalContent;

                // Process slots in the target template
                for (const slot of slottedTemplate.slots) {
                    const processedSlotContent = this.#processSlotContentForReplacementMapping(slot, allTemplates, appSite);
                    processedTemplate = processedTemplate.replace(slot.slotKey, processedSlotContent);
                }

                // Remove remaining slot placeholders
                processedTemplate = TemplateUtils.removeRemainingSlotPlaceholders(processedTemplate);

                // Create the replacement mapping
                const mapping = new ReplacementMapping();
                mapping.originalText = slottedTemplate.fullMatch;
                mapping.replacementText = processedTemplate;
                mapping.type = ReplacementType.SLOTTED_TEMPLATE;
                
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
    static #processSlotContentForReplacementMapping(slot, allTemplates, appSite) {
        let result = slot.content;

        // Process nested slotted templates recursively
        for (const nestedSlottedTemplate of slot.nestedSlottedTemplates) {
            const targetTemplateKey = `${appSite.toLowerCase()}_${nestedSlottedTemplate.templateKey}`;
            const targetTemplate = allTemplates.get(targetTemplateKey);
            
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
                processedTemplate = TemplateUtils.removeRemainingSlotPlaceholders(processedTemplate);

                // Replace in result
                result = result.replace(nestedSlottedTemplate.fullMatch, processedTemplate);
            }
        }

        // Process nested simple placeholders
        for (const nestedPlaceholder of slot.nestedPlaceholders) {
            const targetTemplateKey = `${appSite.toLowerCase()}_${nestedPlaceholder.templateKey}`;
            const targetTemplate = allTemplates.get(targetTemplateKey);
            
            if (targetTemplate) {
                // Use the target template's original content
                const processedTemplate = targetTemplate.originalContent;

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
    static #createJsonArrayReplacementMappings(template, content) {
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
                            mapping.type = ReplacementType.JSON_PLACEHOLDER;
                            
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
                                        emptyMapping.type = ReplacementType.JSON_PLACEHOLDER;
                                        
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

        // Process simple JSON placeholders  
        for (const jsonPlaceholder of template.jsonPlaceholders) {
            const mapping = new ReplacementMapping();
            mapping.originalText = jsonPlaceholder.placeholder;
            mapping.replacementText = jsonPlaceholder.value;
            mapping.type = ReplacementType.JSON_PLACEHOLDER;
            
            template.replacementMappings.push(mapping);
        }
    }

    /**
     * Processes array block content by expanding template for each array item
     * @param {string} blockContent - Template content between {{@ArrayKey}} and {{/ArrayKey}}
     * @param {Array} dataArray - JSON array data
     * @returns {string} Processed array content
     */
    static #processArrayBlockContent(blockContent, dataArray) {
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
                    itemTemplate = this.#replaceAllCaseInsensitive(itemTemplate, placeholder, String(val));
                }
            } else {
                for (const [prop, val] of Object.entries(item)) {
                    const placeholder = `{{$${prop}}}`;
                    itemTemplate = this.#replaceAllCaseInsensitive(itemTemplate, placeholder, String(val));
                }
            }
            
            result += itemTemplate;
        }
        
        return result;
    }

    /**
     * Helper method to recursively walk directory
     * @param {string} dir - Directory to walk
     * @param {Function} callback - Callback function for each file/directory
     */
    static #walkDirectory(dir, callback) {
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
    static #processConditionalBlocks(content, jsonItem) {
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
    static #findConditionalKeysInContent(content) {
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
    static #getConditionValue(jsonItem, key) {
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
    static #replaceAllCaseInsensitive(input, search, replacement) {
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
    static #processConditionalBlockSafely(input, key, condition) {
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