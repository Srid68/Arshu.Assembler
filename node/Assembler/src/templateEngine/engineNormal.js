// Normal template engine for Node.js - IndexOf-based implementation for improved performance

import { JsonConverter } from '../app/jsonConverter.js';
import { TemplateUtils } from '../templateCommon/templateUtils.js';

export class EngineNormal {
    constructor(appViewPrefix = '') {
        this.appViewPrefix = appViewPrefix;
    }

    setAppViewPrefix(prefix) {
        this.appViewPrefix = prefix;
    }

    getAppViewPrefix() {
        return this.appViewPrefix;
    }

    /**
     * Merges templates by r                // Remove any remaining slot placeholders
                processedTemplate = TemplateUtils.removeRemainingSlotPlaceholders(processedTemplate);

                // Recursively process any remaining template placeholders in the processed template
                processedTemplate = this.processTemplateSlots(processedTemplate, appSite, appView, templates);

                // Replace the entire slotted sectiong placeholders with corresponding HTML
     * This is a hybrid method that processes both slotted templates and simple placeholders
     * JSON files with matching names are automatically merged with HTML templates before processing
     * @param {string} appSite - The application site name for template key generation
     * @param {string} appFile - The application file name
     * @param {string|null} appView - The application view name (optional)
     * @param {Map<string, TemplateResult>} templates - Dictionary of available templates
     * @param {boolean} enableJsonProcessing - Whether to enable JSON data processing
     * @returns {string} HTML with placeholders replaced
     */
    mergeTemplates(appSite, appFile, appView, templates, enableJsonProcessing = true) {
        if (!templates || templates.size === 0) {
            return '';
        }

        // Use the getTemplate method to retrieve the main template (html and json)
        const { html: mainTemplateHtml, json: mainTemplateJson } = this.getTemplate(
            appSite,
            appFile,
            templates,
            appView,
            this.appViewPrefix,
            true
        );

        if (!mainTemplateHtml) {
            return '';
        }

        let contentHtml = mainTemplateHtml;

        // Apply JSON merging to the main template if it has JSON and JSON processing is enabled
        if (enableJsonProcessing && mainTemplateJson) {
            contentHtml = this.mergeTemplateWithJson(contentHtml, mainTemplateJson);
        }

        // Step 2: Process each template with its associated JSON (matching C# approach exactly)
        const processedTemplates = new Map();
        const allJsonValues = new Map();

        // Add main template JSON values to the global collection if it exists
        if (enableJsonProcessing && mainTemplateJson) {
            try {
                const jsonObj = JsonConverter.parseJsonString(mainTemplateJson);
                for (const [k, v] of jsonObj) {
                    if (typeof v === 'string') {
                        allJsonValues.set(k.toLowerCase(), v);
                    }
                }
            } catch (e) {
                // Ignore JSON parsing errors
            }
        }

        // Process all templates with their JSON data
        for (const [key, template] of templates) {
            let htmlContent = template.html;
            const jsonContent = template.json;

            if (enableJsonProcessing && jsonContent) {
                htmlContent = this.mergeTemplateWithJson(htmlContent, jsonContent);
                try {
                    const jsonObj = JsonConverter.parseJsonString(jsonContent);
                    for (const [jsonKey, jsonValue] of jsonObj) {
                        if (typeof jsonValue === 'string') {
                            allJsonValues.set(jsonKey.toLowerCase(), jsonValue);
                        }
                    }
                } catch (e) {
                    // Ignore JSON parsing errors
                }
            }
            processedTemplates.set(key, htmlContent);
        }

        // Iterative processing with change detection until no more changes occur
        let previous;
        do {
            previous = contentHtml;
            contentHtml = this.mergeTemplateSlots(contentHtml, appSite, appView, processedTemplates);
            contentHtml = this.replaceTemplatePlaceholdersWithJson(contentHtml, appSite, processedTemplates, allJsonValues, appView);
        } while (contentHtml !== previous);

        return contentHtml;
    }

    /**
     * Retrieves a template from the templates map based on various scenarios including AppView fallback logic
     * @param {string} appSite - Application site
     * @param {string} templateName - Template name
     * @param {Map<string, TemplateResult>} templates - Available templates
     * @param {string|null} appView - Application view
     * @param {string} appViewPrefix - Application view prefix
     * @param {boolean} useAppViewFallback - Whether to use AppView fallback
     * @returns {Object} Object with html and json properties
     */
    getTemplate(appSite, templateName, templates, appView = null, appViewPrefix = null, useAppViewFallback = true) {
        if (!templates || templates.size === 0) {
            return { html: null, json: null };
        }

        const viewPrefix = appViewPrefix || this.appViewPrefix;
        const primaryTemplateKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if (useAppViewFallback && appView && appView.trim() !== '' && viewPrefix && viewPrefix.trim() !== '' &&
            templateName.toLowerCase().includes(viewPrefix.toLowerCase())) {
            // Direct replacement: Replace the AppViewPrefix with the AppView value
            // For example: Html3AContent with AppViewPrefix=Html3A and AppView=Html3B becomes Html3BContent
            const appKey = templateName.replace(new RegExp(viewPrefix, 'gi'), appView);
            const fallbackTemplateKey = `${appSite.toLowerCase()}_${appKey.toLowerCase()}`;
            const fallbackTemplate = templates.get(fallbackTemplateKey);
            if (fallbackTemplate) {
                return { html: fallbackTemplate.html, json: fallbackTemplate.json };
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        const primaryTemplate = templates.get(primaryTemplateKey);
        if (primaryTemplate) {
            return { html: primaryTemplate.html, json: primaryTemplate.json };
        }

        return { html: null, json: null };
    }

    /**
     * Replace placeholders using both templates and JSON values
     * @param {string} html - HTML content
     * @param {string} appSite - Application site
     * @param {Map<string, string>} htmlFiles - HTML template files
     * @param {Map<string, string>} jsonValues - JSON values
     * @param {string|null} appView - Application view
     * @returns {string} Processed HTML
     */
    replaceTemplatePlaceholdersWithJson(html, appSite, htmlFiles, jsonValues, appView = null) {
        let result = html;
        let searchPos = 0;

        while (searchPos < result.length) {
            // Look for opening placeholder {{
            const openStart = result.indexOf('{{', searchPos);
            if (openStart === -1) break;

            // Make sure it's not a slotted template or special placeholder
            if (openStart + 2 < result.length && 
                ['#', '@', '$', '/'].includes(result[openStart + 2])) {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            const closeStart = result.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            // Extract placeholder name
            const placeholderName = result.substring(openStart + 2, closeStart).trim();
            if (!placeholderName || !TemplateUtils.isAlphaNumeric(placeholderName)) {
                searchPos = openStart + 2;
                continue;
            }

            // Convert htmlFiles Map to templates format for getTemplate
            const templatesForGetTemplate = new Map();
            for (const [key, value] of htmlFiles) {
                templatesForGetTemplate.set(key, { html: value, json: null });
            }

            // Look up replacement in templates
            const { html: templateContent } = this.getTemplate(
                appSite, 
                placeholderName, 
                templatesForGetTemplate, 
                appView, 
                this.appViewPrefix, 
                true
            );

            let processedReplacement = null;

            if (templateContent) {
                processedReplacement = this.replaceTemplatePlaceholdersWithJson(
                    templateContent, 
                    appSite, 
                    htmlFiles, 
                    jsonValues || new Map(), 
                    appView
                );
            } else if (jsonValues && jsonValues.has(placeholderName.toLowerCase())) {
                // If template not found, try JSON value
                processedReplacement = jsonValues.get(placeholderName.toLowerCase());
            }

            if (processedReplacement !== null) {
                const placeholder = result.substring(openStart, closeStart + 2);
                result = result.replace(placeholder, processedReplacement);
                searchPos = openStart + processedReplacement.length;
            } else {
                searchPos = closeStart + 2;
            }
        }

        return result;
    }

    /**
     * Merge JSON data into the HTML template
     * @param {string} template - HTML template
     * @param {string} jsonText - JSON data as string
     * @returns {string} Merged HTML
     */
    mergeTemplateWithJson(template, jsonText) {
        // Parse JSON using JsonConverter
        const jsonObj = JsonConverter.parseJsonString(jsonText);
        const dict = new Map();
        
        // Convert JsonObject to Map with proper array handling
        for (const [key, value] of jsonObj) {
            if (value && value.constructor && value.constructor.name === 'JsonArray') {
                // Convert JsonArray to proper format
                const arr = [];
                for (const item of value) {
                    if (item && item.constructor && item.constructor.name === 'JsonObject') {
                        const obj = new Map();
                        for (const [subKey, subValue] of item) {
                            obj.set(subKey, subValue);
                        }
                        arr.push(obj);
                    } else {
                        // Handle array of simple values
                        const simpleObj = new Map();
                        simpleObj.set('Value', item);
                        arr.push(simpleObj);
                    }
                }
                dict.set(key, arr);
            } else {
                dict.set(key, value);
            }
        }

        let result = template;

        // Process JSON arrays with template blocks
        for (const [jsonKey, dataList] of dict) {
            if (Array.isArray(dataList)) {
                // Try to find matching template block for this JSON array
                const keyNorm = jsonKey.toLowerCase();
                const possibleTags = [jsonKey, keyNorm, keyNorm.replace(/s$/, ''), keyNorm + 's'];

                for (const tag of possibleTags) {
                    const blockStartTag = `{{@${tag}}}`;
                    const blockEndTag = `{{/${tag}}}`;
                    
                    const startIdx = result.toLowerCase().indexOf(blockStartTag.toLowerCase());
                    if (startIdx !== -1) {
                        const searchFrom = startIdx + blockStartTag.length;
                        const endIdx = result.toLowerCase().indexOf(blockEndTag.toLowerCase(), searchFrom);
                        
                        if (endIdx !== -1 && endIdx > startIdx) {
                            // Found valid block - process it
                            const contentStartIdx = startIdx + blockStartTag.length;
                            const blockContent = result.substring(contentStartIdx, endIdx);
                            let mergedBlock = '';
                            
                            // Find all conditional keys in the template
                            const conditionalKeys = new Set();
                            let condIdx = 0;
                            while (true) {
                                const condStart = blockContent.toLowerCase().indexOf('{{@', condIdx);
                                if (condStart === -1) break;
                                const condEnd = blockContent.indexOf('}}', condStart);
                                if (condEnd === -1) break;
                                const condKey = blockContent.substring(condStart + 3, condEnd).trim();
                                conditionalKeys.add(condKey);
                                condIdx = condEnd + 2;
                            }
                            
                            // Process each item in the array
                            for (const item of dataList) {
                                let itemBlock = blockContent;
                                
                                // Replace all {{$key}} placeholders - handle JsonObject
                                if (item.constructor.name === 'JsonObject' && typeof item.entries === 'function') {
                                    // Handle JsonConverter's JsonObject
                                    for (const [itemKey, itemValue] of item.entries()) {
                                        const placeholder = `{{$${itemKey}}}`;
                                        const valueStr = itemValue != null ? String(itemValue) : '';
                                        itemBlock = this.replaceAllCaseInsensitive(itemBlock, placeholder, valueStr);
                                    }
                                } else if (item instanceof Map) {
                                    // Handle Map
                                    for (const [itemKey, itemValue] of item) {
                                        const placeholder = `{{$${itemKey}}}`;
                                        const valueStr = itemValue != null ? String(itemValue) : '';
                                        itemBlock = this.replaceAllCaseInsensitive(itemBlock, placeholder, valueStr);
                                    }
                                }
                                
                                // Handle conditional blocks
                                for (const condKey of conditionalKeys) {
                                    let condValue = false;
                                    let condObj = null;
                                    
                                    if (item.constructor.name === 'JsonObject' && typeof item.get === 'function') {
                                        // Try case-insensitive lookup for JsonObject
                                        condObj = item.get(condKey);
                                        if (condObj == null) {
                                            // Try lowercase version
                                            condObj = item.get(condKey.toLowerCase());
                                        }
                                    } else if (item instanceof Map) {
                                        // Try case-insensitive lookup for Map
                                        condObj = item.get(condKey);
                                        if (condObj == null) {
                                            // Try lowercase version
                                            condObj = item.get(condKey.toLowerCase());
                                        }
                                    }
                                    
                                    if (condObj != null) {
                                        if (typeof condObj === 'boolean') {
                                            condValue = condObj;
                                        } else if (typeof condObj === 'string') {
                                            condValue = condObj.toLowerCase() === 'true';
                                        } else if (typeof condObj === 'number') {
                                            condValue = condObj !== 0;
                                        }
                                    }
                                    itemBlock = this.handleConditional(itemBlock, condKey, condValue);
                                }
                                mergedBlock += itemBlock;
                            }
                            
                            
                            // Replace the block in result
                            result = result.substring(0, startIdx) + mergedBlock + result.substring(endIdx + blockEndTag.length);
                            break; // Found and processed the block
                        }
                    }
                }
            }
        }

        // Process negative blocks for empty arrays AFTER main array processing
        for (const [key, value] of dict) {
            if (Array.isArray(value)) {
                const keyNorm = key.toLowerCase();
                const possibleTags = [key, keyNorm, keyNorm.replace(/s$/, ''), keyNorm + 's'];

                for (const tag of possibleTags) {
                    const emptyStartTag = `{{^${tag}}}`;
                    const emptyEndTag = `{{/${tag}}}`;
                    let searchIdx = 0;
                    
                    while (true) {
                        const emptyStartIdx = result.toLowerCase().indexOf(emptyStartTag.toLowerCase(), searchIdx);
                        if (emptyStartIdx === -1) break;
                        
                        const emptySearchFrom = emptyStartIdx + emptyStartTag.length;
                        const emptyEndIdx = result.toLowerCase().indexOf(emptyEndTag.toLowerCase(), emptySearchFrom);
                        if (emptyEndIdx === -1) break;
                        
                        let replacement = '';
                        if (value.length === 0) {
                            // For empty arrays, use the content between the negative tags
                            replacement = result.substring(emptyStartIdx + emptyStartTag.length, emptyEndIdx);
                        }
                        // For non-empty arrays, replacement stays empty (removes the block)
                        
                        result = result.substring(0, emptyStartIdx) + replacement + result.substring(emptyEndIdx + emptyEndTag.length);
                        searchIdx = emptyStartIdx + replacement.length;
                    }
                }
            }
        }

        // Handle simple placeholder replacement for non-array values
        for (const [key, value] of dict) {
            if (!Array.isArray(value) && typeof value === 'string') {
                const placeholder = `{{$${key}}}`;
                result = this.replaceAllCaseInsensitive(result, placeholder, value);
            }
        }
        
        return result;
    }

    /**
     * Replace all occurrences case-insensitively
     */
    replaceAllCaseInsensitive(str, search, replacement) {
        const regex = new RegExp(search.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi');
        return str.replace(regex, replacement);
    }

    /**
     * Handle conditional blocks in templates
     */
    handleConditional(template, condKey, condValue) {
        let result = template;
        
        // Handle both {{/Key}} and {{ /Key}} patterns
        const endPatterns = [`{{/${condKey}}}`, `{{ /${condKey}}}`];
        const startTag = `{{@${condKey}}}`;
        
        for (const endTag of endPatterns) {
            let searchIdx = 0;
            
            while (true) {
                const startIdx = result.toLowerCase().indexOf(startTag.toLowerCase(), searchIdx);
                if (startIdx === -1) break;
                
                const contentStart = startIdx + startTag.length;
                const endIdx = result.toLowerCase().indexOf(endTag.toLowerCase(), contentStart);
                if (endIdx === -1) break;
                
                const conditionalContent = result.substring(contentStart, endIdx);
                const replacement = condValue ? conditionalContent : '';
                
                result = result.substring(0, startIdx) + replacement + result.substring(endIdx + endTag.length);
                searchIdx = startIdx + replacement.length;
            }
        }
        
        return result;
    }

    /**
     * Recursively merges slotted templates with content
     * @param {string} contentHtml - Content HTML containing slot patterns
     * @param {string} appSite - Application site name
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} templates - Available templates
     * @returns {string} Merged HTML with slots filled
     */
    mergeTemplateSlots(contentHtml, appSite, appView, templates) {
        if (!contentHtml || !templates || templates.size === 0) {
            return contentHtml;
        }

        let previous;
        do {
            previous = contentHtml;
            contentHtml = this.processTemplateSlots(contentHtml, appSite, appView, templates);
        } while (contentHtml !== previous);

        return contentHtml;
    }

    /**
     * Process slotted templates using IndexOf approach
     * @param {string} contentHtml - Content HTML
     * @param {string} appSite - Application site
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} templates - Available templates
     * @returns {string} Processed HTML
     */
    processTemplateSlots(contentHtml, appSite, appView, templates) {
        let result = contentHtml;
        let searchPos = 0;

        while (searchPos < result.length) {
            // Look for opening tag {{#
            const openStart = result.indexOf('{{#', searchPos);
            if (openStart === -1) break;

            // Find the end of the template name
            const openEnd = result.indexOf('}}', openStart + 3);
            if (openEnd === -1) break;

            // Extract template name
            const templateName = result.substring(openStart + 3, openEnd).trim();
            if (!templateName || !TemplateUtils.isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            const closeTag = `{{/${templateName}}}`;
            const closeStart = TemplateUtils.findMatchingCloseTag(
                result, 
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
            const innerContent = result.substring(innerStart, closeStart);

            // Convert templates Map to format expected by getTemplate
            const templatesForGetTemplate = new Map();
            for (const [key, value] of templates) {
                templatesForGetTemplate.set(key, { html: value, json: null });
            }

            // Process the template replacement
            const { html: templateHtml } = this.getTemplate(
                appSite, 
                templateName, 
                templatesForGetTemplate, 
                appView, 
                this.appViewPrefix, 
                true
            );

            if (templateHtml) {
                // Extract slot contents
                const slotContents = this.extractSlotContents(innerContent, appSite, appView, templates);

                // Replace slots in template
                let processedTemplate = templateHtml;
                for (const [key, value] of slotContents) {
                    // Convert @HTMLPLACEHOLDER to $HTMLPLACEHOLDER for replacement
                    const replacementKey = key.replace('{{@HTMLPLACEHOLDER', '{{$HTMLPLACEHOLDER');
                    processedTemplate = processedTemplate.replace(replacementKey, value);
                }

                // Remove any remaining slot placeholders
                processedTemplate = TemplateUtils.removeRemainingSlotPlaceholders(processedTemplate);

                // Recursively process any remaining template placeholders in the processed template
                processedTemplate = this.processTemplateSlots(processedTemplate, appSite, appView, templates);

                // Replace the entire slotted section
                const fullMatch = result.substring(openStart, closeStart + closeTag.length);
                result = result.replace(fullMatch, processedTemplate);
                searchPos = openStart + processedTemplate.length;
            } else {
                searchPos = closeStart + closeTag.length;
            }
        }

        return result;
    }

    /**
     * Extract slot contents using IndexOf approach
     * @param {string} innerContent - Inner content to process
     * @param {string} appSite - Application site
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} templates - Available templates
     * @returns {Map<string, string>} Slot contents map
     */
    extractSlotContents(innerContent, appSite, appView, templates) {
        const slotContents = new Map();
        let searchPos = 0;

        while (searchPos < innerContent.length) {
            const placeholderStart = innerContent.indexOf('{{@HTMLPLACEHOLDER', searchPos);
            if (placeholderStart === -1) break;

            const afterPlaceholder = placeholderStart + 18;
            let pos = afterPlaceholder;

            // Skip digits to find the number (if any)
            let placeholderNumber = '';
            while (pos < innerContent.length && /\d/.test(innerContent[pos])) {
                placeholderNumber += innerContent[pos];
                pos++;
            }

            if (pos + 1 < innerContent.length && innerContent.substring(pos, pos + 2) === '}}') {
                const placeholderEnd = pos + 2;
                const placeholder = innerContent.substring(placeholderStart, placeholderEnd);
                
                // Find the closing tag - handle both numbered and non-numbered placeholders
                const closingTag = placeholderNumber ? `{{/HTMLPLACEHOLDER${placeholderNumber}}}` : `{{/HTMLPLACEHOLDER}}`;
                const openTag = placeholderNumber ? `{{@HTMLPLACEHOLDER${placeholderNumber}}}` : `{{@HTMLPLACEHOLDER}}`;
                
                const closingStart = TemplateUtils.findMatchingCloseTag(innerContent, placeholderEnd, openTag, closingTag);
                
                if (closingStart !== -1) {
                    const slotContent = innerContent.substring(placeholderEnd, closingStart);
                    
                    // Generate slot key for replacement (convert @HTMLPLACEHOLDER to $HTMLPLACEHOLDER)
                    const slotKey = placeholderNumber ? `{{$HTMLPLACEHOLDER${placeholderNumber}}}` : `{{$HTMLPLACEHOLDER}}`;
                    
                    // FIXED: Process both slotted templates AND simple placeholders in slot content
                    // This enables proper nested template processing to match the preprocessing implementation
                    let recursiveResult = this.mergeTemplateSlots(slotContent, appSite, appView, templates);
                    recursiveResult = this.replaceTemplatePlaceholders(recursiveResult, appSite, appView, templates);
                    
                    slotContents.set(slotKey, recursiveResult);
                    searchPos = closingStart + closingTag.length;
                } else {
                    searchPos = placeholderEnd;
                }
            } else {
                searchPos = placeholderStart + 1;
            }
        }

        return slotContents;
    }

    /**
     * Helper method to process simple placeholders only (without slotted template processing)
     * @param {string} html - HTML content
     * @param {string} appSite - Application site
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} htmlFiles - Available templates
     * @returns {string} Processed HTML
     */
    replaceTemplatePlaceholders(html, appSite, appView, htmlFiles) {
        let result = html;
        let searchPos = 0;

        // Try to get JSON values from the main template if available
        let jsonValues = null;
        if (htmlFiles.has('__json_values__')) {
            const jsonRaw = htmlFiles.get('__json_values__');
            if (jsonRaw && jsonRaw.trim() !== '') {
                // Parse as key=value pairs separated by newlines (custom format for this fix)
                jsonValues = new Map();
                const lines = jsonRaw.split('\n');
                for (const line of lines) {
                    const parts = line.split('=', 2);
                    if (parts.length === 2) {
                        jsonValues.set(parts[0].trim().toLowerCase(), parts[1].trim());
                    }
                }
            }
        }

        while (searchPos < result.length) {
            // Look for opening placeholder {{
            const openStart = result.indexOf('{{', searchPos);
            if (openStart === -1) break;

            // Skip slotted templates ({{# and {{@) and JSON arrays ({{^ and {{/)
            const thirdChar = result[openStart + 2];
            if (thirdChar === '#' || thirdChar === '@' || thirdChar === '^' || thirdChar === '/') {
                searchPos = openStart + 2;
                continue;
            }

            // Find the closing }}
            const closeStart = result.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            // Extract placeholder name
            const placeholderName = result.substring(openStart + 2, closeStart).trim();
            if (!placeholderName || !TemplateUtils.isAlphaNumeric(placeholderName)) {
                searchPos = closeStart + 2;
                continue;
            }

            // Convert htmlFiles Map to templates format for getTemplate
            const templatesForGetTemplate = new Map();
            for (const [key, value] of htmlFiles) {
                templatesForGetTemplate.set(key, { html: value, json: null });
            }

            // Look up replacement in templates
            const { html: templateContent } = this.getTemplate(
                appSite, 
                placeholderName, 
                templatesForGetTemplate, 
                appView, 
                this.appViewPrefix, 
                true
            );

            let processedReplacement = null;

            if (templateContent) {
                processedReplacement = templateContent;
            } else if (jsonValues && jsonValues.has(placeholderName.toLowerCase())) {
                // If template not found, try JSON value
                processedReplacement = jsonValues.get(placeholderName.toLowerCase());
            }

            if (processedReplacement !== null) {
                const placeholder = result.substring(openStart, closeStart + 2);
                result = result.replace(placeholder, processedReplacement);
                searchPos = openStart + processedReplacement.length;
            } else {
                searchPos = closeStart + 2;
            }
        }

        return result;
    }
}