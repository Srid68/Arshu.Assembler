// JSON Object implementation for Node.js
import { JsonArray } from './jsonArray.js';

export class JsonObject extends Map {
    constructor(data = null) {
        super();
        if (data) {
            if (typeof data === 'object' && !Array.isArray(data)) {
                for (const [key, value] of Object.entries(data)) {
                    this.set(key, value);
                }
            }
        }
    }

    /**
     * Compare this JsonObject with another
     * @param {JsonObject} toCompare - The object to compare with
     * @returns {boolean} True if objects are equal
     */
    compare(toCompare) {
        if (this.size !== toCompare.size) {
            return false;
        }

        for (const [key, value] of this) {
            if (!toCompare.has(key)) {
                return false;
            }

            const otherValue = toCompare.get(key);
            if (value !== otherValue) {
                if (typeof value === 'object' && typeof otherValue === 'object') {
                    if (JSON.stringify(value) !== JSON.stringify(otherValue)) {
                        return false;
                    }
                } else {
                    return false;
                }
            }
        }

        return true;
    }

    /**
     * Get value by key (case-insensitive)
     * @param {string} key - The key to look for
     * @returns {*} The value or undefined
     */
    getValue(key) {
        // First try exact match
        if (this.has(key)) {
            return this.get(key);
        }

        // Try case-insensitive search
        const lowerKey = key.toLowerCase();
        for (const [k, v] of this) {
            if (k.toLowerCase() === lowerKey) {
                return v;
            }
        }

        return undefined;
    }

    /**
     * Set value by key
     * @param {string} key - The key
     * @param {*} value - The value
     */
    setValue(key, value) {
        this.set(key, value);
    }

    /**
     * Check if key exists (case-insensitive)
     * @param {string} key - The key to check
     * @returns {boolean} True if key exists
     */
    hasKey(key) {
        // First try exact match
        if (this.has(key)) {
            return true;
        }

        // Try case-insensitive search
        const lowerKey = key.toLowerCase();
        for (const k of this.keys()) {
            if (k.toLowerCase() === lowerKey) {
                return true;
            }
        }

        return false;
    }

    /**
     * Convert to plain object
     * @returns {Object} Plain JavaScript object
     */
    toObject() {
        const result = {};
        for (const [key, value] of this) {
            if (value instanceof JsonObject) {
                result[key] = value.toObject();
            } else if (value instanceof JsonArray) {
                result[key] = value.toArray();
            } else {
                result[key] = value;
            }
        }
        return result;
    }
}