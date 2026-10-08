using System;
using UnityEngine;

namespace Opus.Games.PhantomHand.EditorTools
{
    /// <summary>
    /// Pure rules of the bake of the Asset Store props (PhantomModelImporter.Local.cs): which source axis becomes up and which one the width, how a pack that came in
    /// centimetres is brought back, where the pivot goes, whether the result is a plausible table / book stack / sideboard. Nothing here touches an asset or a GameObject
    /// (and no Quaternion or Matrix4x4, whose maths is native), so the tests run outside the editor, in the spirit of PropFit.
    ///
    /// The sources are measured, never assumed: the importer instantiates a copy at the origin and passes the world min / max of its mesh bounds. A <see cref="Fit"/> is the
    /// transform of the wrapper's "Fit" container, which holds that copy: q_wrapper = R (Scale * q_source) + Offset, with R the proper rotation whose rows are XSrc, YSrc, ZSrc.
    /// Unity applies localScale first, then localRotation, then localPosition, so the container gets Scale, Quaternion.Inverse(LookRotation(ZSrc, YSrc)) and Offset as they are.
    /// </summary>
    public static class PhLocalFit
    {
        /// <summary>Source unit corrections tried in this order: the authored size first, then centimetres read as metres (x0.01), then metres read as centimetres (x100), then feet read as
        /// metres (x0.3048: the furniture pack imports its cabinet at 3.97 x 3.39 x 1.62, which is a 1.21 x 1.03 x 0.49 m cabinet in feet).</summary>
        public static readonly float[] UnitFactors = { 1f, 0.01f, 100f, 0.3048f };

        /// <summary>Which source axis is tried as "up": Y as authored, then Z (a Z-up export that lost its root rotation), then X.</summary>
        public static readonly int[] UpOrder = { 1, 2, 0 };

        /// <summary>A table is never much taller than it is long: an axis whose extent is more than this many times the longer horizontal side is not up. (A sliver of a height needs no rule of its own:
        /// it would have to be stretched far beyond <see cref="TableMaxSqueeze"/>.)</summary>
        public const float TableHeightToLongMax = 1.3f;

        /// <summary>The most one stretch factor of the table may differ from another (the table is mapped to a fixed box, so a model of another proportion is distorted).</summary>
        public const float TableMaxSqueeze = 2.5f;

        public enum Pivot { TopCentre, BottomCentre, BackBottomCentre }

        public struct Fit
        {
            public Vector3 XSrc, YSrc, ZSrc;     // unit source-frame vectors that become the wrapper's +x, +y, +z (XSrc = YSrc x ZSrc: a rotation, never a mirror)
            public Vector3 Scale;                // along the SOURCE axes, applied first
            public Vector3 Offset;               // added last

            public Vector3 Map(Vector3 q)
            {
                var s = new Vector3(q.x * Scale.x, q.y * Scale.y, q.z * Scale.z);
                return new Vector3(Vector3.Dot(XSrc, s), Vector3.Dot(YSrc, s), Vector3.Dot(ZSrc, s)) + Offset;
            }
        }

        /// <summary>Sideboard limits in metres: width (the longer side seen from above), height, depth.</summary>
        public struct FurnitureRanges
        {
            public float WMin, WMax, HMin, HMax, DMin, DMax;
            public FurnitureRanges(float wMin, float wMax, float hMin, float hMax, float dMin, float dMax) { WMin = wMin; WMax = wMax; HMin = hMin; HMax = hMax; DMin = dMin; DMax = dMax; }
            public bool Contains(float w, float h, float d) { return w >= WMin && w <= WMax && h >= HMin && h <= HMax && d >= DMin && d <= DMax; }
        }

        public static readonly FurnitureRanges Sideboard = new FurnitureRanges(0.4f, 2.5f, 0.3f, 2.2f, 0.2f, 0.9f);

        public struct FurnitureResult
        {
            public Fit Fit;
            public int UpAxis, WidthAxis, DepthAxis;     // source axes (0 x, 1 y, 2 z)
            public float Unit;                           // 1, 0.01 or 100
            public Vector3 Size;                         // wrapper space: width, height, depth in metres
        }

        public struct TableResult
        {
            public Fit Fit;
            public int UpAxis, WidthAxis, DepthAxis;
            public Vector3 Factors;                      // applied to the width, height and depth (wrapper axes)
        }

        // ---- axes --------------------------------------------------------------------------------------------------------

        public static Vector3 Axis(int i) { return i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward; }

        public static float Comp(Vector3 v, int i) { return i == 0 ? v.x : i == 1 ? v.y : v.z; }

        /// <summary>"+y", "-x" ... for a unit axis vector (what the log says about the source orientation).</summary>
        public static string AxisName(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            int i = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
            return (Comp(v, i) < 0f ? "-" : "+") + "xyz"[i];
        }

        /// <summary>Of the two axes that are not up: the longer one is the width, the other the depth (a tie goes to the lower index).</summary>
        public static void Horizontal(Vector3 size, int up, out int width, out int depth)
        {
            int a = up == 0 ? 1 : 0, b = up == 2 ? 1 : 2;
            if (Comp(size, b) > Comp(size, a)) { width = b; depth = a; } else { width = a; depth = b; }
        }

        /// <summary>A frame with the given source axes as wrapper x and y, z = x cross y (so it is a rotation and the triangles keep their winding).</summary>
        public static Fit Frame(int widthAxis, int upAxis, Vector3 scale)
        {
            Vector3 x = Axis(widthAxis), y = Axis(upAxis);
            return new Fit { XSrc = x, YSrc = y, ZSrc = Vector3.Cross(x, y), Scale = scale };
        }

        // ---- bounds and pivots -------------------------------------------------------------------------------------------

        /// <summary>The box a source box [min, max] occupies in the wrapper once mapped (all eight corners; Offset included).</summary>
        public static void MappedBounds(Fit f, Vector3 min, Vector3 max, out Vector3 lo, out Vector3 hi)
        {
            lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var p = f.Map(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
        }

        /// <summary>Sets Offset so that the pivot point of the mapped box is the wrapper's origin; size is the mapped box. Back = the lowest wrapper z (the front is +z).</summary>
        public static Fit Anchor(Fit f, Vector3 min, Vector3 max, Pivot pivot, out Vector3 size)
        {
            f.Offset = Vector3.zero;
            Vector3 lo, hi; MappedBounds(f, min, max, out lo, out hi);
            size = hi - lo;
            float cx = (lo.x + hi.x) * 0.5f, cz = (lo.z + hi.z) * 0.5f;
            Vector3 p = pivot == Pivot.TopCentre ? new Vector3(cx, hi.y, cz) : pivot == Pivot.BottomCentre ? new Vector3(cx, lo.y, cz) : new Vector3(cx, lo.y, lo.z);
            f.Offset = -p;
            return f;
        }

        private static bool Finite(Vector3 s) { return s.x > 0f && s.y > 0f && s.z > 0f && !float.IsInfinity(s.x) && !float.IsInfinity(s.y) && !float.IsInfinity(s.z); }

        // ---- stone -------------------------------------------------------------------------------------------------------

        /// <summary>Uniform scale that makes the largest extent 1 (ThreatDrop then scales the unit stone to 11 cm). 0 when the bounds are empty or not numbers.</summary>
        public static float StoneScale(Vector3 size)
        {
            if (!Finite(size)) return 0f;
            return 1f / Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        }

        // ---- table -------------------------------------------------------------------------------------------------------

        /// <summary>Maps the measured table to a w x d x h box with the top surface centred at y = 0 (BuildTable's convention). The up axis is Y unless its extent is not a plausible
        /// height next to the longer horizontal side, then Z, then X; the longer horizontal side becomes the width. The scale is per axis (the table is stretched to the box).</summary>
        public static bool FitTable(Vector3 min, Vector3 max, float w, float d, float h, out TableResult res, out string why)
        {
            res = default(TableResult); why = null;
            Vector3 size = max - min;
            if (!Finite(size)) { why = "empty or non-finite bounds " + size.ToString("F4"); return false; }
            string firstWhy = null;
            foreach (int up in UpOrder)
            {
                int wa, da; Horizontal(size, up, out wa, out da);
                float height = Comp(size, up), longest = Comp(size, wa);
                if (height > TableHeightToLongMax * longest) continue;
                float fw = w / longest, fh = h / height, fd = d / Comp(size, da);
                float hiF = Mathf.Max(fw, Mathf.Max(fh, fd)), loF = Mathf.Min(fw, Mathf.Min(fh, fd));
                if (hiF > TableMaxSqueeze * loF)
                {
                    if (firstWhy == null)
                        firstWhy = "with " + "xyz"[up] + " up the stretch factors " + fw.ToString("F3") + " (width), " + fh.ToString("F3") + " (height), " + fd.ToString("F3") + " (depth) differ by more than "
                                   + TableMaxSqueeze.ToString("F1") + "x: not a table of about " + w.ToString("F1") + " x " + d.ToString("F1") + " x " + h.ToString("F2") + " m";
                    continue;
                }
                var scale = Vector3.one; scale = SetComp(scale, wa, fw); scale = SetComp(scale, up, fh); scale = SetComp(scale, da, fd);
                Vector3 sz;
                res.Fit = Anchor(Frame(wa, up, scale), min, max, Pivot.TopCentre, out sz);
                res.UpAxis = up; res.WidthAxis = wa; res.DepthAxis = da; res.Factors = new Vector3(fw, fh, fd);
                return true;
            }
            why = firstWhy ?? "no axis of " + size.ToString("F4") + " is a plausible table height (at most " + TableHeightToLongMax.ToString("F1") + " times the longer horizontal side)";
            return false;
        }

        private static Vector3 SetComp(Vector3 v, int i, float value)
        {
            if (i == 0) v.x = value; else if (i == 1) v.y = value; else v.z = value;
            return v;
        }

        // ---- sideboard ---------------------------------------------------------------------------------------------------

        /// <summary>Authored size, pivot = bottom centre of the back face, front = the source direction ZSrc (see <see cref="AxisName"/>), or the opposite one when flipFront is set (the front of a file
        /// cannot be read from its bounds: the bake says which direction it took, and flipFront turns the cabinet round about the vertical when that was the wrong one). For the unit factors in order
        /// (1, x0.01, x100, x0.3048) and, inside each, the up axes in order (Y, Z, X) the first assignment whose width / height / depth lie in the ranges wins; the file's size is corrected at most once.</summary>
        public static bool FitFurniture(Vector3 min, Vector3 max, FurnitureRanges r, bool flipFront, out FurnitureResult res, out string why)
        {
            res = default(FurnitureResult); why = null;
            Vector3 size = max - min;
            if (!Finite(size)) { why = "empty or non-finite bounds " + size.ToString("F4"); return false; }
            foreach (float k in UnitFactors)
                foreach (int up in UpOrder)
                {
                    int wa, da; Horizontal(size, up, out wa, out da);
                    if (!r.Contains(Comp(size, wa) * k, Comp(size, up) * k, Comp(size, da) * k)) continue;
                    Vector3 sz;
                    Fit f = Frame(wa, up, Vector3.one * k);
                    if (flipFront) { f.XSrc = -f.XSrc; f.ZSrc = -f.ZSrc; }                // a half turn about y keeps it a rotation
                    res.Fit = Anchor(f, min, max, Pivot.BackBottomCentre, out sz);
                    res.UpAxis = up; res.WidthAxis = wa; res.DepthAxis = da; res.Unit = k; res.Size = sz;
                    return true;
                }
            why = "bounds " + size.ToString("F4") + " m fit no width " + r.WMin.ToString("F1") + "-" + r.WMax.ToString("F1") + " / height " + r.HMin.ToString("F1") + "-" + r.HMax.ToString("F1")
                  + " / depth " + r.DMin.ToString("F1") + "-" + r.DMax.ToString("F1") + " m, not even at x0.01, x100 or x0.3048";
            return false;
        }

        // ---- books -------------------------------------------------------------------------------------------------------

        /// <summary>The axis a book lies on: its thinnest (Y wins a tie, then X).</summary>
        public static int ThinAxis(Vector3 size)
        {
            int t = 1;
            if (Comp(size, 0) < Comp(size, t)) t = 0;
            if (Comp(size, 2) < Comp(size, t)) t = 2;
            return t;
        }

        /// <summary>Stacks the books bottom to top at authored size, each laid on its thinnest side (a book already flat keeps its orientation) and turned about the vertical by its
        /// own yaw (degrees). The pivot is the bottom centre of the stack's box. Rejected unless every extent of the stack is within [minM, maxM].</summary>
        public static bool StackBooks(Vector3[] mins, Vector3[] maxs, float[] yawDeg, float minM, float maxM, out Fit[] fits, out Vector3 size, out string why)
        {
            fits = null; size = Vector3.zero; why = null;
            int n = mins == null ? 0 : mins.Length;
            if (n == 0 || maxs.Length != n || yawDeg.Length != n) { why = "no books"; return false; }
            fits = new Fit[n];
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            float y = 0f;
            for (int k = 0; k < n; k++)
            {
                Vector3 s = maxs[k] - mins[k];
                if (!Finite(s)) { why = "book " + k + ": empty or non-finite bounds " + s.ToString("F4"); fits = null; return false; }
                int t = ThinAxis(s), a = t == 0 ? 1 : 0;
                Fit f = Frame(a, t, Vector3.one);
                float c = Mathf.Cos(yawDeg[k] * Mathf.Deg2Rad), sn = Mathf.Sin(yawDeg[k] * Mathf.Deg2Rad);
                Vector3 x0 = f.XSrc, z0 = f.ZSrc;
                f.XSrc = c * x0 + sn * z0; f.ZSrc = -sn * x0 + c * z0;
                Vector3 sz;
                f = Anchor(f, mins[k], maxs[k], Pivot.BottomCentre, out sz);
                f.Offset += new Vector3(0f, y, 0f);
                y += sz.y;
                Vector3 l, h; MappedBounds(f, mins[k], maxs[k], out l, out h);
                lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, h);
                fits[k] = f;
            }
            size = hi - lo;                       // every book is centred on the stack axis and the first rests on y = 0, so the pivot is already the bottom centre of this box
            if (!(size.x >= minM && size.x <= maxM && size.y >= minM && size.y <= maxM && size.z >= minM && size.z <= maxM))
            {
                why = "the stack measures " + size.ToString("F3") + " m, every extent must be " + (minM * 100f).ToString("F0") + " to " + (maxM * 100f).ToString("F0") + " cm";
                fits = null; return false;
            }
            return true;
        }
    }
}
