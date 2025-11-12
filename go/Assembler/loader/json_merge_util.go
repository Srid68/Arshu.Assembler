package loader

import (
	"assembler/app/json"
	"assembler/common"
	"fmt"
)

// MergeTemplateWithJson merges an HTML template with JSON data.
// Takes a JsonObject (matching C# signature) and converts it to map[string]interface{} internally
func MergeTemplateWithJson(template string, jsonObject *json.JsonObject) string {
	if jsonObject == nil {
		return template
	}

	// Convert JsonObject to map[string]interface{} for compatibility with common.MergeTemplateWithJson
	dict := make(map[string]interface{})
	for key, value := range jsonObject.Iter() {
		dict[key] = ConvertJsonValueToInterface(value)
	}

	return common.MergeTemplateWithJson(template, dict)
}

// ConvertMapToJsonObject converts a map[string]interface{} to JsonObject
func ConvertMapToJsonObject(data map[string]interface{}) *json.JsonObject {
	jsonObj := json.NewJsonObject()
	for key, value := range data {
		jsonValue := convertInterfaceToJsonValue(value)
		jsonObj.Set(key, jsonValue)
	}
	return jsonObj
}

// convertInterfaceToJsonValue converts interface{} to JsonValue
func convertInterfaceToJsonValue(value interface{}) json.JsonValue {
	switch v := value.(type) {
	case string:
		return json.JsonValue{Kind: json.JsonString, StrVal: v}
	case float64:
		if v == float64(int64(v)) {
			return json.JsonValue{Kind: json.JsonInteger, IntVal: int64(v)}
		}
		return json.JsonValue{Kind: json.JsonNumber, NumVal: v}
	case int:
		return json.JsonValue{Kind: json.JsonInteger, IntVal: int64(v)}
	case int64:
		return json.JsonValue{Kind: json.JsonInteger, IntVal: v}
	case bool:
		return json.JsonValue{Kind: json.JsonBool, BoolVal: v}
	case []interface{}:
		jsonArray := json.NewJsonArray()
		for _, item := range v {
			jsonArray.Add(convertInterfaceToJsonValue(item))
		}
		return json.JsonValue{Kind: json.JsonArrayKind, ArrVal: jsonArray}
	case map[string]interface{}:
		return json.JsonValue{Kind: json.JsonObjectKind, ObjVal: ConvertMapToJsonObject(v)}
	case nil:
		return json.JsonValue{Kind: json.JsonNull}
	default:
		return json.JsonValue{Kind: json.JsonString, StrVal: fmt.Sprint(v)}
	}
}

// ConvertJsonValueToInterface converts a JsonValue to interface{} for use with existing merge logic
func ConvertJsonValueToInterface(value json.JsonValue) interface{} {
	switch value.Kind {
	case json.JsonNull:
		return nil
	case json.JsonString:
		return value.StrVal
	case json.JsonNumber:
		return value.NumVal
	case json.JsonInteger:
		return value.IntVal
	case json.JsonBool:
		return value.BoolVal
	case json.JsonArrayKind:
		if value.ArrVal == nil {
			return []interface{}{}
		}
		arr := make([]interface{}, 0)
		for _, item := range *value.ArrVal {
			arr = append(arr, ConvertJsonValueToInterface(item))
		}
		return arr
	case json.JsonObjectKind:
		if value.ObjVal == nil {
			return map[string]interface{}{}
		}
		obj := make(map[string]interface{})
		for k, v := range value.ObjVal.Iter() {
			obj[k] = ConvertJsonValueToInterface(v)
		}
		return obj
	default:
		return nil
	}
}