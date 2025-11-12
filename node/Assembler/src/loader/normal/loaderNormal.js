// Normal loader equivalent to C# ILoaderNormal
import fs from 'fs';
import path from 'path';
import { Logger } from '../../../../Arshu/src/common/Logger.js';
import { normalizeFileContent, replaceCaseInsensitive } from '../../common/commonUtil.js';
import { mergeTemplateWithJson } from '../jsonMergeUtil.js';

export class LoaderNormal {
    static #cache = new Map(); // key: root|appSite|searchAppSites -> Map(key -> {html,json})
    #rootDirPath;
    #appSite;
    #templates;
    #parentMap; // For JSON inheritance

    constructor(rootDirPath, appSite, searchAppSites = '') {
        this.#rootDirPath = rootDirPath || '';
        this.#appSite = appSite || '';
        this.searchAppSites = searchAppSites || '';
        this.#templates = new Map();
        this.#parentMap = new Map();

        // Auto-load if parameters are provided (matching C# pattern)
        if (rootDirPath && appSite !== undefined) {
            this.load(rootDirPath, appSite, searchAppSites);
        }
    }

    // ILoaderNormal: load
    load(rootDirPath, appSite, searchAppSites) {
        if (rootDirPath) this.#rootDirPath = rootDirPath;
        if (appSite) this.#appSite = appSite;
        if (typeof searchAppSites === 'string') this.searchAppSites = searchAppSites;

        const cacheKey = `${path.dirname(this.#rootDirPath)}|${this.#appSite}|${this.searchAppSites}`;
        if (LoaderNormal.#cache.has(cacheKey)) {
            this.#templates = LoaderNormal.#cache.get(cacheKey);
            Logger.debug(`LoaderNormal: using cached templates for ${this.#appSite} (${this.#templates.size})`, 'LoaderNormal');
            return true;
        }

        const result = new Map();
        // primary
        this.#loadTemplatesFromSingleAppSite(this.#rootDirPath, this.#appSite, result);
        // fallbacks
        if (this.searchAppSites) {
            for (const raw of this.searchAppSites.split(',')) {
                const site = raw.trim();
                if (!site) continue;
                this.#loadTemplatesFromSingleAppSite(this.#rootDirPath, site, result, true);
            }
        }
        this.#templates = result;
        LoaderNormal.#cache.set(cacheKey, result);

        // Build parent-child relationship map for JSON inheritance
        this.#parentMap = this.BuildParentMap();
        Logger.debug(`Built parent map with ${this.#parentMap.size} relationships for JSON inheritance`, 'LoaderNormal');

        return true;
    }

    // ILoaderNormal: hasTemplate
    hasTemplate(appSite, templateName) {
        const key = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        return this.#templates.has(key);
    }

    // ILoaderNormal: clearCache
    static clearCache() {
        LoaderNormal.#cache.clear();
    }

    // Instance clearCache for interface compatibility
    clearCache() {
        LoaderNormal.#cache.clear();
    }

    // ILoaderNormal: getTemplateHtml with AppView + SearchAppSites logic
    getTemplateHtml(appSite, templateName, appView = null, appViewPrefix = null) {
        const rec = this.#getTemplateInternal(appSite, templateName, appView, appViewPrefix);
        return rec ? rec.html : null;
    }

    // ILoaderNormal: mergeHtmlWithJson centralizing JSON merging
    mergeHtmlWithJson(html, appSite, templateName) {
        if (!html) return html;

        // Get JSON with inheritance resolution (matching C# GetTemplateJsonWithInheritance)
        const jsonObj = this.GetTemplateJsonWithInheritance(appSite, templateName);
        if (!jsonObj) return html;

        return mergeTemplateWithJson(html, jsonObj);
    }

    // ============== internals ==============
    #getTemplateInternal(appSite, templateName, appView, appViewPrefix) {
        const viewPrefix = appViewPrefix || '';
        if (appView && viewPrefix && templateName.toLowerCase().includes(viewPrefix.toLowerCase())) {
            const appKey = replaceCaseInsensitive(templateName, viewPrefix, appView);
            const fallbackKey = `${appSite.toLowerCase()}_${appKey.toLowerCase()}`;
            if (this.#templates.has(fallbackKey)) return this.#templates.get(fallbackKey);
        }
        const primaryKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        if (this.#templates.has(primaryKey)) return this.#templates.get(primaryKey);
        if (this.searchAppSites) {
            for (const raw of this.searchAppSites.split(',')) {
                const site = raw.trim();
                if (!site) continue;
                const searchKey = `${site.toLowerCase()}_${templateName.toLowerCase()}`;
                if (this.#templates.has(searchKey)) {
                    Logger.debug(`LoaderNormal: fallback template '${templateName}' from '${site}'`, 'LoaderNormal');
                    return this.#templates.get(searchKey);
                }
            }
        }
        return null;
    }

    #loadTemplatesFromSingleAppSite(rootDirPath, appSite, result, isFallback = false) {
        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);
        if (!fs.existsSync(appSitesPath) || !fs.statSync(appSitesPath).isDirectory()) {
            Logger.warn(`AppSites directory not found: ${appSitesPath}`, 'LoaderNormal');
            return;
        }
        Logger.debug(`LoaderNormal: loading ${isFallback ? 'fallback ' : ''}templates from ${appSitesPath}`, 'LoaderNormal');
        this.#walkDirectory(appSitesPath, (filePath, stats) => {
            if (!stats.isFile() || path.extname(filePath).toLowerCase() !== '.html') return;
            const fileName = path.basename(filePath, '.html');
            const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;
            if (result.has(key)) return; // primary wins
            const htmlContent = normalizeFileContent(fs.readFileSync(filePath, 'utf8'));
            let jsonContent = null;
            const jsonFile = filePath.replace(/\.html$/i, '.json');
            if (fs.existsSync(jsonFile)) {
                jsonContent = normalizeFileContent(fs.readFileSync(jsonFile, 'utf8'));
            } else {
                const dir = path.dirname(filePath);
                const baseName = path.basename(filePath, '.html').toLowerCase();
                const entries = fs.readdirSync(dir);
                for (const entry of entries) {
                    const entryPath = path.join(dir, entry);
                    if (fs.statSync(entryPath).isFile() && path.extname(entry).toLowerCase() === '.json') {
                        const entryBase = path.basename(entry, path.extname(entry)).toLowerCase();
                        if (entryBase === baseName) {
                            jsonContent = normalizeFileContent(fs.readFileSync(entryPath, 'utf8'));
                            break;
                        }
                    }
                }
            }
            result.set(key, { html: htmlContent, json: jsonContent });
        });
    }

    #walkDirectory(dir, callback) {
        const files = fs.readdirSync(dir);
        for (const file of files) {
            const filePath = path.join(dir, file);
            const stats = fs.statSync(filePath);
            callback(filePath, stats);
            if (stats.isDirectory()) this.#walkDirectory(filePath, callback);
        }
    }

    // ============== JSON Inheritance Support (Private) ==============
    // NOTE: The following methods are INTENTIONAL COPIES used across all loaders for architectural separation.
    // Each loader/engine pair is independent to allow individual evolution without shared dependencies.
    // DO NOT extract these to shared utilities - that would create tight coupling.

    /**
     * Builds a parent-child relationship map by analyzing template placeholders
     * Tracks which template is the parent of another based on {{TemplateName}} references
     * Matches C# BuildParentMap()
     */
    BuildParentMap() {
        const parentMap = new Map();

        Logger.debug(`Building parent map for appSite: ${this.#appSite}`, 'LoaderNormal');

        for (const [templateKey, template] of this.#templates) {
            const html = template.html;

            // Find all {{TemplateName}} placeholders in this template
            let searchPos = 0;
            while (searchPos < html.length) {
                const openStart = html.indexOf('{{', searchPos);
                if (openStart === -1) break;

                // Skip special placeholders (#, @, $, /)
                if (openStart + 2 < html.length &&
                    (html[openStart + 2] === '#' || html[openStart + 2] === '@' ||
                     html[openStart + 2] === '$' || html[openStart + 2] === '/')) {
                    searchPos = openStart + 2;
                    continue;
                }

                const closeStart = html.indexOf('}}', openStart + 2);
                if (closeStart === -1) break;

                const placeholderName = html.substring(openStart + 2, closeStart).trim();

                // Check if this is a valid alphanumeric template name
                if (placeholderName && this.IsAlphaNumeric(placeholderName)) {
                    // This template (templateKey) is the parent of the placeholder template
                    const childTemplateKey = `${this.#appSite.toLowerCase()}_${placeholderName.toLowerCase()}`;

                    if (!parentMap.has(childTemplateKey)) {
                        parentMap.set(childTemplateKey, templateKey);
                        Logger.debug(`Parent relationship: ${childTemplateKey} -> parent: ${templateKey}`, 'LoaderNormal');
                    }
                }

                searchPos = closeStart + 2;
            }
        }

        Logger.debug(`Built parent map with ${parentMap.size} relationships`, 'LoaderNormal');
        return parentMap;
    }

    /**
     * Gets parsed JSON with inheritance resolution
     * Resolves keys ending with # by searching up the parent tree
     * Matches C# GetTemplateJsonWithInheritance()
     */
    GetTemplateJsonWithInheritance(appSite, templateName) {
        const templateKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        Logger.debug(`GetTemplateJsonWithInheritance: templateKey=${templateKey}`, 'LoaderNormal');

        // Try to get JSON from primary appSite template
        let jsonContent = null;
        let actualTemplateKey = templateKey;

        if (this.#templates.has(templateKey)) {
            jsonContent = this.#templates.get(templateKey).json;
        } else {
            // Try searchAppSites fallback
            if (this.searchAppSites) {
                for (const searchAppSite of this.searchAppSites.split(',')) {
                    const site = searchAppSite.trim();
                    if (!site) continue;

                    const searchKey = `${site.toLowerCase()}_${templateName.toLowerCase()}`;
                    if (this.#templates.has(searchKey)) {
                        jsonContent = this.#templates.get(searchKey).json;
                        actualTemplateKey = searchKey; // Update key for inheritance resolution
                        break;
                    }
                }
            }
        }

        if (!jsonContent) {
            Logger.debug(`No JSON found for templateKey=${templateKey}`, 'LoaderNormal');
            return null;
        }

        // Parse JSON string
        let jsonObj;
        try {
            jsonObj = JSON.parse(jsonContent);
        } catch (ex) {
            Logger.error(`Failed to parse JSON for ${templateKey}: ${ex.message}`, 'LoaderNormal');
            return null;
        }

        const rawKeys = Object.keys(jsonObj).join(', ');
        Logger.debug(`Raw JSON keys for ${templateKey}: ${rawKeys}`, 'LoaderNormal');

        const resolvedJson = {};

        // Process each JSON key and resolve inheritance
        for (const [key, value] of Object.entries(jsonObj)) {
            // Check if this is an inheritable key (ends with #)
            if (key.endsWith('#') && typeof value === 'string') {
                // Resolve inherited value
                const actualKey = key.substring(0, key.length - 1);
                Logger.debug(`Found inheritance key: ${key}, defaultValue=${value}, resolving for actualKey=${actualKey}`, 'LoaderNormal');
                const resolvedValue = this.ResolveJsonKeyWithInheritance(actualKey, value, actualTemplateKey);
                if (resolvedValue !== null) {
                    resolvedJson[actualKey] = resolvedValue;
                    Logger.debug(`Resolved inherited key ${key} -> ${actualKey} = ${resolvedValue}`, 'LoaderNormal');
                    continue;
                }
            }

            // Normal key - keep as is
            resolvedJson[key] = value;
        }

        return resolvedJson;
    }

    /**
     * Resolves a JSON key by searching up the parent tree
     * Matches C# ResolveJsonKeyWithInheritance()
     */
    ResolveJsonKeyWithInheritance(actualKey, defaultValue, currentTemplateKey) {
        Logger.debug(`Resolving inherited key: ${actualKey} for template ${currentTemplateKey}`, 'LoaderNormal');

        // Search up the parent tree for the key
        const inheritedValue = this.SearchParentTreeForKey(actualKey, currentTemplateKey);

        if (inheritedValue !== null) {
            Logger.debug(`Found inherited value for ${actualKey}: ${inheritedValue}`, 'LoaderNormal');
            return inheritedValue;
        }

        // If not found in parents, use the default value
        Logger.debug(`No inherited value found for ${actualKey}, using default: ${defaultValue}`, 'LoaderNormal');
        return defaultValue;
    }

    /**
     * Searches up the parent tree to find a JSON key value
     * Matches C# SearchParentTreeForKey()
     */
    SearchParentTreeForKey(key, currentTemplateKey) {
        // Get parent template key
        if (!this.#parentMap.has(currentTemplateKey)) {
            Logger.debug(`No parent found for ${currentTemplateKey}`, 'LoaderNormal');
            return null;
        }

        const parentKey = this.#parentMap.get(currentTemplateKey);
        Logger.debug(`Checking parent ${parentKey} for key ${key}`, 'LoaderNormal');

        // Get parent's template
        if (!this.#templates.has(parentKey)) {
            Logger.debug(`Parent template ${parentKey} not found in templates`, 'LoaderNormal');
            return null;
        }

        const parentTemplate = this.#templates.get(parentKey);
        if (!parentTemplate.json) {
            Logger.debug(`Parent template ${parentKey} has no JSON data, searching further up`, 'LoaderNormal');
            // Parent has no JSON, search further up the tree
            return this.SearchParentTreeForKey(key, parentKey);
        }

        // Parse parent's JSON
        try {
            const parentJsonObj = JSON.parse(parentTemplate.json);

            // Look for the key (case-insensitive)
            for (const [jsonKey, jsonValue] of Object.entries(parentJsonObj)) {
                if (jsonKey.toLowerCase() === key.toLowerCase()) {
                    if (typeof jsonValue === 'string') {
                        Logger.debug(`Found key ${key} in parent ${parentKey}: ${jsonValue}`, 'LoaderNormal');
                        return jsonValue;
                    }
                }
            }

            Logger.debug(`Key ${key} not found in parent ${parentKey}, searching further up`, 'LoaderNormal');
            // Not found in this parent, search further up the tree
            return this.SearchParentTreeForKey(key, parentKey);
        } catch (ex) {
            Logger.error(`Error parsing JSON for parent ${parentKey}: ${ex.message}`, 'LoaderNormal');
            return null;
        }
    }

    /**
     * Checks if a string contains only alphanumeric characters
     * Matches C# IsAlphaNumeric()
     */
    IsAlphaNumeric(str) {
        for (let i = 0; i < str.length; i++) {
            const code = str.charCodeAt(i);
            if (!(code > 47 && code < 58) && // 0-9
                !(code > 64 && code < 91) && // A-Z
                !(code > 96 && code < 123)) { // a-z
                return false;
            }
        }
        return true;
    }
}
