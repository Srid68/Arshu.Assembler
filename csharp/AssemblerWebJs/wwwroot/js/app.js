// Client-side JavaScript for AssemblerWebJs

// Function to convert JsonConverter Key/Value array format to object format
function convertKeyValueArrayToObject(data) {
    if (Array.isArray(data)) {
        // Convert [{Key: "key1", Value: {...}}, ...] to {key1: {...}, ...}
        const result = {};
        data.forEach(item => {
            if (item && typeof item === 'object') {
                // Handle both uppercase and lowercase Key/Value properties
                const key = item.Key || item.key;
                const value = item.Value || item.value;
                if (key && value !== undefined) {
                    result[key] = value;
                }
            }
        });
        return result;
    }
    return data; // Return as-is if not an array
}

// Function to load available scenarios from API
async function loadScenarios() {
    // console.log('loadScenarios() called');
    try {
        // console.log('Fetching /api/scenarios...');
        const response = await fetch('/api/scenarios');
        // console.log('Response status:', response.status);
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const scenarios = await response.json();
        // console.log('Scenarios received:', scenarios);
        const appSiteSelect = document.getElementById('appSiteSelect');
        // console.log('Select element found:', appSiteSelect);
        
        // Clear existing options
        appSiteSelect.innerHTML = '<option value="">Select a scenario...</option>';
        
        // Add scenarios as options
        scenarios.forEach((scenario, index) => {
            // console.log(`Adding scenario ${index + 1}:`, scenario);
            const option = document.createElement('option');
            option.value = JSON.stringify({
                appSite: scenario.AppSite || scenario.appSite,
                appFile: scenario.AppFile || scenario.appFile,
                appView: scenario.AppView || scenario.appView,
                appViewPrefix: scenario.AppViewPrefix || scenario.appViewPrefix
            });
            // Use the correct property name (lowercase)
            const displayText = scenario.DisplayText || scenario.displayText;
            option.textContent = displayText;
            option.innerText = displayText; // Fallback
            option.innerHTML = displayText; // Another fallback
            
            // console.log(`Option ${index + 1} created:`, {
            //     value: option.value,
            //     textContent: option.textContent,
            //     innerText: option.innerText,
            //     innerHTML: option.innerHTML,
            //     originalDisplayText: scenario.DisplayText,
            //     lowercaseDisplayText: scenario.displayText
            // });
            
            appSiteSelect.appendChild(option);
        });
        
        console.log(`Loaded ${scenarios.length} scenarios`);
    } catch (error) {
        console.error('Error loading scenarios:', error);
        const appSiteSelect = document.getElementById('appSiteSelect');
        if (appSiteSelect) {
            appSiteSelect.innerHTML = '<option value="">Error loading scenarios</option>';
        }
    }
}

// Function to get templates for selected scenario
async function getTemplates() {
    const appSiteSelect = document.getElementById('appSiteSelect');
    const resultDiv = document.getElementById('result');
    const selectedValue = appSiteSelect.value;
    
    if (!selectedValue) {
        resultDiv.innerHTML = '<div class="error">Please select a Scenario first.</div>';
        return;
    }

    let scenario;
    try {
        scenario = JSON.parse(selectedValue);
    } catch (error) {
        resultDiv.innerHTML = '<div class="error">Invalid scenario selection.</div>';
        return;
    }
    
    try {
        const clientStartTime = performance.now();
        resultDiv.innerHTML = '<div class="loading">Loading templates...</div>';
        const response = await fetch(`/api/templates/${scenario.appSite}?appFile=${encodeURIComponent(scenario.appFile)}&appView=${encodeURIComponent(scenario.appView)}`);
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        const clientEndTime = performance.now();
        const clientTimeMs = (clientEndTime - clientStartTime).toFixed(2);
        const data = await response.json();
        // console.log('Full server response:', data);
        // console.log('data.Templates:', data.Templates);
        // console.log('data.templates:', data.templates);
        // console.log('Type of data.Templates:', typeof data.Templates);
        
    // Handle both old format (direct templates) and new format (with Templates property)
    // Check for both uppercase and lowercase versions
    let templates = data.Templates || data.templates || data;
    let preprocessTemplates = data.PreProcessTemplates || data.preProcessTemplates || {};
    // Convert JsonConverter Key/Value array format to object format if needed
    templates = convertKeyValueArrayToObject(templates);
    preprocessTemplates = convertKeyValueArrayToObject(preprocessTemplates);
    // Timing info from backend
    const serverTimeMs = data.ServerTimeMs || data.serverTimeMs || null;
        
        // Debug: Check the first template
        const firstTemplate = Object.entries(templates)[0];
        if (firstTemplate) {
            // console.log('First template key:', firstTemplate[0]);
            // console.log('First template value:', firstTemplate[1]);
            // console.log('HTML content:', firstTemplate[1]?.html);  // Changed to lowercase
            // console.log('JSON content:', firstTemplate[1]?.json);  // Changed to lowercase
        }
        
        // Format the templates for display
        let html = `<div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;">
            <h3 style="margin: 0;">Templates for "${scenario.appSite}" → "${scenario.appFile}"${scenario.appView ? ` (View: ${scenario.appView})` : ''}:</h3>
            <button onclick="clearResults()" style="background-color: #6c757d; color: white; border: none; padding: 6px 12px; border-radius: 4px; cursor: pointer; font-size: 12px;">✕ Clear</button>
        </div>`;
        
        if (!templates || Object.keys(templates).length === 0) {
            html += '<p>No templates found.</p>';
        } else {
            html += '<div style="overflow-x: auto;">';
            html += '<table style="border-collapse: collapse; width: 100%; margin-top: 10px; table-layout: fixed;">';
            html += '<thead><tr style="background-color: #e9e9e9;"><th style="border: 1px solid #ddd; padding: 12px; text-align: left; width: 25%;">Template Key</th><th style="border: 1px solid #ddd; padding: 12px; text-align: left; width: 37.5%;">HTML Content Preview</th><th style="border: 1px solid #ddd; padding: 12px; text-align: left; width: 37.5%;">JSON Content Preview</th></tr></thead>';
            html += '<tbody>';
            
            Object.entries(templates).forEach(([key, template]) => {
                // Safe handling of potentially undefined values - handle both uppercase and lowercase property names
                const htmlContent = template?.Html || template?.html || '';
                const jsonContent = template?.Json || template?.json || '';
                
                // console.log(`Processing template ${key}:`);
                // console.log(`  Template object:`, template);
                // console.log(`  HTML content (Html):`, template?.Html);
                // console.log(`  HTML content (html):`, template?.html);
                // console.log(`  JSON content (Json):`, template?.Json);
                // console.log(`  JSON content (json):`, template?.json);
                // console.log(`  Final HTML content:`, htmlContent);
                // console.log(`  Final JSON content:`, jsonContent);
                
                const htmlPreview = htmlContent.length > 150 ? htmlContent.substring(0, 150) + '...' : htmlContent;
                const jsonPreview = jsonContent || 'No JSON file';
                
                // Fix the display logic - don't check for empty, just show content
                const htmlDisplayText = htmlContent || 'Empty HTML';
                
                html += `<tr style="border-bottom: 1px solid #eee;">
                    <td data-label="Template Key" style="border: 1px solid #ddd; padding: 8px; font-weight: bold; vertical-align: top; word-wrap: break-word; overflow-wrap: break-word;">${key}</td>
                    <td data-label="HTML Content" style="border: 1px solid #ddd; padding: 8px; vertical-align: top; word-wrap: break-word; overflow-wrap: break-word;"><code style="background-color: #f8f9fa; padding: 2px 4px; border-radius: 3px; font-size: xx-small; display: block; white-space: pre-wrap; line-height: 1.2;">${htmlDisplayText.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</code></td>
                    <td data-label="JSON Content" style="border: 1px solid #ddd; padding: 8px; vertical-align: top; word-wrap: break-word; overflow-wrap: break-word;"><pre style="background-color: #f8f9fa; padding: 8px; border-radius: 3px; font-size: 11px; line-height: 1.3; margin: 0; white-space: pre-wrap; overflow-wrap: break-word; max-height: none; overflow: visible;">${jsonPreview.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</pre></td>
                </tr>`;
            });
            
            html += '</tbody></table>';
            html += '</div>';
        }
        
        // Add PreProcess Templates section
        html += `<div style="margin-top: 30px;">
            <h3 style="margin-bottom: 10px;">PreProcess Templates for "${scenario.appSite}":</h3>`;
        
        if (!preprocessTemplates || Object.keys(preprocessTemplates).length === 0) {
            html += '<p>No PreProcess templates found.</p>';
        } else {
            html += '<div style="overflow-x: auto;">';
            html += '<table style="border-collapse: collapse; width: 100%; margin-top: 10px; table-layout: fixed;">';
            html += '<thead><tr style="background-color: #e9f4ff;"><th style="border: 1px solid #ddd; padding: 12px; text-align: left; width: 25%;">Template Key</th><th style="border: 1px solid #ddd; padding: 12px; text-align: left; width: 75%;">Full PreProcess JSON Structure</th></tr></thead>';
            html += '<tbody>';
            
            Object.entries(preprocessTemplates).forEach(([key, template]) => {
                // Display the full JSON structure of the preprocessed template
                const fullJsonStructure = JSON.stringify(template, null, 2);
                
                html += `<tr style="border-bottom: 1px solid #eee;">
                    <td style="border: 1px solid #ddd; padding: 8px; font-weight: bold; vertical-align: top; word-wrap: break-word; overflow-wrap: break-word;">${key}</td>
                    <td style="border: 1px solid #ddd; padding: 8px; vertical-align: top; word-wrap: break-word; overflow-wrap: break-word;"><pre style="background-color: #f8f9fa; padding: 8px; border-radius: 3px; font-size: 10px; line-height: 1.3; margin: 0; white-space: pre-wrap; overflow-wrap: break-word; max-height: 400px; overflow-y: auto;">${fullJsonStructure.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</pre></td>
                </tr>`;
            });
            
            html += '</tbody></table>';
            html += '</div>';
        }
        
        html += '</div>';  // Close PreProcess Templates section
        
    // Timing info display
    let timingHtml = '<div style="margin-top:10px;">';
    if (serverTimeMs !== null) timingHtml += `<div>Server Time: <b>${serverTimeMs} ms</b></div>`;
    timingHtml += `<div>Client Time: <b>${clientTimeMs} ms</b></div>`;
    timingHtml += '</div>';
    resultDiv.innerHTML = html + timingHtml;
    } catch (error) {
        resultDiv.innerHTML = `<div class="error">Error: ${error.message}</div>`;
        console.error('Error getting templates:', error);
    }
}

// Function to merge templates using both EngineNormal and EnginePreProcess
async function mergeTemplates() {
    const appSiteSelect = document.getElementById('appSiteSelect');
    const resultDiv = document.getElementById('result');
    const selectedValue = appSiteSelect.value;
    
    if (!selectedValue) {
        resultDiv.innerHTML = '<div class="error">Please select a Scenario first.</div>';
        return;
    }

    let scenario;
    try {
        scenario = JSON.parse(selectedValue);
    } catch (error) {
        resultDiv.innerHTML = '<div class="error">Invalid scenario selection.</div>';
        return;
    }
    
    try {
        const clientStartTime = performance.now();
        resultDiv.innerHTML = '<div class="loading">Loading templates and performing merge...</div>';
        // Get templates from API
        const response = await fetch(`/api/templates/${scenario.appSite}?appFile=${encodeURIComponent(scenario.appFile)}&appView=${encodeURIComponent(scenario.appView)}`);
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        const clientEndTime = performance.now();
        const clientTimeMs = (clientEndTime - clientStartTime).toFixed(2);
        const data = await response.json();
    // Handle both old format (direct templates) and new format (with Templates property)
    // Check for both uppercase and lowercase versions
    let templates = data.Templates || data.templates || data;
    let preprocessTemplates = data.PreProcessTemplates || data.preProcessTemplates || {};
    // Convert JsonConverter Key/Value array format to object format if needed
    templates = convertKeyValueArrayToObject(templates);
    preprocessTemplates = convertKeyValueArrayToObject(preprocessTemplates);
    // Timing info from backend
    const serverTimeMs = data.ServerTimeMs || data.serverTimeMs || null;
        
        //console.log('Templates loaded for merge:', templates);
        //console.log('PreProcess templates loaded for merge:', preprocessTemplates);
        //console.log('PreProcess templates keys:', Object.keys(preprocessTemplates));
        //console.log('PreProcess templates first entry:', Object.entries(preprocessTemplates)[0]);
        
        // Convert normal templates to Map format expected by EngineNormal
        const templatesMap = new Map();
        for (const [key, template] of Object.entries(templates)) {
            const jsonContent = template.Json || template.json || null;
            //console.log(`Template ${key} JSON content:`, jsonContent);
            templatesMap.set(key, {
                html: template.Html || template.html || '',
                json: jsonContent
            });
        }
        
        //console.log('Templates map prepared:', templatesMap);
        //console.log('PreProcess templates prepared:', preprocessTemplates);
        
        // Create EngineNormal instance
        const normalEngine = new EngineNormal();
        normalEngine.setAppViewPrefix(scenario.appViewPrefix || '');
        
        // Create EnginePreProcess instance
        const preprocessEngine = new EnginePreProcess();
        preprocessEngine.setAppViewPrefix(scenario.appViewPrefix || '');
        
        //console.log('Engines created with AppViewPrefix:', scenario.appViewPrefix);
        
        // Perform Normal engine merge with timing
        const normalStartTime = performance.now();
        const normalMergedResult = normalEngine.mergeTemplates(
            scenario.appSite,
            scenario.appFile,
            scenario.appView || null,
            templatesMap,
            true // enableJsonProcessing
        );
        const normalEndTime = performance.now();
        const normalExecutionTime = (normalEndTime - normalStartTime).toFixed(2);

        // Perform PreProcess engine merge with timing
        const preprocessStartTime = performance.now();
        const preprocessMergedResult = preprocessEngine.mergeTemplates(
            scenario.appSite,
            scenario.appFile,
            scenario.appView || null,
            preprocessTemplates,
            true // enableJsonProcessing
        );
        const preprocessEndTime = performance.now();
        const preprocessExecutionTime = (preprocessEndTime - preprocessStartTime).toFixed(2);
        
        //console.log('Normal merge result:', normalMergedResult);
        //console.log('PreProcess merge result:', preprocessMergedResult);
        
        // Check if output sizes match
        const sizesMatch = normalMergedResult.length === preprocessMergedResult.length;
        
        // Display results for both engines
        let timingHtml = '<div style="margin-top:10px;">';
        if (serverTimeMs !== null) timingHtml += `<div>Server Time: <b>${serverTimeMs} ms</b></div>`;
        timingHtml += `<div>Client Time: <b>${clientTimeMs} ms</b></div>`;
        timingHtml += '</div>';
        const displayHtml = `
            <div style="margin-bottom: 30px;">
                <h3${!sizesMatch ? ' style="background-color: red; color: white; padding: 5px;"' : ''}>Normal Engine Merged Result for "${scenario.appSite}" → "${scenario.appFile}"${scenario.appView ? ` (View: ${scenario.appView})` : ''}:</h3>
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;">
                    <div style="color: #666;">
                        <span>Output Length: ${normalMergedResult.length} characters</span>
                        <span style="margin-left: 20px;">Execution Time: ${normalExecutionTime}ms</span>
                    </div>
                    <button onclick="clearResults()" style="padding: 4px 8px; border: 1px solid #ccc; background: #f8f8f8; border-radius: 3px; cursor: pointer;">Clear</button>
                </div>
                <div style="overflow-x: auto;">
                    <div style="border: 1px solid #ddd; padding: 15px; background: #f8f9fa; border-radius: 4px; white-space: pre-wrap; font-family: 'Courier New', monospace; font-size: 12px; line-height: 1.4;">
${normalMergedResult.replace(/</g, '&lt;').replace(/>/g, '&gt;')}
                    </div>
                </div>
            </div>
            
            <div style="margin-bottom: 20px;">
                <h3>PreProcess Engine Merged Result for "${scenario.appSite}" → "${scenario.appFile}"${scenario.appView ? ` (View: ${scenario.appView})` : ''}:</h3>
                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;">
                    <div style="color: #666;">
                        <span>Output Length: ${preprocessMergedResult.length} characters</span>
                        <span style="margin-left: 20px;">Execution Time: ${preprocessExecutionTime}ms</span>
                    </div>
                </div>
                <div style="overflow-x: auto;">
                    <div style="border: 1px solid #007bff; padding: 15px; background: #e7f3ff; border-radius: 4px; white-space: pre-wrap; font-family: 'Courier New', monospace; font-size: 12px; line-height: 1.4;">
${preprocessMergedResult.replace(/</g, '&lt;').replace(/>/g, '&gt;')}
                    </div>
                </div>
            </div>
        `;
        resultDiv.innerHTML = timingHtml + displayHtml ;
        
    } catch (error) {
        console.error('Error merging templates:', error);
        resultDiv.innerHTML = `<div class="error">Error merging templates: ${error.message}</div>`;
    }
}

// Function to clear results
function clearResults() {
    const resultDiv = document.getElementById('result');
    resultDiv.innerHTML = '';
}

// Initialize the page
document.addEventListener('DOMContentLoaded', function() {
    console.log('AssemblerWebJs loaded successfully');
    loadScenarios();

    // Next/Previous scenario navigation
    document.getElementById('prevScenarioBtn').onclick = function() {
        const select = document.getElementById('appSiteSelect');
        if (select.selectedIndex > 1) select.selectedIndex--;
        else select.selectedIndex = 1;
        mergeTemplates();
    };
    document.getElementById('nextScenarioBtn').onclick = function() {
        const select = document.getElementById('appSiteSelect');
        if (select.selectedIndex < select.options.length - 1) select.selectedIndex++;
        mergeTemplates();
    };
});
