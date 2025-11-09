import { promises as fs } from 'fs';
import path from 'path';

export class Scenario {
    constructor(appSite, appFile, appView) {
        this.appSite = appSite;
        this.appFile = appFile;
        this.appView = appView;
    }

    toString() {
        return `${this.appSite}:${this.appFile}:${this.appView}`;
    }
}

class ConfigUtil {
    static DefaultAppFile = 'Index';
    static _cachedAppSites = null;
    static _cachedScenarios = null;
    static _wwwrootPath = null;

    /**
     * Extracts unique AppSites from scenarios
     */
    static extractAppSitesFromScenarios(scenarios) {
        const appSitesSet = new Set();
        for (const scenario of scenarios) {
            if (scenario.appSite) {
                appSitesSet.add(scenario.appSite.toLowerCase());
            }
        }

        console.log(`[ConfigUtil] Extracted ${appSitesSet.size} AppSites from folder scan`);

        return appSitesSet;
    }

    /**
     * Discovers scenarios by scanning AppSites folder structure
     */
    static async loadScenariosInternal(wwwrootPath) {
        const appSitesPath = path.join(wwwrootPath, 'AppSites');

        try {
            await fs.access(appSitesPath);
        } catch {
            throw new Error(`AppSites directory not found: ${appSitesPath}`);
        }

        const scenarios = [];

        // Get all directories in AppSites folder
        const entries = await fs.readdir(appSitesPath, { withFileTypes: true });
        const appSiteDirs = entries
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name)
            .sort();

        for (const appSite of appSiteDirs) {
            // Get all HTML files in the appSite directory (top level only)
            const appSiteDir = path.join(appSitesPath, appSite);
            const files = await fs.readdir(appSiteDir, { withFileTypes: true });
            let htmlFiles = files
                .filter(file => file.isFile() && file.name.endsWith('.html'))
                .map(file => file.name);

            // If no HTML files found, use DefaultAppFile
            if (htmlFiles.length === 0) {
                htmlFiles = [ConfigUtil.DefaultAppFile];
            }

            for (const htmlFile of htmlFiles) {
                const appFile = htmlFile === ConfigUtil.DefaultAppFile ? ConfigUtil.DefaultAppFile : path.parse(htmlFile).name;

                // Check for Views folder
                const viewsPath = path.join(appSiteDir, 'Views');
                let viewDirs = [];

                try {
                    await fs.access(viewsPath);
                    const stats = await fs.stat(viewsPath);
                    if (stats.isDirectory()) {
                        // Get all subdirectories in Views folder
                        const viewEntries = await fs.readdir(viewsPath, { withFileTypes: true });
                        viewDirs = viewEntries
                            .filter(dirent => dirent.isDirectory())
                            .map(dirent => dirent.name);
                    }
                } catch {
                    // No Views folder, viewDirs stays empty
                }

                // Only add empty AppView scenario if no specific Views exist
                if (viewDirs.length === 0) {
                    scenarios.push(new Scenario(appSite, appFile, ''));
                } else {
                    // Add specific view scenarios
                    for (const viewDir of viewDirs) {
                        scenarios.push(new Scenario(appSite, appFile, viewDir));
                    }
                }
            }
        }

        if (scenarios.length === 0) {
            throw new Error('No scenarios found in AppSites folder');
        }

        console.log(`[ConfigUtil] Loaded ${scenarios.length} scenarios from AppSites folder`);

        return scenarios;
    }

    /**
     * Loads AppSites from wwwroot path and caches them. Call this during startup.
     */
    static async load(wwwrootPath) {
        this._wwwrootPath = wwwrootPath;
        this._cachedScenarios = await this.loadScenariosInternal(wwwrootPath);
        this._cachedAppSites = this.extractAppSitesFromScenarios(this._cachedScenarios);
    }

    /**
     * Reloads AppSites and Scenarios from the stored wwwroot path. Throws if not loaded.
     */
    static async reload() {
        if (!this._wwwrootPath) {
            throw new Error('ConfigUtil not loaded. Call load(wwwrootPath) first.');
        }

        this._cachedScenarios = await this.loadScenariosInternal(this._wwwrootPath);
        this._cachedAppSites = this.extractAppSitesFromScenarios(this._cachedScenarios);
    }

    /**
     * Gets the cached AppSites. Throws if not loaded.
     */
    static getAppSites() {
        if (!this._cachedAppSites) {
            throw new Error('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return this._cachedAppSites;
    }

    /**
     * Gets the cached Scenarios. Throws if not loaded.
     */
    static getScenarios() {
        if (!this._cachedScenarios) {
            throw new Error('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return this._cachedScenarios;
    }

    /**
     * Filters scenarios by appSite
     */
    static filterByAppSite(scenarios, appSiteFilter) {
        if (!appSiteFilter) {
            return scenarios;
        }

        return scenarios.filter(s =>
            s.appSite.toLowerCase() === appSiteFilter.toLowerCase()
        );
    }
}

export { ConfigUtil };
