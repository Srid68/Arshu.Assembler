// PreProcess template model structures for Node.js

import { JsonConverter } from '../app/jsonConverter.js';

/**
 * Types of template replacements (using numeric values to match C#)
 */
export const ReplacementType = {
    JSON_PLACEHOLDER: 0,
    SIMPLE_TEMPLATE: 1,
    SLOTTED_TEMPLATE: 2
};

/**
 * Contains preprocessed templates for a site with efficient lookup structures
 */
export class PreprocessedSiteTemplates {
    constructor() {
        this.siteName = '';
        this.templates = new Map();
        this.rawTemplates = new Map();
        this.templateKeys = new Set();
    }


}

/**
 * Represents a preprocessed template with parsed structure for efficient merging
 */
export class PreprocessedTemplate {
    constructor() {
        this.originalContent = '';
        this.placeholders = [];
        this.slottedTemplates = [];
        this.jsonData = null;
        this.jsonPlaceholders = [];
        this.replacementMappings = [];
    }

    // Helper properties to check template state
    get hasPlaceholders() {
        return this.placeholders.length > 0;
    }

    get hasSlottedTemplates() {
        return this.slottedTemplates.length > 0;
    }

    get hasJsonData() {
        return this.jsonData !== null && this.jsonData.size > 0;
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

    /**
     * Convert to plain object for JSON serialization
     * @returns {Object} Plain object representation
     */
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
            requiresProcessing: this.requiresProcessing
        };
    }
}

/**
 * Represents a JSON placeholder like {{$key}} with precomputed replacement value
 */
export class JsonPlaceholder {
    constructor(key = '', placeholder = '', value = '') {
        this.key = key;
        this.placeholder = placeholder;
        this.value = value;
    }

    toObject() {
        return {
            key: this.key,
            placeholder: this.placeholder,
            value: this.value
        };
    }
}

/**
 * Represents a pre-computed replacement for ultra-fast template merging
 */
export class ReplacementMapping {
    constructor() {
        this.startIndex = 0;
        this.endIndex = 0;
        this.originalText = '';
        this.replacementText = '';
        this.type = ReplacementType.SIMPLE_TEMPLATE;
    }

    toObject() {
        return {
            startIndex: this.startIndex,
            endIndex: this.endIndex,
            originalText: this.originalText,
            replacementText: this.replacementText,
            type: this.type
        };
    }
}

/**
 * Represents a simple placeholder like {{ComponentName}}
 */
export class TemplatePlaceholder {
    constructor() {
        this.name = '';
        this.startIndex = 0;
        this.endIndex = 0;
        this.fullMatch = '';
        this.templateKey = '';
        this.jsonData = null;
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
            nestedSlots: this.nestedSlots.map(ns => ns.toObject())
        };
    }
}

/**
 * Represents a slotted template like {{#TemplateName}} ... {{/TemplateName}}
 */
export class SlottedTemplate {
    constructor() {
        this.name = '';
        this.startIndex = 0;
        this.endIndex = 0;
        this.fullMatch = '';
        this.innerContent = '';
        this.slots = [];
        this.templateKey = '';
        this.jsonData = null;
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
            jsonData: this.jsonData ? this.jsonData.toObject() : null
        };
    }
}

/**
 * Represents a slot within a slotted template like {{@HTMLPLACEHOLDER[N]}} ... {{/HTMLPLACEHOLDER[N]}}
 */
export class SlotPlaceholder {
    constructor() {
        this.nestedSlots = [];
        this.number = '';
        this.startIndex = 0;
        this.endIndex = 0;
        this.content = '';
        this.slotKey = '';
        this.openTag = '';
        this.closeTag = '';
        this.nestedPlaceholders = [];
        this.nestedSlottedTemplates = [];
    }

    // Helper properties
    get hasNestedPlaceholders() {
        return this.nestedPlaceholders.length > 0;
    }

    get hasNestedSlottedTemplates() {
        return this.nestedSlottedTemplates.length > 0;
    }

    get requiresNestedProcessing() {
        return this.hasNestedPlaceholders || this.hasNestedSlottedTemplates;
    }

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
            requiresNestedProcessing: this.requiresNestedProcessing
        };
    }
}