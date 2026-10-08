using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests.PlayMode
{
    internal sealed class FakeTransport : IHapticTransport
    {
        public readonly List<string> Sent = new List<string>();
        public bool HasDevice { get; set; } = true;
        public event Action<string> OnMessage { add { } remove { } }
        public event Action OnDeviceKnown { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Send(string json) { Sent.Add(json); }
        public void Dispose() { }
    }

    internal sealed class FakeHands : IHandSource
    {
        public Vector3 Wrist = new Vector3(0.18f, 0.771f, 0.40f);
        public Vector3 Palm = new Vector3(0.18f, 0.775f, 0.47f);
        public int Version;
        public bool TryGetJointPose(string joint, out double[] p, out double[] r)
        {
            var v = joint == OpusJoints.RPalm ? Palm : Wrist;
            p = new double[] { v.x, v.y, v.z }; r = new double[] { 0, 0, 0, 1 }; return true;
        }
        public TrackingConfidence GetConfidence(HandSide side) { return TrackingConfidence.High; }
        public bool IsTracked(HandSide side) { return side == HandSide.Right; }
        public float GetPinchStrength(HandSide side) { return 0f; }
        public int GetDataVersion(HandSide side) { return Version; }
    }

    internal sealed class FakeSession : ISessionContext
    {
        public SessionClock Clock { get; } = SessionClock.Manual();
        public int BlockIndex { get { return 0; } }
    }

    /// <summary>One frame at a time, in the order the composition root uses: haptic Pump -> module Tick -> presenter -> physics.</summary>
    internal sealed class Fx : IDisposable
    {
        public const double FrameMs = 1000.0 / 72.0;
        public readonly FakeSession S = new FakeSession();
        public readonly PhantomHandModule M = new PhantomHandModule();
        public readonly List<TrialEvent> Ev = new List<TrialEvent>();
        public readonly FakeTransport T = new FakeTransport();
        public readonly HapticClient H;
        public readonly FakeHands Hands = new FakeHands();
        public readonly GameObject Root;
        public readonly ArmThreatPresenter P;
        private readonly SimulationMode _prevMode;
        public Action<Fx> OnFrame;

        public Fx(string paramsJson)
        {
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            M.OnTrialEvent += Ev.Add;
            M.Configure(new ParamSet(JObject.Parse(paramsJson)), S);
            H = new HapticClient(T);
            Root = new GameObject("PH_TestRoot");
            P = Root.AddComponent<ArmThreatPresenter>();
            P.Bind(M, H, null, null, S.Clock, Hands);
            P.SetCalibration(Hands.Wrist, Vector3.forward);
        }

        public double Now { get { return S.Clock.NowMs; } }

        public void Step()
        {
            S.Clock.Advance(FrameMs);
            Hands.Version++;
            H.Pump(Now);
            M.Tick(Now);
            P.Tick();
            if (P.threat.IsBuilt) P.threat.CountStep((float)(FrameMs / 1000.0));
            Physics.Simulate((float)(FrameMs / 1000.0));
            if (OnFrame != null) OnFrame(this);
        }

        public void StepUntil(Func<bool> cond, double maxMs, string what)
        {
            double end = Now + maxMs;
            while (!cond()) { Step(); if (Now > end) Assert.Fail("timeout waiting for " + what + " in phase " + M.CurrentPhase); }
        }

        public void ToInduction()
        {
            M.Begin();
            Step();
            M.SubmitCalibration(new double[] { Hands.Wrist.x, Hands.Wrist.y, Hands.Wrist.z }, new[] { 0.0, 0, 1 }, true);
            Step();
            Assert.AreEqual(PhPhase.ProbePre, M.CurrentPhase);
            M.SubmitProbe(new ProbeResult { When = "pre", Confirmed = true, PerceivedXm = -0.16, ActualXm = 0.0, DriftCm = 0 });
            Step();
            Assert.AreEqual(PhPhase.Induction, M.CurrentPhase);
        }

        public List<JObject> Events(string type)
        {
            return Ev.Where(e => e.Type == type).Select(e => JObject.FromObject(e.Data)).ToList();
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            Physics.simulationMode = _prevMode;
        }
    }

    public class PhantomHandPresentationTests
    {
        private const string Sync = "{\"condition_order\":\"sync_first\",\"induction_s\":40}";
        private const string Async = "{\"condition_order\":\"async_first\",\"induction_s\":60}";

        // -- strokes -------------------------------------------------------------------------------------------

        [Test]
        public void StrokeCount_EqualsPlan_AndEveryStrokeIsRecordedOnce()
        {
            using (var f = new Fx(Sync))
            {
                f.ToInduction();
                int planned = f.M.CurrentStrokes.Count;
                Assert.GreaterOrEqual(planned, 25);
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Induction, 60000, "induction end");
                var strokes = f.Events("stroke");
                Assert.AreEqual(planned, strokes.Count, "stroke events must equal the plan");
                CollectionAssert.AreEqual(Enumerable.Range(0, planned).ToList(), strokes.Select(s => s["index"].Value<int>()).ToList());
                // 2 cues per stroke were handed to the sleeve and none were dropped
                var cues = f.T.Sent.Select(JObject.Parse).Where(j => (string)j["cue"] == "stroke").ToList();
                Assert.AreEqual(planned * 2, cues.Count);
            }
        }

        [Test]
        public void Sync_TimingError_IsWithin20ms_ForEveryStroke()
        {
            using (var f = new Fx(Sync))
            {
                f.ToInduction();
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Induction, 60000, "induction end");
                var strokes = f.Events("stroke");
                Assert.IsNotEmpty(strokes);
                foreach (var s in strokes)
                {
                    Assert.IsFalse(s["timing_err_ms"].Type == JTokenType.Null, "SYNC stroke without timing error: " + s);
                    double err = s["timing_err_ms"].Value<double>();
                    Assert.LessOrEqual(Math.Abs(err), 20.0, "stroke " + s["index"]);
                    // the error is exactly send + lead - measured pass (mean over A, B)
                    double manual = ((s["cue_a_send_ms"].Value<double>() + 40 - s["brush_pass_a_ms"].Value<double>()) +
                                     (s["cue_b_send_ms"].Value<double>() + 40 - s["brush_pass_b_ms"].Value<double>())) / 2.0;
                    Assert.AreEqual(manual, err, 1e-6);
                }
            }
        }

        [Test]
        public void Async_CueDelay_Is500To700ms_AndAboutHalfTheStrokesAreSwapped()
        {
            using (var f = new Fx(Async))
            {
                f.ToInduction();
                Assert.AreEqual(PhCondition.Async, f.M.CurrentCondition);
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Induction, 80000, "induction end");
                var strokes = f.Events("stroke");
                Assert.GreaterOrEqual(strokes.Count, 40);
                int swapped = 0;
                foreach (var s in strokes)
                {
                    Assert.IsTrue(s["timing_err_ms"].Type == JTokenType.Null, "ASYNC has no timing error");
                    bool sw = s["swapped"].Value<bool>(); if (sw) swapped++;
                    double a = s["brush_pass_a_ms"].Value<double>(), b = s["brush_pass_b_ms"].Value<double>();
                    // motor 0 (A) cue belongs to the A slot unless swapped; delay = send + lead - slot pass
                    double d0 = s["cue_a_send_ms"].Value<double>() + 40 - (sw ? b : a);
                    double d1 = s["cue_b_send_ms"].Value<double>() + 40 - (sw ? a : b);
                    foreach (double d in new[] { d0, d1 })
                    {
                        Assert.GreaterOrEqual(d, 500 - 16, "stroke " + s["index"] + " delay " + d);
                        Assert.LessOrEqual(d, 700 + 16, "stroke " + s["index"] + " delay " + d);
                    }
                }
                double frac = swapped / (double)strokes.Count;
                Assert.That(frac, Is.InRange(0.3, 0.7), "swapped fraction " + frac);
            }
        }

        [Test]
        public void Display_ShowsCondition_ThenIdle()
        {
            using (var f = new Fx(Sync))
            {
                f.ToInduction();
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Induction, 60000, "induction end");
                var texts = f.T.Sent.Select(JObject.Parse).Where(j => (string)j["type"] == "display").Select(j => (string)j["text"]).ToList();
                CollectionAssert.AreEqual(new[] { "SYNC", "IDLE" }, texts);
            }
        }

        [Test]
        public void Brush_KeepsStroking_WhenTheArmIsInvisible()
        {
            using (var f = new Fx(Sync))
            {
                f.ToInduction();
                f.P.arm.Alpha = 0f;           // A2: arm gone, brush must not care
                f.P.arm.Visible = false;
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Induction, 60000, "induction end");
                Assert.AreEqual(f.M.CurrentStrokes.Count, f.Events("stroke").Count);
            }
        }

        // -- arm -----------------------------------------------------------------------------------------------

        [Test]
        public void VirtualPose_IsConstantDuringInduction_EvenWhenTheRealWristMoves()
        {
            using (var f = new Fx(Sync))
            {
                f.ToInduction();
                int h0 = f.P.arm.PoseHash();
                var hashes = new HashSet<int>();
                var rng = new System.Random(5);
                f.OnFrame = x =>
                {
                    x.Hands.Wrist += new Vector3((float)(rng.NextDouble() - 0.5) * 0.04f, 0, (float)(rng.NextDouble() - 0.5) * 0.04f);
                    if (x.M.CurrentPhase == PhPhase.Induction) hashes.Add(x.P.arm.PoseHash());
                };
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Induction, 60000, "induction end");
                Assert.IsTrue(f.P.arm.Frozen);
                Assert.AreEqual(1, hashes.Count);
                Assert.AreEqual(h0, hashes.First());
            }
        }

        [Test]
        public void VirtualPose_Follows_WhenFollowDuringInductionIsTrue()
        {
            using (var f = new Fx("{\"condition_order\":\"sync_first\",\"induction_s\":40,\"follow_during_induction\":true}"))
            {
                f.ToInduction();
                Assert.IsFalse(f.P.arm.Frozen);
                int h0 = f.P.arm.PoseHash();
                f.Hands.Wrist += new Vector3(0.05f, 0, 0);
                for (int i = 0; i < 5; i++) f.Step();
                Assert.AreNotEqual(h0, f.P.arm.PoseHash());
            }
        }

        [TestCase(15f)]
        [TestCase(22.5f)]
        [TestCase(8f)]
        public void VirtualArm_IsOffsetByOffsetCm_ToTheLeft_OnTheTablePlane(float offsetCm)
        {
            using (var f = new Fx("{\"condition_order\":\"sync_first\",\"induction_s\":40,\"offset_cm\":" + offsetCm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"))
            {
                f.ToInduction();
                var d = f.P.arm.WristWorld - f.Hands.Wrist;
                Assert.AreEqual(-offsetCm / 100f, d.x, 0.005f, "lateral offset (left = -x)");
                Assert.AreEqual(0f, d.y, 0.001f, "same table plane");
                Assert.AreEqual(0f, d.z, 0.001f);
                Assert.AreEqual(0f, Vector3.Angle(f.P.arm.transform.forward, Vector3.forward), 0.01f, "same yaw");
                Assert.AreEqual(0f, Vector3.Angle(f.P.arm.transform.up, Vector3.up), 0.01f, "palm down");
            }
        }

        [Test]
        public void VirtualArm_Offset_FollowsRotatedForearmAxis()
        {
            var go = new GameObject("arm");
            try
            {
                var arm = go.AddComponent<VirtualArmRig>();
                arm.Build(0.25f, 0.05f, 0.10f);
                var real = new Vector3(0.2f, 0.77f, 0.4f);
                var axis = Quaternion.Euler(0, 30, 0) * Vector3.forward;
                arm.PlaceFromCalibration(real, axis, 15f);
                var d = arm.WristWorld - real;
                Assert.AreEqual(0.15f, d.magnitude, 0.005f);
                Assert.AreEqual(0f, Vector3.Dot(d.normalized, axis), 0.01f, "offset is perpendicular to the forearm axis");
                Assert.Less(Vector3.Dot(d.normalized, Quaternion.Euler(0, 30, 0) * Vector3.right), -0.99f, "offset is to the left");
                Assert.AreEqual(0f, Vector3.Angle(arm.transform.forward, axis), 0.01f);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void VirtualArm_WorldPos_PlacesMotorsAlongTheForearm_AndAlphaSwapsMaterials()
        {
            var go = new GameObject("arm");
            try
            {
                var arm = go.AddComponent<VirtualArmRig>();
                arm.Build(0.25f, 0.05f, 0.10f);
                arm.PlaceFromCalibration(new Vector3(0.2f, 0.77f, 0.4f), Vector3.forward, 15f);
                var a = arm.WorldPos(5f); var b = arm.WorldPos(15f);
                Assert.AreEqual(0.10f, Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z)), 0.002f);
                Assert.AreEqual(0.05f, (a - arm.WristWorld).z * -1f, 0.002f);
                Assert.AreEqual(arm.WristWorld.x, a.x, 0.002f);
                // alpha: transparent twins below 1, hidden at 0, restored at 1
                var r = go.GetComponentsInChildren<MeshRenderer>().First(x => x.name == "Forearm");
                var opaque = r.sharedMaterial;
                arm.Alpha = 0.5f;
                Assert.AreNotSame(opaque, r.sharedMaterial);
                Assert.AreEqual(0.5f, r.sharedMaterial.GetColor("_BaseColor").a, 1e-3f);
                arm.Alpha = 0f; Assert.IsFalse(r.enabled);
                arm.Alpha = 1f; Assert.IsTrue(r.enabled); Assert.AreSame(opaque, r.sharedMaterial);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // -- threat --------------------------------------------------------------------------------------------

        [Test]
        public void TwentyDrops_AllCollideWithTheVirtualHand_WithinOneSecond()
        {
            var prev = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            var armGo = new GameObject("arm"); var dropGo = new GameObject("drop");
            try
            {
                var arm = armGo.AddComponent<VirtualArmRig>(); arm.Build(0.25f, 0.05f, 0.10f);
                arm.PlaceFromCalibration(new Vector3(0.18f, 0.771f, 0.40f), Vector3.forward, 15f);
                var drop = dropGo.AddComponent<ThreatDrop>(); drop.Build();
                var impacts = new List<KeyValuePair<double, bool>>();
                Vector3? last = null;
                drop.OnImpact += (ms, pos, ok) => { impacts.Add(new KeyValuePair<double, bool>(ms, ok)); last = pos; };
                double now = 0; const double dt = 1000.0 / 72.0;
                for (int i = 0; i < 20; i++)
                {
                    int before = impacts.Count;
                    Assert.IsTrue(drop.Begin(arm.PalmTopWorld + Vector3.up * 0.40f, arm.PalmTopWorld, now));
                    double t0 = now;
                    int guard = 0;
                    while (impacts.Count == before && guard++ < 400)
                    {
                        now += dt; drop.Tick(now); drop.CountStep((float)(dt / 1000.0)); Physics.Simulate((float)(dt / 1000.0));
                    }
                    Assert.AreEqual(before + 1, impacts.Count, "drop " + i + " produced no impact");
                    Assert.IsTrue(impacts[before].Value, "drop " + i + " ok");
                    double fall = impacts[before].Key - drop.ReleaseMs;
                    Assert.Less(fall, 1000.0, "drop " + i + " fall time");
                    Assert.Greater(fall, 150.0, "drop " + i + " fall time sanity");
                    Assert.AreEqual(arm.PalmTopWorld.x, last.Value.x, 0.06f);
                    Assert.AreEqual(0.6, (drop.ReleaseMs - t0) / 1000.0, 0.03, "telegraph length");
                    // let it roll off and fade, then next drop
                    for (int k = 0; k < 200 && drop.Phase != ThreatDrop.State.Done; k++) { now += dt; drop.Tick(now); drop.CountStep((float)(dt / 1000.0)); Physics.Simulate((float)(dt / 1000.0)); }
                    Assert.AreEqual(ThreatDrop.State.Done, drop.Phase);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(armGo); UnityEngine.Object.DestroyImmediate(dropGo); Physics.simulationMode = prev; }
        }

        [Test]
        public void Drop_IsAborted_WithALogLine_WhenTheRealHandIsUnderIt()
        {
            var armGo = new GameObject("arm"); var dropGo = new GameObject("drop");
            try
            {
                var arm = armGo.AddComponent<VirtualArmRig>(); arm.Build(0.25f, 0.05f, 0.10f);
                arm.PlaceFromCalibration(new Vector3(0.18f, 0.771f, 0.40f), Vector3.forward, 15f);
                var drop = dropGo.AddComponent<ThreatDrop>(); drop.Build();
                var top = arm.PalmTopWorld;
                drop.RealHandPosition = () => new Vector3(top.x + 0.02f, top.y, top.z);
                bool ok = true; int n = 0;
                drop.OnImpact += (ms, pos, o) => { ok = o; n++; };
                UnityEngine.TestTools.LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("threat aborted"));
                Assert.IsFalse(drop.Begin(top + Vector3.up * 0.4f, top, 0));
                Assert.AreEqual(1, n); Assert.IsFalse(ok);
                Assert.AreEqual(ThreatDrop.State.Done, drop.Phase);
                Assert.IsFalse(drop.Body.gameObject.activeSelf, "nothing falls");
            }
            finally { UnityEngine.Object.DestroyImmediate(armGo); UnityEngine.Object.DestroyImmediate(dropGo); }
        }

        [Test]
        public void Miss_IsReported_AsOkFalse_After2Seconds()
        {
            var prev = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            var dropGo = new GameObject("drop");
            try
            {
                var drop = dropGo.AddComponent<ThreatDrop>(); drop.Build();   // no hand anywhere: the stone falls through the air
                bool? ok = null; double at = 0;
                drop.OnImpact += (ms, pos, o) => { ok = o; at = ms; };
                drop.Begin(new Vector3(0, 100, 0), new Vector3(0, 99.6f, 0), 0);
                double now = 0;
                for (int i = 0; i < 400 && ok == null; i++) { now += 1000.0 / 72; drop.Tick(now); drop.CountStep(1f / 72f); Physics.Simulate(1f / 72f); }
                Assert.IsNotNull(ok); Assert.IsFalse(ok.Value);
                Assert.AreEqual(drop.ReleaseMs + 2000.0, at, 30.0);
            }
            finally { UnityEngine.Object.DestroyImmediate(dropGo); Physics.simulationMode = prev; }
        }

        [Test]
        public void FullThreat_EmitsImpactAndResponse_WithEmgMissingWhenNodeBIsAbsent()
        {
            using (var f = new Fx("{\"condition_order\":\"sync_first\",\"induction_s\":30}"))
            {
                f.ToInduction();
                f.StepUntil(() => f.M.CurrentPhase == PhPhase.Threat, 60000, "threat");
                f.StepUntil(() => f.M.CurrentPhase != PhPhase.Threat, 8000, "threat end");
                var imp = f.Events("threat_impact");
                Assert.AreEqual(1, imp.Count);
                Assert.IsTrue(imp[0]["ok"].Value<bool>());
                var resp = f.Events("threat_response");
                Assert.AreEqual(1, resp.Count, "threat_response must arrive before the 5 s threat phase ends");
                Assert.AreEqual("missing", (string)resp[0]["quality"]["emg"]);
                Assert.AreEqual("missing", (string)resp[0]["quality"]["imu"]);
                Assert.AreNotEqual("missing", (string)resp[0]["quality"]["wrist"], "hand tracking alone is a valid fallback");
                Assert.IsTrue(resp[0]["emg_peak_x"].Type == JTokenType.Null);
                Assert.IsTrue(resp[0]["emg_latency_ms"].Type == JTokenType.Null);
                Assert.IsTrue(imp[0]["impact_ms"].Value<double>() > f.M.InductionStartMs);
            }
        }

        // -- response collector (injected latencies) -----------------------------------------------------------------

        private static ThreatResponse Run(double impact, double? wristOnset, double? imuOnset, double? emgOnset, bool withNodeB, bool withNodeA = true)
        {
            var imu = new TimeSeriesRing<ImuSample>(s => s.TMs);
            var emg = new TimeSeriesRing<EmgSample>(s => s.TMs);
            var rng = new System.Random(3);
            var col = new ThreatResponseCollector(() => withNodeA ? imu : null, () => withNodeB ? emg : null);
            double t0 = impact - 2600, t1 = impact + 1700;
            int ver = 0;
            Vector3 pos = new Vector3(0.18f, 0.77f, 0.40f);
            double prevT = t0;
            for (double t = t0; t <= t1; t += 1000.0 / 72)
            {
                if (wristOnset.HasValue && t > impact + wristOnset.Value) pos += new Vector3(1.0f, 0, 0) * (float)((t - prevT) / 1000.0);   // 1 m/s
                prevT = t;
                col.PushWrist(t, pos, ++ver);
            }
            for (double t = t0; t <= t1; t += 10)
            {
                bool jolt = imuOnset.HasValue && t >= impact + imuOnset.Value && t < impact + imuOnset.Value + 200;
                imu.Add(new ImuSample { TMs = t, Ax = (rng.NextDouble() - 0.5) * 0.04, Ay = (rng.NextDouble() - 0.5) * 0.04, Az = 9.81 + (rng.NextDouble() - 0.5) * 0.04 + (jolt ? 4.0 : 0) });
                bool burst = emgOnset.HasValue && t >= impact + emgOnset.Value && t < impact + emgOnset.Value + 250;
                emg.Add(new EmgSample { TMs = t, Value = 5 + (rng.NextDouble() - 0.5) * 1.0 + (burst ? 60 : 0) });
            }
            col.Arm(impact);
            Assert.IsNull(col.Tick(impact + 1000), "not before 1.5 s have passed");
            var r = col.Tick(impact + 1500);
            Assert.IsNotNull(r);
            return r;
        }

        [Test]
        public void Collector_RecoversInjectedLatencies_Within15ms()
        {
            var r = Run(10000, 180, 150, 120, true);
            Assert.AreEqual(180.0, r.WristLatencyMs.Value, 15.0);
            Assert.AreEqual(150.0, r.ImuLatencyMs.Value, 15.0);
            Assert.AreEqual(120.0, r.EmgLatencyMs.Value, 15.0);
            Assert.AreEqual("ok", r.Quality["wrist"]); Assert.AreEqual("ok", r.Quality["imu"]); Assert.AreEqual("ok", r.Quality["emg"]);
            Assert.Greater(r.WristPeakMps.Value, 0.9); Assert.Greater(r.ImuPeak.Value, 3.0); Assert.Greater(r.EmgPeakX.Value, 5.0);
        }

        [Test]
        public void Collector_NodeBAbsent_GivesNullEmgAndAFlag_OtherStreamsStillWork()
        {
            var r = Run(10000, 180, 150, 120, false);
            Assert.IsNull(r.EmgPeakX); Assert.IsNull(r.EmgLatencyMs);
            Assert.AreEqual("missing", r.Quality["emg"]);
            Assert.AreEqual(180.0, r.WristLatencyMs.Value, 15.0);
            Assert.AreEqual(150.0, r.ImuLatencyMs.Value, 15.0);
        }

        [Test]
        public void Collector_HandTrackingAlone_IsAValidFallback()
        {
            var r = Run(10000, 180, null, null, false, withNodeA: false);
            Assert.AreEqual("missing", r.Quality["imu"]); Assert.AreEqual("missing", r.Quality["emg"]);
            Assert.AreEqual(180.0, r.WristLatencyMs.Value, 15.0);
            Assert.IsNull(r.ImuPeak);
        }

        [Test]
        public void Collector_SkipsRepeatedDataVersions()
        {
            var col = new ThreatResponseCollector(null, null);
            Assert.IsTrue(col.PushWrist(0, Vector3.zero, 1));
            Assert.IsFalse(col.PushWrist(5, Vector3.zero, 1));
            Assert.IsTrue(col.PushWrist(14, Vector3.zero, 2));
            Assert.AreEqual(2, col.WristSamples);
        }
    }
}
