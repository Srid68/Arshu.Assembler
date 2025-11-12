import { Logger } from '../../../../Arshu/src/common/Logger.js';

/**
 * PreProcess template engine implementation that delegates to ILoaderPreProcess
 * All parsing and merging logic is in the loader, engine only coordinates
 */
export class EnginePreProcess {
    constructor(appViewPrefix = '') {
        this.appViewPrefix = appViewPrefix;
    }

    /**
     * Merges templates using loader instance via ILoaderPreProcess interface
     * This method only coordinates - all logic is delegated to the loader
     * @param {string} appSite The application site name
     * @param {string} appFile The application file name
     * @param {string|null} appView The application view name (optional)
     * @param {ILoaderPreProcess} loader The loader instance that implements ILoaderPreProcess
     * @param {boolean} enableJsonProcessing Whether to enable JSON data processing
     * @returns {string} HTML with placeholders replaced
     */
    mergeTemplates(appSite, appFile, appView, loader, enableJsonProcessing = true) {
        Logger.debug(`MergeTemplates called: appSite=${appSite}, appFile=${appFile}, appView=${appView || 'null'}, enableJson=${enableJsonProcessing}`, 'EnginePreProcess');

        if (!loader) {
            Logger.warn('No loader provided', 'EnginePreProcess');
            return '';
        }

        // Get main template from loader
        const mainPreprocessed = loader.getTemplateHtml(appSite, appFile, appView, this.appViewPrefix);
        if (!mainPreprocessed) {
            Logger.warn(`Main template not found for appSite=${appSite}, appFile=${appFile}`, 'EnginePreProcess');
            return '';
        }

        Logger.debug(`Main template found, original size: ${mainPreprocessed.originalContent.length}`, 'EnginePreProcess');

        // Start with original content
        let contentHtml = mainPreprocessed.originalContent;

        // Delegate all replacement logic to the loader
        contentHtml = loader.applyAllReplacementMappings(
            contentHtml,
            appSite,
            mainPreprocessed,
            appView,
            this.appViewPrefix,
            enableJsonProcessing
        );

        Logger.debug(`MergeTemplates complete: output size=${contentHtml.length}`, 'EnginePreProcess');

        return contentHtml;
    }
}
