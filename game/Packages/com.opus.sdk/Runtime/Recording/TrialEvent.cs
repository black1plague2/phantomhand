using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>One line of events.ndjson — mirrors contracts/schemas/event.schema.json exactly.</summary>
    public sealed class TrialEvent
    {
        [JsonProperty("t_ms")] public double TMs;
        [JsonProperty("seq")] public int Seq;
        [JsonProperty("block")] public int Block;
        [JsonProperty("trial", NullValueHandling = NullValueHandling.Include)] public int? Trial;
        [JsonProperty("type")] public string Type;
        [JsonProperty("hand", NullValueHandling = NullValueHandling.Ignore)] public string Hand;
        [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)] public TrialTarget Target;
        [JsonProperty("outcome", NullValueHandling = NullValueHandling.Ignore)] public string Outcome;
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)] public object Data;

        public static TrialEvent Create(int block, int? trial, string type) => new TrialEvent { Block = block, Trial = trial, Type = type };
    }

    public sealed class TrialTarget
    {
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)] public string Id;
        [JsonProperty("pos", NullValueHandling = NullValueHandling.Ignore)] public double[] Pos;
        [JsonProperty("azimuthDeg", NullValueHandling = NullValueHandling.Ignore)] public double? AzimuthDeg;
        [JsonProperty("elevationDeg", NullValueHandling = NullValueHandling.Ignore)] public double? ElevationDeg;
        [JsonProperty("reachPercent", NullValueHandling = NullValueHandling.Ignore)] public double? ReachPercent;
        [JsonProperty("isDistractor", NullValueHandling = NullValueHandling.Ignore)] public bool? IsDistractor;
    }
}
