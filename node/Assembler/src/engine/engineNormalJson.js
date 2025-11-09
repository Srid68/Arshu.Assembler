import { Logger } from '@arshu/arshu/logger';
import { isAlphaNumeric, findMatchingCloseTag, removeRemainingSlotPlaceholders } from '../common/commonUtil.js';
import { mergeTemplateWithJson } from './jsonMergeUtil.js';

class EngineNormalJson {
    constructor() {
        this.appViewPrefix = '';
    }

    mergeTemplates(appSite, appFile, appView, loader, enableJsonProcessing = true) {
        Logger.debug(`MergeTemplates called: appSite=${appSite}, appFile=${appFile}, appView=${appView || 'null'}, enableJson=${enableJsonProcessing}`, 'EngineNormalJson');

        if (!loader) {
            Logger.warn('No loader provided', 'EngineNormalJson');
            return '';
        }

        let contentHtml = loader.getTemplateHtml(appSite, appFile, appView, this.appViewPrefix);
        if (!contentHtml) {
            Logger.warn(`Main template not found for appSite=${appSite}, appFile=${appFile}`, 'EngineNormalJson');
            return '';
        }

        Logger.debug(`Main template found, html size: ${contentHtml.length}`, 'EngineNormalJson');

        if (enableJsonProcessing) {
            const jsonData = loader.getTemplateJson(appSite, appFile);
            if (jsonData) {
                Logger.debug(`Merging main template with JSON`, 'EngineNormalJson');
                contentHtml = mergeTemplateWithJson(contentHtml, jsonData);
                Logger.debug(`After main JSON merge: ${contentHtml.length} chars`, 'EngineNormalJson');
            }
        }

        let previous;
        const maxPasses = 10;
        let actualPasses = 0;
        for (let pass = 0; pass < maxPasses; pass++) {
            previous = contentHtml;
            actualPasses = pass + 1;

            Logger.debug(`Pass ${actualPasses}, current size: ${contentHtml.length}`, 'EngineNormalJson');

            contentHtml = this.mergeTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing);
            Logger.debug(`After slot merge: ${contentHtml.length} chars`, 'EngineNormalJson');

            contentHtml = this.replaceTemplatePlaceholders(contentHtml, appSite, appView, loader, enableJsonProcessing);
            Logger.debug(`After placeholder replacement: ${contentHtml.length} chars`, 'EngineNormalJson');

            if (contentHtml === previous) {
                Logger.debug(`No changes in pass ${actualPasses}, stopping`, 'EngineNormalJson');
                break;
            }
        }

        Logger.debug(`MergeTemplates complete after ${actualPasses} passes: output size=${contentHtml.length}`, 'EngineNormalJson');
        return contentHtml;
    }

    getTemplateWithJson(appSite, templateName, loader, appView, enableJsonProcessing) {
        let html = loader.getTemplateHtml(appSite, templateName, appView, this.appViewPrefix);
        if (!html) {
            return null;
        }

        Logger.debug(`GetTemplateWithJson: template=${templateName}, html size=${html.length}`, 'EngineNormalJson');

        if (enableJsonProcessing) {
            const jsonData = loader.getTemplateJson(appSite, templateName);
            if (jsonData) {
                Logger.debug(`Merging JSON for template ${templateName}`, 'EngineNormalJson');
                const originalSize = html.length;
                html = mergeTemplateWithJson(html, jsonData);
                Logger.debug(`After JSON merge for ${templateName}: size ${originalSize} -> ${html.length}`, 'EngineNormalJson');
            } else {
                Logger.debug(`No JSON data found for template ${templateName}`, 'EngineNormalJson');
            }
        }

        return html;
    }

    mergeTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing) {
        if (!contentHtml) {
            return contentHtml;
        }

        let previous;
        do {
            previous = contentHtml;
            contentHtml = this.processTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing);
        } while (contentHtml !== previous);
        return contentHtml;
    }

    processTemplateSlots(contentHtml, appSite, appView, loader, enableJsonProcessing) {
        let result = contentHtml;
        let searchPos = 0;

        while (searchPos < result.length) {
            const openStart = result.indexOf('{{#', searchPos);
            if (openStart === -1) break;

            const openEnd = result.indexOf('}}', openStart + 3);
            if (openEnd === -1) break;

            const templateName = result.substring(openStart + 3, openEnd).trim();
            if (!templateName || !isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            const closeTag = `{{/${templateName}}}`;
            const closeStart = findMatchingCloseTag(result, openEnd + 2, `{{#${templateName}}}`, closeTag);
            if (closeStart === -1) {
                searchPos = openStart + 1;
                continue;
            }

            const innerStart = openEnd + 2;
            const innerContent = result.substring(innerStart, closeStart);

            const templateHtml = this.getTemplateWithJson(appSite, templateName, loader, appView, enableJsonProcessing);

            if (templateHtml) {
                const slotContents = this.extractSlotContents(innerContent, appSite, appView, loader, enableJsonProcessing);
                let processedTemplate = templateHtml;
                for (const key in slotContents) {
                    if (!Object.prototype.hasOwnProperty.call(slotContents, key)) continue;
                    const replacementValue = slotContents[key] ?? '';
                    processedTemplate = processedTemplate.split(key).join(replacementValue);
                }

                processedTemplate = removeRemainingSlotPlaceholders(processedTemplate);

                const fullMatch = result.substring(openStart, closeStart + closeTag.length);
                result = result.replace(fullMatch, processedTemplate);
                searchPos = openStart + processedTemplate.length;
            } else {
                searchPos = openStart + 1;
            }
        }

        return result;
    }

    extractSlotContents(innerContent, appSite, appView, loader, enableJsonProcessing) {
        const slotContents = {};
        let searchPos = 0;

        while (searchPos < innerContent.length) {
            const slotStart = innerContent.indexOf('{{@HTMLPLACEHOLDER', searchPos);
            if (slotStart === -1) break;

            const afterPlaceholder = slotStart + 18;
            let slotNum = '';
            let pos = afterPlaceholder;

            while (pos < innerContent.length && /\d/.test(innerContent[pos])) {
                slotNum += innerContent[pos];
                pos++;
            }

            if (pos + 1 >= innerContent.length || innerContent.substring(pos, pos + 2) !== '}}') {
                searchPos = slotStart + 1;
                continue;
            }

            const slotOpenEnd = pos + 2;
            const openTag = `{{@HTMLPLACEHOLDER${slotNum}}}`;
            const closeTag = `{{/HTMLPLACEHOLDER${slotNum}}}`;

            const closeStart = findMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag);
            if (closeStart === -1) {
                searchPos = slotStart + 1;
                continue;
            }

            const slotContent = innerContent.substring(slotOpenEnd, closeStart);
            const slotKey = `{{$HTMLPLACEHOLDER${slotNum}}}`;

            let recursiveResult = this.mergeTemplateSlots(slotContent, appSite, appView, loader, enableJsonProcessing);
            recursiveResult = this.replaceTemplatePlaceholders(recursiveResult, appSite, appView, loader, enableJsonProcessing);
            slotContents[slotKey] = recursiveResult;

            searchPos = closeStart + closeTag.length;
        }

        return slotContents;
    }

    replaceTemplatePlaceholders(html, appSite, appView, loader, enableJsonProcessing) {
        let result = html;
        let searchPos = 0;

        while (searchPos < result.length) {
            const openStart = result.indexOf('{{', searchPos);
            if (openStart === -1) break;

            if (openStart + 2 < result.length && ['#', '@', '$', '/'].includes(result[openStart + 2])) {
                searchPos = openStart + 2;
                continue;
            }

            const closeStart = result.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            const placeholderName = result.substring(openStart + 2, closeStart).trim();
            if (!placeholderName || !isAlphaNumeric(placeholderName)) {
                searchPos = openStart + 2;
                continue;
            }

            const templateContent = this.getTemplateWithJson(appSite, placeholderName, loader, appView, enableJsonProcessing);

            if (templateContent) {
                const processedReplacement = this.replaceTemplatePlaceholders(templateContent, appSite, appView, loader, enableJsonProcessing);
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

export {
    EngineNormalJson,
};
