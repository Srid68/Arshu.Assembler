// JSON Converter for parsing and normalizing JSON data

import { JsonObject } from './json/jsonObject.js';
import { JsonArray } from './json/jsonArray.js';

export class JsonConverter {
    /**
     * Parses JSON string and returns a normalized JsonObject
     * @param {string} jsonContent - The JSON content to parse
     * @returns {JsonObject} JsonObject containing parsed JSON data
     */
    static parseJsonString(jsonContent) {
        try {
            if (!jsonContent || typeof jsonContent !== 'string') {
                return new JsonObject();
            }

            const parsed = JSON.parse(jsonContent);
            return JsonConverter.normalizeJsonObject(parsed);
        } catch (error) {
            // Return empty JsonObject if JSON parsing fails
            return new JsonObject();
        }
    }

    /**
     * Converts plain JavaScript object to JsonObject
     * @param {Object} obj - The object to normalize
     * @returns {JsonObject} Normalized JsonObject
     */
    static normalizeJsonObject(obj) {
        if (!obj || typeof obj !== 'object') {
            return new JsonObject();
        }

        if (Array.isArray(obj)) {
            return JsonConverter.normalizeJsonArray(obj);
        }

        const jsonObject = new JsonObject();
        
        for (const [key, value] of Object.entries(obj)) {
            const convertedValue = JsonConverter.convertValue(value);
            jsonObject.set(key, convertedValue);
        }
        
        return jsonObject;
    }

    /**
     * Converts plain JavaScript array to JsonArray
     * @param {Array} arr - The array to normalize
     * @returns {JsonArray} Normalized JsonArray
     */
    static normalizeJsonArray(arr) {
        if (!Array.isArray(arr)) {
            return new JsonArray();
        }

        const jsonArray = new JsonArray();
        
        for (const item of arr) {
            const convertedValue = JsonConverter.convertValue(item);
            jsonArray.push(convertedValue);
        }
        
        return jsonArray;
    }

    /**
     * Converts any JavaScript value to appropriate JSON type
     * @param {*} value - The value to convert
     * @returns {*} Converted value
     */
    static convertValue(value) {
        if (value === null || value === undefined) {
            return null;
        }

        if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
            return value;
        }

        if (Array.isArray(value)) {
            return JsonConverter.normalizeJsonArray(value);
        }

        if (typeof value === 'object') {
            return JsonConverter.normalizeJsonObject(value);
        }

        return value;
    }

    /**
     * Converts JsonObject back to plain JavaScript object
     * @param {JsonObject|JsonArray|*} value - The value to convert
     * @returns {*} Plain JavaScript value
     */
    static toPlainObject(value) {
        if (value instanceof JsonObject) {
            return value.toObject();
        }

        if (value instanceof JsonArray) {
            return value.toArray();
        }

        if (Array.isArray(value)) {
            return value.map(item => JsonConverter.toPlainObject(item));
        }

        if (value && typeof value === 'object') {
            const result = {};
            for (const [key, val] of Object.entries(value)) {
                result[key] = JsonConverter.toPlainObject(val);
            }
            return result;
        }

        return value;
    }
}