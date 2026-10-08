using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Opus.Sdk
{
    /// <summary>
    /// Deserialized `manifest.json` (contracts/schemas/game-manifest.schema.json).
    /// Loaded with Newtonsoft so the raw `paramSchema` JSON Schema object is preserved untouched
    /// for <see cref="ParamBinder"/> and for the app's dynamic-form renderer.
    /// </summary>
    [Serializable]
    public sealed class GameManifest
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("version")] public string Version;
        [JsonProperty("sdkVersion")] public string SdkVersion;
        [JsonProperty("displayName")] public Dictionary<string, string> DisplayName = new Dictionary<string, string>();
        [JsonProperty("description")] public Dictionary<string, string> Description = new Dictionary<string, string>();
        [JsonProperty("input")] public List<string> Input = new List<string>();
        [JsonProperty("posture")] public string Posture = "seated";
        [JsonProperty("bodyRegions")] public List<string> BodyRegions = new List<string>();
        [JsonProperty("paramSchema")] public JObject ParamSchema;
        [JsonProperty("presets")] public List<GamePreset> Presets = new List<GamePreset>();
        [JsonProperty("events")] public List<string> Events = new List<string>();
        [JsonProperty("metrics")] public List<string> Metrics = new List<string>();

        public static GameManifest FromJson(string json)
        {
            var manifest = JsonConvert.DeserializeObject<GameManifest>(json);
            if (manifest == null) throw new FormatException("manifest.json deserialized to null");
            if (string.IsNullOrEmpty(manifest.Id)) throw new FormatException("manifest.json missing required field 'id'");
            if (manifest.ParamSchema == null) throw new FormatException("manifest.json missing required field 'paramSchema'");
            return manifest;
        }
    }

    [Serializable]
    public sealed class GamePreset
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("label")] public Dictionary<string, string> Label = new Dictionary<string, string>();
        [JsonProperty("params")] public JObject Params;
    }
}
