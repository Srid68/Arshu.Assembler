#nullable enable

using System;

namespace Arshu.App.Json
{
    /// <summary>
    /// Enum representing different JSON value types, similar to Rust JsonValue enum
    /// </summary>
    public enum JsonValueType
    {
        String,
        Number,
        Integer,
        Bool,
        Array,
        Object,
        Null
    }

    /// <summary>
    /// Unified JSON value type that can hold any JSON-compatible value
    /// This uses a struct + enum approach similar to Rust's enum implementation
    /// </summary>
    public readonly struct JsonValue : IEquatable<JsonValue>
    {
        private readonly JsonValueType _type;
        private readonly object? _value;

        private JsonValue(JsonValueType type, object? value)
        {
            _type = type;
            _value = value;
        }

        // Factory methods (similar to Rust enum constructors)
        public static JsonValue String(string value) => new(JsonValueType.String, value);
        public static JsonValue Number(double value) => new(JsonValueType.Number, value);
        public static JsonValue Integer(long value) => new(JsonValueType.Integer, value);
        public static JsonValue Bool(bool value) => new(JsonValueType.Bool, value);
        public static JsonValue Array(JsonArray value) => new(JsonValueType.Array, value);
        public static JsonValue Object(JsonObject value) => new(JsonValueType.Object, value);
        public static JsonValue Null() => new(JsonValueType.Null, null);

        // Type checking methods (similar to Rust's pattern matching)
        public JsonValueType Type => _type;
        public bool IsString => _type == JsonValueType.String;
        public bool IsNumber => _type == JsonValueType.Number;
        public bool IsInteger => _type == JsonValueType.Integer;
        public bool IsBool => _type == JsonValueType.Bool;
        public bool IsArray => _type == JsonValueType.Array;
        public bool IsObject => _type == JsonValueType.Object;
        public bool IsNull => _type == JsonValueType.Null;

        // Safe accessor methods (similar to Rust's Option<T>)
        public string? AsString() => _type == JsonValueType.String ? (string?)_value : null;
        public double? AsNumber() => _type == JsonValueType.Number ? (double?)_value : null;
        public long? AsInteger() => _type == JsonValueType.Integer ? (long?)_value : null;
        public bool? AsBool() => _type == JsonValueType.Bool ? (bool?)_value : null;
        public JsonArray? AsArray() => _type == JsonValueType.Array ? (JsonArray?)_value : null;
        public JsonObject? AsObject() => _type == JsonValueType.Object ? (JsonObject?)_value : null;

        // Unsafe accessor methods (throw on wrong type, similar to Rust's unwrap)
        public string GetString() => _type == JsonValueType.String ? (string)_value! : throw new InvalidOperationException($"JsonValue is {_type}, not String");
        public double GetNumber() => _type == JsonValueType.Number ? (double)_value! : throw new InvalidOperationException($"JsonValue is {_type}, not Number");
        public long GetInteger() => _type == JsonValueType.Integer ? (long)_value! : throw new InvalidOperationException($"JsonValue is {_type}, not Integer");
        public bool GetBool() => _type == JsonValueType.Bool ? (bool)_value! : throw new InvalidOperationException($"JsonValue is {_type}, not Bool");
        public JsonArray GetArray() => _type == JsonValueType.Array ? (JsonArray)_value! : throw new InvalidOperationException($"JsonValue is {_type}, not Array");
        public JsonObject GetObject() => _type == JsonValueType.Object ? (JsonObject)_value! : throw new InvalidOperationException($"JsonValue is {_type}, not Object");

        // Pattern matching method (similar to Rust's match expression)
        public T Match<T>(
            Func<string, T> onString,
            Func<double, T> onNumber,
            Func<long, T> onInteger,
            Func<bool, T> onBool,
            Func<JsonArray, T> onArray,
            Func<JsonObject, T> onObject,
            Func<T> onNull)
        {
            return _type switch
            {
                JsonValueType.String => onString((string)_value!),
                JsonValueType.Number => onNumber((double)_value!),
                JsonValueType.Integer => onInteger((long)_value!),
                JsonValueType.Bool => onBool((bool)_value!),
                JsonValueType.Array => onArray((JsonArray)_value!),
                JsonValueType.Object => onObject((JsonObject)_value!),
                JsonValueType.Null => onNull(),
                _ => throw new InvalidOperationException($"Unknown JsonValue type: {_type}")
            };
        }

        // Void pattern matching (for side effects only)
        public void Match(
            Action<string>? onString = null,
            Action<double>? onNumber = null,
            Action<long>? onInteger = null,
            Action<bool>? onBool = null,
            Action<JsonArray>? onArray = null,
            Action<JsonObject>? onObject = null,
            Action? onNull = null)
        {
            switch (_type)
            {
                case JsonValueType.String:
                    onString?.Invoke((string)_value!);
                    break;
                case JsonValueType.Number:
                    onNumber?.Invoke((double)_value!);
                    break;
                case JsonValueType.Integer:
                    onInteger?.Invoke((long)_value!);
                    break;
                case JsonValueType.Bool:
                    onBool?.Invoke((bool)_value!);
                    break;
                case JsonValueType.Array:
                    onArray?.Invoke((JsonArray)_value!);
                    break;
                case JsonValueType.Object:
                    onObject?.Invoke((JsonObject)_value!);
                    break;
                case JsonValueType.Null:
                    onNull?.Invoke();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown JsonValue type: {_type}");
            }
        }

        // Implicit conversions (similar to Rust's From trait)
        public static implicit operator JsonValue(string value) => String(value);
        public static implicit operator JsonValue(double value) => Number(value);
        public static implicit operator JsonValue(long value) => Integer(value);
        public static implicit operator JsonValue(int value) => Integer(value);
        public static implicit operator JsonValue(bool value) => Bool(value);
        public static implicit operator JsonValue(JsonArray value) => Array(value);
        public static implicit operator JsonValue(JsonObject value) => Object(value);

        // Equality and comparison
        public bool Equals(JsonValue other)
        {
            if (_type != other._type) return false;
            return _type switch
            {
                JsonValueType.Null => true,
                _ => Equals(_value, other._value)
            };
        }

        public override bool Equals(object? obj) => obj is JsonValue other && Equals(other);


        public static bool operator ==(JsonValue left, JsonValue right) => left.Equals(right);
        public static bool operator !=(JsonValue left, JsonValue right) => !left.Equals(right);

        // String representation
        public override string ToString()
        {
            return _type switch
            {
                JsonValueType.String => (string)_value!,
                JsonValueType.Number => ((double)_value!).ToString(),
                JsonValueType.Integer => ((long)_value!).ToString(),
                JsonValueType.Bool => ((bool)_value!).ToString().ToLowerInvariant(),
                JsonValueType.Array => "[Array]",
                JsonValueType.Object => "[Object]",
                JsonValueType.Null => "null",
                _ => $"[Unknown:{_type}]"
            };
        }

        //public override int GetHashCode() => HashCode.Combine(_type, _value);
        public override int GetHashCode()
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER || NET6_0_OR_GREATER
            // Use the modern, allocation-free API on newer frameworks
            return HashCode.Combine(_type, _value);
#else
    // Use the classic, compatible approach on older frameworks
    unchecked
    {
        int hash = 17;
        hash = hash * 23 + _type.GetHashCode();
        hash = hash * 23 + (_value?.GetHashCode() ?? 0);
        return hash;
    }
#endif
        }
    }
}