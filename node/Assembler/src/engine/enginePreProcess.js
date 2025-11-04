import { Logger } from '@arshu/common';
import { CommonUtil } from '../common/commonUtil.js';

/**
 * PreProcess template engine implementation that only does merging using preprocessed data structures
 * All parsing is done by TemplateLoader, this engine only handles merging
 */
export class EnginePreProcess {
    constructor(appViewPrefix = '') {
        this.appViewPrefix = appViewPrefix;
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
        Logger.debug(`MergeTemplates called: appSite=${appSite}, appFile=${appFile}, appView=${appView || 'null'}, enableJson=${enableJsonProcessing}`, 'EnginePreProcess');

        if (!preprocessedTemplates ||
            (preprocessedTemplates instanceof Map && preprocessedTemplates.size === 0) ||
            (!(preprocessedTemplates instanceof Map) && Object.keys(preprocessedTemplates).length === 0)) {
            Logger.warn('No preprocessed templates available', 'EnginePreProcess');
            return '';
        }

        const templateCount = preprocessedTemplates instanceof Map ? preprocessedTemplates.size : Object.keys(preprocessedTemplates).length;
        Logger.debug(`Using ${templateCount} preprocessed templates`, 'EnginePreProcess');

        // Use the getTemplate method to retrieve the main template
        const mainPreprocessed = this.getTemplate(appSite, appFile, preprocessedTemplates, appView, this.appViewPrefix, true);
        if (!mainPreprocessed) {
            Logger.warn(`Main template not found for appSite=${appSite}, appFile=${appFile}`, 'EnginePreProcess');
            return '';
        }

        Logger.debug(`Main template found, original size: ${mainPreprocessed.originalContent.length}`, 'EnginePreProcess');

        // Start with original content
        let contentHtml = mainPreprocessed.originalContent;

        // Apply ALL replacement mappings from ALL templates (TemplateLoader did all the processing)
        contentHtml = this.applyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed);

        Logger.debug(`MergeTemplates complete: output size=${contentHtml.length}`, 'EnginePreProcess');

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
            const appKey = CommonUtil.replaceCaseInsensitive(templateName, viewPrefix, appView);
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

        Logger.debug(`Starting ApplyTemplateReplacements, initial size: ${content.length}`, 'EnginePreProcess');

        // Apply replacement mappings from all templates in multiple passes until no more changes
        const maxPasses = 10; // Prevent infinite loops
        let currentPass = 0;

        do {
            const previous = result;
            currentPass++;

            Logger.debug(`Replacement pass ${currentPass}, current size: ${result.length}`, 'EnginePreProcess');

            let slottedCount = 0;
            let simpleCount = 0;
            let jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template (to avoid overwriting component content)
            if (mainTemplate && currentPass === 1 && enableJsonProcessing) {
                for (const mapping of mainTemplate.replacementMappings) {
                    if ((mapping.type === 0 || mapping.type === "JsonPlaceholder") && result.includes(mapping.originalText)) {
                        Logger.debug(`Applying main template JSON placeholder: ${mapping.originalText} -> ${mapping.replacementText}`, 'EnginePreProcess');
                        result = result.replaceAll(mapping.originalText, mapping.replacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            // Apply replacement mappings from all templates
            const templates = preprocessedTemplates instanceof Map ?
                Array.from(preprocessedTemplates.values()) :
                Object.values(preprocessedTemplates);

            for (const template of templates) {
                // CRITICAL: Apply slotted template mappings FIRST (before JSON processing changes the content)
                for (const mapping of template.replacementMappings) {
                    if ((mapping.type === 2 || mapping.type === "SlottedTemplate") && result.includes(mapping.originalText)) {
                        result = result.replaceAll(mapping.originalText, mapping.replacementText);
                        slottedCount++;
                    }
                }

                // Then apply other replacement mappings (simple templates) with AppView logic
                // Replacement text already has JSON values baked in by LoaderPreProcess
                for (const mapping of template.replacementMappings) {
                    if ((mapping.type === 1 || mapping.type === "SimpleTemplate") && result.includes(mapping.originalText)) {
                        // Apply AppView logic before replacement
                        const replacementText = this.applyAppViewLogicToReplacement(mapping.originalText, mapping.replacementText, preprocessedTemplates, appView);
                        Logger.debug(`Applying simple template: ${mapping.originalText} -> replacement text (first 200 chars): ${replacementText.substring(0, Math.min(200, replacementText.length))}`, 'EnginePreProcess');
                        result = result.replaceAll(mapping.originalText, replacementText);
                        simpleCount++;
                    }
                }
            }

            Logger.debug(`Pass ${currentPass} applied: ${jsonPlaceholderCount} main JSON placeholders, ${slottedCount} slotted, ${simpleCount} simple`, 'EnginePreProcess');

            // Break if no changes were made or max passes reached
            if (result === previous || currentPass >= maxPasses) {
                break;
            }
        } while (true);

        // All JSON replacements are handled in LoaderPreProcess during replacement mapping creation
        // The engine only does simple string replacements using pre-prepared mappings
        Logger.debug(`Replacement complete after ${currentPass} passes, final size: ${result.length}`, 'EnginePreProcess');

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
        return appViewTemplate.originalContent;
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