using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand;
using Opus.Sdk;

namespace Opus.Shell.Tests
{
    /// <summary>EditMode tests for the PH U5 plain-C# logic: endpoints, live status/trace, cue events, operator commands, scripted participant.</summary>
    public class PhantomEndpointsTests
    {
        [Test]
        public void ParseHostPort_Variants()
        {
            string h; int p;
            PhantomEndpoints.ParseHostPort("192.168.1.5:8790", out h, out p); Assert.AreEqual("192.168.1.5", h); Assert.AreEqual(8790, p);
            PhantomEndpoints.ParseHostPort("192.168.1.5", out h, out p); Assert.AreEqual("192.168.1.5", h); Assert.AreEqual(0, p);
            PhantomEndpoints.ParseHostPort("[::1]:9000", out h, out p); Assert.AreEqual("::1", h); Assert.AreEqual(9000, p);
            PhantomEndpoints.ParseHostPort("", out h, out p); Assert.IsNull(h);
            PhantomEndpoints.ParseHostPort("1.2.3.4:abc", out h, out p); Assert.IsNull(h);
            PhantomEndpoints.ParseHostPort("1.2.3.4:99999", out h, out p); Assert.IsNull(h);
            PhantomEndpoints.ParseHostPort(null, out h, out p); Assert.IsNull(h);
        }

        [Test]
        public void Resolve_EnvironmentWinsOverSettings_AndFlagsEnvironment()
        {
            var s = UnityEngine.ScriptableObject.CreateInstance<PhantomHandSettings>();
            s.hubHost = "10.0.0.2"; s.nodeAHost = "10.0.0.3"; s.nodeBHost = "10.0.0.4"; s.discoveryPort = 8791;
            var none = PhantomEndpoints.Resolve(n => null, s);
            Assert.AreEqual("10.0.0.2", none.HubHost); Assert.AreEqual("10.0.0.3", none.NodeAHost); Assert.AreEqual(8790, none.NodeAPort);
            Assert.IsFalse(none.FromEnvironment);

            var env = new Dictionary<string, string>
            {
                { PhantomEndpoints.EnvHub, "127.0.0.1:40123" }, { PhantomEndpoints.EnvNodeA, "127.0.0.1:39790" },
                { PhantomEndpoints.EnvNodeB, "127.0.0.1:39792" }, { PhantomEndpoints.EnvDiscoveryPort, "39791" },
            };
            var e = PhantomEndpoints.Resolve(k => env.ContainsKey(k) ? env[k] : null, s);
            Assert.AreEqual("127.0.0.1", e.HubHost); Assert.AreEqual(40123, e.HubPort);
            Assert.AreEqual("127.0.0.1", e.NodeAHost); Assert.AreEqual(39790, e.NodeAPort);
            Assert.AreEqual(39792, e.NodeBPort); Assert.AreEqual(39791, e.DiscoveryPort);
            Assert.IsTrue(e.FromEnvironment);
            UnityEngine.Object.DestroyImmediate(s);
        }

        [Test]
        public void Resolve_NullSettingsAndEnv_MeansDiscovery()
        {
            var e = PhantomEndpoints.Resolve(null, null);
            Assert.IsNull(e.HubHost); Assert.IsNull(e.NodeAHost); Assert.IsNull(e.NodeBHost);
            Assert.AreEqual(8791, e.DiscoveryPort); Assert.AreEqual(8790, e.NodeBPort);
        }
    }

    public class PhantomLiveStatusTests
    {
        [Test]
        public void GameState_HasAllRequiredKeys_AndNullsWhereUnknown()
        {
            var g = PhantomLiveStatus.GameState("induction", "sync", 41.54, true, false, 0.4);
            Assert.AreEqual("induction", (string)g["phase"]);
            Assert.AreEqual("sync", (string)g["condition"]);
            Assert.AreEqual(41.5, (double)g["remaining_s"], 1e-9);
            Assert.IsTrue((bool)g["nodes"]["haptic"]["connected"]);
            Assert.IsFalse((bool)g["nodes"]["bio"]["connected"]);
            Assert.AreEqual(JTokenType.Null, g["nodes"]["bio"]["emg_level"].Type, "emg_level is null while the bio node is offline");

            var idle = PhantomLiveStatus.GameState(null, null, null, false, false, null);
            Assert.AreEqual("idle", (string)idle["phase"]);
            Assert.AreEqual(JTokenType.Null, idle["condition"].Type);
            Assert.AreEqual(JTokenType.Null, idle["remaining_s"].Type);
        }

        [Test]
        public void GameState_EmgLevel_IsClamped()
        {
            var g = PhantomLiveStatus.GameState("agency", null, 5, true, true, 1.7);
            Assert.AreEqual(1.0, (double)g["nodes"]["bio"]["emg_level"], 1e-9);
        }

        private static List<KeyValuePair<double, double>> Series(double from, double to, double stepMs, Func<double, double> f)
        {
            var l = new List<KeyValuePair<double, double>>();
            for (double t = from; t <= to; t += stepMs) l.Add(new KeyValuePair<double, double>(t, f(t)));
            return l;
        }

        [Test]
        public void Trace_20HzBins_AreAveragedAndAligned()
        {
            var d = new TraceDownsampler();
            // 100 Hz EMG ramp 0..., accel constant 9.8; call at 1000 ms -> bins up to 900 ms, first call starts 500 ms back (400..900)
            var emg = Series(0, 1000, 10, t => t);
            var acc = Series(0, 1000, 10, t => 9.8);
            var tr = d.Build(emg, acc, 1000);
            Assert.IsNotNull(tr);
            Assert.AreEqual(20, (int)tr["fs_hz"]);
            Assert.AreEqual(400.0, (double)tr["t0_ms"], 1e-9);
            var e = (JArray)tr["emg_env"];
            Assert.AreEqual(10, e.Count);
            Assert.AreEqual(422.5, (double)e[0], 0.1, "mean of the samples at 400..440 ms");
            Assert.AreEqual(9.8, (double)((JArray)tr["accel_mag"])[3], 1e-6);
            // second call: continues exactly where the first stopped, no overlap
            var tr2 = d.Build(Series(900, 1500, 10, t => t), Series(900, 1500, 10, t => 9.8), 1500);
            Assert.AreEqual(900.0, (double)tr2["t0_ms"], 1e-9);
        }

        [Test]
        public void Trace_CapsAt100_AndSkipsOldest()
        {
            var d = new TraceDownsampler();
            d.Build(new List<KeyValuePair<double, double>>(), new List<KeyValuePair<double, double>>(), 1000);
            var tr = d.Build(Series(1000, 21000, 10, t => 1), Series(1000, 21000, 10, t => 1), 21000);
            Assert.AreEqual(100, ((JArray)tr["emg_env"]).Count);
            Assert.AreEqual(100, ((JArray)tr["accel_mag"]).Count);
            Assert.AreEqual(21000 - 100 - 100 * 50.0, (double)tr["t0_ms"], 1e-6);
        }

        [Test]
        public void Trace_GapsAreCarried_AndSilentStreamIsEmpty()
        {
            var d = new TraceDownsampler();
            var emg = new List<KeyValuePair<double, double>> { new KeyValuePair<double, double>(420, 100), new KeyValuePair<double, double>(620, 200) };
            var tr = d.Build(emg, new List<KeyValuePair<double, double>>(), 1000);
            var e = (JArray)tr["emg_env"];
            Assert.AreEqual(10, e.Count);
            Assert.AreEqual(100.0, (double)e[0], 1e-9, "leading gap takes the first value");
            Assert.AreEqual(100.0, (double)e[3], 1e-9, "interior gap carries");
            Assert.AreEqual(200.0, (double)e[9], 1e-9);
            Assert.AreEqual(0, ((JArray)tr["accel_mag"]).Count, "no accel samples = empty array");
        }

        [Test]
        public void Trace_LessThanOneBin_ReturnsNull()
        {
            var d = new TraceDownsampler();
            Assert.IsNotNull(d.Build(new List<KeyValuePair<double, double>>(), new List<KeyValuePair<double, double>>(), 1000));
            Assert.IsNull(d.Build(new List<KeyValuePair<double, double>>(), new List<KeyValuePair<double, double>>(), 1020));
        }

        [Test]
        public void Trace_FromRings_MatchesPureCore()
        {
            var emg = new TimeSeriesRing<EmgSample>(s => s.TMs);
            var imu = new TimeSeriesRing<ImuSample>(s => s.TMs);
            for (double t = 0; t <= 1000; t += 10)
            {
                emg.Add(new EmgSample { TMs = t, Value = 500 });
                imu.Add(new ImuSample { TMs = t, Ax = 0, Ay = 0, Az = 9.81 });
            }
            var tr = new TraceDownsampler().Build(emg, imu, 1000);
            Assert.AreEqual(10, ((JArray)tr["emg_env"]).Count);
            Assert.AreEqual(500.0, (double)((JArray)tr["emg_env"])[0], 1e-9);
            Assert.AreEqual(9.81, (double)((JArray)tr["accel_mag"])[0], 1e-6);
        }

        [Test]
        public void Trace_StatusStaysUnder4KB()
        {
            var d = new TraceDownsampler();
            var tr = d.Build(Series(0, 30000, 10, t => 412.5), Series(0, 30000, 10, t => 9.81), 30000);
            Assert.Less(tr.ToString(Newtonsoft.Json.Formatting.None).Length, 4096);
        }

        [Test]
        public void MetricsFor_OnlyKnownValues_AndConditionLabel()
        {
            Assert.IsNull(PhantomLiveStatus.MetricsFor(new ConditionResult { Condition = PhCondition.Sync }));
            var r = new ConditionResult { Condition = PhCondition.Sync, PreDriftCm = 0.5, PostDriftCm = 3.5, Ownership = 2 };
            r.Threat = new ThreatResponse { EmgPeakX = 5.2, EmgLatencyMs = 110 };
            r.Threat.Quality["emg"] = "degraded";
            var m = PhantomLiveStatus.MetricsFor(r);
            Assert.AreEqual(3.0, (double)m["drift_change_cm"]["value"], 1e-9);
            Assert.AreEqual("ok", (string)m["ownership"]["quality"]);
            Assert.AreEqual("degraded", (string)m["flinch_emg_peak_x"]["quality"]);
            Assert.AreEqual("sync", (string)m["condition"]["value"]);
            Assert.IsNull(m["flinch_imu_peak"]);
        }
    }

    public class HapticCueEventAdapterTests
    {
        private static HapticCueRecord Sent(string id, int motor, double sentMs) =>
            new HapticCueRecord { Cue = "stroke", CueId = id, Motor = motor, SentMs = sentMs, SentAtMs = sentMs, Delivered = false };

        private static List<TrialEvent> Collect(HapticCueEventAdapter a)
        {
            var l = new List<TrialEvent>(); a.OnEvent += l.Add; return l;
        }

        [Test]
        public void SendThenAck_WritesOneDeliveredEvent_WithConditionIndexOfTheSend()
        {
            int? cond = 0;
            var a = new HapticCueEventAdapter { Trial = () => cond };
            var ev = Collect(a);
            var r = Sent("stroke_1", 0, 26562);
            a.OnRecord(r, 26562);
            Assert.AreEqual(0, ev.Count, "nothing is written until the outcome is known");
            cond = 1; // condition changes before the ack arrives
            r.Delivered = true; r.AckLatencyMs = 13;
            a.OnRecord(r, 26575);
            Assert.AreEqual(1, ev.Count);
            var d = (JObject)ev[0].Data;
            Assert.AreEqual("haptic_cue", ev[0].Type);
            Assert.AreEqual(0, ev[0].Trial);
            Assert.AreEqual("stroke", (string)d["cue"]); Assert.AreEqual(0, (int)d["motor"]);
            Assert.IsTrue((bool)d["delivered"]); Assert.AreEqual(13.0, (double)d["ack_latency_ms"], 1e-9);
            Assert.AreEqual(1, a.Scheduled); Assert.AreEqual(1, a.Delivered); Assert.AreEqual(1.0, a.DeliveryRate, 1e-9);
        }

        [Test]
        public void DroppedBeforeSend_IsUndeliveredWithReason()
        {
            var a = new HapticCueEventAdapter { Trial = () => 1 };
            var ev = Collect(a);
            a.OnRecord(new HapticCueRecord { Cue = "stroke", CueId = "s", Motor = 1, SentMs = null, Delivered = false, Reason = "no_device" }, 5);
            Assert.AreEqual(1, ev.Count);
            var d = (JObject)ev[0].Data;
            Assert.IsFalse((bool)d["delivered"]); Assert.AreEqual("no_device", (string)d["reason"]);
            Assert.AreEqual(1, ev[0].Trial);
            Assert.AreEqual(0.0, a.DeliveryRate, 1e-9);
        }

        [Test]
        public void NoAck_TimesOut_AsNotDelivered()
        {
            var a = new HapticCueEventAdapter { Trial = () => 0 };
            var ev = Collect(a);
            a.OnRecord(Sent("s", 0, 1000), 1000);
            a.Tick(1000 + HapticCueEventAdapter.AckTimeoutMs - 1);
            Assert.AreEqual(0, ev.Count);
            a.Tick(1000 + HapticCueEventAdapter.AckTimeoutMs + 1);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual("no_ack", (string)((JObject)ev[0].Data)["reason"]);
            Assert.AreEqual(0, a.PendingCount);
        }

        [Test]
        public void Rejection_AndFlush_AreUndelivered()
        {
            var a = new HapticCueEventAdapter { Trial = () => 0 };
            var ev = Collect(a);
            var r1 = Sent("a", 0, 100); a.OnRecord(r1, 100);
            r1.Reason = "rate_limited"; a.OnRecord(r1, 110);
            a.OnRecord(Sent("b", 1, 200), 200);
            a.FlushAll();
            Assert.AreEqual(2, ev.Count);
            Assert.AreEqual("rate_limited", (string)((JObject)ev[0].Data)["reason"]);
            Assert.AreEqual("no_ack", (string)((JObject)ev[1].Data)["reason"]);
            Assert.AreEqual(2, a.Scheduled); Assert.AreEqual(0, a.Delivered);
        }

        [Test]
        public void MotorWindow_CoversPulseAndRingdown()
        {
            var a = new HapticCueEventAdapter();
            a.OnRecord(Sent("s", 0, 5000), 5000);
            Assert.IsFalse(a.InMotorWindow(4999));
            Assert.IsTrue(a.InMotorWindow(5000)); Assert.IsTrue(a.InMotorWindow(5250));
            Assert.IsFalse(a.InMotorWindow(5251));
        }

        [Test]
        public void OtherCues_AreIgnored()
        {
            var a = new HapticCueEventAdapter();
            var ev = Collect(a);
            a.OnRecord(new HapticCueRecord { Cue = "success", CueId = "x", SentMs = 1, Delivered = true }, 1);
            Assert.AreEqual(0, ev.Count); Assert.AreEqual(0, a.Scheduled);
        }
    }

    public class PhantomCommandRouterTests
    {
        private sealed class Ctx : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex => 0;
        }

        private static PhantomHandModule Module(out Ctx ctx, bool begin = true)
        {
            ctx = new Ctx();
            var m = new PhantomHandModule();
            m.Configure(new ParamSet(new JObject()), ctx);
            if (begin) m.Begin();
            return m;
        }

        [Test]
        public void UnknownCommand_IsNotHandled()
        {
            Ctx c; var m = Module(out c);
            Assert.IsFalse(PhantomCommandRouter.Handle("explode", null, m, true).Handled);
        }

        [Test]
        public void PhaseNext_AdvancesAnActiveRun_AndIsIgnoredWithoutOne()
        {
            Ctx c; var m = Module(out c);
            Assert.AreEqual(PhPhase.Calibrate, m.CurrentPhase);
            var r = PhantomCommandRouter.Handle("phase_next", null, m, true);
            Assert.IsTrue(r.Handled); Assert.IsNull(r.Error);
            Assert.AreEqual(PhPhase.ProbePre, m.CurrentPhase);
            Assert.AreEqual("calibrate", r.LogPhase);

            Ctx c2; var idle = Module(out c2, begin: false);
            var ignored = PhantomCommandRouter.Handle("phase_next", null, idle, false);
            Assert.IsTrue(ignored.Handled); Assert.IsNull(ignored.Error);
            Assert.AreEqual(PhPhase.Idle, idle.CurrentPhase);
        }

        [Test]
        public void AbortPhase_MovesOn_AndAsksForMotorStop()
        {
            Ctx c; var m = Module(out c);
            PhantomCommandRouter.Handle("phase_next", null, m, true);   // probe_pre
            var events = new List<TrialEvent>(); m.OnTrialEvent += events.Add;
            var r = PhantomCommandRouter.Handle("abort_phase", null, m, true);
            Assert.IsTrue(r.Handled); Assert.IsNull(r.Error); Assert.IsTrue(r.StopMotors);
            Assert.AreEqual(PhPhase.Induction, m.CurrentPhase);
            Assert.IsTrue(events.Any(e => e.Type == "drift_probe"), "the aborted probe is recorded as unconfirmed");
        }

        [Test]
        public void SetConditionOrder_Validation_AndLockAfterInduction()
        {
            Ctx c; var m = Module(out c);
            var bad = PhantomCommandRouter.Handle("set_condition_order", new JObject { ["condition_order"] = "random" }, m, true);
            Assert.IsNotNull(bad.Error);
            Assert.IsNotNull(PhantomCommandRouter.Handle("set_condition_order", null, m, true).Error);

            var ok = PhantomCommandRouter.Handle("set_condition_order", new JObject { ["condition_order"] = "sync_first" }, m, true);
            Assert.IsNull(ok.Error); Assert.AreEqual("sync_first", ok.PendingOrder);
            Assert.AreEqual(PhCondition.Sync, m.Machine.Order[0]);

            PhantomCommandRouter.Handle("phase_next", null, m, true);   // probe_pre
            PhantomCommandRouter.Handle("phase_next", null, m, true);   // induction
            Assert.AreEqual(PhPhase.Induction, m.CurrentPhase);
            var late = PhantomCommandRouter.Handle("set_condition_order", new JObject { ["condition_order"] = "async_first" }, m, true);
            Assert.IsNotNull(late.Error);
            Assert.IsNull(late.PendingOrder);
        }

        [Test]
        public void SetConditionOrder_WithNoRun_IsKeptForTheNextRun()
        {
            var r = PhantomCommandRouter.Handle("set_condition_order", new JObject { ["condition_order"] = "async_first" }, null, false);
            Assert.IsTrue(r.Handled); Assert.IsNull(r.Error); Assert.AreEqual("async_first", r.PendingOrder);
        }
    }

    public class ScriptedParticipantTests
    {
        private sealed class Ctx : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex => 0;
        }

        [Test]
        public void ScriptedHands_ReportsJointsAndAdvancesDataVersion()
        {
            var h = new ScriptedHands();
            double[] p, r;
            Assert.IsTrue(h.TryGetJointPose(OpusJoints.RWrist, out p, out r));
            Assert.AreEqual(0.771, p[1], 1e-9);
            int v0 = h.GetDataVersion(HandSide.Right); h.Step();
            Assert.AreEqual(v0 + 1, h.GetDataVersion(HandSide.Right));
            h.RightTracked = false;
            Assert.IsFalse(h.TryGetJointPose(OpusJoints.RIndexTip, out p, out r));
            Assert.AreEqual(TrackingConfidence.None, h.GetConfidence(HandSide.Right));
            Assert.IsTrue(h.TryGetJointPose(OpusJoints.Head, out p, out r));
            foreach (var j in OpusJoints.All.Where(x => x.StartsWith("l_"))) Assert.IsTrue(h.TryGetJointPose(j, out p, out r), j);
        }

        [Test]
        public void AutoParticipant_PlaysDemoRun_ThroughAllPhasesToDone()
        {
            var ctx = new Ctx();
            var m = new PhantomHandModule();
            m.Configure(new ParamSet(new JObject { ["demo_mode"] = true, ["induction_s"] = 30 }), ctx);
            var events = new List<TrialEvent>(); m.OnTrialEvent += events.Add;
            var hands = new ScriptedHands();
            var auto = new PhantomAutoParticipant(hands) { SubmitCalibrationItself = true, CalibrationTarget = () => new[] { 0.18, 0.771, 0.40 } };
            m.Begin();
            var phases = new List<PhPhase> { m.CurrentPhase };
            for (int i = 0; i < 400000 && !m.Machine.IsDone; i++)   // 20 ms steps, up to ~8000 s of session time
            {
                ctx.Clock.Advance(20);
                double now = ctx.Clock.NowMs;
                auto.Tick(now, m);
                // the UI presenter's job in the real scene: feed the probe from the hands
                if (m.CurrentPhase == PhPhase.ProbePre || m.CurrentPhase == PhPhase.ProbePost)
                {
                    double[] tip, wr, ri, rot;
                    hands.TryGetJointPose(OpusJoints.LIndexTip, out tip, out rot);
                    hands.TryGetJointPose(OpusJoints.RWrist, out wr, out rot);
                    hands.TryGetJointPose(OpusJoints.RIndexTip, out ri, out rot);
                    m.FeedProbe(now, true, tip, wr, ri[0]);
                }
                m.Tick(now);
                if (phases[phases.Count - 1] != m.CurrentPhase) phases.Add(m.CurrentPhase);
            }
            Assert.IsTrue(m.Machine.IsDone, "run did not finish; phases=" + string.Join(",", phases));
            var expected = new[] { PhPhase.Calibrate, PhPhase.ProbePre, PhPhase.Induction, PhPhase.Threat, PhPhase.ProbePost,
                                   PhPhase.ProbePre, PhPhase.Induction, PhPhase.Threat, PhPhase.ProbePost, PhPhase.Questionnaire,
                                   PhPhase.Witness, PhPhase.Done };
            CollectionAssert.AreEqual(expected, phases);
            Assert.AreEqual(2, events.Count(e => e.Type == "drift_probe" && (bool)Get(e, "confirmed") && (string)Get(e, "when") == "post"));
            Assert.AreEqual(3, events.Count(e => e.Type == "questionnaire_item"));
            Assert.IsTrue(m.Results[0].DriftChangeCm.HasValue && m.Results[1].DriftChangeCm.HasValue);
            var sync = m.Results.First(r => r.Condition == PhCondition.Sync);
            var asyn = m.Results.First(r => r.Condition == PhCondition.Async);
            Assert.Greater(sync.DriftChangeCm.Value, asyn.DriftChangeCm.Value, "scripted participant drifts more under SYNC");
        }

        private static object Get(TrialEvent e, string key)
        {
            var j = JObject.FromObject(e.Data);
            var t = j[key];
            return t.Type == JTokenType.Boolean ? (object)(bool)t : (string)t;
        }
    }
}
