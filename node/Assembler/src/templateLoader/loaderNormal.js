// Normal template loader for Node.js - handles loading and caching of HTML templates from filesystem

import fs from 'fs';
import path from 'path';

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
    static loadGetTemplateFiles(rootDirPath, appSite) {
        const cacheKey = `${path.dirname(rootDirPath)}|${appSite}`;
        
        if (this.#htmlTemplatesCache.has(cacheKey)) {
            return this.#htmlTemplatesCache.get(cacheKey);
        }

        const result = new Map();
        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);
        
        if (!fs.existsSync(appSitesPath) || !fs.statSync(appSitesPath).isDirectory()) {
            this.#htmlTemplatesCache.set(cacheKey, result);
            return result;
        }

        // Recursively find all HTML files
        this.#walkDirectory(appSitesPath, (filePath, stats) => {
            if (stats.isFile() && path.extname(filePath) === '.html') {
                const fileName = path.basename(filePath, '.html');
                const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;
                
                const htmlContent = fs.readFileSync(filePath, 'utf8');
                const jsonFile = filePath.replace('.html', '.json');
                let jsonContent = null;
                
                if (fs.existsSync(jsonFile)) {
                    jsonContent = fs.readFileSync(jsonFile, 'utf8');
                }
                
                result.set(key, new TemplateResult(htmlContent, jsonContent));
            }
        });

        this.#htmlTemplatesCache.set(cacheKey, result);
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