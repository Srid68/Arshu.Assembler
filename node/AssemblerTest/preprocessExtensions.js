// Extension methods for PreprocessedSiteTemplates to match C# structure
// This file corresponds to PreprocessExtensions.cs in the C# implementation

/**
 * Extension methods for PreprocessedSiteTemplates
 * These methods provide serialization and summary functionality for preprocessed templates
 */

/**
 * Serializes the preprocessed site templates to a formatted JSON string for debugging and validation
 * @param {PreprocessedSiteTemplates} siteTemplates - The preprocessed site templates
 * @param {boolean} indented - Whether to format the JSON with indentation for readability
 * @returns {string} JSON representation of the preprocessed templates structure
 */
export function toJson(siteTemplates, indented = true) {
    return serializePreprocessedTemplates(siteTemplates, indented);
}

/**
 * Creates a summary object containing key statistics about the preprocessed templates
 * @param {PreprocessedSiteTemplates} siteTemplates - The preprocessed site templates
 * @returns {Object} Summary object with template statistics
 */
export function createSummary(siteTemplates) {
    let templatesRequiringProcessing = 0;
    let templatesWithJsonData = 0;
    let templatesWithPlaceholders = 0;

    for (const template of siteTemplates.templates.values()) {
        if (template.requiresProcessing) {
            templatesRequiringProcessing++;
        }
        if (template.hasJsonData) {
            templatesWithJsonData++;
        }
        if (template.hasPlaceholders) {
            templatesWithPlaceholders++;
        }
    }

    return {
        siteName: siteTemplates.siteName,
        templatesRequiringProcessing,
        templatesWithJsonData,
        templatesWithPlaceholders,
        totalTemplates: siteTemplates.templates.size
    };
}

/**
 * Serializes the summary to JSON for easier analysis
 * @param {PreprocessedSiteTemplates} siteTemplates - The preprocessed site templates
 * @param {boolean} indented - Whether to format the JSON with indentation
 * @returns {string} JSON representation of the template summary
 */
export function toSummaryJson(siteTemplates, indented = true) {
    return serializePreprocessedSummary(createSummary(siteTemplates), indented);
}

// Serialization helpers (formerly in JsonConverter)
/**
 * Serializes preprocessed templates to JSON format
 * @param {PreprocessedSiteTemplates} templates - The preprocessed templates to serialize
 * @param {boolean} indented - Whether to indent the JSON output
 * @returns {string} JSON string representation
 */
export function serializePreprocessedTemplates(templates, indented = true) {
    // Sort templates to match C# ordering (main template first, then content templates)
    const sortedEntries = Array.from(templates.templates.entries()).sort(([keyA], [keyB]) => {
        // Main template (without "content" suffix) comes first
        const isMainA = !keyA.includes('content');
        const isMainB = !keyB.includes('content');
        if (isMainA && !isMainB) return -1;
        if (!isMainA && isMainB) return 1;
        return keyA.localeCompare(keyB);
    });

    const obj = {
        siteName: templates.siteName,
        templates: Object.fromEntries(
            sortedEntries.map(([key, template]) => [key, template.toObject()])
        ),
        rawTemplates: Object.fromEntries(templates.rawTemplates),
        templateKeys: Array.from(templates.templateKeys).sort((a, b) => {
            // Same ordering logic for template keys
            const isMainA = !a.includes('content');
            const isMainB = !b.includes('content');
            if (isMainA && !isMainB) return -1;
            if (!isMainA && isMainB) return 1;
            return a.localeCompare(b);
        })
    };
    return JSON.stringify(obj, null, indented ? 2 : 0).replace(/\r\n/g, '\n');
}

/**
 * Serializes preprocessed summary to JSON format
 * @param {Object} summary - The summary object to serialize
 * @param {boolean} indented - Whether to indent the JSON output
 * @returns {string} JSON string representation
 */
export function serializePreprocessedSummary(summary, indented = true) {
    return JSON.stringify(summary, null, indented ? 2 : 0).replace(/\r\n/g, '\n');
}