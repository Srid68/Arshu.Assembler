package json

// JsonArray is a slice of JsonValue, matching Rust/C# structure
type JsonArray []JsonValue

func NewJsonArray() *JsonArray {
	arr := make(JsonArray, 0)
	return &arr
}

func (ja *JsonArray) Add(value JsonValue) {
	*ja = append(*ja, value)
}

func (ja *JsonArray) Get(index int) (JsonValue, bool) {
	if index < 0 || index >= len(*ja) {
		return JsonValue{Kind: JsonNull}, false
	}
	return (*ja)[index], true
}

func (ja *JsonArray) Len() int {
	return len(*ja)
}

func (ja *JsonArray) Clear() {
	*ja = make(JsonArray, 0)
}
