using System;
using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Orientation of a RIGHT hand measured from its own bones (wrist, middle fingertip, index and pinky knuckles), so the importer never
    /// trusts a hard-coded import rotation (FBX axis conversion, node rotations, file units). Works in whatever space Unity imported the
    /// model in: <see cref="ToLocal"/> maps a source point into the wrapper frame used by VirtualArmRig: wrist at the origin, +z toward
    /// the fingers, +y dorsal (palm DOWN), thumb on -x, scaled so wrist -> middle fingertip = the requested hand length.
    /// Chirality: for a right hand in Unity's (left-handed) numbers the palm normal is cross(forward, thumb); a model that Unity imported
    /// mirrored would come out palm-up, which <see cref="PalmSideWitness"/> reports.
    /// </summary>
    public struct HandFrame
    {
        public Vector3 Wrist;          // source-space wrist (hand.R)
        public Vector3 Forward;        // unit, wrist -> middle fingertip
        public Vector3 Thumb;          // unit, toward the thumb side, orthogonal to Forward
        public Vector3 Palm;           // unit, palm normal (out of the palm)
        public float Scale;            // wrapper metres per source unit
        public float MeasuredLength;   // wrist -> middle fingertip in source units

        public Vector3 Dorsal { get { return -Palm; } }

        /// <summary>Source-space point -> wrapper space.</summary>
        public Vector3 ToLocal(Vector3 p)
        {
            Vector3 q = p - Wrist;
            return new Vector3(-Vector3.Dot(Thumb, q), Vector3.Dot(Dorsal, q), Vector3.Dot(Forward, q)) * Scale;
        }

        /// <summary>Source-space direction -> wrapper space (no translation, no scale).</summary>
        public Vector3 DirToLocal(Vector3 d)
        {
            return new Vector3(-Vector3.Dot(Thumb, d), Vector3.Dot(Dorsal, d), Vector3.Dot(Forward, d));
        }
    }

    /// <summary>Pure maths for the rigged hand: frame from bones, measurements of the posed mesh, curl schedule. No Unity objects, so EditMode tests feed it numbers.</summary>
    public static class PhHandFit
    {
        /// <summary>Underside of the hand relative to the wrist reference point P (the table plane, 0.5 mm above to avoid z-fighting).</summary>
        public const float TableFloorY = -0.0205f;
        /// <summary>The wrist band (from the cut end of the mesh) used to centre the hand on the arm axis and level it with the forearm.</summary>
        public const float WristBandM = 0.03f;
        /// <summary>Largest correction the importer applies when levelling / centring (anything bigger is a measurement failure).</summary>
        public const float MaxShiftM = 0.012f;
        /// <summary>Wrist to middle fingertip of the baked hand (the importer's target, the arm's plausibility check).</summary>
        public const float DefaultHandLenM = 0.19f;

        // ---- frame ----------------------------------------------------------------------------------------------------

        public static bool TryFrame(Vector3 wrist, Vector3 middleTip, Vector3 indexKnuckle, Vector3 pinkyKnuckle, float handLenM, out HandFrame frame)
        {
            frame = default(HandFrame);
            Vector3 d = middleTip - wrist;
            float len = d.magnitude;
            if (len < 1e-6f || handLenM <= 0f) return false;
            Vector3 f = d / len;
            Vector3 t0 = indexKnuckle - pinkyKnuckle;                   // pinky -> index: across the knuckles toward the thumb
            Vector3 t = t0 - f * Vector3.Dot(f, t0);
            float tl = t.magnitude;
            if (tl < 1e-4f * len) return false;
            t /= tl;
            Vector3 n = Vector3.Cross(f, t);                            // right hand, Unity numerics
            frame = new HandFrame { Wrist = wrist, Forward = f, Thumb = t, Palm = n.normalized, Scale = handLenM / len, MeasuredLength = len };
            return true;
        }

        /// <summary>Positive when the thumb lies on the palm side of the knuckle line (the rig's thumb is opposed toward the palm): an independent
        /// check of the palm side that does not use the chirality rule.</summary>
        public static float PalmSideWitness(HandFrame frame, Vector3 thumbPoint, Vector3 knuckleMid)
        {
            return Vector3.Dot(thumbPoint - knuckleMid, frame.Palm);
        }

        /// <summary>Largest projection of (p - origin) on dir over pts (the fingertip is the farthest vertex along the wrist -> middle direction).</summary>
        public static float FarthestAlong(IList<Vector3> pts, Vector3 origin, Vector3 dir, out int index)
        {
            index = -1;
            float best = float.NegativeInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                float s = Vector3.Dot(pts[i] - origin, dir);
                if (s > best) { best = s; index = i; }
            }
            return best;
        }

        // ---- skinning (the importer's vertex measurements) ---------------------------------------------------------------

        /// <summary>CPU skinning as Unity defines it: v' = sum(w_k * M_k * v) with M_k = bone_k.localToWorldMatrix * bindpose_k, i.e. world-space positions that do not depend on
        /// the renderer's own transform. Weights are renormalised; a vertex without any weight is returned unchanged. The importer uses this instead of
        /// SkinnedMeshRenderer.BakeMesh: run 2 measured BakeMesh(useScale: true) returning unscaled mesh units (the hand ~1.4 cm) for a renderer whose lossy scale is 100,
        /// which collapsed the whole hand onto the renderer's position.</summary>
        public static Vector3[] SkinVertices(IList<Vector3> vertices, IList<BoneWeight> weights, IList<Matrix4x4> boneToWorldTimesBind)
        {
            var o = new Vector3[vertices.Count];
            for (int i = 0; i < o.Length; i++)
            {
                BoneWeight bw = weights[i];
                Vector3 p = Vector3.zero; float sum = 0f;
                Accumulate(ref p, ref sum, boneToWorldTimesBind, bw.boneIndex0, bw.weight0, vertices[i]);
                Accumulate(ref p, ref sum, boneToWorldTimesBind, bw.boneIndex1, bw.weight1, vertices[i]);
                Accumulate(ref p, ref sum, boneToWorldTimesBind, bw.boneIndex2, bw.weight2, vertices[i]);
                Accumulate(ref p, ref sum, boneToWorldTimesBind, bw.boneIndex3, bw.weight3, vertices[i]);
                o[i] = sum > 1e-6f ? p / sum : vertices[i];
            }
            return o;
        }

        private static void Accumulate(ref Vector3 p, ref float sum, IList<Matrix4x4> m, int bone, float w, Vector3 v)
        {
            if (w <= 0f || bone < 0 || bone >= m.Count) return;
            p += m[bone].MultiplyPoint3x4(v) * w;
            sum += w;
        }

        /// <summary>Plausibility of a fitted hand in wrapper space (the rigged hand and the glove fallback): the fingertip at expectedLenM, the wrist end at or behind the origin
        /// (the glove keeps a 4.5 cm cuff), 9-20 cm wide, 3.5-9 cm thick and the thumb side (-x) reaching further than the pinky side. A collapsed or exploded mesh
        /// (run 2: a 2 mm speck at the fingertip position) must never be baked as the default hand.</summary>
        public static bool BoundsPlausible(Vector3 min, Vector3 max, float expectedLenM, out string why)
        {
            Vector3 s = max - min;
            if (Mathf.Abs(max.z - expectedLenM) > 0.01f) why = "fingertip at z " + max.z.ToString("F4") + " instead of " + expectedLenM.ToString("F3");
            else if (min.z > 0.01f || min.z < -0.06f) why = "wrist end at z " + min.z.ToString("F4");
            else if (s.x < 0.09f || s.x > 0.20f) why = "width " + s.x.ToString("F4");
            else if (s.y < 0.035f || s.y > 0.09f) why = "thickness " + s.y.ToString("F4");
            else if (-min.x < max.x) why = "the thumb side (-x) does not reach further than the pinky side";
            else why = null;
            return why == null;
        }

        // ---- measurements of the posed mesh (wrapper space) ------------------------------------------------------------

        public static float Percentile(IList<float> values, float p)
        {
            if (values.Count == 0) return 0f;
            var s = new List<float>(values);
            s.Sort();
            float f = Mathf.Clamp01(p) * (s.Count - 1);
            int i = Mathf.FloorToInt(f), j = Mathf.Min(s.Count - 1, i + 1);
            return Mathf.Lerp(s[i], s[j], f - i);
        }

        public struct WristStats
        {
            public float MinZ;        // the cut end of the mesh
            public float CenterX;     // lateral centre of the wrist band
            public float Bottom;      // underside (3rd percentile of y) of the wrist band
            public float Top;         // dorsal (97th percentile of y)
            public float Width;       // x extent (2nd..98th percentile)
            public int Count;
        }

        /// <summary>Wrist band: vertices within bandM of the lowest z.</summary>
        public static WristStats Wrist(IList<Vector3> localPts, float bandM)
        {
            var w = new WristStats();
            if (localPts.Count == 0) return w;
            float zmin = float.MaxValue;
            for (int i = 0; i < localPts.Count; i++) zmin = Mathf.Min(zmin, localPts[i].z);
            var xs = new List<float>(); var ys = new List<float>();
            for (int i = 0; i < localPts.Count; i++)
                if (localPts[i].z <= zmin + bandM) { xs.Add(localPts[i].x); ys.Add(localPts[i].y); }
            float x0 = Percentile(xs, 0.02f), x1 = Percentile(xs, 0.98f);
            w.MinZ = zmin; w.Count = xs.Count; w.CenterX = (x0 + x1) * 0.5f; w.Width = x1 - x0;
            w.Bottom = Percentile(ys, 0.03f); w.Top = Percentile(ys, 0.97f);
            return w;
        }

        /// <summary>Vertical correction that puts the wrist underside on the table plane level (clamped: a bigger value means the measurement failed).</summary>
        public static float LevelShiftY(float wristBottom)
        {
            return Mathf.Clamp(TableFloorY - wristBottom, -MaxShiftM, MaxShiftM);
        }

        /// <summary>Lateral correction that puts the wrist band on the arm axis (clamped like <see cref="LevelShiftY"/>).</summary>
        public static float CenterShiftX(float wristCenterX)
        {
            return Mathf.Clamp(-wristCenterX, -MaxShiftM, MaxShiftM);
        }

        /// <summary>Dorsal height (97th percentile of y) of the vertices inside the z range and the |x - centreX| window.</summary>
        public static float TopY(IList<Vector3> localPts, float z0, float z1, float centreX, float halfWidth)
        {
            var ys = new List<float>();
            for (int i = 0; i < localPts.Count; i++)
            {
                var p = localPts[i];
                if (p.z > z0 && p.z < z1 && Mathf.Abs(p.x - centreX) <= halfWidth) ys.Add(p.y);
            }
            return Percentile(ys, 0.97f);
        }

        // ---- rig: bone names and the curl schedule ----------------------------------------------------------------------

        public const string WristBone = "hand.R", ThumbName = "thumb";
        public static readonly string[] FingerNames = { "index", "middle", "ring", "pinky" };
        public static readonly string[] Digits = { "index", "middle", "ring", "pinky", ThumbName };

        /// <summary>Bone of a digit: segment 0 = "{digit}_base.R", 1..3 = "{digit}_01.R" .. "_03.R".</summary>
        public static string Bone(string digit, int segment)
        {
            return segment <= 0 ? digit + "_base.R" : digit + "_0" + segment + ".R";
        }

        /// <summary>The leaf bone at the tip of a digit's last driven bone.</summary>
        public static string EndBone(string digit) { return Bone(digit, 3) + "_end"; }

        // Closing angle added to each driven bone at curl = 1 (degrees, toward the palm). The rig carries two skinned chains per digit
        // in the file, but only {digit}_base/_01/_02/_03 (and hand.R, pulse.R) hold vertex weights: the parallel {digit}_Ctrl chain,
        // the {digit}_tip leaves and hand.R.001 have empty clusters, so driving the main chain is enough. _base (the metacarpal) stays put.
        private static readonly float[] FingerDeg = { 72f, 79f, 51f };    // MCP, PIP, DIP (the old procedural hand: 82/95/60 minus its relaxed 10/16/9)
        private static readonly float[] ThumbDeg = { 15f, 35f, 30f };

        /// <summary>Every bone driven by Curl, root to tip per digit.</summary>
        public static IEnumerable<string> DrivenBones()
        {
            foreach (var d in Digits) for (int s = 1; s <= 3; s++) yield return Bone(d, s);
        }

        /// <summary>Closing angle for a bone name (0 when the bone is not driven).</summary>
        public static float CurlDegrees(string boneName)
        {
            foreach (var d in Digits)
                for (int s = 1; s <= 3; s++)
                    if (boneName == Bone(d, s)) return (d == ThumbName ? ThumbDeg : FingerDeg)[s - 1];
            return 0f;
        }

        /// <summary>The bone that follows a driven bone along its digit (its direction defines the flexion axis); null for bones that are not driven.</summary>
        public static string ChildBone(string boneName)
        {
            foreach (var d in Digits)
                for (int s = 1; s <= 3; s++)
                    if (boneName == Bone(d, s)) return s < 3 ? Bone(d, s + 1) : EndBone(d);
            return null;
        }

        /// <summary>Flexion axis of a bone pointing along boneDir for a palm facing palmNormal: rotating boneDir about it by a positive angle turns it toward the palm.
        /// Zero when the bone points along the palm normal (degenerate).</summary>
        public static Vector3 FlexionAxis(Vector3 boneDir, Vector3 palmNormal)
        {
            Vector3 a = Vector3.Cross(boneDir, palmNormal);
            float m = a.magnitude;
            return m < 1e-6f ? Vector3.zero : a / m;
        }
    }
}
