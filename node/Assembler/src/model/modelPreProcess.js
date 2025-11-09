// PreProcess template model structures for Node.js

import { JsonConverter } from '../app/jsonConverter.js';

// Types of template replacements (match C# names)
export const ReplacementType = {
    JsonPlaceholder: 0,
    SimpleTemplate: 1,
    SlottedTemplate: 2,
};

// Contains preprocessed templates for a site with efficient lookup structures
export class PreprocessedSiteTemplates {
    constructor(siteName = '', templates = new Map(), rawTemplates = new Map(), templateKeys = new Set()) {
        this.siteName = siteName;
        this.templates = templates; // Map<string, PreprocessedTemplate>
        this.rawTemplates = rawTemplates; // Map<string, string>
        this.templateKeys = templateKeys; // Set<string>
    }
}

// Represents a preprocessed template with parsed structure for efficient merging
export class PreprocessedTemplate {
    constructor(originalContent = '', jsonData = null) {
        this.originalContent = originalContent;
        this.placeholders = [];
        this.slottedTemplates = [];
        this.jsonData = jsonData;
        this.jsonPlaceholders = [];
        this.replacementMappings = [];
        this.parentTemplateKey = null;
        this.childTemplateKeys = [];
        this.inheritableJsonKeys = [];
    }

    // Helper properties to check template state
    get hasPlaceholders() {
        return this.placeholders.length > 0;
    }

    get hasSlottedTemplates() {
        return this.slottedTemplates.length > 0;
    }

    get hasJsonData() {
        if (this.jsonData === null || this.jsonData === undefined) return false;
        if (typeof this.jsonData.size === 'number') return this.jsonData.size > 0; // JsonObject (Map-like)
        if (typeof this.jsonData === 'object') return Object.keys(this.jsonData).length > 0; // plain object
        return false;
    }

    get hasJsonPlaceholders() {
        return this.jsonPlaceholders.length > 0;
    }

    get hasReplacementMappings() {
        return this.replacementMappings.length > 0;
    }

    get requiresProcessing() {
        return this.hasPlaceholders || this.hasSlottedTemplates ||
               this.hasJsonData || this.hasJsonPlaceholders || this.hasReplacementMappings;
    }

    // Convert to plain object for JSON serialization
    toObject() {
        return {
            originalContent: this.originalContent,
            placeholders: this.placeholders.map(p => p.toObject()),
            slottedTemplates: this.slottedTemplates.map(st => st.toObject()),
            jsonData: this.jsonData ? JsonConverter.toPlainObject(this.jsonData) : null,
            jsonPlaceholders: this.jsonPlaceholders.map(jp => jp.toObject()),
            replacementMappings: this.replacementMappings.map(rm => rm.toObject()),
            hasPlaceholders: this.hasPlaceholders,
            hasSlottedTemplates: this.hasSlottedTemplates,
            hasJsonData: this.hasJsonData,
            hasJsonPlaceholders: this.hasJsonPlaceholders,
            hasReplacementMappings: this.hasReplacementMappings,
            requiresProcessing: this.requiresProcessing,
        };
    }
}

// Represents a JSON placeholder like {{$key}} with precomputed replacement value
export class JsonPlaceholder {
    constructor(key = '', placeholder = '', value = '') {
        this.key = key;
        this.placeholder = placeholder;
        this.value = value;
    }

    toObject() {
        return { key: this.key, placeholder: this.placeholder, value: this.value };
    }
}

// Represents a pre-computed replacement for ultra-fast template merging
export class ReplacementMapping {
    constructor(originalText = '', replacementText = '', type = ReplacementType.SimpleTemplate, startIndex = 0, endIndex = 0, targetTemplateName = null) {
        this.startIndex = startIndex;
        this.endIndex = endIndex;
        this.originalText = originalText;
        this.replacementText = replacementText;
        this.type = type;
        this.targetTemplateName = targetTemplateName;
    }

    toObject() {
        return {
            startIndex: this.startIndex,
            endIndex: this.endIndex,
            originalText: this.originalText,
            replacementText: this.replacementText,
            type: this.type,
            targetTemplateName: this.targetTemplateName,
        };
    }
}

// Represents a simple placeholder like {{ComponentName}}
export class TemplatePlaceholder {
    constructor(name = '', startIndex = 0, endIndex = 0, fullMatch = '', templateKey = '', jsonData = null) {
        this.name = name;
        this.startIndex = startIndex;
        this.endIndex = endIndex;
        this.fullMatch = fullMatch;
        this.templateKey = templateKey;
        this.jsonData = jsonData;
        this.nestedPlaceholders = [];
        this.nestedSlots = [];
    }

    toObject() {
        return {
            name: this.name,
            startIndex: this.startIndex,
            endIndex: this.endIndex,
            fullMatch: this.fullMatch,
            templateKey: this.templateKey,
            nestedPlaceholders: this.nestedPlaceholders.map(np => np.toObject()),
            nestedSlots: this.nestedSlots.map(ns => ns.toObject()),
        };
    }
}

// Represents a slotted template like {{#TemplateName}} ... {{/TemplateName}}
export class SlottedTemplate {
    constructor(name = '', startIndex = 0, endIndex = 0, fullMatch = '', innerContent = '', templateKey = '', jsonData = null) {
        this.name = name;
        this.startIndex = startIndex;
        this.endIndex = endIndex;
        this.fullMatch = fullMatch;
        this.innerContent = innerContent;
        this.slots = [];
        this.templateKey = templateKey;
        this.jsonData = jsonData;
    }

    toObject() {
        return {
            name: this.name,
            startIndex: this.startIndex,
            endIndex: this.endIndex,
            fullMatch: this.fullMatch,
            innerContent: this.innerContent,
            slots: this.slots.map(s => s.toObject()),
            templateKey: this.templateKey,
            jsonData: this.jsonData ? JsonConverter.toPlainObject(this.jsonData) : null,
        };
    }
}

// Represents a slot within a slotted template like {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}}
export class SlotPlaceholder {
    constructor(number = '', startIndex = 0, endIndex = 0, content = '', slotKey = '', openTag = '', closeTag = '') {
        this.nestedSlots = [];
        this.number = number;
        this.startIndex = startIndex;
        this.endIndex = endIndex;
        this.content = content;
        this.slotKey = slotKey;
        this.openTag = openTag;
        this.closeTag = closeTag;
        this.nestedPlaceholders = [];
        this.nestedSlottedTemplates = [];
    }

    // Helper properties
    get hasNestedPlaceholders() { return this.nestedPlaceholders.length > 0; }
    get hasNestedSlottedTemplates() { return this.nestedSlottedTemplates.length > 0; }
    get requiresNestedProcessing() { return this.hasNestedPlaceholders || this.hasNestedSlottedTemplates; }

    toObject() {
        return {
            nestedSlots: this.nestedSlots.map(ns => ns.toObject()),
            number: this.number,
            startIndex: this.startIndex,
            endIndex: this.endIndex,
            content: this.content,
            slotKey: this.slotKey,
            openTag: this.openTag,
            closeTag: this.closeTag,
            nestedPlaceholders: this.nestedPlaceholders.map(np => np.toObject()),
            nestedSlottedTemplates: this.nestedSlottedTemplates.map(nst => nst.toObject()),
            hasNestedPlaceholders: this.hasNestedPlaceholders,
            hasNestedSlottedTemplates: this.hasNestedSlottedTemplates,
            requiresNestedProcessing: this.requiresNestedProcessing,
        };
    }
}
