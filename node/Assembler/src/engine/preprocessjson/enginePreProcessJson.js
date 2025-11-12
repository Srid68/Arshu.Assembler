import { Logger } from '../../../../Arshu/src/common/Logger.js';

/**
 * PreProcessJson template engine implementation that delegates to ILoaderJson
 * All parsing and merging logic is in the loader, engine only coordinates
 */
class EnginePreProcessJson {
    constructor() {
        this.appViewPrefix = '';
    }

    /**
     * Merges templates using loader instance via ILoaderJson interface
     * This method only coordinates - all logic is delegated to the loader
     * @param {string} appSite The application site name
     * @param {string} appFile The application file name
     * @param {string|null} appView The application view name (optional)
     * @param {ILoaderJson} loader The loader instance that implements ILoaderJson
     * @param {boolean} enableJsonProcessing Whether to enable JSON data processing
     * @returns {string} HTML with placeholders replaced
     */
    mergeTemplates(appSite, appFile, appView, loader, enableJsonProcessing = true) {
        Logger.debug(`MergeTemplates called: appSite=${appSite}, appFile=${appFile}, appView=${appView || 'null'}, enableJson=${enableJsonProcessing}`, 'EnginePreProcessJson');

        if (!loader) {
            Logger.warn('No loader provided', 'EnginePreProcessJson');
            return '';
        }

        // Get main template from loader
        const mainPreprocessed = loader.getTemplateHtml(appSite, appFile, appView, this.appViewPrefix);
        if (!mainPreprocessed) {
            Logger.warn(`Main template not found for appSite=${appSite}, appFile=${appFile}`, 'EnginePreProcessJson');
            return '';
        }

        Logger.debug(`Main template found, original size: ${mainPreprocessed.originalContent.length}`, 'EnginePreProcessJson');

        // Start with original content
        let contentHtml = mainPreprocessed.originalContent;

        // Apply main template JSON merge
        if (enableJsonProcessing) {
            contentHtml = loader.mergeHtmlWithJson(contentHtml, appSite, appFile);
            Logger.debug(`After main template JSON merge: ${contentHtml.length} chars`, 'EnginePreProcessJson');
        }

        // Delegate all replacement logic to the loader
        contentHtml = loader.applyAllReplacementMappings(
            contentHtml,
            appSite,
            mainPreprocessed,
            appView,
            this.appViewPrefix,
            enableJsonProcessing
        );

        Logger.debug(`MergeTemplates complete: output size=${contentHtml.length}`, 'EnginePreProcessJson');
        return contentHtml;
    }
}

export {
    EnginePreProcessJson,
};
