/**
 * PreProcess template engine implementation that only does merging using preprocessed data structures
 * All parsing is done by TemplateLoader, this engine only handles merging
 */
class EnginePreProcess {
    constructor(appViewPrefix = '') {
        this.appViewPrefix = appViewPrefix;
    }

    /**
     * Sets the application view prefix
     * @param {string} prefix The application view prefix
     */
    setAppViewPrefix(prefix) {
        this.appViewPrefix = prefix;
    }

    /**
     * Merges templates using preprocessed data structures
     * This method only does merging using preprocessed data structures - no loading or parsing
     * @param {string} appSite The application site name for template key generation
     * @param {string} appFile The application file name
     * @param {string|null} appView The application view name (optional)
     * @param {Object.<string, PreprocessedTemplate>} preprocessedTemplates Dictionary of preprocessed templates for this specific appSite
     * @param {boolean} enableJsonProcessing Whether to enable JSON data processing
     * @returns {string} HTML with placeholders replaced using preprocessed structures
     */
    mergeTemplates(appSite, appFile, appView, preprocessedTemplates, enableJsonProcessing = true) {
        if (typeof Logger !== 'undefined') {
            Logger.debug(`EnginePreProcess.MergeTemplates START: appSite=${appSite}, appFile=${appFile}, appView=${appView || ''}, enableJsonProcessing=${enableJsonProcessing}`, 'EnginePreProcess');
        }

        if (!preprocessedTemplates ||
            (preprocessedTemplates instanceof Map && preprocessedTemplates.size === 0) ||
            (!(preprocessedTemplates instanceof Map) && Object.keys(preprocessedTemplates).length === 0)) {
            if (typeof Logger !== 'undefined') {
                Logger.warn('No preprocessed templates provided', 'EnginePreProcess');
            }
            return '';
        }

        // Use the getTemplate method to retrieve the main template
        const mainPreprocessed = this.getTemplate(appSite, appFile, preprocessedTemplates, appView, this.appViewPrefix, true);
        if (!mainPreprocessed) {
            if (typeof Logger !== 'undefined') {
                Logger.warn(`Main preprocessed template not found for ${appSite}_${appFile}`, 'EnginePreProcess');
            }
            return '';
        }

        // Start with original content
        let contentHtml = mainPreprocessed.originalContent || mainPreprocessed.OriginalContent;
        if (typeof Logger !== 'undefined') {
            Logger.debug(`Main template loaded: ${contentHtml.length} chars`, 'EnginePreProcess');
        }

        // Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
        contentHtml = this.applyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed);

        if (typeof Logger !== 'undefined') {
            Logger.debug(`EnginePreProcess.MergeTemplates COMPLETE: final output length = ${contentHtml.length}`, 'EnginePreProcess');
        }

        return contentHtml;
    }

    /**
     * Retrieves a template from the preprocessed templates dictionary based on various scenarios including AppView fallback logic
     * @param {string} appSite The application site name
     * @param {string} templateName The template name (can be appFile or placeholderName)
     * @param {Object.<string, PreprocessedTemplate>} preprocessedTemplates Dictionary of preprocessed templates
     * @param {string|null} appView The application view name (optional)
     * @param {string|null} appViewPrefix The application view prefix (optional, uses instance property if not provided)
     * @param {boolean} useAppViewFallback Whether to apply AppView fallback logic
     * @returns {PreprocessedTemplate|null} The template if found, null otherwise
     */
    getTemplate(appSite, templateName, preprocessedTemplates, appView = null, appViewPrefix = null, useAppViewFallback = true) {
        if (!preprocessedTemplates || 
            (preprocessedTemplates instanceof Map && preprocessedTemplates.size === 0) ||
            (!(preprocessedTemplates instanceof Map) && Object.keys(preprocessedTemplates).length === 0)) {
            return null;
        }

        const viewPrefix = appViewPrefix || this.appViewPrefix;
        
        // FIRST: Check for AppView-specific template resolution when AppView context is provided
        if (useAppViewFallback && appView && viewPrefix && templateName.toLowerCase().includes(viewPrefix.toLowerCase())) {
            // Direct replacement: Replace the AppViewPrefix with the AppView value
            // For example: Html3AContent with AppViewPrefix=Html3A and AppView=html3B becomes html3BContent
            const appKey = this.replaceAllCaseInsensitive(templateName, viewPrefix, appView);
            const fallbackTemplateKey = `${appSite.toLowerCase()}_${appKey.toLowerCase()}`;
            
            if (preprocessedTemplates instanceof Map) {
                if (preprocessedTemplates.has(fallbackTemplateKey)) {
                    return preprocessedTemplates.get(fallbackTemplateKey);
                }
            } else if (preprocessedTemplates[fallbackTemplateKey]) {
                return preprocessedTemplates[fallbackTemplateKey]; // Found AppView-specific template, use it
            }
        }

        // SECOND: If no AppView-specific template found, try primary template
        const primaryTemplateKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        if (preprocessedTemplates instanceof Map) {
            if (preprocessedTemplates.has(primaryTemplateKey)) {
                return preprocessedTemplates.get(primaryTemplateKey);
            }
        } else if (preprocessedTemplates[primaryTemplateKey]) {
            return preprocessedTemplates[primaryTemplateKey];
        }
        return null;
    }

    /**
     * Applies all replacement mappings from all templates - NO processing logic, only direct replacements
     * @param {string} content The content to apply replacements to
     * @param {Object.<string, PreprocessedTemplate>} preprocessedTemplates Dictionary of preprocessed templates
     * @param {boolean} enableJsonProcessing Whether to enable JSON processing
     * @param {string|null} appView The application view name
     * @param {PreprocessedTemplate|null} mainTemplate The main template (optional)
     * @returns {string} Content with all replacements applied
     */
    applyTemplateReplacements(content, preprocessedTemplates, enableJsonProcessing, appView, mainTemplate = null) {
        let result = content;

        // Apply replacement mappings from all templates in multiple passes until no more changes
        const maxPasses = 10; // Prevent infinite loops
        let currentPass = 0;

        do {
            const previous = result;
            currentPass++;

            let slottedCount = 0, simpleCount = 0, jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template (to avoid overwriting component content)
            if (mainTemplate && currentPass === 1 && enableJsonProcessing) {
                const mainReplacementMappings = mainTemplate.replacementMappings || mainTemplate.ReplacementMappings || [];
                for (const mapping of mainReplacementMappings) {
                    const mappingType = mapping.type || mapping.Type;
                    if ((mappingType === 0 || mappingType === "JsonPlaceholder") && result.includes(mapping.originalText || mapping.OriginalText)) {
                        const originalText = mapping.originalText || mapping.OriginalText;
                        const replacementText = mapping.replacementText || mapping.ReplacementText;
                        if (typeof Logger !== 'undefined') {
                            Logger.debug(`Applying main template JSON placeholder: ${originalText} -> ${replacementText}`, 'EnginePreProcess');
                        }
                        result = result.replaceAll(originalText, replacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            // Apply replacement mappings from all templates
            const templates = preprocessedTemplates instanceof Map ?
                Array.from(preprocessedTemplates.values()) :
                Object.values(preprocessedTemplates);

            for (const template of templates) {
                const replacementMappings = template.replacementMappings || template.ReplacementMappings || [];

                // Apply slotted template mappings
                for (const mapping of replacementMappings) {
                    const mappingType = mapping.type || mapping.Type;
                    if ((mappingType === 2 || mappingType === "SlottedTemplate") && result.includes(mapping.originalText || mapping.OriginalText)) {
                        const originalText = mapping.originalText || mapping.OriginalText;
                        const replacementText = mapping.replacementText || mapping.ReplacementText;
                        if (typeof Logger !== 'undefined') {
                            Logger.debug(`Applying slotted template: ${originalText.substring(0, Math.min(50, originalText.length))}... -> ${replacementText.length} chars`, 'EnginePreProcess');
                        }
                        result = result.replaceAll(originalText, replacementText);
                        slottedCount++;
                    }
                }

                // Apply simple template mappings (components) - replacement text already has JSON values baked in by LoaderPreProcess
                for (const mapping of replacementMappings) {
                    const mappingType = mapping.type || mapping.Type;
                    if ((mappingType === 1 || mappingType === "SimpleTemplate") && result.includes(mapping.originalText || mapping.OriginalText)) {
                        const originalText = mapping.originalText || mapping.OriginalText;
                        const replacementText = mapping.replacementText || mapping.ReplacementText;
                        // Apply AppView logic to handle runtime template selection with baked-in JSON values
                        const finalReplacementText = this.applyAppViewLogicToReplacement(originalText, replacementText, preprocessedTemplates, appView);
                        if (typeof Logger !== 'undefined') {
                            Logger.info(`Applying simple template: ${originalText} -> replacement text (first 200 chars): ${finalReplacementText.substring(0, Math.min(200, finalReplacementText.length))}`, 'EnginePreProcess');
                        }
                        result = result.replaceAll(originalText, finalReplacementText);
                        simpleCount++;
                    }
                }
            }

            if (typeof Logger !== 'undefined') {
                Logger.debug(`Pass ${currentPass} applied: ${jsonPlaceholderCount} main JSON placeholders, ${slottedCount} slotted, ${simpleCount} simple`, 'EnginePreProcess');
            }

            // Break if no changes were made or max passes reached
            if (result === previous || currentPass >= maxPasses) {
                break;
            }
        } while (true);

        // All JSON replacements are handled in LoaderPreProcess during replacement mapping creation
        // The engine only does simple string replacements using pre-prepared mappings
        if (typeof Logger !== 'undefined') {
            Logger.info(`Replacement complete after ${currentPass} passes, final size: ${result.length}`, 'EnginePreProcess');
        }

        return result;
    }

    /**
     * Handle conditional blocks like {{@Selected}}...{{/Selected}}
     * Ported from C# HandleConditional method
     * @param {string} input Input string
     * @param {string} key Conditional key (e.g., "Selected")
     * @param {boolean} condition Whether to include or remove the conditional content
     * @returns {string} String with conditional blocks processed
     */
    handleConditional(input, key, condition) {
        // Support spaces inside block tags, e.g. {{@Selected}} ... {{ /Selected}}
        const condStart = `{{@${key}}}`;
        const condEnd = `{{ /${key}}}`;
        
        let startIdx = input.toLowerCase().indexOf(condStart.toLowerCase());
        let endIdx = input.toLowerCase().indexOf(condEnd.toLowerCase());
        
        while (startIdx !== -1 && endIdx !== -1) {
            const content = input.substring(startIdx + condStart.length, endIdx);
            if (condition) {
                input = input.substring(0, startIdx) + content + input.substring(endIdx + condEnd.length);
            } else {
                input = input.substring(0, startIdx) + input.substring(endIdx + condEnd.length);
            }
            startIdx = input.toLowerCase().indexOf(condStart.toLowerCase());
            endIdx = input.toLowerCase().indexOf(condEnd.toLowerCase());
        }
        
        // Also handle without space: {{/Selected}}
        const condEndNoSpace = `{{/${key}}}`;
        startIdx = input.toLowerCase().indexOf(condStart.toLowerCase());
        endIdx = input.toLowerCase().indexOf(condEndNoSpace.toLowerCase());
        
        while (startIdx !== -1 && endIdx !== -1) {
            const content = input.substring(startIdx + condStart.length, endIdx);
            if (condition) {
                input = input.substring(0, startIdx) + content + input.substring(endIdx + condEndNoSpace.length);
            } else {
                input = input.substring(0, startIdx) + input.substring(endIdx + condEndNoSpace.length);
            }
            startIdx = input.toLowerCase().indexOf(condStart.toLowerCase());
            endIdx = input.toLowerCase().indexOf(condEndNoSpace.toLowerCase());
        }
        
        return input;
    }

    /**
     * Helper method to replace all case-insensitive occurrences
     * @param {string} input Input string
     * @param {string} search Search string
     * @param {string} replacement Replacement string
     * @returns {string} String with all occurrences replaced
     */
    replaceAllCaseInsensitive(input, search, replacement) {
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
     * Applies AppView fallback logic to template replacement text using the centralized getTemplate method
     * @param {string} originalText Original placeholder text
     * @param {string} replacementText Current replacement text
     * @param {Object.<string, PreprocessedTemplate>} preprocessedTemplates Dictionary of preprocessed templates
     * @param {string|null} appView The application view name
     * @returns {string} Updated replacement text with AppView logic applied
     */
    applyAppViewLogicToReplacement(originalText, replacementText, preprocessedTemplates, appView) {
        // If no appView context, use the default replacement text (which already has JSON values baked in)
        if (!appView) {
            return replacementText;
        }

        // Extract placeholder name from {{PlaceholderName}} format
        const placeholderName = this.extractPlaceholderName(originalText);
        if (!placeholderName) {
            return replacementText;
        }

        // First get the appSite from the template key pattern
        const sampleKey = Object.keys(preprocessedTemplates)[0];
        if (!sampleKey) {
            return replacementText;
        }

        const parts = sampleKey.split('_');
        if (parts.length < 2) {
            return replacementText;
        }

        const appSite = parts[0]; // Extract appSite from the key pattern

        // Use GetTemplate to find the AppView-specific template variant
        const appViewTemplate = this.getTemplate(appSite, placeholderName, preprocessedTemplates, appView, this.appViewPrefix, true);

        // If no AppView-specific template found, use the default replacement text
        if (!appViewTemplate) {
            return replacementText;
        }

        // Return the AppView-specific template's original content (which already has JSON baked in by LoaderPreProcess)
        return (appViewTemplate.originalContent || appViewTemplate.OriginalContent) || replacementText;
    }

    /**
     * Extracts placeholder name from {{PlaceholderName}} format
     * @param {string} originalText Original placeholder text
     * @returns {string} Extracted placeholder name or empty string
     */
    extractPlaceholderName(originalText) {
        if (!originalText || !originalText.startsWith('{{') || !originalText.endsWith('}}')) {
            return '';
        }
        
        return originalText.substring(2, originalText.length - 2).trim();
    }
}