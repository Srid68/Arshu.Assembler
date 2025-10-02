package json

import "fmt"

// JsonKind is the type of value stored in JsonValue
type JsonKind int

const (
	JsonNull JsonKind = iota
	JsonString
	JsonNumber
	JsonInteger
	JsonBool
	JsonArrayKind
	JsonObjectKind
)

// JsonValue is an enum-like type for all JSON-compatible values.
type JsonValue struct {
	Kind    JsonKind
	StrVal  string
	NumVal  float64
	IntVal  int64
	BoolVal bool
	ArrVal  *JsonArray
	ObjVal  *JsonObject
}

func (v JsonValue) String() string {
	switch v.Kind {
	case JsonNull:
		return "null"
	case JsonString:
		return v.StrVal
	case JsonNumber:
		return fmt.Sprintf("%f", v.NumVal)
	case JsonInteger:
		return fmt.Sprintf("%d", v.IntVal)
	case JsonBool:
		return fmt.Sprintf("%t", v.BoolVal)
	case JsonArrayKind:
		return "[Array]"
	case JsonObjectKind:
		return "[Object]"
	default:
		return "unknown"
	}
}

// JsonObject provides a map of string to JsonValue, similar to C# and Rust.
type JsonObject struct {
	data map[string]JsonValue
}

func NewJsonObject() *JsonObject {
	return &JsonObject{data: make(map[string]JsonValue)}
}

func (jo *JsonObject) Set(key string, value JsonValue) {
	jo.data[key] = value
}

func (jo *JsonObject) Get(key string) (JsonValue, bool) {
	val, ok := jo.data[key]
	return val, ok
}

func (jo *JsonObject) Remove(key string) {
	delete(jo.data, key)
}

func (jo *JsonObject) Contains(key string) bool {
	_, ok := jo.data[key]
	return ok
}

func (jo *JsonObject) Len() int {
	return len(jo.data)
}

func (jo *JsonObject) Keys() []string {
	keys := make([]string, 0, len(jo.data))
	for k := range jo.data {
		keys = append(keys, k)
	}
	return keys
}

func (jo *JsonObject) Clear() {
	jo.data = make(map[string]JsonValue)
}

// Iter provides iteration over key-value pairs, similar to Rust implementation
func (jo *JsonObject) Iter() map[string]JsonValue {
	// Return a copy to prevent external modification
	result := make(map[string]JsonValue)
	for k, v := range jo.data {
		result[k] = v
	}
	return result
}
