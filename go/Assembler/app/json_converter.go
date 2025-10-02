package app

import (
	"assembler/app/json"
	stdjson "encoding/json"
	"strconv"
)

// JsonConverter centralizes all JSON parsing and conversion logic
// Provides a unified interface for JSON operations, making it easy to swap out
// the underlying JSON library (currently using encoding/json, but could use others)
type JsonConverter struct{}

// ParseJsonString parses JSON string and returns a normalized JsonObject
// This is the unified entry point for all JSON parsing in the application
func ParseJsonString(jsonContent string) *json.JsonObject {
	// Parse using standard library
	var rawValue interface{}
	err := stdjson.Unmarshal([]byte(jsonContent), &rawValue)
	if err != nil {
		// Return empty JsonObject if JSON parsing fails
		return json.NewJsonObject()
	}

	// Convert to our JsonObject format
	if objectMap, ok := rawValue.(map[string]interface{}); ok {
		return normalizeJsonObject(objectMap)
	}

	// Non-object JSON becomes empty object
	return json.NewJsonObject()
}

// ParseJsonStringAsJsonValues parses JSON string and returns JsonObject with JsonValue types
// This provides type-safe access similar to the Rust implementation
func ParseJsonStringAsJsonValues(jsonContent string) *json.JsonObject {
	// For now, both functions work the same way since we always use JsonValue internally
	// This separation allows for future implementation differences if needed
	return ParseJsonString(jsonContent)
}

// normalizeJsonObject converts map[string]interface{} to uniform JsonObject for consistent processing
func normalizeJsonObject(rawMap map[string]interface{}) *json.JsonObject {
	jsonObject := json.NewJsonObject()

	for key, value := range rawMap {
		convertedValue := convertJsonValue(value)
		jsonObject.Set(key, convertedValue)
	}

	return jsonObject
}

// convertJsonValue converts any interface{} value to appropriate JsonValue type
func convertJsonValue(value interface{}) json.JsonValue {
	switch v := value.(type) {
	case string:
		return json.JsonValue{
			Kind:   json.JsonString,
			StrVal: v,
		}
	case float64:
		// JSON numbers are always float64 in Go's encoding/json
		// Check if it's actually an integer
		if v == float64(int64(v)) {
			return json.JsonValue{
				Kind:   json.JsonInteger,
				IntVal: int64(v),
			}
		}
		return json.JsonValue{
			Kind:   json.JsonNumber,
			NumVal: v,
		}
	case bool:
		return json.JsonValue{
			Kind:    json.JsonBool,
			BoolVal: v,
		}
	case []interface{}:
		jsonArray := json.NewJsonArray()
		for _, item := range v {
			convertedItem := convertJsonValue(item)
			jsonArray.Add(convertedItem)
		}
		return json.JsonValue{
			Kind:   json.JsonArrayKind,
			ArrVal: jsonArray,
		}
	case map[string]interface{}:
		jsonObject := normalizeJsonObject(v)
		return json.JsonValue{
			Kind:   json.JsonObjectKind,
			ObjVal: jsonObject,
		}
	case nil:
		return json.JsonValue{
			Kind: json.JsonNull,
		}
	default:
		// Fallback to string representation
		return json.JsonValue{
			Kind:   json.JsonString,
			StrVal: toString(v),
		}
	}
}

// Helper method to get a string value from JsonObject with fallback
func GetStringValue(jsonObject *json.JsonObject, key string) string {
	if value, exists := jsonObject.Get(key); exists {
		if value.Kind == json.JsonString {
			return value.StrVal
		}
		return value.String()
	}
	return ""
}

// Helper method to check if JsonObject contains a key with a non-empty string value
func HasNonEmptyString(jsonObject *json.JsonObject, key string) bool {
	stringValue := GetStringValue(jsonObject, key)
	return stringValue != ""
}

// toString converts any value to string, similar to other implementations
func toString(value interface{}) string {
	switch v := value.(type) {
	case string:
		return v
	case int:
		return strconv.Itoa(v)
	case int64:
		return strconv.FormatInt(v, 10)
	case float64:
		return strconv.FormatFloat(v, 'g', -1, 64)
	case bool:
		return strconv.FormatBool(v)
	default:
		// Use JSON marshaling as fallback
		if bytes, err := stdjson.Marshal(v); err == nil {
			return string(bytes)
		}
		return ""
	}
}

// SerializeToJSON serializes any serializable object to JSON string
func SerializeToJSON(obj interface{}, indented bool) (string, error) {
	var b []byte
	var err error
	if indented {
		b, err = stdjson.MarshalIndent(obj, "", "  ")
	} else {
		b, err = stdjson.Marshal(obj)
	}
	return string(b), err
}
