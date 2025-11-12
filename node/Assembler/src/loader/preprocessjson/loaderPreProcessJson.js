import { promises as fs } from 'fs';
import path from 'path';
import { Logger } from '../../../../Arshu/src/common/Logger.js';
import { normalizeFileContent, isAlphaNumeric, findMatchingCloseTag, removeRemainingSlotPlaceholders, replaceCaseInsensitive } from '../../common/commonUtil.js';
import {
    PreprocessedTemplate,
    SlottedTemplate,
    SlotPlaceholder,
    TemplatePlaceholder,
    ReplacementMapping,
    ReplacementType,
    JsonPlaceholder
} from '../../model/modelPreProcess.js';
import { mergeTemplateWithJson } from '../jsonMergeUtil.js';

const _preprocessedTemplatesCache = new Map();

export class LoaderPreProcessJson {
    constructor(rootDirPath, appSite, searchAppSites) {
        this.searchAppSites = searchAppSites;
        this._templates = new Map();
        this.rootDirPath = rootDirPath;
        this.appSite = appSite;
    }

    async load() {
        const siteTemplates = await this.loadProcessGetTemplateFiles(this.rootDirPath, this.appSite);
        this._templates = siteTemplates.templates;

        if (this.searchAppSites) {
            const searchAppSitesArray = this.searchAppSites.split(',');
            for (const searchAppSite of searchAppSitesArray) {
                const trimmedSite = searchAppSite.trim();
                if (trimmedSite) {
                    const searchSiteTemplates = await this.loadProcessGetTemplateFiles(this.rootDirPath, trimmedSite);
                    searchSiteTemplates.templates.forEach((value, key) => {
                        if (!this._templates.has(key)) {
                            this._templates.set(key, value);
                        }
                    });
                }
            }
        }
    }

    getTemplateHtml(appSite, templateName, appView = null, appViewPrefix = null) {
        return this.getTemplateInternal(appSite, templateName, appView, appViewPrefix);
    }

    getTemplateJson(appSite, templateName) {
        const template = this.getTemplateInternal(appSite, templateName, null, null);
        return template ? template.jsonData : null;
    }

    // Centralized HTML+JSON merge to mirror C# loader API
    mergeHtmlWithJson(html, appSite, templateName) {
        if (!html) return html;
        const template = this.getTemplateInternal(appSite, templateName, null, null);
        if (!template || !template.jsonData) {
            Logger.debug(`No JSON data found for ${templateName}, returning original HTML`, 'LoaderPreProcessJson');
            return html;
        }
        Logger.debug(`Merging HTML with JSON for ${templateName}`, 'LoaderPreProcessJson');
        return mergeTemplateWithJson(html, template.jsonData);
    }

    hasTemplate(appSite, templateName) {
        const key = `${appSite.toLowerCase()}_${templateName.toLowerCase()}`;
        return this._templates.has(key);
    }

    static clearCache() {
        _preprocessedTemplatesCache.clear();
    }

    // Instance clearCache for interface compatibility
    clearCache() {
        _preprocessedTemplatesCache.clear();
    }

    getAllTemplates() {
        const allTemplates = {};
        this._templates.forEach((value, key) => {
            allTemplates[key] = value;
        });
        return allTemplates;
    }

    // Applies all replacement mappings from all templates to the given content
    // Mirrors C# ILoaderJson<PreprocessedTemplate>.ApplyAllReplacementMappings
    applyAllReplacementMappings(content, appSite, mainTemplate, appView, appViewPrefix, enableJsonProcessing) {
        let result = content || '';

        Logger.debug(`Starting ApplyTemplateReplacements, initial size: ${result.length}`, 'LoaderPreProcessJson');

        let previous;
        const maxPasses = 10;
        let currentPass = 0;

        do {
            previous = result;
            currentPass++;
            Logger.debug(`Replacement pass ${currentPass}, current size: ${result.length}`, 'LoaderPreProcessJson');

            let slottedCount = 0, simpleCount = 0, jsonPlaceholderCount = 0;

            // FIRST: Apply JSON placeholder mappings ONLY from the main template in the first pass
            if (mainTemplate && currentPass === 1 && enableJsonProcessing) {
                for (const mapping of mainTemplate.replacementMappings) {
                    if (mapping.type !== ReplacementType.JsonPlaceholder) continue;
                    if (result.includes(mapping.originalText)) {
                        Logger.debug(`Applying main template JSON placeholder: ${mapping.originalText} -> ${mapping.replacementText}`, 'LoaderPreProcessJson');
                        result = result.split(mapping.originalText).join(mapping.replacementText);
                        jsonPlaceholderCount++;
                    }
                }
            }

            // Apply mappings from every template currently loaded in this loader
            for (const [_, template] of this._templates) {
                // Slotted templates first
                for (const mapping of template.replacementMappings) {
                    if (mapping.type !== ReplacementType.SlottedTemplate) continue;
                    if (result.includes(mapping.originalText)) {
                        let replacementText = mapping.replacementText;
                        if (enableJsonProcessing && mapping.targetTemplateName) {
                            replacementText = this.mergeHtmlWithJson(replacementText, appSite, mapping.targetTemplateName);
                            Logger.debug(`After merging JSON for slotted template ${mapping.targetTemplateName}: ${replacementText.length} chars`, 'LoaderPreProcessJson');
                        }
                        result = result.split(mapping.originalText).join(replacementText);
                        slottedCount++;
                    }
                }

                // Simple templates next with AppView logic
                for (const mapping of template.replacementMappings) {
                    if (mapping.type !== ReplacementType.SimpleTemplate) continue;
                    if (!result.includes(mapping.originalText)) continue;

                    let replacementText = mapping.replacementText;

                    // AppView fallback if available
                    if (appView && mapping.targetTemplateName) {
                        const viewPrefix = appViewPrefix || '';
                        if (viewPrefix && mapping.targetTemplateName.toLowerCase().includes(viewPrefix.toLowerCase())) {
                            const appKey = replaceCaseInsensitive(mapping.targetTemplateName, viewPrefix, appView);
                            const appViewTpl = this.getTemplateInternal(appSite, appKey, null, null);
                            if (appViewTpl) {
                                replacementText = appViewTpl.originalContent;
                            }
                        }
                    }

                    if (enableJsonProcessing && mapping.targetTemplateName) {
                        replacementText = this.mergeHtmlWithJson(replacementText, appSite, mapping.targetTemplateName);
                        Logger.debug(`After merging JSON for simple template ${mapping.targetTemplateName}: ${replacementText.length} chars`, 'LoaderPreProcessJson');
                    }

                    result = result.split(mapping.originalText).join(replacementText);
                    simpleCount++;
                }
            }

            Logger.debug(`Pass ${currentPass} applied: ${jsonPlaceholderCount} main JSON placeholders, ${slottedCount} slotted, ${simpleCount} simple`, 'LoaderPreProcessJson');
        } while (result !== previous && currentPass < maxPasses);

        Logger.debug(`Replacement complete after ${currentPass} passes, final size: ${result.length}`, 'LoaderPreProcessJson');
        return result;
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
                        Logger.debug(`Template '${templateName}' not found in '${appSite}', using fallback from '${trimmedSite}'`, 'LoaderPreProcessJson');
                        return this._templates.get(searchKey);
                    }
                }
            }
        }
        return null;
    }

    async loadProcessGetTemplateFiles(rootDirPath, appSite) {
        Logger.debug(`LoadProcessGetTemplateFiles called for appSite: ${appSite}`, 'LoaderPreProcessJson');
        const cacheKey = `${path.dirname(rootDirPath)}|${appSite}`;

        if (_preprocessedTemplatesCache.has(cacheKey)) {
            const cached = _preprocessedTemplatesCache.get(cacheKey);
            Logger.debug(`Returning cached templates for ${appSite} (${cached.templates.size} templates)`, 'LoaderPreProcessJson');
            return cached;
        }

        const result = {
            siteName: appSite,
            templates: new Map(),
            rawTemplates: new Map(),
            templateKeys: [],
        };

        const appSitesPath = path.join(rootDirPath, 'AppSites', appSite);

        try {
            await fs.access(appSitesPath);
        } catch (error) {
            Logger.warn(`AppSites directory not found: ${appSitesPath}`, 'LoaderPreProcessJson');
            _preprocessedTemplatesCache.set(cacheKey, result);
            return result;
        }

        Logger.debug(`Loading templates from: ${appSitesPath}`, 'LoaderPreProcessJson');
        const files = await this.readDirRecursive(appSitesPath);

        for (const file of files) {
            if (file.endsWith('.html')) {
                const fileName = path.basename(file, '.html');
                const key = `${appSite.toLowerCase()}_${fileName.toLowerCase()}`;
                const content = normalizeFileContent(await fs.readFile(file, 'utf-8'));
                Logger.debug(`Loading template: ${key} (size: ${content.length})`, 'LoaderPreProcessJson');

                const jsonFile = file.replace('.html', '.json');
                let jsonData = null;
                try {
                    const jsonContent = normalizeFileContent(await fs.readFile(jsonFile, 'utf-8'));
                    if (jsonContent) {
                        jsonData = JSON.parse(jsonContent);
                        Logger.debug(`Found JSON file for ${key}, parsed to JsonObject`, 'LoaderPreProcessJson');
                    }
                } catch (ex) {
                    // File may not exist
                }

                result.rawTemplates.set(key, content);
                result.templateKeys.push(key);

                const preprocessed = this.preprocessTemplate(content, jsonData, appSite, key);
                result.templates.set(key, preprocessed);

                Logger.debug(`Preprocessed ${key}: ${preprocessed.replacementMappings.length} replacements, ${preprocessed.slottedTemplates.length} slotted, ${preprocessed.placeholders.length} placeholders`, 'LoaderPreProcessJson');
            }
        }

        Logger.debug(`Loaded ${result.templates.size} templates for ${appSite}`, 'LoaderPreProcessJson');
        this.createAllReplacementMappingsForSite(result, appSite);
        Logger.debug(`Created all replacement mappings for ${appSite}`, 'LoaderPreProcessJson');

        _preprocessedTemplatesCache.set(cacheKey, result);
        return result;
    }

    preprocessTemplate(content, jsonData, appSite, templateKey) {
        const template = new PreprocessedTemplate(content, jsonData);
        if (!content) return template;

        this.parseSlottedTemplates(content, appSite, template);
        this.parsePlaceholderTemplates(content, appSite, template);

        if (template.hasJsonData) {
            this.preprocessJsonTemplates(template);
        }

        return template;
    }

    createAllReplacementMappingsForSite(siteTemplates, appSite) {
        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 0: JSON inheritance`, 'LoaderPreProcessJson');
        const parentMap = this.BuildParentMapForPreProcessJson(siteTemplates, appSite);
        this.ResolveJsonInheritanceForAllTemplatesJson(siteTemplates, parentMap);
        this.RecreateJsonPlaceholderMappingsAfterInheritanceJson(siteTemplates);

        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 1: JSON arrays`, 'LoaderPreProcessJson');
        siteTemplates.templates.forEach(template => {
            this.createJsonArrayReplacementMappings(template, template.originalContent);
        });

        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 2: Simple placeholders`, 'LoaderPreProcessJson');
        siteTemplates.templates.forEach(template => {
            this.createPlaceholderReplacementMappings(template, siteTemplates.templates, appSite);
        });

        Logger.debug(`Creating replacement mappings for ${appSite} - Phase 3: Slotted templates`, 'LoaderPreProcessJson');
        siteTemplates.templates.forEach(template => {
            this.createSlottedTemplateReplacementMappings(template, siteTemplates.templates, appSite);
        });

        let totalMappings = 0;
        siteTemplates.templates.forEach(template => {
            totalMappings += template.replacementMappings.length;
        });
        Logger.info(`Total replacement mappings created for ${appSite}: ${totalMappings}`, 'LoaderPreProcessJson');
    }

    BuildParentMapForPreProcessJson(siteTemplates, appSite) {
        const parentMap = new Map();
        Logger.debug(`Building parent map for appSite: ${appSite}`, 'LoaderPreProcessJson');

        for (const [templateKey, template] of siteTemplates.templates) {
            for (const placeholder of template.placeholders) {
                const childTemplateKey = `${appSite.toLowerCase()}_${placeholder.name.toLowerCase()}`;
                if (!parentMap.has(childTemplateKey)) {
                    parentMap.set(childTemplateKey, templateKey);
                    Logger.debug(`Parent relationship: ${childTemplateKey} -> parent: ${templateKey}`, 'LoaderPreProcessJson');
                }
            }

            for (const slottedTemplate of template.slottedTemplates) {
                const childTemplateKey = `${appSite.toLowerCase()}_${slottedTemplate.name.toLowerCase()}`;
                if (!parentMap.has(childTemplateKey)) {
                    parentMap.set(childTemplateKey, templateKey);
                    Logger.debug(`Parent relationship (slotted): ${childTemplateKey} -> parent: ${templateKey}`, 'LoaderPreProcessJson');
                }
            }
        }

        Logger.debug(`Built parent map with ${parentMap.size} relationships`, 'LoaderPreProcessJson');
        return parentMap;
    }

    ResolveJsonInheritanceForAllTemplatesJson(siteTemplates, parentMap) {
        for (const [templateKey, template] of siteTemplates.templates) {
            if (!template.jsonData) {
                continue;
            }

            const resolvedJson = {};
            let hasInheritance = false;

            for (const [key, value] of Object.entries(template.jsonData)) {
                if (key.endsWith('#')) {
                    hasInheritance = true;
                    const actualKey = key.slice(0, -1);
                    const hasExplicitValue = this.hasExplicitJsonValue(value);

                    if (hasExplicitValue) {
                        resolvedJson[actualKey] = value;
                        Logger.debug(`Using explicit value for inherited key ${key} -> ${actualKey} = ${value} for template ${templateKey}`, 'LoaderPreProcessJson');
                    } else {
                        const resolvedValue = this.SearchParentTreeForKeyPreProcessJson(actualKey, templateKey, siteTemplates.templates, parentMap);

                        if (resolvedValue !== null) {
                            resolvedJson[actualKey] = resolvedValue;
                            Logger.debug(`Resolved inherited key ${key} -> ${actualKey} = ${resolvedValue} for template ${templateKey}`, 'LoaderPreProcessJson');
                        } else {
                            resolvedJson[actualKey] = value;
                            Logger.debug(`No inherited value found for ${actualKey}, using default: ${value}`, 'LoaderPreProcessJson');
                        }
                    }
                } else {
                    resolvedJson[key] = value;
                }
            }

            if (hasInheritance) {
                template.jsonData = resolvedJson;
                Logger.debug(`Updated JsonData for template ${templateKey} with resolved inheritance`, 'LoaderPreProcessJson');
            }
        }
    }

    hasExplicitJsonValue(value) {
        if (value === null || value === undefined) return false;
        if (typeof value === 'string') return value.trim().length > 0;
        if (Array.isArray(value)) return value.length > 0;
        if (typeof value === 'object') return Object.keys(value).length > 0;
        return true;
    }

    RecreateJsonPlaceholderMappingsAfterInheritanceJson(siteTemplates) {
        for (const [templateKey, template] of siteTemplates.templates) {
            if (!template.jsonData) {
                continue;
            }

            const newMappings = [];
            for (const mapping of template.replacementMappings) {
                if (mapping.type !== ReplacementType.JsonPlaceholder) {
                    newMappings.push(mapping);
                }
            }

            template.replacementMappings = newMappings;
            this.createJsonPlaceholderReplacementMappings(template, template.originalContent);

            Logger.debug(`Recreated JSON placeholder mappings for template ${templateKey} after inheritance resolution`, 'LoaderPreProcessJson');
        }
    }

    SearchParentTreeForKeyPreProcessJson(key, currentTemplateKey, allTemplates, parentMap) {
        if (!parentMap.has(currentTemplateKey)) {
            Logger.debug(`No parent found for ${currentTemplateKey}`, 'LoaderPreProcessJson');
            return null;
        }

        const parentKey = parentMap.get(currentTemplateKey);
        Logger.debug(`Checking parent ${parentKey} for key ${key}`, 'LoaderPreProcessJson');

        if (!allTemplates.has(parentKey)) {
            Logger.debug(`Parent template ${parentKey} not found in templates`, 'LoaderPreProcessJson');
            return null;
        }

        const parentTemplate = allTemplates.get(parentKey);
        if (!parentTemplate.jsonData) {
            Logger.debug(`Parent template ${parentKey} has no JSON data, searching further up`, 'LoaderPreProcessJson');
            return this.SearchParentTreeForKeyPreProcessJson(key, parentKey, allTemplates, parentMap);
        }

        for (const [kvpKey, kvpValue] of Object.entries(parentTemplate.jsonData)) {
            if (kvpKey.toLowerCase() === key.toLowerCase()) {
                if (typeof kvpValue === 'string') {
                    Logger.debug(`Found key ${key} in parent ${parentKey}: ${kvpValue}`, 'LoaderPreProcessJson');
                    return kvpValue;
                }
            }
        }

        Logger.debug(`Key ${key} not found in parent ${parentKey}, searching further up`, 'LoaderPreProcessJson');
        return this.SearchParentTreeForKeyPreProcessJson(key, parentKey, allTemplates, parentMap);
    }

    createPlaceholderReplacementMappings(template, allTemplates, appSite) {
        if (!template.hasPlaceholders) return;

        template.placeholders.forEach(placeholder => {
            const targetTemplateKey = `${appSite.toLowerCase()}_${placeholder.templateKey}`;
            if (allTemplates.has(targetTemplateKey)) {
                const targetTemplate = allTemplates.get(targetTemplateKey);
                const processedTemplate = targetTemplate.originalContent;

                Logger.debug(`Creating replacement mapping: ${placeholder.fullMatch} -> ${placeholder.templateKey}`, 'LoaderPreProcessJson');
                template.replacementMappings.push(new ReplacementMapping(
                    placeholder.fullMatch,
                    processedTemplate,
                    ReplacementType.SimpleTemplate,
                    null,
                    null,
                    placeholder.templateKey
                ));
            }
        });
    }

    createSlottedTemplateReplacementMappings(template, allTemplates, appSite) {
        if (!template.hasSlottedTemplates) return;

        template.slottedTemplates.forEach(slottedTemplate => {
            const fullMatch = slottedTemplate.fullMatch;
            const targetTemplateKey = `${appSite.toLowerCase()}_${slottedTemplate.templateKey}`;

            if (allTemplates.has(targetTemplateKey)) {
                const targetTemplate = allTemplates.get(targetTemplateKey);
                let processedTemplate = targetTemplate.originalContent;

                slottedTemplate.slots.forEach(slot => {
                    const processedSlotContent = this.processSlotContentForReplacementMapping(slot, allTemplates, appSite);
                    processedTemplate = processedTemplate.replace(new RegExp(this.escapeRegExp(slot.slotKey), 'g'), processedSlotContent);
                });

                if (slottedTemplate.slots.length === 0) {
                    const actualInnerContent = slottedTemplate.innerContent;
                    if (actualInnerContent.trim()) {
                        const defaultSlotKey = '{{$HTMLPLACEHOLDER}}';
                        if (processedTemplate.includes(defaultSlotKey)) {
                            processedTemplate = processedTemplate.replace(new RegExp(this.escapeRegExp(defaultSlotKey), 'g'), actualInnerContent.trim());
                        }
                    }
                }

                processedTemplate = removeRemainingSlotPlaceholders(processedTemplate);

                Logger.debug(`Creating slotted replacement mapping: ${slottedTemplate.name} -> ${slottedTemplate.templateKey}`, 'LoaderPreProcessJson');
                template.replacementMappings.push(new ReplacementMapping(
                    fullMatch,
                    processedTemplate,
                    ReplacementType.SlottedTemplate,
                    null,
                    null,
                    slottedTemplate.templateKey
                ));
            }
        });
    }

    processSlotContentForReplacementMapping(slot, allTemplates, appSite) {
        let result = slot.content;

        slot.nestedSlottedTemplates.forEach(nestedSlottedTemplate => {
            const targetTemplateKey = `${appSite.toLowerCase()}_${nestedSlottedTemplate.templateKey}`;
            if (allTemplates.has(targetTemplateKey)) {
                const targetTemplate = allTemplates.get(targetTemplateKey);
                let processedTemplate = targetTemplate.originalContent;

                nestedSlottedTemplate.slots.forEach(nestedSlot => {
                    const processedNestedSlotContent = this.processSlotContentForReplacementMapping(nestedSlot, allTemplates, appSite);
                    processedTemplate = processedTemplate.replace(new RegExp(this.escapeRegExp(nestedSlot.slotKey), 'g'), processedNestedSlotContent);
                });

                processedTemplate = removeRemainingSlotPlaceholders(processedTemplate);
                result = result.replace(nestedSlottedTemplate.fullMatch, processedTemplate);
            }
        });

        return result;
    }

    parseSlottedTemplates(content, appSite, template) {
        let searchPos = 0;
        while (searchPos < content.length) {
            const openStart = content.indexOf('{{#', searchPos);
            if (openStart === -1) break;

            const openEnd = content.indexOf('}}', openStart + 3);
            if (openEnd === -1) break;

            const templateName = content.substring(openStart + 3, openEnd).trim();
            if (!templateName || !isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            const closeTag = `{{/${templateName}}}`;
            const closeStart = findMatchingCloseTag(content, openEnd + 2, `{{#${templateName}}}`, closeTag);
            if (closeStart === -1) {
                searchPos = openStart + 1;
                continue;
            }

            const innerStart = openEnd + 2;
            const innerContent = content.substring(innerStart, closeStart);
            const fullMatch = content.substring(openStart, closeStart + closeTag.length);

            const slottedTemplate = new SlottedTemplate(
                templateName,
                openStart,
                closeStart + closeTag.length,
                fullMatch,
                innerContent,
                templateName.toLowerCase()
            );

            this.parseSlots(innerContent, slottedTemplate, appSite);
            template.slottedTemplates.push(slottedTemplate);
            searchPos = closeStart + closeTag.length;
        }
    }

    parseSlots(innerContent, slottedTemplate, appSite) {
        let searchPos = 0;
        while (searchPos < innerContent.length) {
            const slotStart = innerContent.indexOf('{{@HTMLPLACEHOLDER', searchPos);
            if (slotStart === -1) break;

            const afterPlaceholder = slotStart + 18;
            let slotNum = '';
            let pos = afterPlaceholder;

            while (pos < innerContent.length && /\d/.test(innerContent[pos])) {
                slotNum += innerContent[pos];
                pos++;
            }

            if (pos + 1 >= innerContent.length || innerContent.substring(pos, pos + 2) !== '}}') {
                searchPos = slotStart + 1;
                continue;
            }

            const slotOpenEnd = pos + 2;
            const openTag = `{{@HTMLPLACEHOLDER${slotNum}}}`;
            const closeTag = `{{/HTMLPLACEHOLDER${slotNum}}}`;
            const closeStart = findMatchingCloseTag(innerContent, slotOpenEnd, openTag, closeTag);

            if (closeStart === -1) {
                searchPos = slotStart + 1;
                continue;
            }

            const slotContent = innerContent.substring(slotOpenEnd, closeStart);
            const slotKey = `{{$HTMLPLACEHOLDER${slotNum}}}`;

            const slot = new SlotPlaceholder(
                slotNum,
                slotStart,
                closeStart + closeTag.length,
                slotContent,
                slotKey,
                openTag,
                closeTag
            );

            this.parseNestedTemplatesInSlot(slot, slottedTemplate.jsonData, appSite);
            slottedTemplate.slots.push(slot);
            searchPos = closeStart + closeTag.length;
        }
    }

    parseNestedTemplatesInSlot(slot, jsonData, appSite) {
        if (!slot.content) return;
        const content = slot.content;
        let searchPos = 0;

        while (searchPos < content.length) {
            const openStart = content.indexOf('{{', searchPos);
            if (openStart === -1) break;

            if (openStart + 2 < content.length && ['#', '@', '$', '/'].includes(content[openStart + 2])) {
                searchPos = openStart + 2;
                continue;
            }

            const closeStart = content.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            const templateName = content.substring(openStart + 2, closeStart).trim();
            if (templateName && isAlphaNumeric(templateName)) {
                const templateKey = templateName.toLowerCase();
                slot.nestedPlaceholders.push(new TemplatePlaceholder(
                    templateName,
                    openStart,
                    closeStart + 2,
                    content.substring(openStart, closeStart + 2),
                    templateKey,
                    jsonData
                ));
            }
            searchPos = closeStart + 2;
        }

        searchPos = 0;
        while (searchPos < content.length) {
            const openStart = content.indexOf('{{#', searchPos);
            if (openStart === -1) break;

            const openEnd = content.indexOf('}}', openStart + 3);
            if (openEnd === -1) break;

            const templateName = content.substring(openStart + 3, openEnd).trim();
            if (!templateName || !isAlphaNumeric(templateName)) {
                searchPos = openStart + 1;
                continue;
            }

            const closeTag = `{{/${templateName}}}`;
            const openTag = `{{#${templateName}}}`;
            const closeStart = findMatchingCloseTag(content, openEnd + 2, openTag, closeTag);

            if (closeStart === -1) {
                searchPos = openStart + 1;
                continue;
            }

            const innerContent = content.substring(openEnd + 2, closeStart);
            const templateKey = templateName.toLowerCase();
            const nestedSlottedTemplate = new SlottedTemplate(
                templateName,
                openStart,
                closeStart + closeTag.length,
                content.substring(openStart, closeStart + closeTag.length),
                innerContent,
                templateKey,
                jsonData
            );

            this.parseSlots(innerContent, nestedSlottedTemplate, appSite);
            slot.nestedSlottedTemplates.push(nestedSlottedTemplate);
            searchPos = closeStart + closeTag.length;
        }
    }

    parsePlaceholderTemplates(content, appSite, template) {
        let searchPos = 0;
        while (searchPos < content.length) {
            const openStart = content.indexOf('{{', searchPos);
            if (openStart === -1) break;

            if (openStart + 2 < content.length && ['#', '@', '$', '/'].includes(content[openStart + 2])) {
                searchPos = openStart + 2;
                continue;
            }

            const closeStart = content.indexOf('}}', openStart + 2);
            if (closeStart === -1) break;

            const placeholderName = content.substring(openStart + 2, closeStart).trim();
            if (placeholderName && isAlphaNumeric(placeholderName)) {
                const placeholder = new TemplatePlaceholder(
                    placeholderName,
                    openStart,
                    closeStart + 2,
                    content.substring(openStart, closeStart + 2),
                    placeholderName.toLowerCase()
                );
                template.placeholders.push(placeholder);
            }
            searchPos = closeStart + 2;
        }
    }

    preprocessJsonTemplates(template) {
        if (!template.jsonData) return;
        const content = template.originalContent;
        this.createJsonArrayReplacementMappings(template, content);
        this.createJsonPlaceholderReplacementMappings(template, content);
    }

    createJsonArrayReplacementMappings(template, content) {
        if (!template.jsonData) {
            Logger.debug(`createJsonArrayReplacementMappings: template.jsonData is null for template ${template.templateKey}`, 'LoaderPreProcessJson');
            return;
        }

        for (const key in template.jsonData) {
            if (Array.isArray(template.jsonData[key])) {
                const dataList = template.jsonData[key];
                Logger.debug(`createJsonArrayReplacementMappings: Found array for key: ${key}, dataList length: ${dataList.length}`, 'LoaderPreProcessJson');

                const keyNorm = key.toLowerCase();
                const possibleTags = [key, keyNorm, keyNorm.endsWith('s') ? keyNorm.slice(0, -1) : keyNorm, keyNorm + 's'];

                for (const tag of possibleTags) {
                    const blockStartTag = `{{@${tag}}}`;
                    const blockEndTag = `{{/${tag}}}`;
                    const startIdx = content.toLowerCase().indexOf(blockStartTag.toLowerCase());

                    if (startIdx !== -1) {
                        Logger.debug(`createJsonArrayReplacementMappings: Found blockStartTag: ${blockStartTag} at index ${startIdx}`, 'LoaderPreProcessJson');
                        const endIdx = content.toLowerCase().indexOf(blockEndTag.toLowerCase(), startIdx + blockStartTag.length);
                        if (endIdx !== -1) {
                            Logger.debug(`createJsonArrayReplacementMappings: Found blockEndTag: ${blockEndTag} at index ${endIdx}`, 'LoaderPreProcessJson');
                            const blockContent = content.substring(startIdx + blockStartTag.length, endIdx);
                            const fullBlock = content.substring(startIdx, endIdx + blockEndTag.length);
                            const processedArrayContent = this.processArrayBlockContentSafely(blockContent, dataList);

                            template.replacementMappings.push(new ReplacementMapping(
                                fullBlock,
                                processedArrayContent,
                                ReplacementType.JsonPlaceholder,
                                startIdx,
                                endIdx + blockEndTag.length
                            ));
                            Logger.debug(`createJsonArrayReplacementMappings: Added replacement mapping for array key: ${key}`, 'LoaderPreProcessJson');

                            const emptyBlockStart = `{{^${tag}}}`;
                            const emptyStartIdx = content.toLowerCase().indexOf(emptyBlockStart.toLowerCase());
                            if (emptyStartIdx !== -1) {
                                const emptyEndIdx = content.toLowerCase().indexOf(blockEndTag.toLowerCase(), emptyStartIdx + emptyBlockStart.length);
                                if (emptyEndIdx !== -1) {
                                    const emptyBlockContent = content.substring(emptyStartIdx + emptyBlockStart.length, emptyEndIdx);
                                    const fullEmptyBlock = content.substring(emptyStartIdx, emptyEndIdx + blockEndTag.length);
                                    const emptyReplacement = dataList.length === 0 ? emptyBlockContent : '';
                                    template.replacementMappings.push(new ReplacementMapping(
                                        fullEmptyBlock,
                                        emptyReplacement,
                                        ReplacementType.JsonPlaceholder,
                                        emptyStartIdx,
                                        emptyEndIdx + blockEndTag.length
                                    ));
                                    Logger.debug(`createJsonArrayReplacementMappings: Added empty block replacement mapping for array key: ${key}`, 'LoaderPreProcessJson');
                                }
                            }
                            break;
                        } else {
                            Logger.debug(`createJsonArrayReplacementMappings: Did not find blockEndTag: ${blockEndTag}`, 'LoaderPreProcessJson');
                        }
                    } else {
                        Logger.debug(`createJsonArrayReplacementMappings: Did not find blockStartTag: ${blockStartTag}`, 'LoaderPreProcessJson');
                    }
                }
            } else {
                Logger.debug(`createJsonArrayReplacementMappings: Key ${key} is not an array. Type: ${typeof template.jsonData[key]}`, 'LoaderPreProcessJson');
            }
        }
    }

    createJsonPlaceholderReplacementMappings(template, content) {
        if (!template.jsonData) return;

        for (const key in template.jsonData) {
            if (typeof template.jsonData[key] === 'string') {
                const placeholder = `{{$${key}}}`;
                if (content.toLowerCase().includes(placeholder.toLowerCase())) {
                    template.replacementMappings.push(new ReplacementMapping(
                        placeholder,
                        template.jsonData[key],
                        ReplacementType.JsonPlaceholder
                    ));

                    if (!template.jsonPlaceholders.some(p => p.placeholder === placeholder)) {
                        template.jsonPlaceholders.push(new JsonPlaceholder(key, placeholder, template.jsonData[key]));
                    }
                }
            }
        }
    }

    processArrayBlockContentSafely(blockContent, arrayData) {
        try {
            let mergedBlock = '';
            for (const item of arrayData) {
                if (typeof item === 'object' && item !== null) {
                    let itemBlock = blockContent;
                    for (const key in item) {
                        const placeholder = `{{$${key}}}`;
                        const value = item[key] === null || item[key] === undefined ? '' : item[key];
                        itemBlock = this.replaceAllCaseInsensitive(itemBlock, placeholder, String(value));
                    }
                    itemBlock = this.processConditionalBlocksSafely(itemBlock, item);
                    mergedBlock += itemBlock;
                }
            }
            return mergedBlock;
        } catch (e) {
            return blockContent;
        }
    }

    processConditionalBlocksSafely(content, jsonItem) {
        try {
            let result = content;
            const conditionalKeys = this.findConditionalKeysInContent(result);
            conditionalKeys.forEach(condKey => {
                const condValue = this.getConditionValue(jsonItem, condKey);
                result = this.processConditionalBlockSafely(result, condKey, condValue);
            });
            return result;
        } catch (e) {
            return content;
        }
    }

    findConditionalKeysInContent(content) {
        const conditionalKeys = new Set();
        const regex = /{{@(\w+)}}/gi;
        let match;
        while ((match = regex.exec(content)) !== null) {
            conditionalKeys.add(match[1]);
        }
        return conditionalKeys;
    }

    getConditionValue(item, condKey) {
        const lowerCondKey = condKey.toLowerCase();
        for (const key in item) {
            if (key.toLowerCase() === lowerCondKey) {
                const val = item[key];
                if (typeof val === 'boolean') return val;
                if (typeof val === 'string') return val.toLowerCase() === 'true';
                if (typeof val === 'number') return val !== 0;
                return false;
            }
        }
        return false;
    }

    processConditionalBlockSafely(input, key, condition) {
        try {
            const tags = [
                { start: `{{@${key}}}`, end: `{{ /${key}}}` },
                { start: `{{@${key}}}`, end: `{{/${key}}}` }
            ];

            tags.forEach(tag => {
                let index = input.toLowerCase().indexOf(tag.start.toLowerCase());
                while (index !== -1) {
                    const endIndex = input.toLowerCase().indexOf(tag.end.toLowerCase(), index + tag.start.length);
                    if (endIndex === -1) break;

                    const content = input.substring(index + tag.start.length, endIndex);
                    if (condition) {
                        input = input.substring(0, index) + content + input.substring(endIndex + tag.end.length);
                        index += content.length;
                    } else {
                        input = input.substring(0, index) + input.substring(endIndex + tag.end.length);
                    }
                    index = input.toLowerCase().indexOf(tag.start.toLowerCase(), index);
                }
            });
            return input;
        } catch (e) {
            return input;
        }
    }

    replaceAllCaseInsensitive(input, search, replacement) {
        if (!search) return input;
        const searchLower = search.toLowerCase();
        let startIndex = 0;
        let result = '';
        while (startIndex < input.length) {
            const index = input.toLowerCase().indexOf(searchLower, startIndex);
            if (index === -1) {
                result += input.substring(startIndex);
                break;
            }
            result += input.substring(startIndex, index) + replacement;
            startIndex = index + search.length;
        }
        return result;
    }

    escapeRegExp(string) {
        return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
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
