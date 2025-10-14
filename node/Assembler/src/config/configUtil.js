import { promises as fs } from 'fs';
import fsSync from 'fs';
import path from 'path';

export class Scenario {
    constructor(appSite, appFile, appView, totalSize = 0, displayName = "", description = "") {
        this.appSite = appSite;
        this.appFile = appFile;
        this.appView = appView;
        this.totalSize = totalSize;
        this.displayName = displayName;
        this.description = description;
    }

    toString() {
        return `${this.appSite}:${this.appFile}:${this.appView}:${this.totalSize}`;
    }

    toCsvLine() {
        return `${this.appSite},${this.appFile},${this.appView},${this.totalSize},"${this.displayName}","${this.description}"`;
    }
}

class ConfigUtil {
    static _cachedAppSites = null;
    static _cachedScenarios = null;
    static _wwwrootPath = null;

    static async generateAppSitesCsv(wwwrootPath) {
        const appSitesPath = path.join(wwwrootPath, 'AppSites');
        const appDataPath = path.join(wwwrootPath, 'App_Data');
        const csvFilePath = path.join(appDataPath, 'appsites.csv');

        try {
            await fs.access(appSitesPath);
        } catch {
            throw new Error(`AppSites directory not found: ${appSitesPath}`);
        }

        // Ensure App_Data directory exists
        await fs.mkdir(appDataPath, { recursive: true });

        // Get all directories in AppSites folder
        const entries = await fs.readdir(appSitesPath, { withFileTypes: true });
        let appSites = entries
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name)
            .sort();

        // Add Index if not present
        if (!appSites.includes('Index')) {
            appSites.push('Index');
        }

        // Write as CSV
        const csv = appSites.join(',');
        await fs.writeFile(csvFilePath, csv);

        console.log(`[ConfigUtil] Generated appsites.csv with ${appSites.length} AppSites`);
    }

    static async loadAppSitesInternal(wwwrootPath) {
        const appDataPath = path.join(wwwrootPath, 'App_Data');
        const csvFilePath = path.join(appDataPath, 'appsites.csv');

        // Generate if not exists
        try {
            await fs.access(csvFilePath);
        } catch {
            console.log('[ConfigUtil] appsites.csv not found, generating...');
            await this.generateAppSitesCsv(wwwrootPath);
        }

        // Read and parse CSV
        const csv = (await fs.readFile(csvFilePath, 'utf8')).trim();

        if (!csv) {
            throw new Error('appsites.csv is empty');
        }

        const appSites = csv.split(',')
            .map(s => s.trim())
            .filter(s => s.length > 0);

        if (appSites.length === 0) {
            throw new Error('No AppSites found in appsites.csv');
        }

        console.log(`[ConfigUtil] Loaded ${appSites.length} AppSites from appsites.csv`);

        return new Set(appSites.map(s => s.toLowerCase()));
    }

    static async calculateTotalTemplateSize(appSitesPath, appSite) {
        let totalSize = 0;
        const appSiteDir = path.join(appSitesPath, appSite);

        try {
            const files = await this.getAllFiles(appSiteDir);
            const templateFiles = files.filter(f => f.endsWith('.html') || f.endsWith('.json'));

            for (const file of templateFiles) {
                const stats = await fs.stat(file);
                totalSize += stats.size;
            }
        } catch {
            return 0;
        }

        return totalSize;
    }

    static async getAllFiles(dirPath, arrayOfFiles = []) {
        try {
            const files = await fs.readdir(dirPath, { withFileTypes: true });

            for (const file of files) {
                const fullPath = path.join(dirPath, file.name);
                if (file.isDirectory()) {
                    arrayOfFiles = await this.getAllFiles(fullPath, arrayOfFiles);
                } else {
                    arrayOfFiles.push(fullPath);
                }
            }
        } catch {
            // Ignore errors
        }

        return arrayOfFiles;
    }

    static generateDisplayName(appSite, appView) {
        let rulePart = appSite.replace('Html', '').replace('Json', '');
        let displayName = '';

        if (appSite.startsWith('Html')) {
            displayName = rulePart + ' (HTML)';
        } else if (appSite.startsWith('Json')) {
            displayName = rulePart + ' (JSON)';
        } else {
            displayName = appSite;
        }

        if (appView) {
            displayName += ` - AppView: ${appView}`;
        }

        return displayName;
    }

    static generateDescription(appSite, appView) {
        let description = '';

        if (appSite.includes('Rule1')) {
            description = 'Simple placeholder replacement';
        } else if (appSite.includes('Rule2')) {
            description = 'Slotted markup patterns';
        } else if (appSite.includes('Rule3')) {
            description = 'Context-based placeholders';
        }

        if (appSite.includes('Html') && appSite.includes('Json')) {
            description += ' with HTML and JSON';
        } else if (appSite.includes('Json')) {
            description += ' with JSON data';
        }

        if (appView) {
            description += ` (${appView} view)`;
        }

        return description;
    }

    static async generateScenariosCsv(wwwrootPath) {
        const appSitesPath = path.join(wwwrootPath, 'AppSites');
        const appDataPath = path.join(wwwrootPath, 'App_Data');
        const csvFilePath = path.join(appDataPath, 'scenarios.csv');

        try {
            await fs.access(appSitesPath);
        } catch {
            throw new Error(`AppSites directory not found: ${appSitesPath}`);
        }

        // Ensure App_Data directory exists
        await fs.mkdir(appDataPath, { recursive: true });

        const scenarios = [];

        // Get all directories in AppSites folder
        const entries = await fs.readdir(appSitesPath, { withFileTypes: true });
        const appSiteDirs = entries
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name)
            .sort();

        for (const appSite of appSiteDirs) {
            const appSiteDir = path.join(appSitesPath, appSite);
            const files = await fs.readdir(appSiteDir);
            const htmlFiles = files.filter(file => file.endsWith('.html'));

            for (const htmlFile of htmlFiles) {
                const appFile = path.parse(htmlFile).name;

                // Calculate total size
                const totalSize = await this.calculateTotalTemplateSize(appSitesPath, appSite);

                // Generate display name and description
                const displayName = this.generateDisplayName(appSite, '');
                const description = this.generateDescription(appSite, '');

                // Add default scenario (no AppView)
                scenarios.push(new Scenario(appSite, appFile, '', totalSize, displayName, description));

                // Check for Views folder
                const viewsPath = path.join(appSitesPath, appSite, 'Views');
                try {
                    await fs.access(viewsPath);
                    const viewFiles = await fs.readdir(viewsPath);
                    const htmlViewFiles = viewFiles.filter(file => file.endsWith('.html'));

                    for (const viewFile of htmlViewFiles) {
                        const viewName = path.parse(viewFile).name;
                        let appView = '';

                        // Extract AppView from view filename
                        if (viewName.toLowerCase().includes('content')) {
                            const contentIndex = viewName.toLowerCase().indexOf('content');
                            if (contentIndex > 0) {
                                const viewPart = viewName.substring(0, contentIndex);
                                if (viewPart.length > 0) {
                                    appView = viewPart.charAt(0).toUpperCase() + viewPart.substring(1);
                                }
                            }
                        }

                        if (appView) {
                            const viewDisplayName = this.generateDisplayName(appSite, appView);
                            const viewDescription = this.generateDescription(appSite, appView);
                            scenarios.push(new Scenario(appSite, appFile, appView, totalSize, viewDisplayName, viewDescription));
                        }
                    }
                } catch {
                    // No Views folder, skip
                }
            }
        }

        // Write as multi-line CSV with header
        const csvLines = ['AppSite,AppFile,AppView,TotalSize,DisplayName,Description'];
        csvLines.push(...scenarios.map(s => s.toCsvLine()));

        await fs.writeFile(csvFilePath, csvLines.join('\n'));

        console.log(`[ConfigUtil] Generated scenarios.csv with ${scenarios.length} scenarios`);
    }

    static parseCsvLine(line) {
        const result = [];
        let current = '';
        let inQuotes = false;

        for (let i = 0; i < line.length; i++) {
            const c = line[i];

            if (c === '"') {
                inQuotes = !inQuotes;
            } else if (c === ',' && !inQuotes) {
                result.push(current);
                current = '';
            } else {
                current += c;
            }
        }

        result.push(current);
        return result;
    }

    static async loadScenariosInternal(wwwrootPath) {
        const appDataPath = path.join(wwwrootPath, 'App_Data');
        const csvFilePath = path.join(appDataPath, 'scenarios.csv');

        // Generate if not exists
        try {
            await fs.access(csvFilePath);
        } catch {
            console.log('[ConfigUtil] scenarios.csv not found, generating...');
            await this.generateScenariosCsv(wwwrootPath);
        }

        // Read CSV lines
        const csvContent = await fs.readFile(csvFilePath, 'utf8');
        const csvLines = csvContent.split('\n').map(l => l.trim()).filter(l => l.length > 0);

        if (csvLines.length === 0) {
            throw new Error('scenarios.csv is empty');
        }

        const scenarios = [];

        // Check if first line is a header
        const hasHeader = csvLines[0].includes('AppSite') && csvLines[0].includes('AppFile');
        const startLine = hasHeader ? 1 : 0;

        for (let i = startLine; i < csvLines.length; i++) {
            const line = csvLines[i].trim();
            if (!line) continue;

            const parts = this.parseCsvLine(line);

            if (parts.length >= 2) {
                const appSite = parts[0].trim();
                const appFile = parts[1].trim();
                const appView = parts.length > 2 ? parts[2].trim() : '';
                const totalSize = parts.length > 3 ? parseInt(parts[3].trim()) || 0 : 0;
                const displayName = parts.length > 4 ? parts[4].trim().replace(/^"|"$/g, '') : '';
                const description = parts.length > 5 ? parts[5].trim().replace(/^"|"$/g, '') : '';

                scenarios.push(new Scenario(appSite, appFile, appView, totalSize, displayName, description));
            }
        }

        if (scenarios.length === 0) {
            throw new Error('No scenarios found in scenarios.csv');
        }

        console.log(`[ConfigUtil] Loaded ${scenarios.length} scenarios from scenarios.csv`);

        return scenarios;
    }

    static async load(wwwrootPath) {
        this._wwwrootPath = wwwrootPath;
        this._cachedAppSites = await this.loadAppSitesInternal(wwwrootPath);
        this._cachedScenarios = await this.loadScenariosInternal(wwwrootPath);
    }

    static async reload() {
        if (!this._wwwrootPath) {
            throw new Error('ConfigUtil not loaded. Call load(wwwrootPath) first.');
        }

        this._cachedAppSites = await this.loadAppSitesInternal(this._wwwrootPath);
        this._cachedScenarios = await this.loadScenariosInternal(this._wwwrootPath);
    }

    static getAppSites() {
        if (!this._cachedAppSites) {
            throw new Error('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return this._cachedAppSites;
    }

    static getScenarios() {
        if (!this._cachedScenarios) {
            throw new Error('AppSitesConfig not loaded. Call load(wwwrootPath) first.');
        }

        return this._cachedScenarios;
    }

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
