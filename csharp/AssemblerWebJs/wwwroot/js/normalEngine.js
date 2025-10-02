// Client-side Normal Template Engine - JavaScript implementation for browser
// Based on the Node.js EngineNormal implementation

class EngineNormal {
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
     * Merges templates by replacing placeholders with corresponding HTML
     * This is a hybrid method that processes both slotted templates and simple placeholders
     * JSON files with matching names are automatically merged with HTML templates before processing
     * @param {string} appSite - The application site name for template key generation
     * @param {string} appFile - The application file name
     * @param {string|null} appView - The application view name (optional)
     * @param {Map<string, Object>} templates - Map of available templates with {html, json} structure
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
                const jsonObj = this.parseJsonString(mainTemplateJson);
                for (const [k, v] of Object.entries(jsonObj)) {
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
                    const jsonObj = this.parseJsonString(jsonContent);
                    for (const [jsonKey, jsonValue] of Object.entries(jsonObj)) {
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
        let maxIterations = 10;
        let iteration = 0;
        
        do {
            previous = contentHtml;
            contentHtml = this.mergeTemplateSlots(contentHtml, appSite, appView, processedTemplates);
            contentHtml = this.replaceTemplatePlaceholdersWithJson(contentHtml, appSite, processedTemplates, allJsonValues, appView);
            iteration++;
        } while (contentHtml !== previous && iteration < maxIterations);

        return contentHtml;
    }

    /**
     * Retrieves a template from the templates map based on various scenarios including AppView fallback logic
     * @param {string} appSite - Application site
     * @param {string} templateName - Template name
     * @param {Map<string, Object>} templates - Available templates
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
     * Replaces template placeholders with JSON values and other templates
     * @param {string} html - HTML content
     * @param {string} appSite - Application site
     * @param {Map<string, string>} htmlFiles - Processed templates
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
                (result[openStart + 2] === '#' || result[openStart + 2] === '@' || 
                 result[openStart + 2] === '$' || result[openStart + 2] === '/')) {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            const closeStart = result.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            // Extract placeholder name
            const placeholderName = result.substring(openStart + 2, closeStart).trim();
            if (!placeholderName || !this.isAlphaNumeric(placeholderName)) {
                searchPos = openStart + 2;
                continue;
            }

            // Look up replacement in templates using getTemplate method
            const templatesForLookup = new Map();
            for (const [key, value] of htmlFiles) {
                templatesForLookup.set(key, { html: value, json: null });
            }

            const { html: templateContent } = this.getTemplate(
                appSite, 
                placeholderName, 
                templatesForLookup, 
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
            }
            // If template not found, try JSON value
            else if (jsonValues && jsonValues.has(placeholderName.toLowerCase())) {
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
     * Merges slotted templates recursively
     * @param {string} contentHtml - Content HTML
     * @param {string} appSite - Application site
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} templates - Available templates
     * @returns {string} Merged HTML
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
     * Processes slotted templates using IndexOf approach
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
            if (!templateName || !this.isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            // Look for corresponding closing tag
            const closeTag = '{{/' + templateName + '}}';
            const closeStart = this.findMatchingCloseTag(result, openEnd + 2, '{{#' + templateName + '}}', closeTag);
            if (closeStart === -1) {
                searchPos = openStart + 1;
                continue;
            }

            // Extract inner content
            const innerStart = openEnd + 2;
            const innerContent = result.substring(innerStart, closeStart);

            // Process the template replacement using getTemplate method
            const templatesForLookup = new Map();
            for (const [key, value] of templates) {
                templatesForLookup.set(key, { html: value, json: null });
            }

            const { html: templateHtml } = this.getTemplate(
                appSite, 
                templateName, 
                templatesForLookup, 
                appView, 
                this.appViewPrefix, 
                true
            );

            if (templateHtml) {
                // Extract slot contents
                const slotContents = this.extractSlotContents(innerContent, appSite, appView, templates);

                // Replace slots in template
                let processedTemplate = templateHtml;
                for (const [slotKey, slotValue] of Object.entries(slotContents)) {
                    processedTemplate = processedTemplate.replace(new RegExp(this.escapeRegExp(slotKey), 'g'), slotValue);
                }

                // Remove any remaining slot placeholders
                processedTemplate = this.removeRemainingSlotPlaceholders(processedTemplate);

                // Replace the entire slotted section
                const fullMatch = result.substring(openStart, closeStart + closeTag.length);
                result = result.replace(fullMatch, processedTemplate);
                searchPos = openStart + processedTemplate.length;
            } else {
                searchPos = openStart + 1;
            }
        }

        return result;
    }

    /**
     * Extracts slot contents from inner content
     * @param {string} innerContent - Inner content
     * @param {string} appSite - Application site
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} templates - Available templates
     * @returns {Object} Slot contents
     */
    extractSlotContents(innerContent, appSite, appView, templates) {
        const slotContents = {};
        let searchPos = 0;

        while (searchPos < innerContent.length) {
            // Look for slot start {{@HTMLPLACEHOLDER
            const slotStart = innerContent.indexOf('{{@HTMLPLACEHOLDER', searchPos);
            if (slotStart === -1) break;

            // Find the number (if any) and closing }}
            const afterPlaceholder = slotStart + 18; // Length of "{{@HTMLPLACEHOLDER"
            let slotNum = '';
            let pos = afterPlaceholder;

            // Extract slot number
            while (pos < innerContent.length && /\d/.test(innerContent[pos])) {
                slotNum += innerContent[pos];
                pos++;
            }

            // Check for closing }}
            if (pos + 1 >= innerContent.length || innerContent.substring(pos, pos + 2) !== '}}') {
                searchPos = slotStart + 1;
                continue;
            }

            const slotOpenEnd = pos + 2;

            // Find matching closing tag
            const closeTag = slotNum === '' ? '{{/HTMLPLACEHOLDER}}' : `{{/HTMLPLACEHOLDER${slotNum}}}`;
            const openTag = slotNum === '' ? '{{@HTMLPLACEHOLDER}}' : `{{@HTMLPLACEHOLDER${slotNum}}}`;

            const closeStart = this.findMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag);
            if (closeStart === -1) {
                searchPos = slotStart + 1;
                continue;
            }

            // Extract slot content
            const slotContent = innerContent.substring(slotOpenEnd, closeStart);

            // Generate slot key
            const slotKey = slotNum === '' ? '{{$HTMLPLACEHOLDER}}' : `{{$HTMLPLACEHOLDER${slotNum}}}`;

            // Process both slotted templates AND simple placeholders in slot content
            let recursiveResult = this.mergeTemplateSlots(slotContent, appSite, appView, templates);
            recursiveResult = this.replaceTemplatePlaceholders(recursiveResult, appSite, appView, templates);
            slotContents[slotKey] = recursiveResult;

            searchPos = closeStart + closeTag.length;
        }

        return slotContents;
    }

    /**
     * Replaces simple template placeholders
     * @param {string} html - HTML content
     * @param {string} appSite - Application site
     * @param {string|null} appView - Application view
     * @param {Map<string, string>} htmlFiles - HTML files
     * @returns {string} Processed HTML
     */
    replaceTemplatePlaceholders(html, appSite, appView, htmlFiles) {
        let result = html;
        let searchPos = 0;

        while (searchPos < result.length) {
            // Look for opening placeholder {{
            const openStart = result.indexOf('{{', searchPos);
            if (openStart === -1) break;

            // Make sure it's not a slotted template or special placeholder
            if (openStart + 2 < result.length && 
                (result[openStart + 2] === '#' || result[openStart + 2] === '@' || 
                 result[openStart + 2] === '$' || result[openStart + 2] === '/')) {
                searchPos = openStart + 2;
                continue;
            }

            // Find closing }}
            const closeStart = result.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            // Extract placeholder name
            const placeholderName = result.substring(openStart + 2, closeStart).trim();
            if (!placeholderName || !this.isAlphaNumeric(placeholderName)) {
                searchPos = openStart + 2;
                continue;
            }

            // Look up replacement in templates using getTemplate method
            const templatesForLookup = new Map();
            for (const [key, value] of htmlFiles) {
                templatesForLookup.set(key, { html: value, json: null });
            }

            const { html: templateContent } = this.getTemplate(
                appSite, 
                placeholderName, 
                templatesForLookup, 
                appView, 
                this.appViewPrefix, 
                true
            );

            let processedReplacement = null;
            if (templateContent) {
                processedReplacement = this.replaceTemplatePlaceholders(templateContent, appSite, appView, htmlFiles);
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
     * Merges HTML template with JSON data using placeholder replacement
     * @param {string} template - HTML template content
     * @param {string} jsonText - JSON data as string
     * @returns {string} Merged HTML with JSON data populated
     */
    mergeTemplateWithJson(template, jsonText) {
        try {
            const jsonObject = this.parseJsonString(jsonText);
            let result = template;

            // Process arrays (both positive and negative blocks)
            for (const [jsonKey, jsonValue] of Object.entries(jsonObject)) {
                if (Array.isArray(jsonValue)) {
                    result = this.processArrayBlock(result, jsonKey, jsonValue);
                    result = this.processNegativeArrayBlock(result, jsonKey, jsonValue);
                }
            }

            // Process simple placeholders
            for (const [key, value] of Object.entries(jsonObject)) {
                if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
                    const placeholder = `{{$${key}}}`;
                    result = result.replace(new RegExp(this.escapeRegExp(placeholder), 'gi'), String(value));
                }
            }

            return result;
        } catch (e) {
            console.error('Error merging template with JSON:', e);
            return template;
        }
    }

    /**
     * Processes array blocks in templates
     * @param {string} template - Template content
     * @param {string} arrayKey - Array key
     * @param {Array} arrayData - Array data
     * @returns {string} Processed template
     */
    processArrayBlock(template, arrayKey, arrayData) {
        let result = template;
        
        // Try multiple tag variations like Node.js implementation
        const keyNorm = arrayKey.toLowerCase();
        const possibleTags = [arrayKey, keyNorm, keyNorm.replace(/s$/, ''), keyNorm + 's'];
        
        for (const tag of possibleTags) {
            const blockStartTag = `{{@${tag}}}`;
            const blockEndTag = `{{/${tag}}}`;
            
            const startIdx = result.toLowerCase().indexOf(blockStartTag.toLowerCase());
            if (startIdx === -1) continue;
            
            const endIdx = result.toLowerCase().indexOf(blockEndTag.toLowerCase(), startIdx + blockStartTag.length);
            if (endIdx === -1) continue;
            
            const blockContent = result.substring(startIdx + blockStartTag.length, endIdx);
            let mergedBlock = '';

            // Only process if array has data
            if (arrayData.length > 0) {
                for (const item of arrayData) {
                    let itemBlock = blockContent;
                    
                    // Process conditional blocks FIRST (like Node.js implementation)
                    for (const [key, value] of Object.entries(item)) {
                        itemBlock = this.handleConditional(itemBlock, key, value);
                    }
                    
                    // Replace placeholders with item values AFTER conditional processing
                    for (const [key, value] of Object.entries(item)) {
                        const placeholder = `{{$${key}}}`;
                        itemBlock = itemBlock.replace(new RegExp(this.escapeRegExp(placeholder), 'gi'), String(value || ''));
                    }
                    
                    mergedBlock += itemBlock;
                }
            }

            result = result.substring(0, startIdx) + mergedBlock + result.substring(endIdx + blockEndTag.length);
            break; // Found and processed the block
        }

        return result;
    }

    /**
     * Processes negative array blocks for empty arrays ({{^arrayKey}}...{{/arrayKey}})
     * @param {string} template - Template content
     * @param {string} arrayKey - Array key
     * @param {Array} arrayData - Array data
     * @returns {string} Processed template
     */
    processNegativeArrayBlock(template, arrayKey, arrayData) {
        let result = template;
        
        // Try multiple tag variations like Node.js implementation
        const keyNorm = arrayKey.toLowerCase();
        const possibleTags = [arrayKey, keyNorm, keyNorm.replace(/s$/, ''), keyNorm + 's'];
        
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
                if (arrayData.length === 0) {
                    // For empty arrays, use the content between the negative tags
                    replacement = result.substring(emptyStartIdx + emptyStartTag.length, emptyEndIdx);
                }
                // For non-empty arrays, replacement stays empty (removes the block)
                
                result = result.substring(0, emptyStartIdx) + replacement + result.substring(emptyEndIdx + emptyEndTag.length);
                searchIdx = emptyStartIdx + replacement.length;
            }
        }

        return result;
    }

    /**
     * Handles conditional blocks in templates
     * @param {string} content - Template content
     * @param {string} key - Condition key
     * @param {*} value - Condition value
     * @returns {string} Processed content
     */
    handleConditional(content, key, value) {
        const condStartTag = `{{@${key}}}`;
        // Handle both {{/key}} and {{ /key}} patterns (with and without space)
        const condEndTag1 = `{{/${key}}}`;
        const condEndTag2 = `{{ /${key}}}`;
        
        const startIdx = content.toLowerCase().indexOf(condStartTag.toLowerCase());
        if (startIdx === -1) {
            return content;
        }
        
        // Try both end tag patterns
        let endIdx = content.toLowerCase().indexOf(condEndTag1.toLowerCase(), startIdx + condStartTag.length);
        let actualEndTag = condEndTag1;
        
        if (endIdx === -1) {
            endIdx = content.toLowerCase().indexOf(condEndTag2.toLowerCase(), startIdx + condStartTag.length);
            actualEndTag = condEndTag2;
        }
        
        if (endIdx === -1) {
            return content;
        }
        
        const condContent = content.substring(startIdx + condStartTag.length, endIdx);
        const condValue = this.isTruthy(value);
        
        const replacement = condValue ? condContent : '';
        const result = content.substring(0, startIdx) + replacement + content.substring(endIdx + actualEndTag.length);
        
        return result;
    }

    // Utility methods
    parseJsonString(jsonText) {
        return JSON.parse(jsonText);
    }

    isAlphaNumeric(str) {
        return /^[a-zA-Z0-9_]+$/.test(str);
    }

    escapeRegExp(string) {
        return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    isTruthy(value) {
        if (typeof value === 'boolean') return value;
        if (typeof value === 'string') return value.toLowerCase() === 'true';
        if (typeof value === 'number') return value !== 0;
        return Boolean(value);
    }

    removeRemainingSlotPlaceholders(template) {
        return template.replace(/\{\{\$HTMLPLACEHOLDER\d*\}\}/g, '');
    }

    findMatchingCloseTag(content, startPos, openTag, closeTag) {
        let pos = startPos;
        let depth = 1;
        
        while (pos < content.length && depth > 0) {
            const nextOpen = content.indexOf(openTag, pos);
            const nextClose = content.indexOf(closeTag, pos);
            
            if (nextClose === -1) return -1;
            
            if (nextOpen !== -1 && nextOpen < nextClose) {
                depth++;
                pos = nextOpen + openTag.length;
            } else {
                depth--;
                if (depth === 0) return nextClose;
                pos = nextClose + closeTag.length;
            }
        }
        
        return -1;
    }
}

// Export for use in other scripts
window.EngineNormal = EngineNormal;