using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Opus.Sdk
{
    /// <summary>Bound, validated parameters for a game block. Immutable view over a JObject.</summary>
    public sealed class ParamSet
    {
        private readonly JObject _values;

        public ParamSet(JObject values)
        {
            _values = values ?? new JObject();
        }

        public bool Has(string key) => _values[key] != null;

        public string GetString(string key, string fallback = null)
        {
            var token = _values[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<string>();
        }

        public int GetInt(string key, int fallback = 0)
        {
            var token = _values[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        public double GetDouble(string key, double fallback = 0)
        {
            var token = _values[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<double>();
        }

        public bool GetBool(string key, bool fallback = false)
        {
            var token = _values[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        public double[] GetDoubleArray(string key)
        {
            var token = _values[key] as JArray;
            if (token == null) return Array.Empty<double>();
            var result = new double[token.Count];
            for (int i = 0; i < token.Count; i++) result[i] = token[i].Value<double>();
            return result;
        }

        public JObject Raw => _values;

        public IEnumerable<string> Keys => ((IDictionary<string, JToken>)_values).Keys;
    }
}
