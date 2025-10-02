using Arshu.App.Flex;
using Arshu.App.Json;
using System;
using System.Collections.Generic;

namespace Arshu.App.FlexJson
{
    public class FlexJsonUtil
    {
        public static JsonObject ConvertFlexMap(FlxMap flxMap)
        {
            JsonObject jsonObject = new JsonObject();

            try
            {
                foreach (KeyValuePair<string, FlxValue> itemMap in flxMap)
                {
                    if (itemMap.Value.IsNull == true)
                    {
                        jsonObject.Add(itemMap.Key, null);
                    }
                    else if (itemMap.Value.ValueType == FlexType.String)
                    {
                        jsonObject.Add(itemMap.Key, itemMap.Value.AsString);
                    }
                    else if (itemMap.Value.ValueType == FlexType.Bool)
                    {
                        jsonObject.Add(itemMap.Key, itemMap.Value.AsBool);
                    }
                    else if (itemMap.Value.ValueType == FlexType.Int)
                    {
                        jsonObject.Add(itemMap.Key, itemMap.Value.AsLong);
                    }
                    else if (itemMap.Value.ValueType == FlexType.Float)
                    {
                        jsonObject.Add(itemMap.Key, itemMap.Value.AsDouble);
                    }
                    else if (itemMap.Value.ValueType == FlexType.Vector)
                    {
                        FlxVector innerVector = itemMap.Value.AsVector;
                        JsonArray jsonArray = ConvertFlexVector(innerVector);
                        jsonObject.Add(itemMap.Key, jsonArray);

                    }
                    else if (itemMap.Value.ValueType == FlexType.Map)
                    {
                        FlxMap innerMap = itemMap.Value.AsMap;
                        JsonObject innerJson = ConvertFlexMap(innerMap);
                        jsonObject.Add(itemMap.Key, innerJson);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ConvertFlexMapToJson:" + ex.ToString());
            }

            return jsonObject;
        }

        public static JsonArray ConvertFlexVector(FlxVector flxVector)
        {
            JsonArray jsonArray = new JsonArray();

            try
            {
                foreach (var itemVector in flxVector)
                {
                    if (itemVector.ValueType == FlexType.String)
                    {
                        jsonArray.Add(itemVector.AsString);
                    }
                    else if (itemVector.ValueType == FlexType.Bool)
                    {
                        jsonArray.Add(itemVector.AsBool);
                    }
                    else if (itemVector.ValueType == FlexType.Int)
                    {
                        jsonArray.Add(itemVector.AsLong);
                    }
                    else if (itemVector.ValueType == FlexType.Float)
                    {
                        jsonArray.Add(itemVector.AsDouble);
                    }
                    else if (itemVector.ValueType == FlexType.Map)
                    {
                        FlxMap innerVectorMap = itemVector.AsMap;
                        JsonObject innerJson = ConvertFlexMap(innerVectorMap);
                        jsonArray.Add(innerJson);
                    }
                    else if (itemVector.ValueType == FlexType.Vector)
                    {
                        FlxVector innerVectorVector = itemVector.AsVector;
                        JsonArray innerJsonArray = ConvertFlexVector(innerVectorVector);
                        jsonArray.Add(innerJsonArray);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ConvertFlexVectorToJson:" + ex.ToString());
            }

            return jsonArray;
        }

        public static FlxValue GetFlexBufferValue(byte[] flexBuffer)
        {
            FlxValue flxValue = FlxValue.FromBytes(flexBuffer);
            return flxValue;
        }

        public static byte[] JsonToFlexBuffer(string jsonData)
        {
            byte[] flexBuffer = { };
            try
            {
                if ((string.IsNullOrWhiteSpace(jsonData) == false) && (jsonData.Contains("{") == true) && (jsonData.Contains("}") == true))
                {
                    flexBuffer = JsonToFlexBufferConverter.Convert(jsonData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SerializeJsonToFlexBuffer:" + ex.ToString());
            }

            return flexBuffer;
        }

        public static JsonObject FlexBufferToJsonObject(byte[] flexBytes, string logInfo)
        {
            JsonObject jsonObject = new JsonObject();

            try
            {
                if (flexBytes.Length > 0)
                {
                    var flxValue = FlxValue.FromBytes(flexBytes);
                    if (flxValue.IsNull == false)
                    {
                        if (flxValue.ValueType == FlexType.Map)
                        {
                            FlxMap flxMap = flxValue.AsMap;
                            if (flxMap.Length > 0)
                            {
                                jsonObject = ConvertFlexMap(flxMap);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DeserializeFlexBufferToJsonObject:" + ex.ToString());
            }

            return jsonObject;
        }

        public static JsonArray FlexBufferToJsonArray(byte[] flexBytes, string logInfo)
        {
            JsonArray jsonArray = new JsonArray();

            try
            {
                if (flexBytes.Length > 0)
                {
                    var flxValue = FlxValue.FromBytes(flexBytes);
                    if (flxValue.IsNull == false)
                    {
                        if (flxValue.ValueType == FlexType.Vector)
                        {
                            FlxVector flxVector = flxValue.AsVector;
                            if (flxVector.Length > 0)
                            {
                                jsonArray = ConvertFlexVector(flxVector);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("DeserializeFlexBufferToJsonArray:" + ex.ToString());
            }

            return jsonArray;
        }

        public static JsonObject JsonToJsonObject(string jsonData)
        {
            JsonObject? jsonObject = new JsonObject();

            try
            {
                if ((string.IsNullOrWhiteSpace(jsonData) == false) && (jsonData.Contains("{") == true) && (jsonData.Contains("}") == true))
                {
                    byte[] flexBuffer = JsonToFlexBufferConverter.Convert(jsonData);
                    if (flexBuffer.Length > 0)
                    {
                        FlxValue flxValue = GetFlexBufferValue(flexBuffer);
                        if (flxValue.IsNull == false)
                        {
                            if (flxValue.ValueType == FlexType.Map)
                            {
                                FlxMap flxMap = flxValue.AsMap;
                                if (flxMap.Length > 0)
                                {
                                    jsonObject = ConvertFlexMap(flxMap);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SerializeJsonToJsonObject:" + ex.ToString());
            }

            return jsonObject;
        }

        public static JsonArray JsonToJsonArray(string jsonData)
        {
            JsonArray jsonArray = new JsonArray();

            try
            {
                if ((string.IsNullOrWhiteSpace(jsonData) == false) && (jsonData.Contains("{") == true) && (jsonData.Contains("}") == true))
                {
                    byte[] flexBuffer = JsonToFlexBufferConverter.Convert(jsonData);
                    if (flexBuffer.Length > 0)
                    {
                        FlxValue flxValue = GetFlexBufferValue(flexBuffer);
                        if (flxValue.IsNull == false)
                        {
                            if (flxValue.ValueType == FlexType.Vector)
                            {
                                FlxVector flxVector = flxValue.AsVector;
                                if (flxVector.Length > 0)
                                {
                                    jsonArray = ConvertFlexVector(flxVector);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SerializeJsonToJsonArray:" + ex.ToString());
            }

            return jsonArray;
        }

        public static byte[] JsonObjectToFlexBuffer(JsonObject jsonObject, string customPropertyIndex = "")
        {
            byte[] flexBytes = { };
            int propertyIndex = 0;
            try
            {
                flexBytes = FlexBufferBuilder.Map(root =>
                {
                    foreach (var item in jsonObject)
                    {
                        propertyIndex++;
                        string propertyName = item.Key;
                        if (string.IsNullOrWhiteSpace(customPropertyIndex) == false)
                        {
                            propertyName = propertyIndex.ToString("D2") + propertyName;
                        }
                        string propertyType = item.Value.GetType().Name;
                        if (item.Value is string)
                        {
                            string? strProperty = item.Value.ToString();
                            if (strProperty != null)
                            {
                                strProperty = strProperty.Replace("\\", "\\\\");
                                root.Add(propertyName, strProperty);
                            }
                            else
                            {
                                root.AddNull(propertyName);
                            }
                        }
                        else if (item.Value is bool)
                        {
                            root.Add(propertyName, (bool)item.Value);
                        }
                        else if (item.Value is Int32)
                        {
                            root.Add(propertyName, (Int32)item.Value);
                        }
                        else if (item.Value is Int64)
                        {
                            root.Add(propertyName, (long)item.Value);
                        }
                        else if (item.Value is double)
                        {
                            root.Add(propertyName, (double)item.Value);
                        }
                        else if (item.Value is decimal)
                        {
                            root.Add(propertyName, Decimal.ToDouble((decimal)item.Value));
                        }
                        else if (item.Value is JsonObject)
                        {
                            byte[] jsonFlexBytes = JsonObjectToFlexBuffer((JsonObject)item.Value, customPropertyIndex);
                            var flx = FlxValue.FromBytes(jsonFlexBytes);
                            if (flx.IsNull == false)
                            {
                                root.Add(propertyName, flx);
                            }
                        }
                        else if (item.Value is JsonArray)
                        {
                            JsonArray jsonArray = (JsonArray)(item.Value);
                            root.Vector(propertyName, innerVectorData =>
                            {
                                foreach (var itemArray in jsonArray)
                                {
                                    Type itemArrayType = itemArray.GetType();
                                    if (itemArray is JsonObject)
                                    {
                                        byte[] jsonFlexBytes = JsonObjectToFlexBuffer((JsonObject)itemArray, customPropertyIndex);
                                        var flx = FlxValue.FromBytes(jsonFlexBytes);
                                        if (flx.IsNull == false)
                                        {
                                            innerVectorData.Add(flx);
                                        }
                                    }
                                    else if (itemArray is string)
                                    {
                                        string? itemArrayText = (string)itemArray;
                                        if (itemArrayText != null)
                                        {
                                            innerVectorData.Add(itemArrayText);
                                        }
                                    }
                                    else if (itemArray is bool)
                                    {
                                        innerVectorData.Add((bool)itemArray);
                                    }
                                    else if (itemArray is Int32)
                                    {
                                        innerVectorData.Add((long)itemArray);
                                    }
                                    else if (itemArray is Int64)
                                    {
                                        innerVectorData.Add((long)itemArray);
                                    }
                                    else if (itemArray is Double)
                                    {
                                        innerVectorData.Add((double)itemArray);
                                    }
                                    else if (itemArray is Decimal)
                                    {
                                        innerVectorData.Add((double)itemArray);
                                    }
                                }
                            });
                        }
                        else if (item.Value is IList<String>)
                        {
                            List<String> jsonList = (List<String>)(item.Value);
                            root.Vector(propertyName, innerVectorData =>
                            {
                                foreach (var itemList in jsonList)
                                {
                                    innerVectorData.Add(itemList);
                                }
                            });
                        }
                        else if (item.Value is IList<Int32>)
                        {
                            List<Int32> jsonList = (List<Int32>)(item.Value);
                            root.Vector(propertyName, innerVectorData =>
                            {
                                foreach (var itemList in jsonList)
                                {
                                    innerVectorData.Add(itemList);
                                }
                            });
                        }
                        else if (item.Value is JsonObject[])
                        {
                            JsonObject[] jsonObjectList = (JsonObject[])(item.Value);
                            root.Vector(propertyName, innerVectorData =>
                            {
                                foreach (JsonObject itemJsonList in jsonObjectList)
                                {
                                    Type itemArrayType = itemJsonList.GetType();
                                    if (itemJsonList is JsonObject)
                                    {
                                        byte[] jsonFlexBytes = JsonObjectToFlexBuffer((JsonObject)itemJsonList, customPropertyIndex);
                                        var flx = FlxValue.FromBytes(jsonFlexBytes);
                                        if (flx.IsNull == false)
                                        {
                                            innerVectorData.Add(flx);
                                        }
                                    }
                                }
                            });
                        }
                        else if (item.Value is IList<JsonObject>)
                        {
                            List<JsonObject> jsonObjectList = (List<JsonObject>)(item.Value);
                            root.Vector(propertyName, innerVectorData =>
                            {
                                foreach (JsonObject itemJsonList in jsonObjectList)
                                {
                                    Type itemArrayType = itemJsonList.GetType();
                                    if (itemJsonList is JsonObject)
                                    {
                                        byte[] jsonFlexBytes = JsonObjectToFlexBuffer((JsonObject)itemJsonList, customPropertyIndex);
                                        var flx = FlxValue.FromBytes(jsonFlexBytes);
                                        if (flx.IsNull == false)
                                        {
                                            innerVectorData.Add(flx);
                                        }
                                    }
                                }
                            });
                        }
                        else
                        {
                            string? strProperty = item.Value.ToString();
                            if (strProperty != null)
                            {
                                strProperty = strProperty.Replace("\\", "\\\\");
                                root.Add(propertyName, strProperty);
                            }
                            else
                            {
                                root.AddNull(propertyName);
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("ConvertJsonObject:" + ex.ToString());
            }

            return flexBytes;
        }

        public static string SerializeFromJsonObject(JsonObject inputObj, bool pretifyJson = true, int truncateKeySize = 0, bool confirmString = true)
        {
            string customPropertyIndex = "";
            if (truncateKeySize > 0) customPropertyIndex = "01";

            byte[] flexBytes = JsonObjectToFlexBuffer(inputObj, customPropertyIndex);

            FlxValue flxValue = FlxValue.FromBytes(flexBytes);

            if (pretifyJson == true)
            {
                return flxValue.ToTruncateKeyPrettyJson(truncateKeySize, confirmString).Replace("\\/", "/");
            }
            else
            {
                return flxValue.ToTruncateKeyJson(truncateKeySize, confirmString).Replace("\\/", "/");
            }
        }
    }
}
