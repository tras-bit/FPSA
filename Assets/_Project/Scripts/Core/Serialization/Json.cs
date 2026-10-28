using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kordon.Core.Serialization
{
    /// <summary>
    /// Read only convenience wrapper around a parsed JSON value.  Config loading
    /// code uses this instead of casting dictionaries everywhere, which keeps
    /// defaults explicit and gives one place to fail loudly on malformed data.
    /// </summary>
    public readonly struct Json
    {
        private readonly object _value;

        public Json(object value)
        {
            _value = value;
        }

        public static Json Parse(string text) => new Json(MiniJson.Parse(text));

        public bool IsNull => _value == null;
        public bool IsObject => _value is IDictionary<string, object>;
        public bool IsArray => _value is IList<object>;
        public object Raw => _value;

        public Json this[string key]
        {
            get
            {
                if (_value is IDictionary<string, object> map && map.TryGetValue(key, out object found))
                {
                    return new Json(found);
                }

                return new Json(null);
            }
        }

        public Json this[int index]
        {
            get
            {
                if (_value is IList<object> list && index >= 0 && index < list.Count)
                {
                    return new Json(list[index]);
                }

                return new Json(null);
            }
        }

        public bool Has(string key) => _value is IDictionary<string, object> map && map.ContainsKey(key);

        public int Count
        {
            get
            {
                switch (_value)
                {
                    case IList<object> list: return list.Count;
                    case IDictionary<string, object> map: return map.Count;
                    default: return 0;
                }
            }
        }

        public IEnumerable<Json> Items()
        {
            if (_value is IList<object> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    yield return new Json(list[i]);
                }
            }
        }

        public IEnumerable<string> Keys()
        {
            if (_value is IDictionary<string, object> map)
            {
                foreach (string key in map.Keys)
                {
                    yield return key;
                }
            }
        }

        public string AsString(string fallback = "")
        {
            if (_value is string s)
            {
                return s;
            }

            if (_value == null)
            {
                return fallback;
            }

            return Convert.ToString(_value, CultureInfo.InvariantCulture) ?? fallback;
        }

        public float AsFloat(float fallback = 0f)
        {
            if (_value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToSingle(_value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public double AsDouble(double fallback = 0d)
        {
            if (_value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToDouble(_value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public int AsInt(int fallback = 0)
        {
            if (_value == null)
            {
                return fallback;
            }

            try
            {
                return Convert.ToInt32(_value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public bool AsBool(bool fallback = false)
        {
            switch (_value)
            {
                case bool b: return b;
                case string s: return bool.TryParse(s, out bool parsed) ? parsed : fallback;
                case null: return fallback;
                default: return AsInt(fallback ? 1 : 0) != 0;
            }
        }

        public TEnum AsEnum<TEnum>(TEnum fallback) where TEnum : struct
        {
            if (_value is string s && Enum.TryParse(s, true, out TEnum parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public override string ToString() => MiniJson.Write(_value, false);
    }

    /// <summary>Helpers for building JSON documents in memory (settings saving, tests).</summary>
    public static class JsonObject
    {
        public static Dictionary<string, object> New() => new Dictionary<string, object>(StringComparer.Ordinal);

        public static Dictionary<string, object> With(this Dictionary<string, object> map, string key, object value)
        {
            map[key] = value;
            return map;
        }
    }
}
