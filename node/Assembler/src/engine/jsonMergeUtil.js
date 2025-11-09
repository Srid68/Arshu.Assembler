import { Logger } from '@arshu/arshu/logger';

/**
 * Replaces all occurrences of a search string with a replacement string, case-insensitively.
 * @param {string} input The string to search within.
 * @param {string} search The string to search for.
 * @param {string} replacement The string to replace with.
 * @returns {string} The modified string.
 */
function replaceAllCaseInsensitive(input, search, replacement) {
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

/**
 * Handles conditional blocks like {{@Selected}}...{{/Selected}}.
 * @param {string} input The template string.
 * @param {string} key The conditional key.
 * @param {boolean} condition The condition to evaluate.
 * @returns {string} The modified template string.
 */
function handleConditional(input, key, condition) {
    const condStart = `{{@${key}}}`;
    // Support spaces in closing tag, e.g., {{ /Selected}}
    const condEndWithSpace = `{{ /${key}}}`;
    const condEndWithoutSpace = `{{/${key}}}`;

    let result = input;

    const processTag = (endTag) => {
        let index = result.toLowerCase().indexOf(condStart.toLowerCase());
        while (index !== -1) {
            const endIndex = result.toLowerCase().indexOf(endTag.toLowerCase(), index + condStart.length);
            if (endIndex === -1) break;

            const content = result.substring(index + condStart.length, endIndex);
            if (condition) {
                result = result.substring(0, index) + content + result.substring(endIndex + endTag.length);
                // Move index past the inserted content to re-evaluate from there
                index = index + content.length;
            } else {
                result = result.substring(0, index) + result.substring(endIndex + endTag.length);
                // After removal, re-start search from the point of removal
                index = index;
            }
        }
    };

    processTag(condEndWithSpace);
    processTag(condEndWithoutSpace);

    return result;
}


/**
 * Merges an HTML template with a JSON object.
 * @param {string} template The HTML template content.
 * @param {object} jsonObject The JSON object.
 * @returns {string} The merged HTML.
 */
function mergeTemplateWithJson(template, jsonObject) {
    if (!template || !jsonObject) {
        return template;
    }

    let result = template;

    // Process arrays: {{@array}}...{{/array}}
    for (const key in jsonObject) {
        if (Array.isArray(jsonObject[key])) {
            const dataList = jsonObject[key];
            const blockStartTag = `{{@${key}}}`;
            const blockEndTag = `{{/${key}}}`;
            const emptyBlockStartTag = `{{^${key}}}`;

            const blockStartIndex = result.toLowerCase().indexOf(blockStartTag.toLowerCase());
            const emptyBlockStartIndex = result.toLowerCase().indexOf(emptyBlockStartTag.toLowerCase());

            if (blockStartIndex !== -1) {
                const blockEndIndex = result.toLowerCase().indexOf(blockEndTag.toLowerCase(), blockStartIndex + blockStartTag.length);
                if (blockEndIndex !== -1) {
                    const blockContent = result.substring(blockStartIndex + blockStartTag.length, blockEndIndex);
                    let mergedBlock = '';

                    const conditionalKeys = new Set();
                    const regex = /{{@(\w+)}}/g;
                    let match;
                    while ((match = regex.exec(blockContent)) !== null) {
                        conditionalKeys.add(match[1]);
                    }

                    for (const item of dataList) {
                        let itemBlock = blockContent;
                        for (const itemKey in item) {
                            const placeholder = `{{$${itemKey}}}`;
                            const value = item[itemKey] === null || item[itemKey] === undefined ? '' : item[itemKey];
                            itemBlock = replaceAllCaseInsensitive(itemBlock, placeholder, String(value));
                        }

                        conditionalKeys.forEach(condKey => {
                            let condValue = false;
                            if (item.hasOwnProperty(condKey)) {
                                const val = item[condKey];
                                if (typeof val === 'boolean') {
                                    condValue = val;
                                } else if (typeof val === 'string') {
                                    condValue = val.toLowerCase() === 'true';
                                } else if (typeof val === 'number') {
                                    condValue = val !== 0;
                                }
                            }
                            itemBlock = handleConditional(itemBlock, condKey, condValue);
                        });

                        mergedBlock += itemBlock;
                    }
                    result = result.substring(0, blockStartIndex) + mergedBlock + result.substring(blockEndIndex + blockEndTag.length);
                }
            }

            // Handle empty array blocks: {{^array}}...{{/array}}
            if (emptyBlockStartIndex !== -1) {
                 const emptyBlockEndIndex = result.toLowerCase().indexOf(blockEndTag.toLowerCase(), emptyBlockStartIndex + emptyBlockStartTag.length);
                 if (emptyBlockEndIndex !== -1) {
                    if (dataList.length === 0) {
                        const emptyContent = result.substring(emptyBlockStartIndex + emptyBlockStartTag.length, emptyBlockEndIndex);
                        result = result.substring(0, emptyBlockStartIndex) + emptyContent + result.substring(emptyBlockEndIndex + blockEndTag.length);
                    } else {
                        result = result.substring(0, emptyBlockStartIndex) + result.substring(emptyBlockEndIndex + blockEndTag.length);
                    }
                 }
            }
        }
    }

    // Replace simple placeholders: {{$key}}
    for (const key in jsonObject) {
        if (!Array.isArray(jsonObject[key])) {
            const placeholder = `{{$${key}}}`;
            const value = jsonObject[key] === null || jsonObject[key] === undefined ? '' : jsonObject[key];
            result = replaceAllCaseInsensitive(result, placeholder, String(value));
        }
    }

    return result;
}

export {
    mergeTemplateWithJson,
};