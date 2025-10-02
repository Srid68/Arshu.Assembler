// JSON Array implementation for Node.js

export class JsonArray extends Array {
    constructor(capacity = null) {
        super();
        if (capacity && typeof capacity === 'number') {
            // Pre-allocate array
            this.length = 0;
        }
    }

    /**
     * Add item to array
     * @param {*} item - Item to add
     */
    add(item) {
        this.push(item);
    }

    /**
     * Get item at index
     * @param {number} index - The index
     * @returns {*} The item or undefined
     */
    get(index) {
        return this[index];
    }

    /**
     * Set item at index
     * @param {number} index - The index
     * @param {*} item - The item to set
     */
    set(index, item) {
        this[index] = item;
    }

    /**
     * Get the size of the array
     * @returns {number} Array length
     */
    size() {
        return this.length;
    }

    /**
     * Check if array is empty
     * @returns {boolean} True if empty
     */
    isEmpty() {
        return this.length === 0;
    }

    /**
     * Convert to plain array
     * @returns {Array} Plain JavaScript array
     */
    toArray() {
        return this.map(item => {
            if (item && typeof item.toObject === 'function') {
                return item.toObject();
            } else if (item && typeof item.toArray === 'function') {
                return item.toArray();
            }
            return item;
        });
    }
}

// Generic JsonArray class
export class GenericJsonArray extends Array {
    constructor(capacity = null) {
        super();
        if (capacity && typeof capacity === 'number') {
            this.length = 0;
        }
    }
}