using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Opus.Sdk
{
    /// <summary>Result of binding raw params against a manifest's paramSchema.</summary>
    public sealed class ParamBindResult
    {
        public ParamSet Params { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;

        public ParamBindResult(ParamSet paramSet, IReadOnlyList<string> errors)
        {
            Params = paramSet;
            Errors = errors;
        }
    }

    /// <summary>
    /// Binds a game's raw params (as prescribed by a program block) against its manifest's `paramSchema`
    /// (JSON Schema, draft 2020-12 subset used by game-manifest.schema.json): fills in `default`s for
    /// missing properties, and validates type/enum/min/max/array bounds. No schema library dependency —
    /// this covers exactly the vocabulary the manifests use (type, enum, default, minimum, maximum, minItems, maxItems, items, required).
    /// </summary>
    public static class ParamBinder
    {
        public static ParamBindResult Bind(JObject paramSchema, JObject input)
        {
            if (paramSchema == null) throw new ArgumentNullException(nameof(paramSchema));
            input = (JObject)(input ?? new JObject()).DeepClone();

            var errors = new List<string>();
            var properties = paramSchema["properties"] as JObject ?? new JObject();
            var required = new HashSet<string>((paramSchema["required"] as JArray)?.Select(t => t.Value<string>()) ?? Enumerable.Empty<string>());

            foreach (var prop in properties.Properties())
            {
                string key = prop.Name;
                var propSchema = prop.Value as JObject ?? new JObject();
                var current = input[key];

                if (current == null || current.Type == JTokenType.Null)
                {
                    var def = propSchema["default"];
                    if (def != null)
                    {
                        input[key] = def.DeepClone();
                        current = input[key];
                    }
                    else if (required.Contains(key))
                    {
                        errors.Add($"missing required param '{key}' and no default in schema");
                        continue;
                    }
                    else
                    {
                        continue; // optional, absent, no default: leave unset
                    }
                }

                ValidateValue(key, current, propSchema, errors);
            }

            // Flag params that aren't declared in the schema at all — silent typos are a common bug source.
            foreach (var extra in input.Properties())
            {
                if (properties[extra.Name] == null)
                    errors.Add($"unknown param '{extra.Name}' not declared in paramSchema");
            }

            return new ParamBindResult(new ParamSet(input), errors);
        }

        private static void ValidateValue(string key, JToken value, JObject propSchema, List<string> errors)
        {
            var enumValues = propSchema["enum"] as JArray;
            if (enumValues != null)
            {
                bool ok = enumValues.Any(e => JToken.DeepEquals(e, value));
                if (!ok) errors.Add($"param '{key}' value '{value}' not in enum [{string.Join(", ", enumValues)}]");
                return;
            }

            string type = propSchema["type"]?.Value<string>();
            switch (type)
            {
                case "integer":
                    if (value.Type != JTokenType.Integer)
                        errors.Add($"param '{key}' expected integer, got {value.Type}");
                    else
                        CheckRange(key, value.Value<double>(), propSchema, errors);
                    break;
                case "number":
                    if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                        errors.Add($"param '{key}' expected number, got {value.Type}");
                    else
                        CheckRange(key, value.Value<double>(), propSchema, errors);
                    break;
                case "boolean":
                    if (value.Type != JTokenType.Boolean)
                        errors.Add($"param '{key}' expected boolean, got {value.Type}");
                    break;
                case "string":
                    if (value.Type != JTokenType.String)
                        errors.Add($"param '{key}' expected string, got {value.Type}");
                    break;
                case "array":
                    if (value.Type != JTokenType.Array)
                    {
                        errors.Add($"param '{key}' expected array, got {value.Type}");
                        break;
                    }
                    var arr = (JArray)value;
                    int? minItems = propSchema["minItems"]?.Value<int>();
                    int? maxItems = propSchema["maxItems"]?.Value<int>();
                    if (minItems.HasValue && arr.Count < minItems.Value)
                        errors.Add($"param '{key}' has {arr.Count} items, minItems is {minItems.Value}");
                    if (maxItems.HasValue && arr.Count > maxItems.Value)
                        errors.Add($"param '{key}' has {arr.Count} items, maxItems is {maxItems.Value}");
                    var itemSchema = propSchema["items"] as JObject;
                    if (itemSchema != null)
                    {
                        foreach (var item in arr)
                            ValidateValue(key + "[]", item, itemSchema, errors);
                    }
                    break;
            }
        }

        private static void CheckRange(string key, double value, JObject propSchema, List<string> errors)
        {
            var min = propSchema["minimum"];
            var max = propSchema["maximum"];
            if (min != null && value < min.Value<double>())
                errors.Add($"param '{key}' value {value} is below minimum {min.Value<double>()}");
            if (max != null && value > max.Value<double>())
                errors.Add($"param '{key}' value {value} is above maximum {max.Value<double>()}");
        }
    }
}
