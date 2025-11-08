/**
 * Assembler.js - Reusable AppSite loading and assembly utilities
 */

/**
 * Load and display an AppSite
 * @param {string} appSiteName - Name of the AppSite to load (e.g., 'Metrics', 'Home')
 * @param {string|null} replaceElement - Element ID to replace, or null to replace entire document
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess' (default: 'Normal')
 * @param {string} mergeMode - Merge mode: 'Server' or 'Client' (default: 'Client')
 * @returns {Promise<void>}
 */
async function loadAppSite(appSiteName, replaceElement = null, engineType = "Normal", mergeMode = "Client") {
    try {
        let html;

        if (mergeMode === 'Server') {
            // Server-side merge using /merge endpoint
            html = await serverMerge(appSiteName, engineType);
        } else {
            // Client-side merge using engines
            html = await clientMerge(appSiteName, engineType);
        }

        if (!html) {
            throw new Error('No HTML content generated');
        }

        // Replace content
        if (replaceElement) {
            // Replace specific element
            const element = document.getElementById(replaceElement);
            if (!element) {
                throw new Error(`Element with ID '${replaceElement}' not found`);
            }
            element.innerHTML = html;
        } else {
            // Navigate to the server URL with appsite query parameter
            // This is the cleanest approach - let the server handle page rendering
            const url = appSiteName === 'Home' ? '/' : `/?appsite=${appSiteName}`;
            window.location.href = url;
        }
    } catch (error) {
        console.error('Failed to load AppSite:', error);
        throw error;
    }
}

/**
 * Server-side merge using /merge endpoint
 * @param {string} appSiteName - Name of the AppSite
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess'
 * @returns {Promise<string>} - Merged HTML
 */
async function serverMerge(appSiteName, engineType) {
    const response = await fetch('/merge', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            appSite: appSiteName,
            engineType: engineType,
            appView: ''
        })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`HTTP error! status: ${response.status} - ${errorText}`);
    }

    const data = await response.json();
    return data.Html;
}

/**
 * Client-side merge using JavaScript engines
 * @param {string} appSiteName - Name of the AppSite
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess'
 * @returns {Promise<string>} - Merged HTML
 */
async function clientMerge(appSiteName, engineType) {
    // Fetch templates from server
    const response = await fetch('/api/templates', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            appsite: appSiteName
        })
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`HTTP error! status: ${response.status} - ${errorText}`);
    }

    const data = await response.json();

    // Use appropriate engine
    let mergedHtml;
    if (engineType === 'PreProcess') {
        // Get PreProcess templates
        const preprocessTemplates = data.PreProcessTemplates || data.preProcessTemplates || {};

        // Create EnginePreProcess instance
        const engine = new EnginePreProcess();
        engine.setAppViewPrefix('');

        // Merge templates (appSite, appFile, appView, templates, enableJsonProcessing)
        mergedHtml = engine.mergeTemplates(appSiteName, 'index', null, preprocessTemplates, true);
    } else {
        // Get Normal templates
        const templates = data.Templates || data.templates || data;

        // Convert templates to Map format expected by EngineNormal
        const templatesMap = new Map();
        for (const [key, template] of Object.entries(templates)) {
            const jsonContent = template.Json || template.json || null;
            templatesMap.set(key, {
                html: template.Html || template.html || '',
                json: jsonContent
            });
        }

        // Create EngineNormal instance
        const engine = new EngineNormal();
        engine.setAppViewPrefix('');

        // Merge templates (appSite, appFile, appView, templates, enableJsonProcessing)
        mergedHtml = engine.mergeTemplates(appSiteName, 'index', null, templatesMap, true);
    }

    return mergedHtml;
}
