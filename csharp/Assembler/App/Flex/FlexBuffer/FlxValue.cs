using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Arshu.App.Flex
{
    public struct FlxValue
    {
        #region Properties

        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly byte _parentWidth;
        private readonly byte _byteWidth;
        private readonly FlexType _type;
 
        #endregion

        #region Constructor

        internal FlxValue(byte[] buffer, int offset, byte parentWidth, byte packedType)
        {
            _buffer = buffer;
            _offset = offset;
            _parentWidth = parentWidth;
            _byteWidth = (byte)(1 << (packedType & 3));
            _type = (FlexType)(packedType >> 2);
        }

        internal FlxValue(byte[] buffer, int offset, byte parentWidth, byte byteWidth, FlexType type)
        {
            _buffer = buffer;
            _offset = offset;
            _parentWidth = parentWidth;
            _byteWidth = byteWidth;
            _type = type;
        }

        #endregion

        #region Public Properties/Methods

        public FlexType ValueType => _type;

        public int BufferOffset => _offset;

        public bool IsNull => _type == FlexType.Null;

        public long AsLong
        {
            get
            {
                if (_type == FlexType.Int)
                {
                    return ReadLong(_buffer, _offset, _parentWidth);
                }

                if (_type == FlexType.IndirectInt)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    return ReadLong(_buffer, indirectOffset, _byteWidth);
                }

                if (_type == FlexType.Uint)
                {
                    var value = ReadULong(_buffer, _offset, _parentWidth);
                    if (value <= long.MaxValue)
                    {
                        return (long)value;
                    }
                }
                if (_type == FlexType.IndirectUInt)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    var value = ReadULong(_buffer, indirectOffset, _byteWidth);
                    if (value <= long.MaxValue)
                    {
                        return (long)value;
                    }
                }
                throw new Exception($"Type {_type} is not convertible to long");
            }
        }

        public ulong AsULong
        {
            get
            {
                if (_type == FlexType.Uint)
                {
                    return ReadULong(_buffer, _offset, _parentWidth);
                }

                if (_type == FlexType.IndirectUInt)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    return ReadULong(_buffer, indirectOffset, _byteWidth);
                }

                if (_type == FlexType.Int)
                {
                    var value = ReadLong(_buffer, _offset, _parentWidth);
                    if (value >= 0)
                    {
                        return (ulong)value;
                    }
                }

                if (_type == FlexType.IndirectInt)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    var value = ReadLong(_buffer, indirectOffset, _byteWidth);
                    if (value >= 0)
                    {
                        return (ulong)value;
                    }
                }
                throw new Exception($"Type {_type} is not convertible to ulong");
            }
        }

        public double AsDouble
        {
            get
            {
                if (_type == FlexType.Float)
                {
                    return ReadDouble(_buffer, _offset, _parentWidth);
                }
                if (_type == FlexType.Int)
                {
                    return ReadLong(_buffer, _offset, _parentWidth);
                }
                if (_type == FlexType.Uint)
                {
                    return ReadULong(_buffer, _offset, _parentWidth);
                }
                if (_type == FlexType.IndirectFloat)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    return ReadDouble(_buffer, indirectOffset, _byteWidth);
                }
                if (_type == FlexType.IndirectUInt)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    return ReadULong(_buffer, indirectOffset, _byteWidth);
                }
                if (_type == FlexType.IndirectInt)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    return ReadLong(_buffer, indirectOffset, _byteWidth);
                }
                throw new Exception($"Type {_type} is not convertible to double");
            }
        }

        public bool AsBool
        {
            get
            {
                if (_type == FlexType.Bool)
                {
                    return _buffer[_offset] != 0;
                }
                if (_type == FlexType.Int)
                {
                    return ReadLong(_buffer, _offset, _parentWidth) != 0;
                }
                if (_type == FlexType.Uint)
                {
                    return ReadULong(_buffer, _offset, _parentWidth) != 0;
                }
                throw new Exception($"Type {_type} is not convertible to bool");
            }
        }

        public string AsString
        {
            get
            {
                if (_type == FlexType.String)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    var size = (int)ReadULong(_buffer, indirectOffset - _byteWidth, _byteWidth);
                    var sizeWidth = (int)_byteWidth;
                    while (_buffer[indirectOffset + size] != 0)
                    {
                        sizeWidth <<= 1;
                        size = (int)ReadULong(_buffer, indirectOffset - sizeWidth, (byte)sizeWidth);
                    }

                    return Encoding.UTF8.GetString(_buffer, indirectOffset, size);
                }

                if (_type == FlexType.Key)
                {
                    var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                    var size = 0;
                    while (indirectOffset + size < _buffer.Length && _buffer[indirectOffset + size] != 0)
                    {
                        size++;
                    }
                    return Encoding.UTF8.GetString(_buffer, indirectOffset, size);
                }

                throw new Exception($"Type {_type} is not convertible to string");
            }
        }

        public FlxValue this[int index] => AsVector[index];

        public FlxValue this[string key] => AsMap[key];

        public FlxVector AsVector
        {
            get
            {
                if (TypesUtil.IsAVector(_type) == false)
                {
                    throw new Exception($"Type {_type} is not a vector.");
                }

                var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                var size = TypesUtil.IsFixedTypedVector(_type)
                    ? TypesUtil.FixedTypedVectorElementSize(_type)
                    : (int)ReadULong(_buffer, indirectOffset - _byteWidth, _byteWidth);
                return new FlxVector(_buffer, indirectOffset, _byteWidth, _type, size);
            }
        }

        public FlxMap AsMap
        {
            get
            {
                if (_type != FlexType.Map)
                {
                    throw new Exception($"Type {_type} is not a map.");
                }

                var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                var size = ReadULong(_buffer, indirectOffset - _byteWidth, _byteWidth);
                return new FlxMap(_buffer, indirectOffset, _byteWidth, (int)size);
            }
        }

        public byte[] AsBlob
        {
            get
            {
                if (_type != FlexType.Blob)
                {
                    throw new Exception($"Type {_type} is not a blob.");
                }
                var indirectOffset = ComputeIndirectOffset(_buffer, _offset, _parentWidth);
                var size = ReadULong(_buffer, indirectOffset - _byteWidth, _byteWidth);
                var blob = new byte[size];
                System.Buffer.BlockCopy(_buffer, indirectOffset, blob, 0, (int)size);
                return blob;
            }
        }

        public static FlxValue FromBytes(byte[] bytes)
        {
            if (bytes.Length < 3)
            {
                throw new Exception($"Invalid buffer {bytes}");
            }

            var byteWidth = bytes[bytes.Length - 1];
            var packedType = bytes[bytes.Length - 2];
            var offset = bytes.Length - byteWidth - 2;
            return new FlxValue(bytes, offset, byteWidth, packedType);
        }

        #endregion

        #region Public Json Methods

        public string ToJson(bool conformString = true)
        {
            if (IsNull)
            {
                return "null";
            }

            if (_type == FlexType.Bool)
            {
                return AsBool ? "true" : "false";
            }

            if (_type == FlexType.Int || _type == FlexType.IndirectInt)
            {
                return AsLong.ToString();
            }

            if (_type == FlexType.Uint || _type == FlexType.IndirectUInt)
            {
                return AsULong.ToString();
            }

            if (_type == FlexType.Float || _type == FlexType.IndirectFloat)
            {
                return AsDouble.ToString(CultureInfo.CurrentCulture);
            }

            if (TypesUtil.IsAVector(_type))
            {
                return AsVector.ToJson(conformString);
            }

            if (_type == FlexType.Key)
            {
                if (conformString == true)
                {
                    var jsonConformString = AsString.Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t")
                        .Replace("/", "\\/");
                    return $"\"{jsonConformString}\"";
                }
                else
                {
                    var jsonString = AsString;
                    return $"\"{jsonString}\"";
                }
            }

            if (_type == FlexType.String)
            {
                if (conformString == true)
                {
                    var jsonConformString = AsString.Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t")
                        .Replace("/", "\\/");
                    return $"\"{jsonConformString}\"";
                }
                else
                {
                    var jsonString = AsString;
                    return $"\"{jsonString}\"";
                }
            }

            if (_type == FlexType.Map)
            {
                return AsMap.ToJson(conformString);
            }

            if (_type == FlexType.Blob)
            {
                return $"\"{Convert.ToBase64String(AsBlob)}\"";
            }

            throw new Exception($"Unexpected type {_type}");
        }

        public string ToPrettyJson(bool conformString = true, string left = "", bool childrenOnly = false)
        {
            if (_type == FlexType.Map)
            {
                return AsMap.ToPrettyJson(conformString, left, childrenOnly);
            }
            if (TypesUtil.IsAVector(_type))
            {
                return AsVector.ToPrettyJson(conformString, left, childrenOnly);
            }

            if (childrenOnly)
            {
                return ToJson(conformString);
            }

            return $"{left}{ToJson(conformString)}";
        }

        public string ToTruncateKeyJson(int truncateKeySize, bool conformString = true)
        {
            if (IsNull)
            {
                return "null";
            }

            if (_type == FlexType.Bool)
            {
                return AsBool ? "true" : "false";
            }

            if (_type == FlexType.Int || _type == FlexType.IndirectInt)
            {
                return AsLong.ToString();
            }

            if (_type == FlexType.Uint || _type == FlexType.IndirectUInt)
            {
                return AsULong.ToString();
            }

            if (_type == FlexType.Float || _type == FlexType.IndirectFloat)
            {
                return AsDouble.ToString(CultureInfo.CurrentCulture);
            }

            if (TypesUtil.IsAVector(_type))
            {
                return AsVector.ToTruncateKeyJson(truncateKeySize, conformString);
            }
            
            if (_type == FlexType.Key)
            {
                if (conformString == true)
                {
                    var jsonConformString = AsString.Substring(truncateKeySize).Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t")
                        .Replace("/", "\\/");
                    return $"\"{jsonConformString}\"";
                }
                else
                {
                    var jsonString = AsString.Substring(truncateKeySize);
                    return $"\"{jsonString}\"";
                }
            }

            if (_type == FlexType.String)
            {
                if (conformString == true)
                {
                    var jsonConformString = AsString.Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t")
                        .Replace("/", "\\/");
                    return $"\"{jsonConformString}\"";
                }
                else
                {
                    var jsonString = AsString;
                    return $"\"{jsonString}\"";
                }
            }

            if (_type == FlexType.Map)
            {
                return AsMap.ToTruncateKeyJson(truncateKeySize, conformString);
            }

            if (_type == FlexType.Blob)
            {
                return $"\"{Convert.ToBase64String(AsBlob)}\"";
            }

            throw new Exception($"Unexpected type {_type}");
        }

        public string ToTruncateKeyPrettyJson(int truncateKeySize, bool conformString = true, string left = "", bool childrenOnly = false)
        {
            if (_type == FlexType.Map)
            {
                return AsMap.ToTruncateKeyPrettyJson(truncateKeySize, conformString, left, childrenOnly);
            }
            if (TypesUtil.IsAVector(_type))
            {
                return AsVector.ToTruncateKeyPrettyJson(truncateKeySize, conformString, left, childrenOnly);
            }

            if (childrenOnly)
            {
                return ToTruncateKeyJsonPretty(truncateKeySize, conformString, left, childrenOnly);
            }

            return $"{left}{ToTruncateKeyJsonPretty(truncateKeySize, conformString, left, childrenOnly)}";
        }

        private string ToTruncateKeyJsonPretty(int truncateKeySize, bool conformString = true, string left = "", bool childrenOnly = false)
        {
            if (IsNull)
            {
                return "null";
            }

            if (_type == FlexType.Bool)
            {
                return AsBool ? "true" : "false";
            }

            if (_type == FlexType.Int || _type == FlexType.IndirectInt)
            {
                return AsLong.ToString();
            }

            if (_type == FlexType.Uint || _type == FlexType.IndirectUInt)
            {
                return AsULong.ToString();
            }

            if (_type == FlexType.Float || _type == FlexType.IndirectFloat)
            {
                return AsDouble.ToString(CultureInfo.CurrentCulture);
            }

            if (TypesUtil.IsAVector(_type))
            {
                return AsVector.ToTruncateKeyPrettyJson(truncateKeySize);
            }

            if (_type == FlexType.Key)
            {
                if (conformString == true)
                {
                    var jsonConformString = AsString.Substring(truncateKeySize).Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t")
                        .Replace("/", "\\/");
                    return $"\"{jsonConformString}\"";
                }
                else
                {
                    var jsonString = AsString.Substring(truncateKeySize);
                    return $"\"{jsonString}\"";
                }

            }

            if (_type == FlexType.String)
            {
                if (conformString == true)
                {
                    var jsonConformString = AsString.Replace("\"", "\\\"")
                        .Replace("\n", "\\n")
                        .Replace("\r", "\\r")
                        .Replace("\t", "\\t")
                        .Replace("/", "\\/");
                    return $"\"{jsonConformString}\"";
                }
                else
                {
                    var jsonString = AsString;
                    return $"\"{jsonString}\"";
                }
            }

            

            if (_type == FlexType.Map)
            {
                return AsMap.ToTruncateKeyPrettyJson(truncateKeySize, conformString, left, childrenOnly);
            }

            if (_type == FlexType.Blob)
            {
                return $"\"{Convert.ToBase64String(AsBlob)}\"";
            }

            throw new Exception($"Unexpected type {_type}");
        }

        #endregion

        #region Internal Methods

        internal static long ReadLong(byte[] bytes, int offset, byte width)
        {
            if (offset < 0 || bytes.Length <= (offset + width) || (offset & (width - 1)) != 0)
            {
                throw new Exception("Bad offset");
            }

            if (width == 1)
            {
                return (sbyte)bytes[offset];
            }

            if (width == 2)
            {
                return BitConverter.ToInt16(bytes, offset);
            }

            if (width == 4)
            {
                return BitConverter.ToInt32(bytes, offset);
            }

            return BitConverter.ToInt64(bytes, offset);
        }

        internal static ulong ReadULong(byte[] bytes, int offset, byte width)
        {
            if (offset < 0 || bytes.Length <= (offset + width) || (offset & (width - 1)) != 0)
            {
                throw new Exception("Bad offset");
            }

            if (width == 1)
            {
                return bytes[offset];
            }

            if (width == 2)
            {
                return BitConverter.ToUInt16(bytes, offset);
            }

            if (width == 4)
            {
                return BitConverter.ToUInt32(bytes, offset);
            }

            return BitConverter.ToUInt64(bytes, offset);
        }

        internal static double ReadDouble(byte[] bytes, int offset, byte width)
        {
            if (offset < 0 || bytes.Length <= (offset + width) || (offset & (width - 1)) != 0)
            {
                throw new Exception("Bad offset");
            }

            if (width != 4 && width != 8)
            {
                throw new Exception($"Bad width {width}");
            }

            if (width == 4)
            {
                return BitConverter.ToSingle(bytes, offset);
            }

            return BitConverter.ToDouble(bytes, offset);
        }

        internal static int ComputeIndirectOffset(byte[] bytes, int offset, byte width)
        {
            var step = (int)ReadULong(bytes, offset, width);
            return offset - step;
        }

        internal byte[] Buffer => _buffer;
        internal int Offset => _offset;

        internal int IndirectOffset => ComputeIndirectOffset(_buffer, _offset, _parentWidth);

        #endregion
    }

    public struct FlxVector : IEnumerable<FlxValue>
    {
        #region Properties

        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _length;
        private readonly byte _byteWidth;
        private readonly FlexType _type;

        /// <summary>
        /// Indentation string used for pretty printing JSON
        /// </summary>
        private static readonly string JSON_INDENT = "  "; // 2 spaces for consistent formatting

        #endregion

        #region Constructor

        internal FlxVector(byte[] buffer, int offset, byte byteWidth, FlexType type, int length)
        {
            _buffer = buffer;
            _offset = offset;
            _byteWidth = byteWidth;
            _type = type;
            _length = length;
        }

        #endregion

        #region IEnumerable Methods

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Public Properties/Methods

        public int Length => _length;

        public FlxValue this[int index]
        {
            get
            {
                if (index < 0 || index >= _length)
                {
                    throw new Exception($"Bad index {index}, should be 0...{_length}");
                }

                if (TypesUtil.IsTypedVector(_type))
                {
                    var elemOffset = _offset + (index * _byteWidth);
                    return new FlxValue(_buffer, elemOffset, _byteWidth, 1, TypesUtil.TypedVectorElementType(_type));
                }

                if (TypesUtil.IsFixedTypedVector(_type))
                {
                    var elemOffset = _offset + (index * _byteWidth);
                    return new FlxValue(_buffer, elemOffset, _byteWidth, 1, TypesUtil.FixedTypedVectorElementType(_type));
                }

                if (_type == FlexType.Vector)
                {
                    var packedType = _buffer[_offset + _length * _byteWidth + index];
                    var elemOffset = _offset + (index * _byteWidth);
                    return new FlxValue(_buffer, elemOffset, _byteWidth, packedType);
                }
                throw new Exception($"Bad index {index}, should be 0...{_length}");
            }
        }

        public IEnumerator<FlxValue> GetEnumerator()
        {
            for (var i = 0; i < _length; i++)
            {
                yield return this[i];
            }
        }

        #endregion

        #region Public List Properties

        public List<string> AsKeys
        {
            get
            {
                List<string> keyList = new List<string>();
                if ((_type == FlexType.String)
                    || (_type == FlexType.Key))
                {
                    for (var i = 0; i < _length; i++)
                    {
                        keyList.Add(this[i].ToJson());
                    }
                }
                return keyList;
            }
        }

        #endregion

        #region Public Json Methods

        public string ToJson(bool conformString = true)
        {
            var builder = new StringBuilder();
            builder.Append("[");
            for (var i = 0; i < _length; i++)
            {
                builder.Append(this[i].ToJson(conformString));
                if (i < _length - 1)
                {
                    builder.Append(",");
                }
            }

            builder.Append("]");

            return builder.ToString();

        }

        public string ToPrettyJson(bool conformString = true, string left = "", bool childrenOnly = false)
        {
            var builder = new StringBuilder();
            if (childrenOnly == false)
            {
                builder.Append(left);
            }

            builder.Append("[\n");
            for (var i = 0; i < _length; i++)
            {
                builder.Append(this[i].ToPrettyJson(conformString, $"{left}{JSON_INDENT}"));
                if (i < _length - 1)
                {
                    builder.Append(",");
                }

                builder.Append("\n");
            }
            builder.Append(left);
            builder.Append("]");

            return builder.ToString();
        }

        public string ToTruncateKeyJson(int truncateKeySize, bool conformString = true)
        {
            var builder = new StringBuilder();
            builder.Append("[");
            for (var i = 0; i < _length; i++)
            {
                builder.Append(this[i].ToTruncateKeyJson(truncateKeySize, conformString));
                if (i < _length - 1)
                {
                    builder.Append(",");
                }
            }

            builder.Append("]");

            return builder.ToString();
        }
 
        public string ToTruncateKeyPrettyJson(int truncateKeySize, bool conformString = true, string left = "", bool childrenOnly = false)
        {
            var builder = new StringBuilder();
            if (childrenOnly == false)
            {
                builder.Append(left);
            }

            builder.Append("[\n");
            for (var i = 0; i < _length; i++)
            {
                builder.Append(this[i].ToTruncateKeyPrettyJson(truncateKeySize, conformString, $"{left}{JSON_INDENT}"));
                if (i < _length - 1)
                {
                    builder.Append(",");
                }

                builder.Append("\n");
            }
            builder.Append($"{left}");
            builder.Append("]");

            return builder.ToString();
        }

        #endregion
    }

    public struct FlxMap : IEnumerable<KeyValuePair<string, FlxValue>>
    {
        #region Properties

        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _length;
        private readonly byte _byteWidth;

        /// <summary>
        /// Indentation string used for pretty printing JSON
        /// </summary>
        private static readonly string JSON_INDENT = "  "; // 2 spaces for consistent formatting

        #endregion

        #region Constructor

        internal FlxMap(byte[] buffer, int offset, byte byteWidth, int length)
        {
            _buffer = buffer;
            _offset = offset;
            _byteWidth = byteWidth;
            _length = length;
        }

        #endregion

        #region Private Methods

        private FlxVector Keys
        {
            get
            {
                var keysOffset = _offset - _byteWidth * 3;
                var indirectOffset = FlxValue.ComputeIndirectOffset(_buffer, keysOffset, _byteWidth);
                var bWidth = FlxValue.ReadULong(_buffer, keysOffset + _byteWidth, _byteWidth);
                return new FlxVector(_buffer, indirectOffset, (byte)bWidth, FlexType.VectorKey, _length);
            }
        }

        private FlxVector Values => new FlxVector(_buffer, _offset, _byteWidth, FlexType.Vector, _length);

        private int Comp(int i, string key)
        {
            // TODO: keep it so we can profile it against byte comparison
            var key2 = Keys[i].AsString;
            return string.Compare(key, key2, StringComparison.Ordinal);
        }

        private int Comp(int i, byte[] key)
        {
            var key2 = Keys[i];
            var indirectOffset = key2.IndirectOffset;
            for (int j = 0; j < key.Length; j++)
            {
                var dif = key[j] - key2.Buffer[indirectOffset + j];
                if (dif != 0)
                {
                    return dif;
                }
            }
            // keys are zero terminated
            return key2.Buffer[indirectOffset + key.Length] == 0 ? 0 : -1;
        }

        #endregion

        #region IEnumerable Methods

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Public Properties/Methods

        public int Length => _length;

        public FlxValue this[string key]
        {
            get
            {
                var index = KeyIndex(key);
                if (index < 0)
                {
                    throw new Exception($"No key '{key}' could be found");
                }
                return Values[index];
            }
        }

        public bool HaveKey(string key)
        {
            bool haveKey = false;
            var index = KeyIndex(key);
            if (index < 0)
            {
                haveKey = false;
            }
            else
            {
                haveKey = true;
            }
            return haveKey;
        }

        public FlxValue ValueByIndex(int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= Length)
            {
                throw new Exception($"Bad Key index {keyIndex}");
            }

            return Values[keyIndex];
        }

        public int KeyIndex(string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var low = 0;
            var high = _length - 1;
            while (low <= high)
            {
                var mid = (high + low) >> 1;
                var dif = Comp(mid, keyBytes);
                if (dif == 0)
                {
                    return mid;
                }
                if (dif < 0)
                {
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }

            return -1;
        }

        #endregion

        #region Public KeyValue Properties

        public IEnumerator<KeyValuePair<string, FlxValue>> GetEnumerator()
        {
            var keys = Keys;
            var values = Values;
            for (var i = 0; i < _length; i++)
            {
                yield return new KeyValuePair<string, FlxValue>(keys[i].AsString, values[i]);
            }
        }

        #endregion

        #region Public Json Methods

        public string ToJson(bool conformString = true)
        {
            var builder = new StringBuilder();
            builder.Append("{");
            var keys = Keys;
            var values = Values;
            for (var i = 0; i < _length; i++)
            {
                builder.Append($"{keys[i].ToJson(conformString)}:{values[i].ToJson(conformString)}");
                if (i < _length - 1)
                {
                    builder.Append(",");
                }
            }
            builder.Append("}");
            return builder.ToString();

        }

        public string ToPrettyJson(bool conformString = true, string left = "", bool childrenOnly = false)
        {
            var builder = new StringBuilder();
            if (childrenOnly == false)
            {
                builder.Append(left);
            }
            builder.Append("{\n");
            var keys = Keys;
            var values = Values;
            for (var i = 0; i < _length; i++)
            {
                builder.Append($"{left}{JSON_INDENT}{keys[i].ToPrettyJson(conformString)} : {values[i].ToPrettyJson(conformString, $"{left}{JSON_INDENT}", true)}");
                if (i < _length - 1)
                {
                    builder.Append(",");
                }

                builder.Append("\n");
            }
            builder.Append(left);
            builder.Append("}");
            return builder.ToString();
        }

        public string ToTruncateKeyJson(int truncateKeySize, bool conformString = true)
        {
            var builder = new StringBuilder();
            builder.Append("{");
            var keys = Keys;
            var values = Values;
            for (var i = 0; i < _length; i++)
            {
                builder.Append($"{keys[i].ToTruncateKeyJson(truncateKeySize, conformString)}:{values[i].ToTruncateKeyJson(truncateKeySize, conformString)}");
                if (i < _length - 1)
                {
                    builder.Append(",");
                }
            }
            builder.Append("}");
            return builder.ToString();
        }

        public string ToTruncateKeyPrettyJson(int truncateKeySize, bool conformString = true, string left = "", bool childrenOnly = false)
        {
            var builder = new StringBuilder();
            if (childrenOnly == false)
            {
                builder.Append(left);
            }
            builder.Append("{\n");
            var keys = Keys;
            var values = Values;
            for (var i = 0; i < _length; i++)
            {
                int spaceLenth = left.Length;
                string key = $"{left}{JSON_INDENT}{keys[i].ToTruncateKeyPrettyJson(truncateKeySize, conformString, "")}";
                string value = $"{values[i].ToTruncateKeyPrettyJson(truncateKeySize, conformString, $"{left}{JSON_INDENT}", true)}";
                builder.Append($"{key}: {value}");
                if (i < _length - 1)
                {
                    builder.Append(",");
                }

                builder.Append("\n");
            }
            builder.Append(left);
            builder.Append("}");
            return builder.ToString();
        }

        #endregion
    }
}