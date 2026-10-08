using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.Presentation;
using Opus.Sdk;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// Where the wearer sits and how high the arm rests. The camera rig is moved so the head is at the scene's eye point (PhSeat);
    /// the calibration accepts an arm that rests higher or lower than the virtual table and then moves the rig so that the wrist is
    /// at table height (PhantomHandUiPresenter). On the first headset night no calibration ever completed.
    /// Category "UnityEngine": transforms and a presenter are built.
    /// </summary>
    [Category("UnityEngine")]
    public class SeatAndCalibrationHeightTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _made) if (g != null) Object.DestroyImmediate(g);
            _made.Clear();
        }

        private Transform New(string name, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            if (parent == null) _made.Add(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            return go.transform;
        }

        [Test]
        public void Seating_PutsTheHeadAtTheEyePoint_FacingTheTable_AndKeepsItsTilt()
        {
            // a wearer who stands 40 cm to the side and 30 cm back, 1.62 m tall, turned 40 degrees and looking 20 degrees down
            var rig = New("rig", null, new Vector3(0.7f, 0f, -0.2f), Quaternion.Euler(0f, 15f, 0f));
            var space = New("TrackingSpace", rig, Vector3.zero, Quaternion.identity);
            var head = New("CenterEyeAnchor", space, new Vector3(0.4f, 1.62f, -0.3f), Quaternion.Euler(20f, 40f, 0f));
            var eye = new Vector3(0f, 1.18f, 0.02f);
            float pitchBefore = Vector3.Angle(head.forward, Vector3.ProjectOnPlane(head.forward, Vector3.up));

            Assert.IsTrue(PhSeat.Align(rig, head, eye));
            Assert.Less((head.position - eye).magnitude, 1e-4f, "the head is at the eye point, in height too");
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Assert.Less(Vector3.Angle(flat, Vector3.forward), 0.01f, "it faces the table");
            Assert.AreEqual(pitchBefore, Vector3.Angle(head.forward, flat), 0.01f, "looking down stays looking down");
            Assert.AreEqual(new Vector3(0.4f, 1.62f, -0.3f), head.localPosition, "the headset's own numbers are untouched: only the rig moved");

            Assert.IsTrue(PhSeat.Align(rig, head, eye), "asking again changes nothing");
            Assert.Less((head.position - eye).magnitude, 1e-4f);
            Assert.IsFalse(PhSeat.Align(null, head, eye)); Assert.IsFalse(PhSeat.Align(rig, null, eye));
        }

        [Test]
        public void AHeadCannotJump_ButTheHeadsetsOriginCan()
        {
            var p = new Vector3(0.1f, 1.2f, 0f);
            Assert.IsFalse(PhSeat.Jumped(p, 10f, p + new Vector3(0.02f, 0f, 0.01f), 13f), "a head moving 2 cm and 3 degrees in a frame");
            Assert.IsTrue(PhSeat.Jumped(p, 10f, p + new Vector3(0.5f, 0f, 0.3f), 10f), "the origin moved");
            Assert.IsTrue(PhSeat.Jumped(p, 350f, p, 30f), "the origin turned 40 degrees (across 0)");
            Assert.IsFalse(PhSeat.Jumped(p, 359f, p, 2f), "3 degrees across 0 is not a jump");
        }

        [Test]
        public void TheTracker_MayIgnoreHeight_ButNotTheTablePlane()
        {
            var target = new[] { 0.18, 0.771, 0.40 };
            var strict = new CalibrationTracker(target);
            Assert.AreEqual(CalibState.Waiting, strict.Update(0, true, new[] { 0.18, 0.711, 0.40 }), "6 cm low: outside the 3 cm sphere");

            var loose = new CalibrationTracker(target, 0.04, 2000, 0.18);
            Assert.AreEqual(CalibState.Holding, loose.Update(0, true, new[] { 0.19, 0.711, 0.41 }), "6 cm low but over the outline");
            Assert.AreEqual(CalibState.Confirmed, loose.Update(2000, true, new[] { 0.19, 0.711, 0.41 }));
            Assert.AreEqual(0.711, loose.Wrist[1], 1e-9, "the measured height is kept");

            var beside = new CalibrationTracker(target, 0.04, 2000, 0.18);
            Assert.AreEqual(CalibState.Waiting, beside.Update(0, true, new[] { 0.24, 0.771, 0.40 }), "6 cm beside the outline is still outside");
            var tooLow = new CalibrationTracker(target, 0.04, 2000, 0.18);
            Assert.AreEqual(CalibState.Waiting, tooLow.Update(0, true, new[] { 0.18, 0.55, 0.40 }), "22 cm low is not an arm on a table");
        }

        // The rule of 9 Oct, after the first person on the headset: the arm may rest anywhere in a wide zone; what counts is that it
        // rests. The numbers of the first test are that person's: the wrist lay 23 cm from the outline's wrist point for a minute.
        [Test]
        public void TheTracker_TakesAnArmWhereItRests_OnceItIsStill()
        {
            var target = new[] { -0.18, 0.771, 0.40 };
            var c = new CalibrationTracker(target, 0.35, 2000, 0.25, 0.03);
            var rest = new[] { -0.07, 0.84, 0.58 };
            Assert.AreEqual(CalibState.Holding, c.Update(0, true, rest));
            Assert.AreEqual(CalibState.Holding, c.Update(1999, true, new[] { -0.072, 0.841, 0.585 }), "a resting arm trembles by millimetres");
            Assert.AreEqual(CalibState.Confirmed, c.Update(2000, true, rest));
            Assert.AreEqual(-0.07, c.Wrist[0], 0.002); Assert.AreEqual(0.58, c.Wrist[2], 0.003);

            // an arm that is still on its way does not confirm, however long it takes: 5 cm a second across the zone
            var moving = new CalibrationTracker(target, 0.35, 2000, 0.25, 0.03);
            for (int ms = 0; ms <= 4000; ms += 20)
                Assert.AreNotEqual(CalibState.Confirmed, moving.Update(ms, true, new[] { -0.25 + 0.00005 * ms, 0.80, 0.45 }), "moving at " + ms + " ms");
            // ... and confirms 2 s after it has come to rest
            double x = -0.25 + 0.00005 * 4000;
            Assert.AreEqual(CalibState.Holding, moving.Update(4020, true, new[] { x, 0.80, 0.45 }));
            CalibState st = CalibState.Holding;
            for (int ms = 4040; ms <= 6100 && st != CalibState.Confirmed; ms += 20) st = moving.Update(ms, true, new[] { x, 0.80, 0.45 });
            Assert.AreEqual(CalibState.Confirmed, st);
            Assert.AreEqual(x, moving.Wrist[0], 0.031, "the wrist that is recorded is where it came to rest");

            var outside = new CalibrationTracker(target, 0.35, 2000, 0.25, 0.03);
            Assert.AreEqual(CalibState.Waiting, outside.Update(0, true, new[] { 0.30, 0.80, 0.40 }), "48 cm away: the other side of the table");
            Assert.AreEqual(CalibState.Waiting, outside.Update(0, true, new[] { -0.18, 1.05, 0.40 }), "28 cm above the table: a hand in the air");
        }

        [Test]
        public void TheLeftArm_HasItsOwnWords()
        {
            StringAssert.Contains("left forearm", PhStrings.Get("calib_title", "en", HandSide.Left));
            StringAssert.Contains("right forearm", PhStrings.Get("calib_title", "en", HandSide.Right));
            StringAssert.Contains("left hand", PhStrings.Get("calib_lost", "en", HandSide.Left));
            string probe = PhStrings.ProbeInstruction("en", HandSide.Left);
            StringAssert.Contains("Keep your left hand still", probe); StringAssert.Contains("With your right index", probe);
            foreach (var key in new[] { "calib_title", "calib_lost", "probe_title" })
                Assert.AreNotEqual(PhStrings.Get(key, "hi", HandSide.Right), PhStrings.Get(key, "hi", HandSide.Left), key + " in Hindi");
            Assert.AreEqual(PhStrings.Get("calib_holding", "en"), PhStrings.Get("calib_holding", "en", HandSide.Left), "a text without a side is the same for both");
        }

        private sealed class OneArm : IHandSource
        {
            public HandSide Side = HandSide.Left;
            public double[] Wrist, Palm;
            public bool TryGetJointPose(string joint, out double[] pos, out double[] rot)
            {
                rot = new double[] { 0, 0, 0, 1 };
                pos = joint == PhArm.Wrist(Side) ? Wrist : joint == PhArm.Palm(Side) ? Palm : null;
                return pos != null;
            }
            public TrackingConfidence GetConfidence(HandSide side) { return side == Side ? TrackingConfidence.High : TrackingConfidence.None; }
            public bool IsTracked(HandSide side) { return side == Side; }
            public float GetPinchStrength(HandSide side) { return 0f; }
            public int GetDataVersion(HandSide side) { return 1; }
        }

        private sealed class Session : ISessionContext
        {
            public SessionClock Clock { get; } = SessionClock.Manual();
            public int BlockIndex { get { return 0; } }
        }

        [Test]
        public void AnArmThatRestsLowerThanTheVirtualTable_Calibrates_AndTheRoomComesDownToIt()
        {
            var root = New("root", null, Vector3.zero, Quaternion.identity);
            var anchors = root.gameObject.AddComponent<PhantomAnchors>();
            anchors.armRestOutline = New("outline", root, new Vector3(0.18f, 0.75f, 0.15f), Quaternion.identity);
            anchors.cameraRig = New("rig", root, Vector3.zero, Quaternion.identity);
            New("eye", anchors.cameraRig, new Vector3(0f, 1.18f, 0.02f), Quaternion.identity).gameObject.AddComponent<Camera>();

            var session = new Session();
            var module = new PhantomHandModule();
            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;
            module.Configure(Ph.P("{\"stimulated_side\":\"left\"}"), session);
            module.Begin();
            Assert.AreEqual(PhPhase.Calibrate, module.CurrentPhase);

            var ui = New("ui", root, Vector3.zero, Quaternion.identity).gameObject.AddComponent<PhantomHandUiPresenter>();
            ui.anchors = anchors; ui.attachPoke = false;
            // the real left forearm lies over the (mirrored) outline, on a table 7 cm lower than the virtual one
            Vector3 target = new Vector3(-0.18f, 0.771f, 0.40f);
            var hands = new OneArm { Wrist = new[] { -0.17, 0.701, 0.41 }, Palm = new[] { -0.17, 0.701, 0.50 } };
            ui.Bind(module, null, null, null, session.Clock, hands);
            Assert.Less((ui.CalibrationTarget() - target).magnitude, 1e-4f, "the outline is laid out for the left arm");

            for (int i = 0; i < 600 && module.CurrentPhase == PhPhase.Calibrate; i++)
            {
                session.Clock.Advance(13.9); module.Tick(session.Clock.NowMs); ui.Tick();
            }
            Assert.AreNotEqual(PhPhase.Calibrate, module.CurrentPhase, "the calibration completed although the arm rests 7 cm low");
            Assert.AreEqual(0.07f, anchors.cameraRig.position.y, 1e-4f, "the camera rig rose by the 7 cm the real table is lower: the room came down to the arm");
            var calib = events.First(e => e.Type == "calibration");
            var data = JObject.FromObject(calib.Data);
            Assert.IsTrue((bool)data["ok"]);
            Assert.AreEqual(0.771, (double)data["wrist_pos"][1], 1e-4, "the recorded wrist is where the wrist is in the room now: at table height");
            Assert.AreEqual(-0.18, (double)data["wrist_pos"][0], 1e-4, "and on the outline's wrist point: the room came to the arm sideways too");
        }

        // The first person on the headset (9 Oct): the left wrist lay 11 cm toward the middle and 18 cm further away than the outline,
        // 7 cm above the virtual table, and nothing confirmed in 60 s. It has to, and the room has to come to that arm.
        [Test]
        public void AnArmThatRestsBesideTheOutline_Calibrates_AndTheRoomComesToIt()
        {
            var root = New("root", null, Vector3.zero, Quaternion.identity);
            var anchors = root.gameObject.AddComponent<PhantomAnchors>();
            anchors.armRestOutline = New("outline", root, new Vector3(0.18f, 0.75f, 0.15f), Quaternion.identity);
            anchors.cameraRig = New("rig", root, Vector3.zero, Quaternion.identity);
            New("eye", anchors.cameraRig, new Vector3(0f, 1.18f, 0.02f), Quaternion.identity).gameObject.AddComponent<Camera>();

            var session = new Session();
            var module = new PhantomHandModule();
            var events = new List<TrialEvent>();
            module.OnTrialEvent += events.Add;
            module.Configure(Ph.P("{\"stimulated_side\":\"left\"}"), session);
            module.Begin();

            var ui = New("ui", root, Vector3.zero, Quaternion.identity).gameObject.AddComponent<PhantomHandUiPresenter>();
            ui.anchors = anchors; ui.attachPoke = false;
            var hands = new OneArm { Wrist = new[] { -0.07, 0.84, 0.58 }, Palm = new[] { -0.05, 0.84, 0.66 } };
            ui.Bind(module, null, null, null, session.Clock, hands);

            double doneAt = -1;
            for (int i = 0; i < 600 && module.CurrentPhase == PhPhase.Calibrate; i++)
            {
                session.Clock.Advance(13.9); module.Tick(session.Clock.NowMs); ui.Tick();
                if (i == 100) Assert.IsNull(ui.Calibration.Wrist, "nothing counts in the first 2 s: the hands that pinched to start are still in the air");
                doneAt = session.Clock.NowMs;
            }
            Assert.AreNotEqual(PhPhase.Calibrate, module.CurrentPhase, "the calibration completed where the arm rested");
            Assert.Less(doneAt, 6000, "2 s to settle, 2 s still, 1 s for the room to move");
            Vector3 moved = anchors.cameraRig.position;
            Assert.AreEqual(-0.11f, moved.x, 1e-3f); Assert.AreEqual(-0.069f, moved.y, 1e-3f); Assert.AreEqual(-0.18f, moved.z, 1e-3f);
            var data = JObject.FromObject(events.First(e => e.Type == "calibration").Data);
            Assert.IsTrue((bool)data["ok"]);
            Assert.AreEqual(-0.18, (double)data["wrist_pos"][0], 1e-3); Assert.AreEqual(0.771, (double)data["wrist_pos"][1], 1e-3);
            Assert.AreEqual(0.40, (double)data["wrist_pos"][2], 1e-3, "the wrist is on the outline's wrist point now");
            Assert.Greater((double)data["forearm_axis"][2], 0.9, "the forearm's own direction is kept: it points away from the wearer, a little inward");
            Assert.Greater((double)data["forearm_axis"][0], 0.1);
        }
    }
}
