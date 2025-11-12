// ILoaderPreProcess interface (documentation + structural contract)
// Loader interface specifically for EnginePreProcess
// Provides preprocessed template access with pre-calculated replacement mappings
// Independent interface to ensure no coupling with other engine implementations

/**
 * @typedef {Object} ILoaderPreProcess
 * @property {string} searchAppSites - Comma-delimited list of fallback AppSites
 * @property {(rootDirPath:string, appSite:string, searchAppSites:string)=>boolean} load - Loads and preprocesses all templates
 * @property {(appSite:string, templateName:string)=>boolean} hasTemplate - Checks if a template exists
 * @property {()=>void} clearCache - Clears any internal caches
 * @property {(appSite:string, templateName:string, appView?:string|null, appViewPrefix?:string|null)=>PreprocessedTemplate|null} getTemplateHtml - Gets preprocessed template with all replacement mappings pre-calculated
 * @property {(html:string, appSite:string, templateName:string)=>string} mergeHtmlWithJson - Merges HTML with JSON data
 * @property {(content:string, appSite:string, mainTemplate:PreprocessedTemplate|null, appView:string|null, appViewPrefix:string|null, enableJsonProcessing:boolean)=>string} applyAllReplacementMappings - Applies all replacement mappings from all templates
 */

// This file intentionally exports a named symbol to make the interface discoverable in imports.
// Implementations should provide these methods; no runtime enforcement is performed.
export const ILoaderPreProcess = Symbol('ILoaderPreProcess');
