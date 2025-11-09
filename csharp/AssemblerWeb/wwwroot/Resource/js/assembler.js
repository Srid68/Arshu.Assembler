/**
 * Assembler.js - Reusable AppSite loading and assembly utilities
 */

/**
 * Load and display an AppSite
 * @param {string} appSiteName - Name of the AppSite to load (e.g., 'Metrics', 'Home')
 * @param {string|null} appView - AppView name (e.g., 'Main'), or null/empty for default
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess' (default: 'Normal')
 * @param {string} mergeMode - Merge mode: 'Server', 'Client', or 'Navigation' (default: 'Client')
 * @param {string|null} replaceElement - Element ID to replace, or null to replace entire document
 * @returns {Promise<void>}
 */
async function loadAppSite(appSiteName, appView = null, engineType = "Normal", mergeMode = "Client", replaceElement = null) {
    try {
        let html;

        if (mergeMode === 'Navigation') {
            // Navigation mode - use RESTful URL pattern /{appSite}/{appView}?engine=
            navigationNavigate(appSiteName, appView, engineType);
            return; // Navigation redirects, no need to continue
        } else if (mergeMode === 'Server') {
            // Server-side merge using /merge endpoint
            html = await serverMerge(appSiteName, appView, engineType);
        } else {
            // Client-side merge using engines
            html = await clientMerge(appSiteName, appView, engineType);
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
            // Replace entire document HTML while maintaining history
            // Build URL for history
            let historyUrl;
            if (appSiteName === 'Home') {
                historyUrl = '/';
            } else if (appView) {
                historyUrl = `/${appSiteName}/${appView}`;
            } else {
                historyUrl = `/${appSiteName}`;
            }

            // Add engine query parameter if not default
            if (engineType && engineType !== 'Normal') {
                historyUrl += `?engine=${engineType}`;
            }

            // Update browser history
            window.history.pushState({ appSite: appSiteName, appView: appView, engineType: engineType, mergeMode: mergeMode }, '', historyUrl);

            // Replace entire HTML document (not document.write which destroys everything)
            document.documentElement.innerHTML = html;
        }
    } catch (error) {
        console.error('Failed to load AppSite:', error);
        throw error;
    }
}

/**
 * Server-side merge using /merge endpoint
 * @param {string} appSiteName - Name of the AppSite
 * @param {string|null} appView - AppView name, or null/empty for default
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess'
 * @returns {Promise<string>} - Merged HTML
 */
async function serverMerge(appSiteName, appView, engineType) {
    const response = await fetch('/merge', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            appSite: appSiteName,
            engineType: engineType,
            appView: appView || ''
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
 * @param {string|null} appView - AppView name, or null/empty for default
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess'
 * @returns {Promise<string>} - Merged HTML
 */
async function clientMerge(appSiteName, appView, engineType) {
    // Fetch templates from server
    const response = await fetch('/api/templates', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            appsite: appSiteName,
            appview: appView || ''
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
        mergedHtml = engine.mergeTemplates(appSiteName, 'index', appView, preprocessTemplates, true);
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
        mergedHtml = engine.mergeTemplates(appSiteName, 'index', appView, templatesMap, true);
    }

    return mergedHtml;
}

/**
 * Navigation mode - navigate to AppSite using RESTful URL pattern
 * @param {string} appSiteName - Name of the AppSite
 * @param {string|null} appView - AppView name, or null/empty for default
 * @param {string} engineType - Engine type: 'Normal' or 'PreProcess'
 */
function navigationNavigate(appSiteName, appView, engineType) {
    // Build RESTful URL: /{appSite} or /{appSite}/{appView}
    let url;
    if (appSiteName === 'Home') {
        url = '/';
    } else if (appView) {
        url = `/${appSiteName}/${appView}`;
    } else {
        url = `/${appSiteName}`;
    }

    // Add engine query parameter if not default
    if (engineType && engineType !== 'Normal') {
        url += `?engine=${engineType}`;
    }

    // Navigate
    window.location.href = url;
}

/**
 * Handle browser back/forward navigation for Server/Client modes
 */
window.addEventListener('popstate', function(event) {
    // Reload the page when back/forward is clicked
    // This works for Server/Client modes that use history.pushState
    window.location.reload();
});
