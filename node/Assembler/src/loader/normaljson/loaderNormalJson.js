import { promises as fs } from 'fs';
import path from 'path';
import { Logger } from '../../../../Arshu/src/common/Logger.js';
import { mergeTemplateWithJson } from '../jsonMergeUtil.js';
import { normalizeFileContent, replaceCaseInsensitive } from '../../common/commonUtil.js';

const _htmlTemplatesCache = new Map();

class LoaderNormalJson {
    constructor(rootDirPath, appSites, searchAppSites) {
        this.searchAppSites = searchAppSites;
        this._templates = new Map();
        this.rootDirPath = rootDirPath;
        this.appSites = appSites;
        this._parentMap = new Map(); // For JSON inheritance
    }

    async load() {
        const primaryTemplates = await this.loadGetTemplateFiles(this.rootDirPath, this.appSites);
        primaryTemplates.forEach((value, key) => this._templates.set(key, value));

        if (this.searchAppSites) {
            const searchAppSitesArray = this.searchAppSites.split(',');
            for (const searchAppSite of searchAppSitesArray) {
                const trimmedSite = searchAppSite.trim();
                if (trimmedSite) {
                    const searchTemplates = await this.loadGetTemplateFiles(this.rootDirPath, trimmedSite);
                    searchTemplates.forEach((value, key) => {
                        if (!this._templates.has(key)) {
                            this._templates.set(key, value);
                        }
                    });
                }
            }
        }

        // Build parent-child relationship map for JSON inheritance
        this._parentMap = this.BuildParentMap();
        Logger.debug(`Built parent map with ${this._parentMap.size} relationships for JSON inheritance`, 'LoaderNormalJson');
    }

    getTemplateHtml(appSite, templateName, appView = null, appViewPrefix = null) {
        const template = this.getTemplateInternal(appSite, templateName, appView, appViewPrefix);
        return template ? template.html : null;
    }

    getTemplateJson(appSite, templateName) {
        // Return JSON with inheritance resolved (matching C# behavior)
        return this.GetTemplateJsonWithInheritance(appSite, templateName);
    }

    // Centralized HTML+JSON merge to mirror C# loader API
    mergeHtmlWithJson(html, appSite, templateName) {
        if (!html) return html;

        // Get JSON with inheritance resolution (matching C# GetTemplateJsonWithInheritance)
        const jsonObj = this.GetTemplateJsonWithInheritance(appSite, templateName);
        if (!jsonObj) {
            Logger.debug(`No JSON data found for ${templateName}, returning original HTML`, 'LoaderNormalJson');
            return html;
        }

        Logger.debug(`Merging HTML with JSON for ${templateName}`, 'LoaderNormalJson');
        return mergeTemplateWithJson(html, jsonObj);
    }

    hasTemplate(appSite, templateName) {
        const key = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        return this._templates.has(key);
    }

    static clearCache() {
        _htmlTemplatesCache.clear();
    }

    // Instance clearCache for interface compatibility
    clearCache() {
        _htmlTemplatesCache.clear();
    }

    getTemplateInternal(appSite, templateName, appView, appViewPrefix) {
        if (appView && appViewPrefix && templateName.toLowerCase().includes(appViewPrefix.toLowerCase())) {
            const appKey = replaceCaseInsensitive(templateName, appViewPrefix, appView);
            const fallbackKey = `${appSite.toLowerCase()}_${appKey.toLowerCase()}`;
            if (this._templates.has(fallbackKey)) {
                return this._templates.get(fallbackKey);
            }
        }

        const primaryKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        if (this._templates.has(primaryKey)) {
            return this._templates.get(primaryKey);
        }

        if (this.searchAppSites) {
            const searchAppSitesArray = this.searchAppSites.split(',');
            for (const searchAppSite of searchAppSitesArray) {
                const trimmedSite = searchAppSite.trim();
                if (trimmedSite) {
                    const searchKey = `${trimmedSite.toLowerCase()}_${templateName.toLowerCase()}`;
                    if (this._templates.has(searchKey)) {
                        Logger.debug(`Template '${templateName}' not found in '${appSite}', using fallback from '${trimmedSite}'`, 'LoaderNormalJson');
                        return this._templates.get(searchKey);
                    }
                }
            }
        }

        return null;
    }

    async loadGetTemplateFiles(rootDirPath, appSite) {
        Logger.debug(`LoadGetTemplateFiles called for appSite: ${appSite}`, 'LoaderNormalJson');
        const cacheKey = `${path.dirname(rootDirPath)}|${appSite}`;
        if (_htmlTemplatesCache.has(cacheKey)) {
            const cached = _htmlTemplatesCache.get(cacheKey);
            Logger.debug(`Returning cached templates for ${appSite} (${cached.size} templates)`, 'LoaderNormalJson');
            return cached;
        }

        const result = new Map();
        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);

        try {
            await fs.access(appSitesPath);
        } catch (error) {
            Logger.warn(`AppSites directory not found: ${appSitesPath}`, 'LoaderNormalJson');
            _htmlTemplatesCache.set(cacheKey, result);
            return result;
        }

        Logger.debug(`Loading templates from: ${appSitesPath}`, 'LoaderNormalJson');
        const files = await this.readDirRecursive(appSitesPath);

        for (const file of files) {
            if (file.endsWith('.html')) {
                const fileName = path.basename(file, '.html');
                const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;
                const htmlContent = normalizeFileContent(await fs.readFile(file, 'utf-8'));
                Logger.debug(`Loading template: ${key} (html size: ${htmlContent.length})`, 'LoaderNormalJson');

                const jsonFile = file.replace('.html', '.json');
                let jsonObject = null;
                try {
                    const jsonContent = normalizeFileContent(await fs.readFile(jsonFile, 'utf-8'));
                    if (jsonContent) {
                        jsonObject = JSON.parse(jsonContent);
                        Logger.debug(`Found and parsed JSON file for ${key} (size: ${jsonContent.length})`, 'LoaderNormalJson');
                    }
                } catch (ex) {
                    // JSON file might not exist, which is fine.
                }
                result.set(key, { html: htmlContent, json: jsonObject });
            }
        }

        Logger.debug(`Loaded ${result.size} templates for ${appSite}`, 'LoaderNormalJson');
        _htmlTemplatesCache.set(cacheKey, result);
        return result;
    }

    async readDirRecursive(dir) {
        const dirents = await fs.readdir(dir, { withFileTypes: true });
        const files = await Promise.all(dirents.map((dirent) => {
            const res = path.resolve(dir, dirent.name);
            return dirent.isDirectory() ? this.readDirRecursive(res) : res;
        }));
        return Array.prototype.concat(...files);
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

        Logger.debug(`Building parent map for appSites: ${this.appSites}`, 'LoaderNormalJson');

        // Process templates in deterministic order to ensure consistent parent relationships
        // Sort keys: SearchAppSites first, then main AppSite (so main AppSite wins in case of conflicts)
        const mainAppSitePrefix = `${this.appSites.toLowerCase()}_`;
        const searchTemplateKeys = [];
        const mainTemplateKeys = [];

        for (const templateKey of this._templates.keys()) {
            if (templateKey.startsWith(mainAppSitePrefix)) {
                mainTemplateKeys.push(templateKey);
            } else {
                searchTemplateKeys.push(templateKey);
            }
        }

        // Process SearchAppSites templates first, then main AppSite (last wins)
        const allKeys = [...searchTemplateKeys, ...mainTemplateKeys];

        for (const templateKey of allKeys) {
            const template = this._templates.get(templateKey);
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
                    // Need to determine which appSite this child belongs to
                    const templateAppSite = templateKey.split('_')[0]; // Extract appSite from key
                    const childTemplateKey = `${templateAppSite.toLowerCase()}_${placeholderName.toLowerCase()}`;

                    // Use "last wins" strategy - later templates (main AppSite) override earlier ones (SearchAppSites)
                    const existingParent = parentMap.get(childTemplateKey);
                    if (existingParent && existingParent !== templateKey) {
                        Logger.debug(`Overwriting parent relationship: ${childTemplateKey} -> parent: ${templateKey} (was: ${existingParent})`, 'LoaderNormalJson');
                    } else if (!existingParent) {
                        Logger.debug(`Parent relationship: ${childTemplateKey} -> parent: ${templateKey}`, 'LoaderNormalJson');
                    }
                    parentMap.set(childTemplateKey, templateKey);
                }

                searchPos = closeStart + 2;
            }
        }

        Logger.debug(`Built parent map with ${parentMap.size} relationships`, 'LoaderNormalJson');
        return parentMap;
    }

    /**
     * Gets parsed JSON with inheritance resolution
     * Resolves keys ending with # by searching up the parent tree
     * Matches C# GetTemplateJsonWithInheritance()
     */
    GetTemplateJsonWithInheritance(appSite, templateName) {
        const templateKey = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        Logger.debug(`GetTemplateJsonWithInheritance: templateKey=${templateKey}`, 'LoaderNormalJson');

        // Try to get JSON from primary appSite template
        let jsonObject = null;
        let actualTemplateKey = templateKey;

        const template = this.getTemplateInternal(appSite, templateName, null, null);
        if (template) {
            jsonObject = template.json;
            // If we got it from searchAppSites fallback, update the key
            if (this.searchAppSites) {
                for (const searchAppSite of this.searchAppSites.split(',')) {
                    const site = searchAppSite.trim();
                    if (!site) continue;
                    const searchKey = `${site.toLowerCase()}_${templateName.toLowerCase()}`;
                    if (this._templates.has(searchKey) && this._templates.get(searchKey) === template) {
                        actualTemplateKey = searchKey;
                        break;
                    }
                }
            }
        }

        if (!jsonObject) {
            Logger.debug(`No JSON found for templateKey=${templateKey}`, 'LoaderNormalJson');
            return null;
        }

        const rawKeys = Object.keys(jsonObject).join(', ');
        Logger.debug(`Raw JSON keys for ${templateKey}: ${rawKeys}`, 'LoaderNormalJson');

        const resolvedJson = {};

        // Process each JSON key and resolve inheritance
        for (const [key, value] of Object.entries(jsonObject)) {
            // Check if this is an inheritable key (ends with #)
            if (key.endsWith('#') && typeof value === 'string') {
                // Resolve inherited value
                const actualKey = key.substring(0, key.length - 1);
                Logger.debug(`Found inheritance key: ${key}, defaultValue=${value}, resolving for actualKey=${actualKey}`, 'LoaderNormalJson');
                const resolvedValue = this.ResolveJsonKeyWithInheritance(actualKey, value, actualTemplateKey);
                if (resolvedValue !== null) {
                    resolvedJson[actualKey] = resolvedValue;
                    Logger.debug(`Resolved inherited key ${key} -> ${actualKey} = ${resolvedValue}`, 'LoaderNormalJson');
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
        Logger.debug(`Resolving inherited key: ${actualKey} for template ${currentTemplateKey}`, 'LoaderNormalJson');

        // Search up the parent tree for the key
        const inheritedValue = this.SearchParentTreeForKey(actualKey, currentTemplateKey);

        if (inheritedValue !== null) {
            Logger.debug(`Found inherited value for ${actualKey}: ${inheritedValue}`, 'LoaderNormalJson');
            return inheritedValue;
        }

        // If not found in parents, use the default value
        Logger.debug(`No inherited value found for ${actualKey}, using default: ${defaultValue}`, 'LoaderNormalJson');
        return defaultValue;
    }

    /**
     * Searches up the parent tree to find a JSON key value
     * Matches C# SearchParentTreeForKey()
     */
    SearchParentTreeForKey(key, currentTemplateKey) {
        // Get parent template key
        if (!this._parentMap.has(currentTemplateKey)) {
            Logger.debug(`No parent found for ${currentTemplateKey}`, 'LoaderNormalJson');
            return null;
        }

        const parentKey = this._parentMap.get(currentTemplateKey);
        Logger.debug(`Checking parent ${parentKey} for key ${key}`, 'LoaderNormalJson');

        // Get parent's template
        if (!this._templates.has(parentKey)) {
            Logger.debug(`Parent template ${parentKey} not found in templates`, 'LoaderNormalJson');
            return null;
        }

        const parentTemplate = this._templates.get(parentKey);
        if (!parentTemplate.json) {
            Logger.debug(`Parent template ${parentKey} has no JSON data, searching further up`, 'LoaderNormalJson');
            // Parent has no JSON, search further up the tree
            return this.SearchParentTreeForKey(key, parentKey);
        }

        // Look for the key in parent's JSON (case-insensitive)
        for (const [jsonKey, jsonValue] of Object.entries(parentTemplate.json)) {
            if (jsonKey.toLowerCase() === key.toLowerCase()) {
                if (typeof jsonValue === 'string') {
                    Logger.debug(`Found key ${key} in parent ${parentKey}: ${jsonValue}`, 'LoaderNormalJson');
                    return jsonValue;
                }
            }
        }

        Logger.debug(`Key ${key} not found in parent ${parentKey}, searching further up`, 'LoaderNormalJson');
        // Not found in this parent, search further up the tree
        return this.SearchParentTreeForKey(key, parentKey);
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

export { LoaderNormalJson };
