// ILoaderJson interface (documentation + structural contract)
// Generic loader interface that provides template extraction (HTML and JSON)
// and JSON merging with inheritance support for clean architecture

/**
 * @typedef {Object} ILoaderJson
 * @property {string} searchAppSites - Comma-delimited list of fallback AppSites
 * @property {(rootDirPath:string, appSite:string, searchAppSites:string)=>boolean} load - Loads and caches all templates
 * @property {(appSite:string, templateName:string)=>boolean} hasTemplate - Checks if a template exists
 * @property {()=>string} getAllTemplatesJson - Gets all templates as serialized JSON string for client-side engine
 * @property {()=>void} clearCache - Clears any internal caches
 * @property {(appSite:string, templateName:string, appView?:string|null, appViewPrefix?:string|null)=>any|null} getTemplateHtml - Gets template (type depends on implementation: string or PreprocessedTemplate)
 * @property {(html:string, appSite:string, templateName:string)=>string} mergeHtmlWithJson - Merges HTML with JSON data using inheritance-aware retrieval
 * @property {(content:string, appSite:string, mainTemplate:any, appView:string|null, appViewPrefix:string|null, enableJsonProcessing:boolean)=>string} applyAllReplacementMappings - Applies all replacement mappings (PreProcess only)
 */

// This file intentionally exports a named symbol to make the interface discoverable in imports.
// Implementations should provide these methods; no runtime enforcement is performed.
export const ILoaderJson = Symbol('ILoaderJson');
