// ILoaderNormal interface (documentation + structural contract)
// Defines the minimal API that Normal engine relies on.

/**
 * @typedef {Object} ILoaderNormal
 * @property {string} searchAppSites - Comma-delimited list of fallback AppSites
 * @property {(rootDirPath:string, appSite:string, searchAppSites:string)=>boolean} load - Loads and caches templates
 * @property {(appSite:string, templateName:string)=>boolean} hasTemplate - Checks if a template exists
 * @property {()=>void} clearCache - Clears any internal caches
 * @property {(appSite:string, templateName:string, appView?:string|null, appViewPrefix?:string|null)=>string|null} getTemplateHtml - Gets raw HTML with AppView and fallback logic
 * @property {(html:string, appSite:string, templateName:string)=>string} mergeHtmlWithJson - Merges HTML with template JSON
 */

// This file intentionally exports a named symbol to make the interface discoverable in imports.
// Implementations should provide these methods; no runtime enforcement is performed.
export const ILoaderNormal = Symbol('ILoaderNormal');

