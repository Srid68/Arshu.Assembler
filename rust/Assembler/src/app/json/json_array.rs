use super::json_object::{JsonValue, JsonObject};
use serde::{Deserialize, Serialize};

/// Uniform JsonArray type for consistent JSON array handling across the application
/// This provides a standard interface that matches the C# JsonArray implementation
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct JsonArray {
    data: Vec<JsonValue>,
}

impl JsonArray {
    pub fn new() -> Self {
        Self {
            data: Vec::new(),
        }
    }

    /// Adds an item to the end of the JsonArray
    pub fn push(&mut self, value: JsonValue) {
        self.data.push(value);
    }

    /// Gets an item by index
    pub fn get(&self, index: usize) -> Option<&JsonValue> {
        self.data.get(index)
    }

    /// Gets a mutable reference to an item by index
    pub fn get_mut(&mut self, index: usize) -> Option<&mut JsonValue> {
        self.data.get_mut(index)
    }

    /// Gets a string value by index
    pub fn get_string(&self, index: usize) -> Option<&String> {
        match self.get(index)? {
            JsonValue::String(s) => Some(s),
            _ => None,
        }
    }

    /// Gets an integer value by index
    pub fn get_i64(&self, index: usize) -> Option<i64> {
        match self.get(index)? {
            JsonValue::Integer(i) => Some(*i),
            JsonValue::Number(f) => Some(*f as i64),
            _ => None,
        }
    }

    /// Gets a float value by index
    pub fn get_f64(&self, index: usize) -> Option<f64> {
        match self.get(index)? {
            JsonValue::Number(f) => Some(*f),
            JsonValue::Integer(i) => Some(*i as f64),
            _ => None,
        }
    }

    /// Gets a boolean value by index
    pub fn get_bool(&self, index: usize) -> Option<bool> {
        match self.get(index)? {
            JsonValue::Bool(b) => Some(*b),
            _ => None,
        }
    }

    /// Gets an array value by index
    pub fn get_array(&self, index: usize) -> Option<&JsonArray> {
        match self.get(index)? {
            JsonValue::Array(arr) => Some(arr),
            _ => None,
        }
    }

    /// Gets an object value by index
    pub fn get_object(&self, index: usize) -> Option<&JsonObject> {
        match self.get(index)? {
            JsonValue::Object(obj) => Some(obj),
            _ => None,
        }
    }

    /// Returns the number of items in the array
    pub fn len(&self) -> usize {
        self.data.len()
    }

    /// Checks if the JsonArray is empty
    pub fn is_empty(&self) -> bool {
        self.data.is_empty()
    }

    /// Removes and returns the last item
    pub fn pop(&mut self) -> Option<JsonValue> {
        self.data.pop()
    }

    /// Inserts an item at the specified index
    pub fn insert(&mut self, index: usize, value: JsonValue) {
        self.data.insert(index, value);
    }

    /// Removes an item at the specified index
    pub fn remove(&mut self, index: usize) -> JsonValue {
        self.data.remove(index)
    }

    /// Clears all items
    pub fn clear(&mut self) {
        self.data.clear();
    }

    /// Returns an iterator over references to the items
    pub fn iter(&self) -> impl Iterator<Item = &JsonValue> {
        self.data.iter()
    }

    /// Returns a mutable iterator over references to the items
    pub fn iter_mut(&mut self) -> impl Iterator<Item = &mut JsonValue> {
        self.data.iter_mut()
    }
}

impl Default for JsonArray {
    fn default() -> Self {
        Self::new()
    }
}
