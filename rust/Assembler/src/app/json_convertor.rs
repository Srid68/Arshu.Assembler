use crate::app::json::{JsonObject, JsonArray, JsonValue};
use serde_json::Value;

/// Converts between serde_json::Value and uniform JSON types for normalization
/// Centralizes all JSON parsing and conversion logic to match C# JsonConverter functionality
pub struct JsonConverter;

impl JsonConverter {
    /// Parses JSON string and returns a normalized JsonObject
    /// This is the unified entry point for all JSON parsing in the application
    pub fn parse_json_string(json_content: &str) -> JsonObject {
        match serde_json::from_str::<Value>(json_content) {
            Ok(Value::Object(map)) => Self::normalize_json_object(map),
            Ok(_) => JsonObject::new(), // Non-object JSON becomes empty object
            Err(_) => JsonObject::new(), // Parse error becomes empty object
        }
    }

    /// Converts serde_json::Map to uniform JsonObject for consistent processing
    pub fn normalize_json_object(serde_map: serde_json::Map<String, Value>) -> JsonObject {
        let mut json_object = JsonObject::new();
        
        for (key, value) in serde_map {
            let converted_value = Self::convert_json_value(value);
            json_object.insert(key, converted_value);
        }
        
        json_object
    }

    /// Converts any serde_json::Value to appropriate uniform type
    fn convert_json_value(value: Value) -> JsonValue {
        match value {
            Value::String(s) => JsonValue::String(s),
            Value::Number(n) => {
                if let Some(i) = n.as_i64() {
                    JsonValue::Integer(i)
                } else if let Some(f) = n.as_f64() {
                    JsonValue::Number(f)
                } else {
                    JsonValue::String(n.to_string())
                }
            },
            Value::Bool(b) => JsonValue::Bool(b),
            Value::Array(arr) => {
                let mut json_array = JsonArray::new();
                for item in arr {
                    let converted_item = Self::convert_json_value(item);
                    json_array.push(converted_item);
                }
                JsonValue::Array(json_array)
            },
            Value::Object(map) => {
                let json_object = Self::normalize_json_object(map);
                JsonValue::Object(json_object)
            },
            Value::Null => JsonValue::Null,
        }
    }

    /// Converts JsonObject back to serde_json::Value for compatibility with existing code
    pub fn to_serde_value(json_object: &JsonObject) -> Value {
        let mut map = serde_json::Map::new();
        
        for (key, value) in json_object.iter() {
            let serde_value = Self::json_value_to_serde(value);
            map.insert(key.clone(), serde_value);
        }
        
        Value::Object(map)
    }

    /// Converts JsonValue to serde_json::Value
    fn json_value_to_serde(value: &JsonValue) -> Value {
        match value {
            JsonValue::String(s) => Value::String(s.clone()),
            JsonValue::Integer(i) => Value::Number(serde_json::Number::from(*i)),
            JsonValue::Number(f) => {
                if let Some(num) = serde_json::Number::from_f64(*f) {
                    Value::Number(num)
                } else {
                    Value::Null
                }
            },
            JsonValue::Bool(b) => Value::Bool(*b),
            JsonValue::Array(arr) => {
                let serde_arr: Vec<Value> = arr.iter()
                    .map(|item| Self::json_value_to_serde(item))
                    .collect();
                Value::Array(serde_arr)
            },
            JsonValue::Object(obj) => Self::to_serde_value(obj),
            JsonValue::Null => Value::Null,
        }
    }

    /// Helper method to get a string value from JsonObject with fallback
    pub fn get_string_value(json_object: &JsonObject, key: &str) -> String {
        json_object.get_string(key)
            .map(|s| s.clone())
            .unwrap_or_default()
    }

    /// Helper method to check if JsonObject contains a key with a non-empty string value
    pub fn has_non_empty_string(json_object: &JsonObject, key: &str) -> bool {
        json_object.get_string(key)
            .map(|s| !s.is_empty())
            .unwrap_or(false)
    }

    /// Serializes any serializable object to pretty-printed JSON string
    pub fn to_pretty_json<T: serde::Serialize>(obj: &T) -> String {
        serde_json::to_string_pretty(obj).unwrap_or_else(|_| "{}".to_string())
    }

    /// Serializes any serializable object to compact JSON string
    pub fn to_json<T: serde::Serialize>(obj: &T) -> String {
        serde_json::to_string(obj).unwrap_or_else(|_| "{}".to_string())
    }


    /// Creates a JSON value from basic types
    pub fn create_json_value(value: Value) -> Value {
        value
    }
}
