import { Logger } from '@arshu/arshu/logger';
import { mergeTemplateWithJson } from './jsonMergeUtil.js';
import { ReplacementType } from '../model/modelPreProcess.js';
import { replaceCaseInsensitive } from '../common/commonUtil.js';

class EnginePreProcessJson {
    constructor() {
        this.appViewPrefix = '';
    }

    mergeTemplates(appSite, appFile, appView, loader, enableJsonProcessing = true) {
        Logger.debug(`MergeTemplates called: appSite=${appSite}, appFile=${appFile}, appView=${appView || 'null'}, enableJson=${enableJsonProcessing}`, 'EnginePreProcessJson');

        if (!loader) {
            Logger.warn('No loader provided', 'EnginePreProcessJson');
            return '';
        }

        const preprocessedTemplates = loader.getAllTemplates ? loader.getAllTemplates() : null;
        if (!preprocessedTemplates || Object.keys(preprocessedTemplates).length === 0) {
            Logger.warn('No preprocessed templates available', 'EnginePreProcessJson');
            return '';
        }

        Logger.debug(`Using ${Object.keys(preprocessedTemplates).length} preprocessed templates`, 'EnginePreProcessJson');

        const mainPreprocessed = loader.getTemplateHtml(appSite, appFile, appView, this.appViewPrefix);
        if (!mainPreprocessed) {
            Logger.warn(`Main template not found for appSite=${appSite}, appFile=${appFile}`, 'EnginePreProcessJson');
            return '';
        }

        Logger.debug(`Main template found, original size: ${mainPreprocessed.originalContent.length}`, 'EnginePreProcessJson');

        let contentHtml = mainPreprocessed.originalContent;

        if (enableJsonProcessing) {
            contentHtml = loader.mergeHtmlWithJson(contentHtml, appSite, appFile);
            Logger.debug(`After main template JSON merge: ${contentHtml.length} chars`, 'EnginePreProcessJson');
        }

        contentHtml = this.applyTemplateReplacements(contentHtml, preprocessedTemplates, enableJsonProcessing, appView, mainPreprocessed, loader, appSite);

        Logger.debug(`MergeTemplates complete: output size=${contentHtml.length}`, 'EnginePreProcessJson');
        return contentHtml;
    }

    getTemplate(appSite, templateName, preprocessedTemplates, appView = null, appViewPrefix = null, useAppViewFallback = true) {
        if (!preprocessedTemplates || Object.keys(preprocessedTemplates).length === 0) {
            return null;
        }

        const viewPrefix = appViewPrefix || this.appViewPrefix;

        if (useAppViewFallback && appView && viewPrefix && templateName.toLowerCase().includes(viewPrefix.toLowerCase())) {
            const appKey = replaceCaseInsensitive(templateName, viewPrefix, appView);
            const fallbackTemplateKey = `${appSite.toLowerCase()}_${appKey.toLowerCase()}`;
            if (preprocessedTemplates[fallbackTemplateKey]) {
                return preprocessedTemplates[fallbackTemplateKey];
            }
        }

        const primaryTemplateKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        if (preprocessedTemplates[primaryTemplateKey]) {
            return preprocessedTemplates[primaryTemplateKey];
        }

        return null;
    }

    applyTemplateReplacements(content, preprocessedTemplates, enableJsonProcessing, appView, mainTemplate, loader, appSite) {
        let result = content;
        Logger.debug(`Starting ApplyTemplateReplacements, initial size: ${content.length}`, 'EnginePreProcessJson');

        let previous;
        const maxPasses = 10;
        let currentPass = 0;

        do {
            previous = result;
            currentPass++;
            Logger.debug(`Replacement pass ${currentPass}, current size: ${result.length}`, 'EnginePreProcessJson');

            let slottedCount = 0, simpleCount = 0, jsonPlaceholderCount = 0;

            if (mainTemplate && currentPass === 1 && enableJsonProcessing) {
                for (const mapping of mainTemplate.replacementMappings) {
                    if (mapping.type !== ReplacementType.JsonPlaceholder) continue;
                    if (result.includes(mapping.originalText)) {
                        Logger.debug(`Applying main template JSON placeholder: ${mapping.originalText} -> ${mapping.replacementText}`, 'EnginePreProcessJson');
                        result = result.replace(new RegExp(mapping.originalText, 'g'), mapping.replacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            for (const templateKey in preprocessedTemplates) {
                const template = preprocessedTemplates[templateKey];

                for (const mapping of template.replacementMappings) {
                    if (mapping.type !== ReplacementType.SlottedTemplate) continue;
                    if (result.includes(mapping.originalText)) {
                        let replacementText = mapping.replacementText;
                        if (enableJsonProcessing && mapping.targetTemplateName) {
                            replacementText = loader.mergeHtmlWithJson(replacementText, appSite, mapping.targetTemplateName);
                            Logger.debug(`After merging JSON for slotted template ${mapping.targetTemplateName}: ${replacementText.length} chars`, 'EnginePreProcessJson');
                        }
                        Logger.debug(`Applying slotted template: ${mapping.originalText.substring(0, Math.min(50, mapping.originalText.length))}... -> ${replacementText.length} chars`, 'EnginePreProcessJson');
                        result = result.replace(new RegExp(mapping.originalText, 'g'), replacementText);
                        slottedCount++;
                    }
                }

                for (const mapping of template.replacementMappings) {
                    if (mapping.type !== ReplacementType.SimpleTemplate) continue;
                    if (result.includes(mapping.originalText)) {
                        let replacementText = mapping.replacementText;
                        if (appView && mapping.targetTemplateName) {
                            const appViewTemplate = this.getTemplate(appSite, mapping.targetTemplateName, preprocessedTemplates, appView, this.appViewPrefix, true);
                            if (appViewTemplate) {
                                replacementText = appViewTemplate.originalContent;
                            }
                        }

                        if (enableJsonProcessing && mapping.targetTemplateName) {
                            replacementText = loader.mergeHtmlWithJson(replacementText, appSite, mapping.targetTemplateName);
                            Logger.debug(`After merging JSON for simple template ${mapping.targetTemplateName}: ${replacementText.length} chars`, 'EnginePreProcessJson');
                        }
                        Logger.debug(`Applying simple template: ${mapping.originalText} -> ${replacementText.length} chars`, 'EnginePreProcessJson');
                        result = result.replace(new RegExp(mapping.originalText, 'g'), replacementText);
                        simpleCount++;
                    }
                }
            }
            Logger.debug(`Pass ${currentPass} applied: ${jsonPlaceholderCount} main JSON placeholders, ${slottedCount} slotted, ${simpleCount} simple`, 'EnginePreProcessJson');
        } while (result !== previous && currentPass < maxPasses);

        Logger.debug(`Replacement complete after ${currentPass} passes, final size: ${result.length}`, 'EnginePreProcessJson');
        return result;
    }
}

export {
    EnginePreProcessJson,
};
