use std::collections::HashMap;
use serde::{Deserialize, Serialize};
use std::fmt;

/// Unified JSON value type that can hold any JSON-compatible value
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum JsonValue {
    String(String),
    Number(f64),
    Integer(i64),
    Bool(bool),
    Array(JsonArray),
    Object(JsonObject),
    Null,
}

impl fmt::Display for JsonValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            JsonValue::String(s) => write!(f, "{}", s),
            JsonValue::Number(n) => write!(f, "{}", n),
            JsonValue::Integer(i) => write!(f, "{}", i),
            JsonValue::Bool(b) => write!(f, "{}", b),
            JsonValue::Array(_) => write!(f, "[Array]"),
            JsonValue::Object(_) => write!(f, "[Object]"),
            JsonValue::Null => write!(f, "null"),
        }
    }
}

impl JsonValue {
    /// Returns the value as an object if it's an object
    pub fn as_object(&self) -> Option<&JsonObject> {
        match self {
            JsonValue::Object(obj) => Some(obj),
            _ => None,
        }
    }

    /// Returns the value as an array if it's an array
    pub fn as_array(&self) -> Option<&JsonArray> {
        match self {
            JsonValue::Array(arr) => Some(arr),
            _ => None,
        }
    }

    /// Returns the value as a string if it's a string
    pub fn as_str(&self) -> Option<&str> {
        match self {
            JsonValue::String(s) => Some(s),
            _ => None,
        }
    }

    /// Returns the value as a boolean if it's a boolean
    pub fn as_bool(&self) -> Option<bool> {
        match self {
            JsonValue::Bool(b) => Some(*b),
            _ => None,
        }
    }

    /// Returns the value as an integer if it's an integer
    pub fn as_i64(&self) -> Option<i64> {
        match self {
            JsonValue::Integer(i) => Some(*i),
            JsonValue::Number(f) => Some(*f as i64),
            _ => None,
        }
    }

    /// Returns the value as a float if it's a number
    pub fn as_f64(&self) -> Option<f64> {
        match self {
            JsonValue::Number(f) => Some(*f),
            JsonValue::Integer(i) => Some(*i as f64),
            _ => None,
        }
    }

    /// Checks if the value is null
    pub fn is_null(&self) -> bool {
        matches!(self, JsonValue::Null)
    }
}

/// Uniform JsonObject type for consistent JSON object handling across the application
/// This provides a standard interface that matches the C# JsonObject implementation
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct JsonObject {
    data: HashMap<String, JsonValue>,
}

impl JsonObject {
    pub fn new() -> Self {
        Self {
            data: HashMap::new(),
        }
    }

    /// Inserts a key-value pair into the JsonObject
    pub fn insert(&mut self, key: String, value: JsonValue) {
        self.data.insert(key, value);
    }

    /// Gets a value by key
    pub fn get(&self, key: &str) -> Option<&JsonValue> {
        self.data.get(key)
    }

    /// Gets a mutable reference to a value by key
    pub fn get_mut(&mut self, key: &str) -> Option<&mut JsonValue> {
        self.data.get_mut(key)
    }

    /// Gets a string value by key
    pub fn get_string(&self, key: &str) -> Option<&String> {
        match self.get(key)? {
            JsonValue::String(s) => Some(s),
            _ => None,
        }
    }

    /// Gets an integer value by key
    pub fn get_i64(&self, key: &str) -> Option<i64> {
        match self.get(key)? {
            JsonValue::Integer(i) => Some(*i),
            JsonValue::Number(f) => Some(*f as i64),
            _ => None,
        }
    }

    /// Gets a float value by key
    pub fn get_f64(&self, key: &str) -> Option<f64> {
        match self.get(key)? {
            JsonValue::Number(f) => Some(*f),
            JsonValue::Integer(i) => Some(*i as f64),
            _ => None,
        }
    }

    /// Gets a boolean value by key
    pub fn get_bool(&self, key: &str) -> Option<bool> {
        match self.get(key)? {
            JsonValue::Bool(b) => Some(*b),
            _ => None,
        }
    }

    /// Gets an array value by key
    pub fn get_array(&self, key: &str) -> Option<&JsonArray> {
        match self.get(key)? {
            JsonValue::Array(arr) => Some(arr),
            _ => None,
        }
    }

    /// Gets an object value by key
    pub fn get_object(&self, key: &str) -> Option<&JsonObject> {
        match self.get(key)? {
            JsonValue::Object(obj) => Some(obj),
            _ => None,
        }
    }

    /// Checks if the JsonObject contains a key
    pub fn contains_key(&self, key: &str) -> bool {
        self.data.contains_key(key)
    }

    /// Returns an iterator over the keys
    pub fn keys(&self) -> impl Iterator<Item = &String> {
        self.data.keys()
    }

    /// Returns an iterator over key-value pairs
    pub fn iter(&self) -> impl Iterator<Item = (&String, &JsonValue)> {
        self.data.iter()
    }

    /// Returns the number of key-value pairs
    pub fn len(&self) -> usize {
        self.data.len()
    }

    /// Checks if the JsonObject is empty
    pub fn is_empty(&self) -> bool {
        self.data.is_empty()
    }

    /// Removes a key-value pair and returns the value if it existed
    pub fn remove(&mut self, key: &str) -> Option<JsonValue> {
        self.data.remove(key)
    }

    /// Clears all key-value pairs
    pub fn clear(&mut self) {
        self.data.clear();
    }

    /// Returns a reference to the internal HashMap for compatibility
    pub fn as_object(&self) -> Option<&HashMap<String, JsonValue>> {
        Some(&self.data)
    }
}

impl Default for JsonObject {
    fn default() -> Self {
        Self::new()
    }
}

// Forward declaration for JsonArray (will be defined in json_array.rs)
pub use super::json_array::JsonArray;
