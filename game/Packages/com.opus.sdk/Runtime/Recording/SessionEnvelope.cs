using System.Collections.Generic;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>session.json — mirrors contracts/schemas/session-envelope.schema.json exactly.</summary>
    public sealed class SessionEnvelope
    {
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("contracts_version")] public string ContractsVersion = "0.1";
        [JsonProperty("patient_ref")] public string PatientRef;
        [JsonProperty("program_ref")] public string ProgramRef;
        [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)] public string Mode;
        [JsonProperty("started_at")] public string StartedAt;
        [JsonProperty("ended_at")] public string EndedAt;
        [JsonProperty("end_reason")] public string EndReason;
        [JsonProperty("device")] public DeviceInfo Device = new DeviceInfo();
        [JsonProperty("versions")] public VersionsInfo Versions = new VersionsInfo();
        [JsonProperty("calibration")] public CalibrationInfo Calibration = new CalibrationInfo();
        [JsonProperty("blocks")] public List<BlockInfo> Blocks = new List<BlockInfo>();
        [JsonProperty("chunks")] public int Chunks;
        [JsonProperty("patient_reported", NullValueHandling = NullValueHandling.Ignore)] public PatientReported PatientReported;
    }

    public sealed class DeviceInfo
    {
        [JsonProperty("model")] public string Model = "unknown";
        [JsonProperty("device_id", NullValueHandling = NullValueHandling.Ignore)] public string DeviceId;
        [JsonProperty("os", NullValueHandling = NullValueHandling.Ignore)] public string Os;
        [JsonProperty("tracking_rate_hz", NullValueHandling = NullValueHandling.Ignore)] public double? TrackingRateHz;
    }

    public sealed class VersionsInfo
    {
        [JsonProperty("shell")] public string Shell;
        [JsonProperty("sdk")] public string Sdk;
        [JsonProperty("games", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, string> Games = new Dictionary<string, string>();
    }

    public sealed class CalibrationInfo
    {
        [JsonProperty("affected_side")] public string AffectedSide;
        [JsonProperty("dominant_side", NullValueHandling = NullValueHandling.Ignore)] public string DominantSide;
        [JsonProperty("arm_length_m")] public ArmLength ArmLengthM = new ArmLength();
        [JsonProperty("posture", NullValueHandling = NullValueHandling.Ignore)] public string Posture;
        [JsonProperty("chest_reference", NullValueHandling = NullValueHandling.Ignore)] public double[] ChestReference;
        [JsonProperty("comfortable_envelope", NullValueHandling = NullValueHandling.Ignore)] public object ComfortableEnvelope;
    }

    public sealed class ArmLength
    {
        [JsonProperty("left", NullValueHandling = NullValueHandling.Ignore)] public double? Left;
        [JsonProperty("right", NullValueHandling = NullValueHandling.Ignore)] public double? Right;
    }

    public sealed class BlockInfo
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("game_id")] public string GameId;
        [JsonProperty("game_version")] public string GameVersion;
        [JsonProperty("params")] public object Params;
        [JsonProperty("started_t_ms", NullValueHandling = NullValueHandling.Ignore)] public double? StartedTMs;
        [JsonProperty("ended_t_ms", NullValueHandling = NullValueHandling.Ignore)] public double? EndedTMs;
        [JsonProperty("completed", NullValueHandling = NullValueHandling.Ignore)] public bool? Completed;
    }

    public sealed class PatientReported
    {
        [JsonProperty("pain_0_10", NullValueHandling = NullValueHandling.Ignore)] public int? Pain0To10;
        [JsonProperty("fatigue_0_10", NullValueHandling = NullValueHandling.Ignore)] public int? Fatigue0To10;
        [JsonProperty("enjoyment_1_5", NullValueHandling = NullValueHandling.Ignore)] public int? Enjoyment1To5;
    }
}
