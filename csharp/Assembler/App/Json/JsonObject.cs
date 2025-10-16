#nullable enable

#define SIMPLE_JSON_TYPEINFO
//#define SIMPLE_JSON_CASE_INSENSITIVE

using System;
using System.Collections;
using System.Collections.Generic;

#if DEBUG
using System.Diagnostics;
#endif

namespace Arshu.App.Json
{    
    /// <summary>
    /// Represents the json object.
    /// </summary>
    public class JsonObject : IDictionary<string, object>
    {
        /// <summary>
        /// The internal member dictionary.
        /// </summary>
        private readonly Dictionary<string, object> _members;

        public bool Compare(JsonObject toCompare)
        {
            bool equal = false;
            if (this.Count == toCompare.Count) // Require equal count.
            {
                equal = true;
                foreach (var pair in this)
                {
                    object? value;
                    if (toCompare.TryGetValue(pair.Key, out value))
                    {
                        // Require value be equal.
                        if ((value != null) && (pair.Value != null))
                        {
                            if (value.ToString() != pair.Value.ToString())
                            {
                                equal = false;
                                break;
                            }
                            else if (value.Equals(pair.Value) == false)
                            {
                                equal = false;
                                break;
                            }
                        }
                        else
                        {
                            if ((value != null) || (pair.Value != null))
                            {
                                equal = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Require key be present.
                        equal = false;
                        break;
                    }
                }
            }
            return equal;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="JsonObject"/>.
        /// </summary>
        public JsonObject()
        {
#if SIMPLE_JSON_CASE_INSENSITIVE
            StringComparer keyComparer = StringComparer.OrdinalIgnoreCase;
            _members = new Dictionary<string, object>(keyComparer);
#else
            _members = new Dictionary<string, object>();
#endif
        }

        /// <summary>
        /// Gets a value indicating whether this instance is read only.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is read only; otherwise, <c>false</c>.
        /// </value>
        private bool _isReadOnly = false;
        public bool IsReadOnly
        {
            get
            {
                return _isReadOnly;
            }
            set
            {
                _isReadOnly = value;
            }
        }

        private object? GetAtIndex(IDictionary<string, object> obj, int index)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");
            if (index >= obj.Count)
                throw new ArgumentOutOfRangeException("index");
            int i = 0;
            foreach (object oValue in obj.Values)
                if (i++ == index)
                    return oValue;
            return null;
        }

        /// <summary>
        /// Gets the <see cref="System.Object"/> at the specified index.
        /// </summary>
        /// <value></value>
        public object? this[int index]
        {
            get { return GetAtIndex(_members, index); }
        }

        /// <summary>
        /// Gets or sets the <see cref="System.Object"/> with the specified key.
        /// </summary>
        /// <value></value>
        public object this[string key]
        {
            get
            {
                return _members[key];
            }
            set
            {
                if (IsReadOnly == false)
                {
                    _members[key] = value;
                }
                else
                {
#if DEBUG
                    Debugger.Break();
#endif
                }
            }
        }

        /// <summary>
        /// Determines whether the specified key contains key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>
        ///     <c>true</c> if the specified key contains key; otherwise, <c>false</c>.
        /// </returns>
        public bool ContainsKey(string key)
        {
            bool containsKey = _members.ContainsKey(key);
            return containsKey;
        }

        /// <summary>
        /// Adds the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Add(string key, object? value)
        {
            if (IsReadOnly == false)
            {
                if (value != null)
                {
                    _members.Add(key, value);
                }
                else
                {
                    _members.Add(key, "");
                }
            }
            else
            {
#if DEBUG
                Debugger.Break();
#endif
            }
        }

        /// <summary>
        /// Removes the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns></returns>
        public bool Remove(string key)
        {
            bool removed = false;
            if (IsReadOnly == false)
            {
                removed = _members.Remove(key);
            }
            else
            {
#if DEBUG
                Debugger.Break();
#endif
            }
            return removed;
        }

        public bool TryAdd(string key, object? value)
        {
            bool ret = false;
            if (IsReadOnly == false)
            {
                if (value != null)
                {
                    if (_members.ContainsKey(key) ==true)
                    {
                        _members.Add(key, value);
                        ret = true;
                    }
                }
                else
                {
                    if (_members.ContainsKey(key) == true)
                    {
                        _members.Add(key, "");
                        ret = true;
                    }
                }
            }
            else
            {
#if DEBUG
                Debugger.Break();
#endif
            }

            return ret;
        }

        /// <summary>
        /// Tries the get value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        public bool TryGetValue(string key, out object value)
        {
            bool containsValue = _members.TryGetValue(key, out object? getValue);
            value = string.Empty;
            if (getValue != null)
            {
                value = getValue;
            }
            return containsValue;
        }

        public bool TryGetValueByPath(string pathOrKey, out object? value)
        {
            bool containsValue = false;
            object? result = this;

            //Support arraykey.0.key, arraykey[0]key, arraykey[0].key, arraykey.[0].key, 
            if ((pathOrKey.Contains("[") == true) && (pathOrKey.Contains("]") == true))
            {
                pathOrKey = pathOrKey.Replace("[", ".");
                pathOrKey = pathOrKey.Replace("]", ".");
                pathOrKey = pathOrKey.Replace("..", ".");
            }

            string[] pathSegments = pathOrKey.Split('.');
            if (pathSegments.Length > 1)
            {
                foreach (var segment in pathSegments)
                {
                    containsValue = false;
                    if (result is JsonObject dictionary && dictionary.ContainsKey(segment))
                    {
                        result = dictionary[segment];
                        containsValue = true;
                    }
                    else if (result is List<object> list)
                    {

                        if (segment.ToLower().Contains("last") == false)
                        {
                            if (int.TryParse(segment, out int index) && index >= 0 && index < list.Count)
                            {
                                result = list[index];
                                containsValue = true;
                            }
                        }
                        else
                        {
                            int idxOfDash = segment.IndexOf("-");
                            if (idxOfDash == -1)
                            {
                                result = list[list.Count - 1]; //Last Value
                                containsValue = true;
                            }
                            else
                            {
                                string minusValueTxt = segment.Substring(idxOfDash + 1);
                                if (int.TryParse(minusValueTxt, out int minusValue) == true)
                                {
                                    int lastMinusIndex = list.Count - minusValue;
                                    if (lastMinusIndex >= 0)
                                    {
                                        result = list[lastMinusIndex]; //Last - Minus Index Value
                                        containsValue = true;
                                    }
                                }
                            }
                        }

                    }
                }
            }
            else
            {
                containsValue = TryGetValue(pathOrKey, out result);
            }

            value = null;
            if (containsValue == true)
            {
                value = result;
            }                
            return containsValue;
        }

        public Dictionary<string, object> TraversePath(object obj, string currentPath = "")
        {
            Dictionary<string, object> pathList = new Dictionary<string, object>();

            if (obj is JsonObject dictionary)
            {
                foreach (var kvp in dictionary)
                {
                    string newPath = currentPath + (currentPath == "" ? "" : ".") + kvp.Key;

                    Console.WriteLine($"{newPath}: {kvp.Value}");
                    pathList.Add($"{newPath}", kvp.Value);

                    Dictionary<string, object> innerPathList = TraversePath(kvp.Value, newPath);
                    foreach (var item in innerPathList)
                    {
                        pathList.Add(item.Key, item.Value);
                    }
                }
            }
            else if (obj is List<object> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    string newPath = currentPath + (currentPath == "" ? "" : ".") + i;

                    Console.WriteLine($"{newPath}: {list[i]}");
                    pathList.Add($"{newPath}", list[i]);

                    Dictionary<string, object> innerPathList = TraversePath(list[i], newPath);
                    foreach (var item in innerPathList)
                    {
                        pathList.Add(item.Key, item.Value);
                    }
                }
            }

            // Leaf nodes (scalar values) will not be traversed further
            // Console.WriteLine(obj);
            return pathList;
        }

        /// <summary>
        /// Gets the keys.
        /// </summary>
        /// <value>The keys.</value>
        public ICollection<string> Keys
        {
            get { return _members.Keys; }
        }

        /// <summary>
        /// Gets the values.
        /// </summary>
        /// <value>The values.</value>
        public ICollection<object> Values
        {
            get { return _members.Values; }
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear()
        {
            _members.Clear();
        }

        /// <summary>
        /// Gets the count.
        /// </summary>
        /// <value>The count.</value>
        public int Count
        {
            get { return _members.Count; }
        }

        /// <summary>
        /// Adds the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Add(KeyValuePair<string, object> item)
        {
#if DEBUG
            Debugger.Break();
#endif
            if (IsReadOnly == false)
            {
                _members.Add(item.Key, item.Value);
            }
        }

        /// <summary>
        /// Removes the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns></returns>
        public bool Remove(KeyValuePair<string, object> item)
        {
            bool removed = false;
#if DEBUG
            Debugger.Break();
#endif
            if (IsReadOnly == false)
            {
                removed = _members.Remove(item.Key);
            }
            return removed;
        }

        /// <summary>
        /// Determines whether [contains] [the specified item].
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>
        /// 	<c>true</c> if [contains] [the specified item]; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(KeyValuePair<string, object> item)
        {
            return _members.ContainsKey(item.Key) && _members[item.Key] == item.Value;
        }

        /// <summary>
        /// Copies to.
        /// </summary>
        /// <param name="array">The array.</param>
        /// <param name="arrayIndex">Index of the array.</param>
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            int num = Count;
            foreach (KeyValuePair<string, object> kvp in this)
            {
                array[arrayIndex++] = kvp;
                if (--num <= 0)
                    return;
            }
        }

        /// <summary>
        /// Gets the enumerator.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _members.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _members.GetEnumerator();
        }

    }
}

#nullable disable