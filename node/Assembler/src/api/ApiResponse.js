// Template API response structures for Node.js with manual JSON serialization

/**
 * Template data containing HTML and optional JSON
 */
export class TemplateData {
    constructor() {
        this.html = '';
        this.json = null;
    }
}

/**
 * Preprocessed template metadata
 */
export class PreProcessTemplateMetadata {
    constructor() {
        this.originalContent = '';
        this.placeholders = [];
        this.slottedTemplates = [];
        this.jsonData = null;
        this.jsonPlaceholders = [];
        this.replacementMappings = [];
        this.hasPlaceholders = false;
        this.hasSlottedTemplates = false;
        this.hasJsonData = false;
        this.hasJsonPlaceholders = false;
        this.hasReplacementMappings = false;
        this.requiresProcessing = false;
    }
}

/**
 * API response structure with manual JSON serialization
 */
export class ApiResponse {
    constructor() {
        this.templates = new Map();
        this.preProcessTemplates = new Map();
        this.appSite = '';
        this.appFile = null;
        this.appView = null;
        this.serverTimeMs = 0;
        this.html = '';
        this.engineTimeMs = 0;
    }

    /**
     * Serialize to JSON string manually (no JSON.stringify)
     * @returns {string} JSON string
     */
    serializeToJson() {
        let sb = [];
        sb.push('{');

        // Serialize Templates dictionary
        sb.push('"Templates":');
        sb.push(this._serializeDictionary(this.templates, (v) => this._serializeTemplateData(v)));

        sb.push(',');

        // Serialize PreProcessTemplates dictionary
        sb.push('"PreProcessTemplates":');
        sb.push(this._serializeDictionary(this.preProcessTemplates, (v) => this._serializePreProcessMetadata(v)));

        sb.push(',');

        // Serialize AppSite
        sb.push('"AppSite":"');
        sb.push(ApiResponse._escapeJsonString(this.appSite));
        sb.push('"');

        // Serialize AppFile if not null
        if (this.appFile !== null && this.appFile !== undefined) {
            sb.push(',"AppFile":"');
            sb.push(ApiResponse._escapeJsonString(this.appFile));
            sb.push('"');
        }

        // Serialize AppView if not null
        if (this.appView !== null && this.appView !== undefined) {
            sb.push(',"AppView":"');
            sb.push(ApiResponse._escapeJsonString(this.appView));
            sb.push('"');
        }

        // Serialize ServerTimeMs
        sb.push(',"ServerTimeMs":');
        sb.push(this.serverTimeMs.toString());

        // Merged Html
        sb.push(',"Html":"');
        sb.push(ApiResponse._escapeHtmlString(this.html));
        sb.push('"');

        // Engine Time
        sb.push(',"EngineTimeMs":');
        sb.push(this.engineTimeMs.toString());

        sb.push('}');
        return sb.join('');
    }

    /**
     * Escape JSON string values
     * @private
     */
    static _escapeJsonString(input) {
        if (!input) return '';

        return input
            .replace(/\\/g, '\\\\')
            .replace(/"/g, '\\"')
            .replace(/\r/g, '\\r')
            .replace(/\n/g, '\\n')
            .replace(/\t/g, '\\t')
            .replace(/</g, '\\u003C')
            .replace(/>/g, '\\u003E')
            .replace(/&/g, '\\u0026')
            .replace(/'/g, '\\u0027')
            .replace(/\+/g, '\\u002B');
    }

    /**
     * Escape HTML string values
     * @private
     */
    static _escapeHtmlString(input) {
        if (!input) return '';

        return input
            .replace(/\\/g, '\\\\')
            .replace(/"/g, '\\"')
            .replace(/\r/g, '\\r')
            .replace(/\n/g, '\\n')
            .replace(/\t/g, '\\t');
    }

    /**
     * Serialize a Map/dictionary
     * @private
     */
    _serializeDictionary(dict, valueSerializer) {
        let sb = [];
        sb.push('{');
        let first = true;

        for (const [key, value] of dict) {
            if (!first) sb.push(',');
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('":');
            sb.push(valueSerializer(value));
            first = false;
        }

        sb.push('}');
        return sb.join('');
    }

    /**
     * Serialize TemplateData
     * @private
     */
    _serializeTemplateData(data) {
        let sb = [];
        sb.push('{"Html":"');
        sb.push(ApiResponse._escapeJsonString(data.html));
        sb.push('","Json":');

        if (data.json !== null && data.json !== undefined) {
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(data.json));
            sb.push('"');
        } else {
            sb.push('null');
        }

        sb.push('}');
        return sb.join('');
    }

    /**
     * Serialize PreProcessTemplateMetadata
     * @private
     */
    _serializePreProcessMetadata(metadata) {
        let sb = [];
        sb.push('{');

        // OriginalContent
        sb.push('"OriginalContent":"');
        sb.push(ApiResponse._escapeHtmlString(metadata.originalContent));
        sb.push('",');

        // Placeholders
        sb.push('"Placeholders":');
        sb.push(this._serializePlaceholdersList(metadata.placeholders));
        sb.push(',');

        // SlottedTemplates
        sb.push('"SlottedTemplates":');
        sb.push(this._serializeSlottedTemplatesList(metadata.slottedTemplates));
        sb.push(',');

        // JsonData
        sb.push('"JsonData":');
        if (metadata.jsonData !== null && metadata.jsonData !== undefined) {
            const jsonDataStr = typeof metadata.jsonData === 'string' ? metadata.jsonData : String(metadata.jsonData);
            if (jsonDataStr.startsWith('{') || jsonDataStr.startsWith('[')) {
                // Appears to be JSON already, include as-is
                sb.push(jsonDataStr);
            } else {
                // Treat as string value
                sb.push('"');
                sb.push(ApiResponse._escapeJsonString(jsonDataStr));
                sb.push('"');
            }
        } else {
            sb.push('null');
        }
        sb.push(',');

        // JsonPlaceholders
        sb.push('"JsonPlaceholders":');
        sb.push(this._serializeJsonPlaceholdersList(metadata.jsonPlaceholders));
        sb.push(',');

        // ReplacementMappings
        sb.push('"ReplacementMappings":');
        sb.push(this._serializeReplacementMappingsList(metadata.replacementMappings));
        sb.push(',');

        // Boolean properties
        sb.push('"HasPlaceholders":');
        sb.push(metadata.hasPlaceholders ? 'true' : 'false');
        sb.push(',"HasSlottedTemplates":');
        sb.push(metadata.hasSlottedTemplates ? 'true' : 'false');
        sb.push(',"HasJsonData":');
        sb.push(metadata.hasJsonData ? 'true' : 'false');
        sb.push(',"HasJsonPlaceholders":');
        sb.push(metadata.hasJsonPlaceholders ? 'true' : 'false');
        sb.push(',"HasReplacementMappings":');
        sb.push(metadata.hasReplacementMappings ? 'true' : 'false');
        sb.push(',"RequiresProcessing":');
        sb.push(metadata.requiresProcessing ? 'true' : 'false');

        sb.push('}');
        return sb.join('');
    }

    /**
     * Serialize list of placeholders
     * @private
     */
    _serializePlaceholdersList(placeholders) {
        let sb = [];
        sb.push('[');

        for (let i = 0; i < placeholders.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(this._serializePlaceholder(placeholders[i]));
        }

        sb.push(']');
        return sb.join('');
    }

    /**
     * Serialize placeholder
     * @private
     */
    _serializePlaceholder(placeholder) {
        let sb = [];
        sb.push('{');
        sb.push('"Name":"');
        sb.push(ApiResponse._escapeJsonString(placeholder.name));
        sb.push('","StartIndex":');
        sb.push(placeholder.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(placeholder.endIndex.toString());
        sb.push(',"FullMatch":"');
        sb.push(ApiResponse._escapeJsonString(placeholder.fullMatch));
        sb.push('","TemplateKey":"');
        sb.push(ApiResponse._escapeJsonString(placeholder.templateKey));
        sb.push('","JsonData":');

        if (placeholder.jsonData !== null && placeholder.jsonData !== undefined) {
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(String(placeholder.jsonData)));
            sb.push('"');
        } else {
            sb.push('null');
        }

        sb.push(',"NestedPlaceholders":');
        sb.push(this._serializePlaceholdersList(placeholder.nestedPlaceholders));
        sb.push(',"NestedSlots":');
        sb.push(this._serializeSlotPlaceholdersList(placeholder.nestedSlots));
        sb.push('}');

        return sb.join('');
    }

    /**
     * Serialize list of slot placeholders
     * @private
     */
    _serializeSlotPlaceholdersList(slots) {
        let sb = [];
        sb.push('[');

        for (let i = 0; i < slots.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(this._serializeSlotPlaceholder(slots[i]));
        }

        sb.push(']');
        return sb.join('');
    }

    /**
     * Serialize slot placeholder
     * @private
     */
    _serializeSlotPlaceholder(slot) {
        let sb = [];
        sb.push('{');
        sb.push('"Number":"');
        sb.push(ApiResponse._escapeJsonString(slot.number));
        sb.push('","StartIndex":');
        sb.push(slot.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(slot.endIndex.toString());
        sb.push(',"Content":"');
        sb.push(ApiResponse._escapeJsonString(slot.content));
        sb.push('","SlotKey":"');
        sb.push(ApiResponse._escapeJsonString(slot.slotKey));
        sb.push('","OpenTag":"');
        sb.push(ApiResponse._escapeJsonString(slot.openTag));
        sb.push('","CloseTag":"');
        sb.push(ApiResponse._escapeJsonString(slot.closeTag));
        sb.push('","NestedSlots":');
        sb.push(this._serializeSlotPlaceholdersList(slot.nestedSlots));
        sb.push(',"NestedPlaceholders":');
        sb.push(this._serializePlaceholdersList(slot.nestedPlaceholders));
        sb.push(',"NestedSlottedTemplates":');
        sb.push(this._serializeSlottedTemplatesList(slot.nestedSlottedTemplates));
        sb.push(',"HasNestedPlaceholders":');
        sb.push(slot.hasNestedPlaceholders ? 'true' : 'false');
        sb.push(',"HasNestedSlottedTemplates":');
        sb.push(slot.hasNestedSlottedTemplates ? 'true' : 'false');
        sb.push(',"RequiresNestedProcessing":');
        sb.push(slot.requiresNestedProcessing ? 'true' : 'false');
        sb.push('}');

        return sb.join('');
    }

    /**
     * Serialize list of slotted templates
     * @private
     */
    _serializeSlottedTemplatesList(templates) {
        let sb = [];
        sb.push('[');

        for (let i = 0; i < templates.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(this._serializeSlottedTemplate(templates[i]));
        }

        sb.push(']');
        return sb.join('');
    }

    /**
     * Serialize slotted template
     * @private
     */
    _serializeSlottedTemplate(template) {
        let sb = [];
        sb.push('{');
        sb.push('"Name":"');
        sb.push(ApiResponse._escapeJsonString(template.name));
        sb.push('","StartIndex":');
        sb.push(template.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(template.endIndex.toString());
        sb.push(',"FullMatch":"');
        sb.push(ApiResponse._escapeJsonString(template.fullMatch));
        sb.push('","TemplateKey":"');
        sb.push(ApiResponse._escapeJsonString(template.templateKey));
        sb.push('","Slots":');
        sb.push(this._serializeSlotPlaceholdersList(template.slots));
        sb.push('}');

        return sb.join('');
    }

    /**
     * Serialize list of JSON placeholders
     * @private
     */
    _serializeJsonPlaceholdersList(placeholders) {
        let sb = [];
        sb.push('[');

        for (let i = 0; i < placeholders.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(this._serializeJsonPlaceholder(placeholders[i]));
        }

        sb.push(']');
        return sb.join('');
    }

    /**
     * Serialize JSON placeholder
     * @private
     */
    _serializeJsonPlaceholder(placeholder) {
        let sb = [];
        sb.push('{');
        sb.push('"Key":"');
        sb.push(ApiResponse._escapeJsonString(placeholder.key));
        sb.push('","Placeholder":"');
        sb.push(ApiResponse._escapeJsonString(placeholder.placeholder));
        sb.push('","Value":"');
        sb.push(ApiResponse._escapeJsonString(placeholder.value));
        sb.push('"}');

        return sb.join('');
    }

    /**
     * Serialize list of replacement mappings
     * @private
     */
    _serializeReplacementMappingsList(mappings) {
        let sb = [];
        sb.push('[');

        for (let i = 0; i < mappings.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(this._serializeReplacementMapping(mappings[i]));
        }

        sb.push(']');
        return sb.join('');
    }

    /**
     * Serialize replacement mapping
     * @private
     */
    _serializeReplacementMapping(mapping) {
        let sb = [];
        sb.push('{');
        sb.push('"StartIndex":');
        sb.push(mapping.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(mapping.endIndex.toString());
        sb.push(',"OriginalText":"');
        sb.push(ApiResponse._escapeHtmlString(mapping.originalText));
        sb.push('","ReplacementText":"');
        sb.push(ApiResponse._escapeHtmlString(mapping.replacementText));
        sb.push('","Type":');
        sb.push(mapping.type.toString());
        sb.push('}');

        return sb.join('');
    }

    /**
     * Serialize PreprocessedSiteTemplates to JSON with manual formatting
     * @param {Object} templates - The PreprocessedSiteTemplates object
     * @param {boolean} indented - Whether to format with indentation
     * @returns {string} JSON string
     */
    static serializePreprocessedSiteTemplates(templates, indented = true) {
        if (indented) {
            return ApiResponse._serializePreprocessedSiteTemplatesPretty(templates);
        }
        return ApiResponse._serializePreprocessedSiteTemplatesCompact(templates);
    }

    /**
     * Serialize PreprocessedSummary to JSON with manual formatting
     * @param {Object} summary - The summary object
     * @param {boolean} indented - Whether to format with indentation
     * @returns {string} JSON string
     */
    static serializePreprocessedSummary(summary, indented = true) {
        if (indented) {
            return `{\n  "siteName":"${ApiResponse._escapeJsonString(summary.siteName)}",\n  "templatesRequiringProcessing":${summary.templatesRequiringProcessing},\n  "templatesWithJsonData":${summary.templatesWithJsonData},\n  "templatesWithPlaceholders":${summary.templatesWithPlaceholders},\n  "totalTemplates":${summary.totalTemplates}\n}`;
        }
        return `{"siteName":"${ApiResponse._escapeJsonString(summary.siteName)}","templatesRequiringProcessing":${summary.templatesRequiringProcessing},"templatesWithJsonData":${summary.templatesWithJsonData},"templatesWithPlaceholders":${summary.templatesWithPlaceholders},"totalTemplates":${summary.totalTemplates}}`;
    }

    static _serializePreprocessedSiteTemplatesCompact(templates) {
        const sb = [];
        sb.push('{"siteName":"');
        sb.push(ApiResponse._escapeJsonString(templates.siteName));
        sb.push('","templates":{');

        let first = true;
        for (const [key, template] of templates.templates) {
            if (!first) sb.push(',');
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('":');
            sb.push(ApiResponse._serializePreprocessedTemplateCompact(template));
            first = false;
        }

        sb.push('},"rawTemplates":{');
        first = true;
        for (const [key, raw] of templates.rawTemplates) {
            if (!first) sb.push(',');
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('":"');
            sb.push(ApiResponse._escapeHtmlString(raw));
            sb.push('"');
            first = false;
        }

        sb.push('},"templateKeys":[');
        first = true;
        for (const key of templates.templateKeys) {
            if (!first) sb.push(',');
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('"');
            first = false;
        }
        sb.push(']}');

        return sb.join('');
    }

    static _serializePreprocessedSiteTemplatesPretty(templates) {
        const sb = [];
        sb.push('{\n  "siteName":"');
        sb.push(ApiResponse._escapeJsonString(templates.siteName));
        sb.push('",\n  "templates":{\n');

        let first = true;
        for (const [key, template] of templates.templates) {
            if (!first) sb.push(',\n');
            sb.push('  "');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('":{\n');
            sb.push(ApiResponse._serializePreprocessedTemplatePrettyInner(template));
            sb.push('\n}');
            first = false;
        }

        sb.push('\n},\n  "rawTemplates":{\n');
        first = true;
        for (const [key, raw] of templates.rawTemplates) {
            if (!first) sb.push(',\n');
            sb.push('  "');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('":"');
            sb.push(ApiResponse._escapeHtmlString(raw));
            sb.push('"');
            first = false;
        }

        sb.push('\n},\n  "templateKeys":[\n');
        first = true;
        for (const key of templates.templateKeys) {
            if (!first) sb.push(',\n');
            sb.push('  "');
            sb.push(ApiResponse._escapeJsonString(key));
            sb.push('"');
            first = false;
        }
        sb.push('\n]\n}');

        return sb.join('');
    }

    static _serializePreprocessedTemplateCompact(template) {
        const sb = [];
        sb.push('{"originalContent":"');
        sb.push(ApiResponse._escapeHtmlString(template.originalContent));
        sb.push('","placeholders":');
        sb.push(ApiResponse._serializePlaceholdersListStatic(template.placeholders));
        sb.push(',"slottedTemplates":');
        sb.push(ApiResponse._serializeSlottedTemplatesListStatic(template.slottedTemplates));
        sb.push(',"jsonData":');
        if (template.jsonData !== null && template.jsonData !== undefined) {
            sb.push('"Arshu.App.Json.JsonObject"');
        } else {
            sb.push('null');
        }
        sb.push(',"jsonPlaceholders":');
        sb.push(ApiResponse._serializeJsonPlaceholdersListStatic(template.jsonPlaceholders));
        sb.push(',"replacementMappings":');
        sb.push(ApiResponse._serializeReplacementMappingsListStatic(template.replacementMappings));
        sb.push(',"hasPlaceholders":');
        sb.push(template.hasPlaceholders ? 'true' : 'false');
        sb.push(',"hasSlottedTemplates":');
        sb.push(template.hasSlottedTemplates ? 'true' : 'false');
        sb.push(',"hasJsonData":');
        sb.push(template.hasJsonData ? 'true' : 'false');
        sb.push(',"hasJsonPlaceholders":');
        sb.push(template.hasJsonPlaceholders ? 'true' : 'false');
        sb.push(',"hasReplacementMappings":');
        sb.push(template.hasReplacementMappings ? 'true' : 'false');
        sb.push(',"requiresProcessing":');
        sb.push(template.requiresProcessing ? 'true' : 'false');
        sb.push('}');

        return sb.join('');
    }

    static _serializePreprocessedTemplatePrettyInner(template) {
        const sb = [];
        sb.push('  "originalContent":"');
        sb.push(ApiResponse._escapeHtmlString(template.originalContent));
        sb.push('",\n  "placeholders":[\n');

        for (let i = 0; i < template.placeholders.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializePlaceholderPretty(template.placeholders[i]));
        }
        sb.push('\n]');

        sb.push(',\n  "slottedTemplates":[\n');
        for (let i = 0; i < template.slottedTemplates.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeSlottedTemplatePretty(template.slottedTemplates[i]));
        }
        sb.push('\n]');

        sb.push(',\n  "jsonData":');
        if (template.jsonData !== null && template.jsonData !== undefined) {
            sb.push('"Arshu.App.Json.JsonObject"');
        } else {
            sb.push('null');
        }

        sb.push(',\n  "jsonPlaceholders":[\n');
        for (let i = 0; i < template.jsonPlaceholders.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeJsonPlaceholderPretty(template.jsonPlaceholders[i]));
        }
        sb.push('\n]');

        sb.push(',\n  "replacementMappings":[\n');
        for (let i = 0; i < template.replacementMappings.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeReplacementMappingPretty(template.replacementMappings[i]));
        }
        sb.push('\n]');

        sb.push(',\n  "hasPlaceholders":');
        sb.push(template.hasPlaceholders ? 'true' : 'false');
        sb.push(',\n  "hasSlottedTemplates":');
        sb.push(template.hasSlottedTemplates ? 'true' : 'false');
        sb.push(',\n  "hasJsonData":');
        sb.push(template.hasJsonData ? 'true' : 'false');
        sb.push(',\n  "hasJsonPlaceholders":');
        sb.push(template.hasJsonPlaceholders ? 'true' : 'false');
        sb.push(',\n  "hasReplacementMappings":');
        sb.push(template.hasReplacementMappings ? 'true' : 'false');
        sb.push(',\n  "requiresProcessing":');
        sb.push(template.requiresProcessing ? 'true' : 'false');

        return sb.join('');
    }

    static _serializePlaceholdersListStatic(placeholders) {
        const sb = [];
        sb.push('[');
        for (let i = 0; i < placeholders.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(ApiResponse._serializePlaceholderCompact(placeholders[i]));
        }
        sb.push(']');
        return sb.join('');
    }

    static _serializePlaceholderCompact(ph) {
        const sb = [];
        sb.push('{"Name":"');
        sb.push(ApiResponse._escapeJsonString(ph.name));
        sb.push('","StartIndex":');
        sb.push(ph.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(ph.endIndex.toString());
        sb.push(',"FullMatch":"');
        sb.push(ApiResponse._escapeJsonString(ph.fullMatch));
        sb.push('","TemplateKey":"');
        sb.push(ApiResponse._escapeJsonString(ph.templateKey));
        sb.push('","JsonData":');
        if (ph.jsonData !== null && ph.jsonData !== undefined) {
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(String(ph.jsonData)));
            sb.push('"');
        } else {
            sb.push('null');
        }
        sb.push(',"NestedPlaceholders":');
        sb.push(ApiResponse._serializePlaceholdersListStatic(ph.nestedPlaceholders));
        sb.push(',"NestedSlots":');
        sb.push(ApiResponse._serializeSlotPlaceholdersListStatic(ph.nestedSlots));
        sb.push('}');
        return sb.join('');
    }

    static _serializePlaceholderPretty(ph) {
        const sb = [];
        sb.push('{\n  "Name":"');
        sb.push(ApiResponse._escapeJsonString(ph.name));
        sb.push('",\n  "StartIndex":');
        sb.push(ph.startIndex.toString());
        sb.push(',\n  "EndIndex":');
        sb.push(ph.endIndex.toString());
        sb.push(',\n  "FullMatch":"');
        sb.push(ApiResponse._escapeJsonString(ph.fullMatch));
        sb.push('",\n  "TemplateKey":"');
        sb.push(ApiResponse._escapeJsonString(ph.templateKey));
        sb.push('",\n  "JsonData":');
        if (ph.jsonData !== null && ph.jsonData !== undefined) {
            sb.push('"');
            sb.push(ApiResponse._escapeJsonString(String(ph.jsonData)));
            sb.push('"');
        } else {
            sb.push('null');
        }
        sb.push(',\n  "NestedPlaceholders":[\n');
        for (let i = 0; i < ph.nestedPlaceholders.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializePlaceholderPretty(ph.nestedPlaceholders[i]));
        }
        sb.push('\n],\n  "NestedSlots":[\n');
        for (let i = 0; i < ph.nestedSlots.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeSlotPlaceholderPretty(ph.nestedSlots[i]));
        }
        sb.push('\n]\n}');
        return sb.join('');
    }

    static _serializeSlotPlaceholdersListStatic(slots) {
        const sb = [];
        sb.push('[');
        for (let i = 0; i < slots.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(ApiResponse._serializeSlotPlaceholderCompact(slots[i]));
        }
        sb.push(']');
        return sb.join('');
    }

    static _serializeSlotPlaceholderCompact(slot) {
        const sb = [];
        sb.push('{"Number":"');
        sb.push(ApiResponse._escapeJsonString(slot.number));
        sb.push('","StartIndex":');
        sb.push(slot.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(slot.endIndex.toString());
        sb.push(',"Content":"');
        sb.push(ApiResponse._escapeJsonString(slot.content));
        sb.push('","SlotKey":"');
        sb.push(ApiResponse._escapeJsonString(slot.slotKey));
        sb.push('","OpenTag":"');
        sb.push(ApiResponse._escapeJsonString(slot.openTag));
        sb.push('","CloseTag":"');
        sb.push(ApiResponse._escapeJsonString(slot.closeTag));
        sb.push('","NestedSlots":');
        sb.push(ApiResponse._serializeSlotPlaceholdersListStatic(slot.nestedSlots));
        sb.push(',"NestedPlaceholders":');
        sb.push(ApiResponse._serializePlaceholdersListStatic(slot.nestedPlaceholders));
        sb.push(',"NestedSlottedTemplates":');
        sb.push(ApiResponse._serializeSlottedTemplatesListStatic(slot.nestedSlottedTemplates));
        sb.push(',"HasNestedPlaceholders":');
        sb.push(slot.hasNestedPlaceholders ? 'true' : 'false');
        sb.push(',"HasNestedSlottedTemplates":');
        sb.push(slot.hasNestedSlottedTemplates ? 'true' : 'false');
        sb.push(',"RequiresNestedProcessing":');
        sb.push(slot.requiresNestedProcessing ? 'true' : 'false');
        sb.push('}');
        return sb.join('');
    }

    static _serializeSlotPlaceholderPretty(slot) {
        const sb = [];
        sb.push('{\n  "Number":"');
        sb.push(ApiResponse._escapeJsonString(slot.number));
        sb.push('",\n  "StartIndex":');
        sb.push(slot.startIndex.toString());
        sb.push(',\n  "EndIndex":');
        sb.push(slot.endIndex.toString());
        sb.push(',\n  "Content":"');
        sb.push(ApiResponse._escapeJsonString(slot.content));
        sb.push('",\n  "SlotKey":"');
        sb.push(ApiResponse._escapeJsonString(slot.slotKey));
        sb.push('",\n  "OpenTag":"');
        sb.push(ApiResponse._escapeJsonString(slot.openTag));
        sb.push('",\n  "CloseTag":"');
        sb.push(ApiResponse._escapeJsonString(slot.closeTag));
        sb.push('",\n  "NestedSlots":[\n');
        for (let i = 0; i < slot.nestedSlots.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeSlotPlaceholderPretty(slot.nestedSlots[i]));
        }
        sb.push('\n],\n  "NestedPlaceholders":[\n');
        for (let i = 0; i < slot.nestedPlaceholders.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializePlaceholderPretty(slot.nestedPlaceholders[i]));
        }
        sb.push('\n],\n  "NestedSlottedTemplates":[\n');
        for (let i = 0; i < slot.nestedSlottedTemplates.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeSlottedTemplatePretty(slot.nestedSlottedTemplates[i]));
        }
        sb.push('\n]');
        sb.push(',\n  "HasNestedPlaceholders":');
        sb.push(slot.hasNestedPlaceholders ? 'true' : 'false');
        sb.push(',\n  "HasNestedSlottedTemplates":');
        sb.push(slot.hasNestedSlottedTemplates ? 'true' : 'false');
        sb.push(',\n  "RequiresNestedProcessing":');
        sb.push(slot.requiresNestedProcessing ? 'true' : 'false');
        sb.push('\n}');
        return sb.join('');
    }

    static _serializeSlottedTemplatesListStatic(templates) {
        const sb = [];
        sb.push('[');
        for (let i = 0; i < templates.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(ApiResponse._serializeSlottedTemplateCompact(templates[i]));
        }
        sb.push(']');
        return sb.join('');
    }

    static _serializeSlottedTemplateCompact(st) {
        const sb = [];
        sb.push('{"Name":"');
        sb.push(ApiResponse._escapeJsonString(st.name));
        sb.push('","StartIndex":');
        sb.push(st.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(st.endIndex.toString());
        sb.push(',"FullMatch":"');
        sb.push(ApiResponse._escapeJsonString(st.fullMatch));
        sb.push('","TemplateKey":"');
        sb.push(ApiResponse._escapeJsonString(st.templateKey));
        sb.push('","Slots":');
        sb.push(ApiResponse._serializeSlotPlaceholdersListStatic(st.slots));
        sb.push('}');
        return sb.join('');
    }

    static _serializeSlottedTemplatePretty(st) {
        const sb = [];
        sb.push('{\n  "Name":"');
        sb.push(ApiResponse._escapeJsonString(st.name));
        sb.push('",\n  "StartIndex":');
        sb.push(st.startIndex.toString());
        sb.push(',\n  "EndIndex":');
        sb.push(st.endIndex.toString());
        sb.push(',\n  "FullMatch":"');
        sb.push(ApiResponse._escapeJsonString(st.fullMatch));
        sb.push('",\n  "TemplateKey":"');
        sb.push(ApiResponse._escapeJsonString(st.templateKey));
        sb.push('",\n  "Slots":[\n');
        for (let i = 0; i < st.slots.length; i++) {
            if (i > 0) sb.push(',\n');
            sb.push('  ');
            sb.push(ApiResponse._serializeSlotPlaceholderPretty(st.slots[i]));
        }
        sb.push('\n]\n}');
        return sb.join('');
    }

    static _serializeJsonPlaceholdersListStatic(placeholders) {
        const sb = [];
        sb.push('[');
        for (let i = 0; i < placeholders.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(ApiResponse._serializeJsonPlaceholderCompact(placeholders[i]));
        }
        sb.push(']');
        return sb.join('');
    }

    static _serializeJsonPlaceholderCompact(jp) {
        const sb = [];
        sb.push('{"Key":"');
        sb.push(ApiResponse._escapeJsonString(jp.key));
        sb.push('","Placeholder":"');
        sb.push(ApiResponse._escapeJsonString(jp.placeholder));
        sb.push('","Value":"');
        sb.push(ApiResponse._escapeJsonString(jp.value));
        sb.push('"}');
        return sb.join('');
    }

    static _serializeJsonPlaceholderPretty(jp) {
        const sb = [];
        sb.push('{\n  "Key":"');
        sb.push(ApiResponse._escapeJsonString(jp.key));
        sb.push('",\n  "Placeholder":"');
        sb.push(ApiResponse._escapeJsonString(jp.placeholder));
        sb.push('",\n  "Value":"');
        sb.push(ApiResponse._escapeJsonString(jp.value));
        sb.push('"\n}');
        return sb.join('');
    }

    static _serializeReplacementMappingsListStatic(mappings) {
        const sb = [];
        sb.push('[');
        for (let i = 0; i < mappings.length; i++) {
            if (i > 0) sb.push(',');
            sb.push(ApiResponse._serializeReplacementMappingCompact(mappings[i]));
        }
        sb.push(']');
        return sb.join('');
    }

    static _serializeReplacementMappingCompact(rm) {
        const sb = [];
        sb.push('{"StartIndex":');
        sb.push(rm.startIndex.toString());
        sb.push(',"EndIndex":');
        sb.push(rm.endIndex.toString());
        sb.push(',"OriginalText":"');
        sb.push(ApiResponse._escapeHtmlString(rm.originalText));
        sb.push('","ReplacementText":"');
        sb.push(ApiResponse._escapeHtmlString(rm.replacementText));
        sb.push('","Type":');
        sb.push(rm.type.toString());
        sb.push('}');
        return sb.join('');
    }

    static _serializeReplacementMappingPretty(rm) {
        const sb = [];
        sb.push('{\n  "StartIndex":');
        sb.push(rm.startIndex.toString());
        sb.push(',\n  "EndIndex":');
        sb.push(rm.endIndex.toString());
        sb.push(',\n  "OriginalText":"');
        sb.push(ApiResponse._escapeHtmlString(rm.originalText));
        sb.push('",\n  "ReplacementText":"');
        sb.push(ApiResponse._escapeHtmlString(rm.replacementText));
        sb.push('",\n  "Type":');
        sb.push(rm.type.toString());
        sb.push('\n}');
        return sb.join('');
    }

    /**
     * Create a preprocessed summary from PreprocessedSiteTemplates
     * @param {Object} siteTemplates - The PreprocessedSiteTemplates object
     * @returns {Object} Summary object with counts
     */
    static createPreprocessedSummary(siteTemplates) {
        const summary = {
            siteName: siteTemplates.siteName,
            templatesRequiringProcessing: 0,
            templatesWithJsonData: 0,
            templatesWithPlaceholders: 0,
            totalTemplates: siteTemplates.templates.size
        };

        for (const [key, template] of siteTemplates.templates) {
            if (template.requiresProcessing) {
                summary.templatesRequiringProcessing++;
            }
            if (template.hasJsonData) {
                summary.templatesWithJsonData++;
            }
            if (template.hasPlaceholders) {
                summary.templatesWithPlaceholders++;
            }
        }

        return summary;
    }
}
