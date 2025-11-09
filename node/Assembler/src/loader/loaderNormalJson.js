import { promises as fs } from 'fs';
import path from 'path';
import { Logger } from '@arshu/arshu/logger';
import { normalizeFileContent, replaceCaseInsensitive } from '../common/commonUtil.js';

const _htmlTemplatesCache = new Map();

class LoaderNormalJson {
    constructor(rootDirPath, appSites, searchAppSites) {
        this.searchAppSites = searchAppSites;
        this._templates = new Map();
        this.rootDirPath = rootDirPath;
        this.appSites = appSites;
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
    }

    getTemplateHtml(appSite, templateName, appView = null, appViewPrefix = null) {
        const template = this.getTemplateInternal(appSite, templateName, appView, appViewPrefix);
        return template ? template.html : null;
    }

    getTemplateJson(appSite, templateName) {
        const template = this.getTemplateInternal(appSite, templateName, null, null);
        return template ? template.json : null;
    }

    hasTemplate(appSite, templateName) {
        const key = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        return this._templates.has(key);
    }

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
}

export { LoaderNormalJson };