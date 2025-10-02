#if USE_CUSTOM_JSON
using Arshu.App.Json;
using Arshu.App.Flex;
using Arshu.App.FlexJson;
#else
using System.Text.Json;
using System.Text.Json.Nodes;
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Arshu.App.Json;

/// <summary>
/// Converts between System.Text.Json and custom JSON types for normalization
/// Centralizes all JSON parsing and conversion logic
/// </summary>
public static class JsonConverter
{
    /// <summary>
    /// Parses JSON string and returns a normalized JsonObject
    /// This is the unified entry point for all JSON parsing in the application
    /// </summary>
    /// <param name="jsonContent">The JSON content to parse</param>
    /// <returns>JsonObject containing parsed JSON data</returns>
    public static JsonObject ParseJsonString(string jsonContent)
    {
        try
        {
#if USE_CUSTOM_JSON
            // Convert JSON string to FlexBuffer bytes
            var flexBufferBytes = JsonToFlexBufferConverter.Convert(jsonContent);
            // Convert bytes to FlxValue
            var flxValue = FlxValue.FromBytes(flexBufferBytes);
            // Get FlxMap from FlxValue
            var flxMap = flxValue.AsMap;
            // Convert FlxMap to JsonObject directly
            return FlexJsonUtil.ConvertFlexMap(flxMap);
#else
            // Use System.Text.Json for parsing and convert to Base JsonObject
            var jsonNode = JsonNode.Parse(jsonContent);
            if (jsonNode is System.Text.Json.Nodes.JsonObject systemJsonObject)
            {
                return NormalizeJsonObject(systemJsonObject);
            }
            return new JsonObject();
#endif
        }
        catch
        {
            // Return empty JsonObject if JSON parsing fails
            return new JsonObject();
        }
    }

    /// <summary>
    /// Serializes an object to JSON string in a NativeAOT-friendly way
    /// </summary>
    public static string SerializeObject<T>(T obj, bool indented = false)
    {
        try
        {
            // Use a simple manual serialization approach for NativeAOT compatibility
            if (obj == null) return "null";
            // If this is a dictionary-like type, serialize as JSON object
            if (obj is System.Collections.IDictionary)
            {
                return SerializeDictionaryAsObject(obj, indented);
            }

            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    items.Add(SerializeObject(item, false));
                }
                var arrayJson = "[" + string.Join(",", items) + "]";
                return indented ? FormatJson(arrayJson) : arrayJson;
            }

            // Handle basic types
            if (obj is string str)
                return "\"" + EscapeJsonString(str) + "\"";
            if (obj is bool boolean)
                return boolean.ToString().ToLower();
            if (obj is int || obj is long || obj is double || obj is decimal || obj is float)
                return obj.ToString() ?? "0";
            if (obj.GetType().IsEnum)
                return Convert.ToInt32(obj).ToString();

            // Handle objects using reflection alternative
            return SerializeObjectManually(obj, indented);
        }
        catch
        {
            return "{}";
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern", Justification = "Custom JSON serialization for NativeAOT compatibility")]
    private static string SerializeObjectManually(object obj, bool indented)
    {
        var properties = obj.GetType().GetProperties();
        var items = new List<string>();

        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(obj);
                var serializedValue = SerializeObject(value, false);
                var propertyName = prop.Name;
                items.Add($"\"{propertyName}\":{serializedValue}");
            }
            catch
            {
                // Skip properties that can't be serialized
            }
        }

        var objectJson = "{" + string.Join(",", items) + "}";
        return indented ? FormatJson(objectJson) : objectJson;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern", Justification = "Custom JSON serialization for NativeAOT compatibility")]
    private static string SerializeDictionaryAsObject(object dictionary, bool indented)
    {
        var items = new List<string>();

        // Use reflection to access dictionary entries
        var getEnumeratorMethod = dictionary.GetType().GetMethod("GetEnumerator");
        if (getEnumeratorMethod != null)
        {
            var enumerator = getEnumeratorMethod.Invoke(dictionary, null);
            var enumeratorType = enumerator?.GetType();
            var moveNextMethod = enumeratorType?.GetMethod("MoveNext");
            var currentProperty = enumeratorType?.GetProperty("Current");

            if (enumerator != null && moveNextMethod != null && currentProperty != null)
            {
                while ((bool)(moveNextMethod.Invoke(enumerator, null) ?? false))
                {
                    var current = currentProperty.GetValue(enumerator);
                    if (current != null)
                    {
                        var keyProperty = current.GetType().GetProperty("Key");
                        var valueProperty = current.GetType().GetProperty("Value");

                        if (keyProperty != null && valueProperty != null)
                        {
                            var key = keyProperty.GetValue(current);
                            var value = valueProperty.GetValue(current);

                            var keyJson = key?.ToString() ?? "";
                            // Use the Web serializer to keep behavior consistent (escaping, camelcase rules)
                            var valueJson = SerializeObject(value, false);
                            items.Add($"\"{keyJson}\":{valueJson}");
                        }
                    }
                }
            }
        }

        var objectJson = "{" + string.Join(",", items) + "}";
        return indented ? FormatJson(objectJson) : objectJson;
    }

    private static string EscapeJsonString(string str)
    {
        return str.Replace("\\", "\\\\")
                  // Encode double quote as \u0022 to match System.Text.Json's escaped output in some configurations
                  .Replace("\"", "\\u0022")
                  .Replace("\r", "\\r")
                  .Replace("\n", "\\n")
                  .Replace("\t", "\\t");
    }

    private static string FormatJson(string json)
    {
        // Simple JSON formatting for readability
        var formatted = json.Replace(",", ",\n  ")
                          .Replace("{", "{\n  ")
                          .Replace("}", "\n}")
                          .Replace("[", "[\n  ")
                          .Replace("]", "\n]");
        return formatted;
    }

    /*
/// <summary>
/// Serializes an object with Dictionary-to-object conversion for web API compatibility
/// </summary>
public static string SerializeObjectForWeb<T>(T obj, bool indented = false)
{
    try
    {
        // Removed excessive logging
        if (obj == null) return "null";
        if (obj is System.Collections.IDictionary)
        {
            // Removed excessive logging
            return SerializeDictionaryAsObject(obj, indented);
        }
        if (obj is JsonObject jsonObj)
        {
            // Removed excessive logging
            var items = new List<string>();
            foreach (var kvp in jsonObj)
            {
                var keyJson = kvp.Key ?? "";
                var valueJson = SerializeObjectForWeb(kvp.Value, false);
                items.Add($"\"{keyJson}\":{valueJson}");
            }
            var objectJson = "{" + string.Join(",", items) + "}";
            return indented ? FormatJson(objectJson) : objectJson;
        }
        if (obj is JsonArray jsonArr)
        {
            // Removed excessive logging
            var items = new List<string>();
            foreach (var item in jsonArr)
            {
                items.Add(SerializeObjectForWeb(item, false));
            }
            var arrayJson = "[" + string.Join(",", items) + "]";
            return indented ? FormatJson(arrayJson) : arrayJson;
        }
        if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
        {
            // Removed excessive logging
            if (obj.GetType().IsGenericType && obj.GetType().GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // Removed excessive logging
                return SerializeDictionaryAsObject(obj, indented);
            }
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(SerializeObjectForWeb(item, false));
            }
            var arrayJson = "[" + string.Join(",", items) + "]";
            return indented ? FormatJson(arrayJson) : arrayJson;
        }
        if (obj is string str)
            return "\"" + EscapeJsonStringForWeb(str) + "\"";
        if (obj is bool boolean)
            return boolean.ToString().ToLower();
        if (obj is int || obj is long || obj is double || obj is decimal || obj is float)
            return obj.ToString() ?? "0";
        if (obj.GetType().IsEnum)
            return Convert.ToInt32(obj).ToString();
        // Removed excessive logging
        return SerializeObjectManuallyForWeb(obj, indented);
    }
    catch (Exception)
    {
        return "{}";
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern", Justification = "Custom JSON serialization for NativeAOT compatibility")]
private static string SerializeObjectManuallyForWeb(object obj, bool indented)
{
    if (obj == null)
    {
        // Removed excessive logging
        return "null";
    }
// Removed excessive logging
    var properties = obj.GetType().GetProperties();
// Removed excessive logging
    foreach (var prop in properties)
    {
        // Removed excessive logging
    }
    var items = new List<string>();
    foreach (var prop in properties)
    {
        try
        {
            var value = prop.GetValue(obj);
            // Removed excessive logging
            if (value != null && (value is System.Collections.IDictionary ||
                (value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(Dictionary<,>))))
            {
                var serializedValue = SerializeDictionaryAsObject(value, false);
                var propertyName = prop.Name;
                items.Add($"\"{propertyName}\":{serializedValue}");
            }
            else
            {
                if (value is JsonObject || value is JsonArray)
                {
                    var serializedValue = SerializeObjectForWeb(value, false);
                    var propertyName = prop.Name;
                    items.Add($"\"{propertyName}\":{serializedValue}");
                }
                else
                {
                    var serializedValue = SerializeObjectForWeb(value, false);
                    var propertyName = prop.Name;
                    items.Add($"\"{propertyName}\":{serializedValue}");
                }
            }
        }
        catch (Exception)
        {
            // Removed excessive logging
        }
    }
    var objectJson = "{" + string.Join(",", items) + "}";
    return indented ? FormatJson(objectJson) : objectJson;
}

private static string EscapeJsonStringForWeb(string str)
{
    return str.Replace("\\", "\\\\")
              // Encode double quote as \u0022 to match System.Text.Json's escaped output in some configurations
              .Replace("\"", "\\u0022")
              .Replace("\r", "\\r")
              .Replace("\n", "\\n")
              .Replace("\t", "\\t")
              .Replace("<", "\\u003C")
              .Replace(">", "\\u003E")
              .Replace("&", "\\u0026")
              .Replace("'", "\\u0027")
              .Replace("+", "\\u002B");
}

*/


#if !USE_CUSTOM_JSON
    /// <summary>
    /// Converts System.Text.Json.Nodes.JsonObject to uniform JsonObject for consistent processing (legacy)
    /// </summary>
    public static JsonObject NormalizeJsonObject(System.Text.Json.Nodes.JsonObject systemJsonObject)
    {
        var customJsonObject = new JsonObject();
        
        foreach (var kvp in systemJsonObject)
        {
            var convertedValue = ConvertJsonNode(kvp.Value);
            customJsonObject.Add(kvp.Key, convertedValue);
        }
        
        return customJsonObject;
    }
    
    /// <summary>
    /// Converts any System.Text.Json.JsonNode to appropriate JsonValue type
    /// </summary>
    private static JsonValue ConvertJsonNodeToJsonValue(JsonNode? node)
    {
        if (node == null)
            return JsonValue.Null();
            
        switch (node.GetValueKind())
        {
            case JsonValueKind.String:
                return JsonValue.String(node.GetValue<string>());
                
            case JsonValueKind.Number:
                // Try to preserve the original number type
                if (node.AsValue().TryGetValue<int>(out int intValue))
                    return JsonValue.Integer(intValue);
                if (node.AsValue().TryGetValue<long>(out long longValue))
                    return JsonValue.Integer(longValue);
                if (node.AsValue().TryGetValue<double>(out double doubleValue))
                    return JsonValue.Number(doubleValue);
                return JsonValue.Number((double)node.GetValue<decimal>());
                
            case JsonValueKind.True:
                return JsonValue.Bool(true);
                
            case JsonValueKind.False:
                return JsonValue.Bool(false);
                
            case JsonValueKind.Array:
                var customArray = new JsonArray();
                var arrayNode = node.AsArray();
                if (arrayNode != null)
                {
                    foreach (var item in arrayNode)
                    {
                        var convertedItem = ConvertJsonNodeToJsonValue(item);
                        customArray.Add(ConvertJsonValueToLegacyObject(convertedItem));
                    }
                }
                return JsonValue.Array(customArray);
                
            case JsonValueKind.Object:
                var customObject = new JsonObject();
                var objectNode = node.AsObject();
                if (objectNode != null)
                {
                    foreach (var kvp in objectNode)
                    {
                        var convertedValue = ConvertJsonNodeToJsonValue(kvp.Value);
                        customObject[kvp.Key] = convertedValue;
                    }
                }
                return JsonValue.Object(customObject);
                
            default:
                return JsonValue.String(node.ToString() ?? string.Empty);
        }
    }
    
    /// <summary>
    /// Converts JsonValue to object for legacy array/object creation
    /// </summary>
    private static object ConvertJsonValueToLegacyObject(JsonValue value)
    {
        return value.Match<object>(
            onString: s => s,
            onNumber: n => n,
            onInteger: i => i,
            onBool: b => b,
            onArray: a => a,
            onObject: o => o,
            onNull: () => string.Empty
        );
    }
    
    /// <summary>
    /// Converts any System.Text.Json.JsonNode to appropriate custom type (legacy method)
    /// </summary>
    private static object? ConvertJsonNode(JsonNode? node)
    {
        if (node == null)
            return null;
            
        switch (node.GetValueKind())
        {
            case JsonValueKind.String:
                return node.GetValue<string>();
                
            case JsonValueKind.Number:
                // Try to preserve the original number type
                if (node.AsValue().TryGetValue<int>(out int intValue))
                    return intValue;
                if (node.AsValue().TryGetValue<long>(out long longValue))
                    return longValue;
                if (node.AsValue().TryGetValue<double>(out double doubleValue))
                    return doubleValue;
                return node.GetValue<decimal>();
                
            case JsonValueKind.True:
                return true;
                
            case JsonValueKind.False:
                return false;
                
            case JsonValueKind.Array:
                var customArray = new JsonArray();
                var arrayNode = node.AsArray();
                if (arrayNode != null)
                {
                    foreach (var item in arrayNode)
                    {
                        var convertedItem = ConvertJsonNode(item);
                        customArray.Add(convertedItem ?? new object());
                    }
                }
                return customArray;
                
            case JsonValueKind.Object:
                var customObjectLegacy = new JsonObject();
                var objectNodeLegacy = node.AsObject();
                if (objectNodeLegacy != null)
                {
                    foreach (var kvp in objectNodeLegacy)
                    {
                        var convertedValue = ConvertJsonNode(kvp.Value);
                        if (convertedValue != null)
                        {
                            customObjectLegacy.Add(kvp.Key, convertedValue);
                        }
                    }
                }
                return customObjectLegacy;
                
            default:
                return node.ToString() ?? string.Empty;
        }
    }

#endif

}