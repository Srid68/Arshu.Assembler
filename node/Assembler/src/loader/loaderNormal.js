// Normal template loader for Node.js - handles loading and caching of HTML templates from filesystem

import fs from 'fs';
import path from 'path';
import { Logger } from '@arshu/arshu/logger';
import { normalizeFileContent } from '../common/commonUtil.js';

export class TemplateResult {
    constructor(html, json = null) {
        this.html = html;
        this.json = json;
    }
}

export class LoaderNormal {
    static #htmlTemplatesCache = new Map();

    /**
     * Loads HTML files and corresponding JSON files from the specified application site directory, caching the output per appSite
     * @param {string} rootDirPath - Root directory path
     * @param {string} appSite - Application site name
     * @returns {Map<string, TemplateResult>} Map of templates
     */
    static loadGetTemplateFiles(rootDirPath, appSite, searchAppSites = "") {
        Logger.debug(`LoadGetTemplateFiles called for appSite: ${appSite}, searchAppSites: ${searchAppSites}`, 'LoaderNormal');

        const cacheKey = `${path.dirname(rootDirPath)}|${appSite}|${searchAppSites}`;
        if (this.#htmlTemplatesCache.has(cacheKey)) {
            const cached = this.#htmlTemplatesCache.get(cacheKey);
            Logger.debug(`Returning cached templates for ${appSite} (${cached.size} templates)`, 'LoaderNormal');
            return cached;
        }

        // Load templates from primary appSite
        const result = this.#loadTemplatesFromSingleAppSite(rootDirPath, appSite);

        // Load templates from searchAppSites for fallback
        if (searchAppSites && searchAppSites.trim() !== "") {
            const searchAppSitesArray = searchAppSites.split(',');
            for (const searchAppSiteRaw of searchAppSitesArray) {
                const searchAppSite = searchAppSiteRaw.trim();
                if (!searchAppSite) continue;
                const searchTemplates = this.#loadTemplatesFromSingleAppSite(rootDirPath, searchAppSite);
                for (const [k, v] of searchTemplates.entries()) {
                    if (!result.has(k)) {
                        result.set(k, v);
                        Logger.debug(`Added fallback template '${k}' from '${searchAppSite}'`, 'LoaderNormal');
                    }
                }
            }
        }

        this.#htmlTemplatesCache.set(cacheKey, result);
        return result;
    }

    // Helper to load templates from a single AppSite (no caching/fallback)
    static #loadTemplatesFromSingleAppSite(rootDirPath, appSite) {
        const result = new Map();
        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);
        if (!fs.existsSync(appSitesPath) || !fs.statSync(appSitesPath).isDirectory()) {
            Logger.warn(`AppSites directory not found: ${appSitesPath}`, 'LoaderNormal');
            return result;
        }
        Logger.debug(`Loading templates from: ${appSitesPath}`, 'LoaderNormal');
        this.#walkDirectory(appSitesPath, (filePath, stats) => {
            if (stats.isFile() && path.extname(filePath) === '.html') {
                const fileName = path.basename(filePath, '.html');
                const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;
                const htmlContent = normalizeFileContent(fs.readFileSync(filePath, 'utf8'));
                Logger.debug(`Loading template: ${key} (html size: ${htmlContent.length})`, 'LoaderNormal');
                // Find JSON file case-insensitively
                const jsonFile = filePath.replace('.html', '.json');
                let jsonContent = null;
                if (fs.existsSync(jsonFile)) {
                    jsonContent = normalizeFileContent(fs.readFileSync(jsonFile, 'utf8'));
                    Logger.debug(`Found JSON file for ${key} (size: ${jsonContent.length})`, 'LoaderNormal');
                } else {
                    // Try case-insensitive search in the same directory
                    const dir = path.dirname(filePath);
                    const baseName = path.basename(filePath, '.html').toLowerCase();
                    const entries = fs.readdirSync(dir);
                    for (const entry of entries) {
                        const entryPath = path.join(dir, entry);
                        if (fs.statSync(entryPath).isFile() && path.extname(entry).toLowerCase() === '.json') {
                            const entryBase = path.basename(entry, path.extname(entry)).toLowerCase();
                            if (entryBase === baseName) {
                                jsonContent = normalizeFileContent(fs.readFileSync(entryPath, 'utf8'));
                                Logger.debug(`Found JSON file (case-insensitive) for ${key} (size: ${jsonContent.length})`, 'LoaderNormal');
                                break;
                            }
                        }
                    }
                }
                result.set(key, new TemplateResult(htmlContent, jsonContent));
            }
        });
        Logger.debug(`Loaded ${result.size} templates for ${appSite}`, 'LoaderNormal');
        return result;
    }

    /**
     * Clear all cached templates (useful for testing or when templates change)
     */
    static clearCache() {
        this.#htmlTemplatesCache.clear();
    }

    /**
     * Helper method to recursively walk directory
     * @param {string} dir - Directory to walk
     * @param {Function} callback - Callback function for each file/directory
     */
    static #walkDirectory(dir, callback) {
        const files = fs.readdirSync(dir);
        
        for (const file of files) {
            const filePath = path.join(dir, file);
            const stats = fs.statSync(filePath);
            
            callback(filePath, stats);
            
            if (stats.isDirectory()) {
                this.#walkDirectory(filePath, callback);
            }
        }
    }
}