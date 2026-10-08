using System;
using Opus.Games.PhantomHand.Presentation;

namespace Opus.Games.PhantomHand.EditorTools
{
    /// <summary>Where a prop's wrapper origin goes. Back centre = the middle of the face that sits on the wall (picture, window); bristle tip = the brush convention
    /// (tip at the origin, handle along +y).</summary>
    public enum PropPivot { TopCentre, BottomCentre, BackCentre, BristleTip }

    public sealed class PropSpec
    {
        public readonly string Wrapper, Glb;
        public readonly PropPivot Pivot;
        public readonly float[] Size;           // expected x, y, z extent in metres, measured from the node tree (not from the accessors)

        public PropSpec(string wrapper, string glb, PropPivot pivot, float x, float y, float z) { Wrapper = wrapper; Glb = glb; Pivot = pivot; Size = new[] { x, y, z }; }
    }

    /// <summary>
    /// Pure rules of the GLB prop bake (no Unity objects, so the tests also run outside the editor): which files become which wrappers, where their pivots go and
    /// whether the bake measured what the file should contain, in the spirit of PhHandFit.BoundsPlausible.
    /// The expected sizes are the bounds of the NODE-TRANSFORMED geometry. The first brief listed raw accessor bounds, which ignore the node matrices: they put the
    /// lamp at 1.22 m instead of 0.882 m and the bowl at 14 cm instead of 17.8 cm (its striker lies beside it), and a 25 % check against them would reject the lamp.
    /// </summary>
    public static class PropFit
    {
        public const float Tolerance = 0.25f;
        private const string Root = "Assets/Art/PhantomHand/Models/";

        public static readonly PropSpec[] Specs =
        {
            new PropSpec(PhModels.Brush, Root + "Brush/PH_Brush.glb", PropPivot.BristleTip, 0.032f, 0.193f, 0.032f),
            new PropSpec(PhModels.Lamp, Root + "Lamp/PH_PendantLamp.glb", PropPivot.TopCentre, 0.384f, 0.882f, 0.384f),
            new PropSpec(PhModels.Plant, Root + "Plant/PH_Plant.glb", PropPivot.BottomCentre, 0.563f, 0.579f, 0.572f),
            new PropSpec(PhModels.Window, Root + "Window/PH_Window.glb", PropPivot.BackCentre, 1.020f, 1.200f, 0.180f),
            new PropSpec(PhModels.Bowl, Root + "Props/PH_SingingBowl.glb", PropPivot.BottomCentre, 0.1535f, 0.090f, 0.1775f),
            new PropSpec(PhModels.Cup, Root + "Props/PH_TeaCup.glb", PropPivot.BottomCentre, 0.144f, 0.0685f, 0.144f),
            new PropSpec(PhModels.Picture, Root + "Props/PH_FramedPicture.glb", PropPivot.BackCentre, 0.500f, 0.400f, 0.025f),
        };

        /// <summary>True when every axis of the measured size is within <see cref="Tolerance"/> of the expected one. A model that came out 100x too small (centimetres read as
        /// metres), with a node transform missed or on the wrong axis does not pass, and neither does NaN.</summary>
        public static bool SizePlausible(float[] size, float[] expected, out string why)
        {
            why = null;
            string axes = "xyz";
            for (int i = 0; i < 3; i++)
                if (!(Math.Abs(size[i] - expected[i]) <= Tolerance * expected[i]))
                {
                    why = axes[i] + " is " + size[i].ToString("F4") + " m, expected " + expected[i].ToString("F4") + " m +-" + (Tolerance * 100f).ToString("F0") + " %";
                    return false;
                }
            return true;
        }

        /// <summary>Moves the model so that the pivot is at the origin (positions only) and returns where the pivot was. Centres are the middle of the bounding box;
        /// the brush tip comes from the vertices of its PH_Bristle material, never from the file's origin.</summary>
        public static float[] Recentre(GlbModel model, PropPivot pivot)
        {
            float[] min, max; model.Bounds(out min, out max);
            float[] o = { (min[0] + max[0]) * 0.5f, 0f, (min[2] + max[2]) * 0.5f };
            switch (pivot)
            {
                case PropPivot.TopCentre: o[1] = max[1]; break;
                case PropPivot.BottomCentre: o[1] = min[1]; break;
                case PropPivot.BackCentre: o[1] = (min[1] + max[1]) * 0.5f; o[2] = min[2]; break;
                default: o = BristleTip(model); break;
            }
            foreach (var p in model.Parts)
                for (int i = 0; i < p.Positions.Length; i++) p.Positions[i] -= o[i % 3];
            return o;
        }

        /// <summary>The brush's handle must point along +y (the bristles at the low end): the origin is the lowest bristle vertex, centred on the lowest 5 % of the bristles.</summary>
        private static float[] BristleTip(GlbModel model)
        {
            GlbPart bristle = null; double sumB = 0, sumR = 0; long nB = 0, nR = 0;
            foreach (var p in model.Parts)
            {
                bool isBristle = p.Material.Name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isBristle) bristle = p;
                for (int i = 1; i < p.Positions.Length; i += 3) { if (isBristle) { sumB += p.Positions[i]; nB++; } else { sumR += p.Positions[i]; nR++; } }
            }
            if (bristle == null) throw new InvalidOperationException(model.Source + ": no PH_Bristle material to find the brush tip from");
            if (nR == 0 || !(sumB / nB < sumR / nR)) throw new InvalidOperationException(model.Source + ": PH_Bristle is not at the low (-y) end, the handle must point along +y");
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 1; i < bristle.Positions.Length; i += 3) { lo = Math.Min(lo, bristle.Positions[i]); hi = Math.Max(hi, bristle.Positions[i]); }
            double sx = 0, sz = 0; int n = 0;
            for (int i = 0; i < bristle.Positions.Length; i += 3)
                if (bristle.Positions[i + 1] <= lo + 0.05f * (hi - lo)) { sx += bristle.Positions[i]; sz += bristle.Positions[i + 2]; n++; }
            return new[] { (float)(sx / n), lo, (float)(sz / n) };
        }
    }
}
