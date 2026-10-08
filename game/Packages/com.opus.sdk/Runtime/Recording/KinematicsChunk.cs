using System.Collections.Generic;
using Newtonsoft.Json;

namespace Opus.Sdk
{
    /// <summary>The joint names kinematics-chunk.schema.json allows. Keep in sync with the schema's enum.</summary>
    public static class OpusJoints
    {
        public const string Head = "head";
        public const string LWrist = "l_wrist";
        public const string RWrist = "r_wrist";
        public const string LIndexTip = "l_index_tip";
        public const string RIndexTip = "r_index_tip";
        public const string LThumbTip = "l_thumb_tip";
        public const string RThumbTip = "r_thumb_tip";
        public const string LPalm = "l_palm";
        public const string RPalm = "r_palm";

        public static readonly string[] All =
        {
            Head, LWrist, RWrist, LIndexTip, RIndexTip, LThumbTip, RThumbTip, LPalm, RPalm
        };
    }

    /// <summary>One kin_###.json file — mirrors contracts/schemas/kinematics-chunk.schema.json exactly.</summary>
    public sealed class KinematicsChunk
    {
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("seq")] public int Seq;
        [JsonProperty("rate_hz")] public double RateHz;
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)] public string Source;
        [JsonProperty("joints")] public List<string> Joints = new List<string>();
        [JsonProperty("t_ms")] public List<double> TMs = new List<double>();
        [JsonProperty("frames")] public Dictionary<string, JointSeries> Frames = new Dictionary<string, JointSeries>();
        [JsonProperty("ground_truth", NullValueHandling = NullValueHandling.Ignore)] public object GroundTruth;
    }

    public sealed class JointSeries
    {
        [JsonProperty("pos")] public List<double[]> Pos = new List<double[]>();
        [JsonProperty("rot", NullValueHandling = NullValueHandling.Ignore)] public List<double[]> Rot;
        [JsonProperty("conf")] public List<double> Conf = new List<double>();
    }
}
