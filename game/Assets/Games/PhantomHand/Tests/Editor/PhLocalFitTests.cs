using NUnit.Framework;
using Opus.Games.PhantomHand.EditorTools;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// The pure rules of the Asset Store prop bake (PhLocalFit): axes, units, pivots, ranges, the stack of books. Nothing here touches an asset, so it also runs outside Unity.
    /// The box the bake must produce is checked by pushing the measured source box through Fit.Map, exactly what the wrapper's "Fit" container does to the pack's mesh.
    /// </summary>
    public class PhLocalFitTests
    {
        private const float Tol = 1e-4f;

        private static void AssertV(Vector3 expected, Vector3 actual, float tol, string what)
        {
            Assert.AreEqual(expected.x, actual.x, tol, what + " x"); Assert.AreEqual(expected.y, actual.y, tol, what + " y"); Assert.AreEqual(expected.z, actual.z, tol, what + " z");
        }

        private static void AssertProperRotation(PhLocalFit.Fit f, string what)
        {
            Assert.AreEqual(1f, f.XSrc.magnitude, Tol, what + " |x|"); Assert.AreEqual(1f, f.YSrc.magnitude, Tol, what + " |y|"); Assert.AreEqual(1f, f.ZSrc.magnitude, Tol, what + " |z|");
            Assert.AreEqual(0f, Vector3.Dot(f.XSrc, f.YSrc), Tol, what + " x.y"); Assert.AreEqual(0f, Vector3.Dot(f.XSrc, f.ZSrc), Tol, what + " x.z"); Assert.AreEqual(0f, Vector3.Dot(f.YSrc, f.ZSrc), Tol, what + " y.z");
            Assert.AreEqual(1f, Vector3.Dot(f.XSrc, Vector3.Cross(f.YSrc, f.ZSrc)), Tol, what + ": determinant +1, a rotation and never a mirror (a mirror would turn the triangles inside out)");
        }

        private static void AssertBox(PhLocalFit.Fit f, Vector3 min, Vector3 max, Vector3 expectedLo, Vector3 expectedHi, string what)
        {
            Vector3 lo, hi; PhLocalFit.MappedBounds(f, min, max, out lo, out hi);
            AssertV(expectedLo, lo, 1e-4f, what + " lo"); AssertV(expectedHi, hi, 1e-4f, what + " hi");
        }

        // ---- axes and frames -------------------------------------------------------------------------------------------------------

        [Test]
        public void EveryFrame_IsAProperRotation_NeverAMirror()
        {
            for (int w = 0; w < 3; w++)
                for (int up = 0; up < 3; up++)
                {
                    if (w == up) continue;
                    var f = PhLocalFit.Frame(w, up, Vector3.one);
                    AssertProperRotation(f, "width " + w + " up " + up);
                    AssertV(PhLocalFit.Axis(w), f.XSrc, 0f, "x is the width axis"); AssertV(PhLocalFit.Axis(up), f.YSrc, 0f, "y is the up axis");
                }
        }

        [Test]
        public void Horizontal_TheLongerOfTheTwoOtherAxesIsTheWidth_ATieGoesToTheLowerIndex()
        {
            int w, d;
            PhLocalFit.Horizontal(new Vector3(1.6f, 0.8f, 0.9f), 1, out w, out d); Assert.AreEqual(0, w); Assert.AreEqual(2, d);
            PhLocalFit.Horizontal(new Vector3(0.9f, 0.8f, 1.6f), 1, out w, out d); Assert.AreEqual(2, w); Assert.AreEqual(0, d);
            PhLocalFit.Horizontal(new Vector3(0.5f, 1.6f, 0.9f), 0, out w, out d); Assert.AreEqual(1, w); Assert.AreEqual(2, d);
            PhLocalFit.Horizontal(new Vector3(0.7f, 0.9f, 0.7f), 1, out w, out d); Assert.AreEqual(0, w, "tie"); Assert.AreEqual(2, d);
        }

        [Test]
        public void AxisName_NamesTheSignedAxis_AndAFrameWithZAsWidthFacesTheNegativeX()
        {
            Assert.AreEqual("+x", PhLocalFit.AxisName(Vector3.right)); Assert.AreEqual("-z", PhLocalFit.AxisName(Vector3.back)); Assert.AreEqual("+y", PhLocalFit.AxisName(Vector3.up));
            // width along the source z, up along y: the front (wrapper +z) is the source's -x, because z cross y points to -x
            Assert.AreEqual("-x", PhLocalFit.AxisName(PhLocalFit.Frame(2, 1, Vector3.one).ZSrc));
            Assert.AreEqual("+z", PhLocalFit.AxisName(PhLocalFit.Frame(0, 1, Vector3.one).ZSrc), "the authored forward when the width is along x");
        }

        [Test]
        public void Map_AppliesTheScaleBeforeTheRotation_AndTheOffsetLast()
        {
            var f = PhLocalFit.Frame(2, 1, new Vector3(2f, 3f, 5f));           // wrapper x = source z, wrapper y = source y, wrapper z = -(source x)
            f.Offset = new Vector3(10f, 20f, 30f);
            AssertV(new Vector3(10f + 5f * 7f, 20f + 3f * 11f, 30f - 2f * 13f), f.Map(new Vector3(13f, 11f, 7f)), Tol, "scaled along the SOURCE axes, then turned, then moved");
        }

        // ---- the stone -------------------------------------------------------------------------------------------------------------

        [Test]
        public void StoneScale_MakesTheLargestExtentOne_NotTheMean()
        {
            Assert.AreEqual(0.5f, PhLocalFit.StoneScale(new Vector3(2f, 1f, 0.5f)), Tol);
            Assert.AreEqual(10f, PhLocalFit.StoneScale(new Vector3(0.1f, 0.08f, 0.09f)), Tol);
            var size = new Vector3(3f, 2f, 1f);
            Assert.AreEqual(1f, Mathf.Max(size.x, size.y, size.z) * PhLocalFit.StoneScale(size), Tol, "after the scale the largest extent is 1 m, so ThreatDrop's 11 cm is the stone's length");
        }

        [Test]
        public void StoneScale_RefusesEmptyAndNonNumericBounds()
        {
            Assert.AreEqual(0f, PhLocalFit.StoneScale(Vector3.zero));
            Assert.AreEqual(0f, PhLocalFit.StoneScale(new Vector3(1f, 0f, 1f)), "flat as a sheet");
            Assert.AreEqual(0f, PhLocalFit.StoneScale(new Vector3(float.NaN, 1f, 1f)));
            Assert.AreEqual(0f, PhLocalFit.StoneScale(new Vector3(float.PositiveInfinity, 1f, 1f)));
        }

        // ---- the table -------------------------------------------------------------------------------------------------------------

        private static readonly Vector3 TableLo = new Vector3(-0.6f, -0.75f, -0.35f), TableHi = new Vector3(0.6f, 0f, 0.35f);     // 1.2 wide x 0.7 deep x 0.75 high, top surface at y = 0

        private static PhLocalFit.TableResult Table(Vector3 min, Vector3 max)
        {
            PhLocalFit.TableResult res; string why;
            Assert.IsTrue(PhLocalFit.FitTable(min, max, 1.2f, 0.7f, 0.75f, out res, out why), why);
            return res;
        }

        [Test]
        public void FitTable_MapsAnUprightTable_ToTheBox_WithTheTopSurfaceAtZero()
        {
            var min = new Vector3(-0.8f, 0f, -0.45f); var max = new Vector3(0.8f, 0.8f, 0.45f);                  // 1.6 x 0.8 x 0.9
            var r = Table(min, max);
            Assert.AreEqual(1, r.UpAxis); Assert.AreEqual(0, r.WidthAxis); Assert.AreEqual(2, r.DepthAxis);
            AssertV(new Vector3(0.75f, 0.9375f, 0.7f / 0.9f), r.Factors, Tol, "stretch factors");
            AssertBox(r.Fit, min, max, TableLo, TableHi, "table");
            AssertProperRotation(r.Fit, "table");
        }

        [Test]
        public void FitTable_TheLongerHorizontalSideBecomesTheWidth_WhicheverAxisItIs()
        {
            var min = new Vector3(-0.45f, 0f, -0.8f); var max = new Vector3(0.45f, 0.8f, 0.8f);                  // long along z
            var r = Table(min, max);
            Assert.AreEqual(2, r.WidthAxis); Assert.AreEqual(0, r.DepthAxis);
            AssertBox(r.Fit, min, max, TableLo, TableHi, "long along z");
            AssertProperRotation(r.Fit, "long along z");
            AssertV(new Vector3(0f, 0f, 1f), r.Fit.XSrc, 0f, "wrapper x = source z");
        }

        [Test]
        public void FitTable_PutsThePivotAtTheTopCentre_WhereverTheSourceSits()
        {
            var min = new Vector3(3f, 1f, -2f); var max = new Vector3(4.6f, 1.8f, -1.1f);
            AssertBox(Table(min, max).Fit, min, max, TableLo, TableHi, "far from the origin");
        }

        [Test]
        public void FitTable_AModelLyingOnItsSide_IsStoodUp_WhenYIsNotAPlausibleHeight()
        {
            var min = Vector3.zero; var max = new Vector3(0.8f, 1.6f, 0.75f);                                    // y is 1.6 m next to a 0.8 m long side: not a table height
            var r = Table(min, max);
            Assert.AreEqual(2, r.UpAxis, "z is up"); Assert.AreEqual(1, r.WidthAxis); Assert.AreEqual(0, r.DepthAxis);
            AssertBox(r.Fit, min, max, TableLo, TableHi, "lying table");
            AssertProperRotation(r.Fit, "lying table");
        }

        [Test]
        public void FitTable_YIsNotTheHeight_WhenItIsFarLongerThanTheLongerHorizontalSide()
        {
            // 1.0 x 1.4 x 0.9: with y up the stretch factors (1.2, 0.54, 0.78) would still pass the squeeze limit, so only the height-to-length rule can tell that z is the height
            var min = Vector3.zero; var max = new Vector3(1.0f, 1.4f, 0.9f);
            var r = Table(min, max);
            Assert.AreEqual(2, r.UpAxis); Assert.AreEqual(1, r.WidthAxis); Assert.AreEqual(0, r.DepthAxis);
            AssertBox(r.Fit, min, max, TableLo, TableHi, "z up");
        }

        [Test]
        public void FitTable_RefusesWhatWouldHaveToBeStretchedBeyondTheLimit()
        {
            PhLocalFit.TableResult r; string why;
            Assert.IsFalse(PhLocalFit.FitTable(Vector3.zero, new Vector3(3f, 0.8f, 0.35f), 1.2f, 0.7f, 0.75f, out r, out why), "a 3 m console is not a 1.2 x 0.7 table");
            StringAssert.Contains("stretch", why);
            // 2.4 x 0.8 x 1.1 needs factors 0.5 / 0.94 / 0.64: within 2.5x of each other, so it is accepted
            Assert.IsTrue(PhLocalFit.FitTable(Vector3.zero, new Vector3(2.4f, 0.8f, 1.1f), 1.2f, 0.7f, 0.75f, out r, out why), why);
        }

        [Test]
        public void FitTable_RefusesEmptyAndNonNumericBounds()
        {
            PhLocalFit.TableResult r; string why;
            Assert.IsFalse(PhLocalFit.FitTable(Vector3.zero, Vector3.zero, 1.2f, 0.7f, 0.75f, out r, out why)); StringAssert.Contains("empty", why);
            Assert.IsFalse(PhLocalFit.FitTable(Vector3.zero, new Vector3(float.NaN, 1f, 1f), 1.2f, 0.7f, 0.75f, out r, out why));
            Assert.IsFalse(PhLocalFit.FitTable(Vector3.zero, new Vector3(1f, 100f, 1f), 1.2f, 0.7f, 0.75f, out r, out why), "no axis is a height next to the others");
        }

        // ---- the sideboard ---------------------------------------------------------------------------------------------------------

        private static PhLocalFit.FurnitureResult Furniture(Vector3 min, Vector3 max)
        {
            PhLocalFit.FurnitureResult res; string why;
            Assert.IsTrue(PhLocalFit.FitFurniture(min, max, PhLocalFit.Sideboard, false, out res, out why), why);
            return res;
        }

        [Test]
        public void FitFurniture_KeepsTheAuthoredSize_BottomCentreOfTheBackFace_FrontPlusZ()
        {
            var min = new Vector3(-0.4f, 0f, -0.2f); var max = new Vector3(0.4f, 0.5f, 0.2f);                    // 0.8 wide, 0.5 high, 0.4 deep
            var r = Furniture(min, max);
            Assert.AreEqual(1f, r.Unit); Assert.AreEqual(1, r.UpAxis); Assert.AreEqual(0, r.WidthAxis); Assert.AreEqual(2, r.DepthAxis);
            AssertBox(r.Fit, min, max, new Vector3(-0.4f, 0f, 0f), new Vector3(0.4f, 0.5f, 0.4f), "sideboard");
            AssertV(new Vector3(0.8f, 0.5f, 0.4f), r.Size, Tol, "size");
            Assert.AreEqual("+z", PhLocalFit.AxisName(r.Fit.ZSrc));
            AssertProperRotation(r.Fit, "sideboard");
        }

        [Test]
        public void FitFurniture_AWidthAlongZ_StillGivesBackFaceAtZeroAndAProperRotation()
        {
            var min = new Vector3(-0.2f, 0f, -0.4f); var max = new Vector3(0.2f, 0.5f, 0.4f);                    // wide along z
            var r = Furniture(min, max);
            Assert.AreEqual(2, r.WidthAxis); Assert.AreEqual(0, r.DepthAxis);
            AssertBox(r.Fit, min, max, new Vector3(-0.4f, 0f, 0f), new Vector3(0.4f, 0.5f, 0.4f), "sideboard along z");
            AssertProperRotation(r.Fit, "sideboard along z");
            Assert.AreEqual("-x", PhLocalFit.AxisName(r.Fit.ZSrc), "the log tells which source direction became the front");
        }

        [Test]
        public void FitFurniture_FlipFront_TurnsTheCabinetAHalfTurn_SameBox_StillARotation()
        {
            var min = new Vector3(-0.4f, 0f, -0.2f); var max = new Vector3(0.4f, 0.5f, 0.2f);
            PhLocalFit.FurnitureResult plain, flipped; string why;
            Assert.IsTrue(PhLocalFit.FitFurniture(min, max, PhLocalFit.Sideboard, false, out plain, out why), why);
            Assert.IsTrue(PhLocalFit.FitFurniture(min, max, PhLocalFit.Sideboard, true, out flipped, out why), why);
            Assert.AreEqual("+z", PhLocalFit.AxisName(plain.Fit.ZSrc)); Assert.AreEqual("-z", PhLocalFit.AxisName(flipped.Fit.ZSrc));
            AssertBox(flipped.Fit, min, max, new Vector3(-0.4f, 0f, 0f), new Vector3(0.4f, 0.5f, 0.4f), "flipped: the same box, the back face still at z = 0");
            AssertProperRotation(flipped.Fit, "flipped");
            Assert.AreEqual(0.4f, plain.Fit.Map(new Vector3(0f, 0.25f, 0.2f)).z, Tol, "the source's +z end is the front, at the wrapper's +z");
            Assert.AreEqual(0f, flipped.Fit.Map(new Vector3(0f, 0.25f, 0.2f)).z, Tol, "after the flip the source's +z end is the back");
        }

        [Test]
        public void FitFurniture_CorrectsACentimetreFileOnce_AndSaysSo()
        {
            var min = new Vector3(-40f, 0f, -20f); var max = new Vector3(40f, 50f, 20f);                         // 80 x 50 x 40: centimetres read as metres
            var r = Furniture(min, max);
            Assert.AreEqual(0.01f, r.Unit, 1e-7f);
            AssertV(new Vector3(0.8f, 0.5f, 0.4f), r.Size, Tol, "size after x0.01");
            AssertBox(r.Fit, min, max, new Vector3(-0.4f, 0f, 0f), new Vector3(0.4f, 0.5f, 0.4f), "scaled sideboard");
            Assert.AreEqual(0.01f, r.Fit.Scale.x, 1e-7f); Assert.AreEqual(0.01f, r.Fit.Scale.y, 1e-7f); Assert.AreEqual(0.01f, r.Fit.Scale.z, 1e-7f);
        }

        [Test]
        public void FitFurniture_CorrectsAFileThatIsAHundredTimesTooSmall()
        {
            var r = Furniture(new Vector3(-0.004f, 0f, -0.002f), new Vector3(0.004f, 0.005f, 0.002f));
            Assert.AreEqual(100f, r.Unit);
            AssertV(new Vector3(0.8f, 0.5f, 0.4f), r.Size, Tol, "size after x100");
        }

        [Test]
        public void FitFurniture_PrefersTheAuthoredSizeAndTheAuthoredUpAxis_BeforeAnyCorrection()
        {
            // 0.5 x 0.4 x 0.3 fits with y up, and also with z up (0.5 wide, 0.3 high, 0.4 deep): y, the authored axis, wins
            var r = Furniture(Vector3.zero, new Vector3(0.5f, 0.4f, 0.3f));
            Assert.AreEqual(1f, r.Unit); Assert.AreEqual(1, r.UpAxis);
        }

        [Test]
        public void FitFurniture_TriesZThenXAsUp_WhenYIsNotAPossibleHeight()
        {
            var r = Furniture(Vector3.zero, new Vector3(0.5f, 2.4f, 0.6f));                                      // y = 2.4 m is over the 2.2 m limit; z = 0.6 m high, 2.4 m wide, 0.5 m deep fits
            Assert.AreEqual(2, r.UpAxis); Assert.AreEqual(1, r.WidthAxis); Assert.AreEqual(0, r.DepthAxis);
            AssertProperRotation(r.Fit, "z up");
        }

        // shapes that no choice of up axis and no unit correction can turn into a sideboard (a box that is only a little off can be read with another axis up, so the limits themselves are pinned below)
        [TestCase(10f, 10f, 10f, "a cube over the width limit, in metres and in feet")]   // 2.51 would be a 0.77 m cube read as feet
        [TestCase(0.39f, 0.39f, 0.39f, "a cube under the width limit")]
        [TestCase(2.0f, 0.01f, 1.0f, "a sheet")]
        [TestCase(0.1f, 3.0f, 0.1f, "a pole")]
        [TestCase(0f, 0.5f, 0.4f, "nothing")]
        [TestCase(float.NaN, 0.5f, 0.4f, "NaN")]
        public void FitFurniture_RefusesWhatNoAxisAndNoUnitCanTurnIntoASideboard(float x, float y, float z, string what)
        {
            PhLocalFit.FurnitureResult r; string why;
            Assert.IsFalse(PhLocalFit.FitFurniture(Vector3.zero, new Vector3(x, y, z), PhLocalFit.Sideboard, false, out r, out why), what);
            Assert.IsNotNull(why);
        }

        [Test]
        public void SideboardRanges_AreTheBriefsNumbers_AndTheirEdgesAreInclusive()
        {
            var r = PhLocalFit.Sideboard;
            Assert.AreEqual(new[] { 0.4f, 2.5f, 0.3f, 2.2f, 0.2f, 0.9f }, new[] { r.WMin, r.WMax, r.HMin, r.HMax, r.DMin, r.DMax }, "width 0.4-2.5, height 0.3-2.2, depth 0.2-0.9 m");
            Assert.IsTrue(r.Contains(1f, 1f, 0.5f));
            Assert.IsTrue(r.Contains(0.4f, 1f, 0.5f)); Assert.IsFalse(r.Contains(0.399f, 1f, 0.5f));
            Assert.IsTrue(r.Contains(2.5f, 1f, 0.5f)); Assert.IsFalse(r.Contains(2.501f, 1f, 0.5f));
            Assert.IsTrue(r.Contains(1f, 0.3f, 0.5f)); Assert.IsFalse(r.Contains(1f, 0.299f, 0.5f));
            Assert.IsTrue(r.Contains(1f, 2.2f, 0.5f)); Assert.IsFalse(r.Contains(1f, 2.201f, 0.5f));
            Assert.IsTrue(r.Contains(1f, 1f, 0.2f)); Assert.IsFalse(r.Contains(1f, 1f, 0.199f));
            Assert.IsTrue(r.Contains(1f, 1f, 0.9f)); Assert.IsFalse(r.Contains(1f, 1f, 0.901f));
            Assert.IsFalse(r.Contains(float.NaN, 1f, 0.5f), "NaN is never in range");
        }

        [TestCase(2.5f, 0.5f, 0.4f)]
        [TestCase(0.4f, 0.5f, 0.2f)]
        [TestCase(0.8f, 0.3f, 0.4f)]
        [TestCase(0.8f, 2.2f, 0.4f)]
        [TestCase(2.0f, 0.5f, 0.9f)]
        public void FitFurniture_AcceptsTheLimitsThemselves(float x, float y, float z)
        {
            Furniture(Vector3.zero, new Vector3(x, y, z));
        }

        // ---- the books -------------------------------------------------------------------------------------------------------------

        private static readonly Vector3 FlatMin = new Vector3(-0.11f, 0f, -0.075f), FlatMax = new Vector3(0.11f, 0.03f, 0.075f);       // 22 x 3 x 15 cm, lying

        private static bool Stack(Vector3[] mins, Vector3[] maxs, float[] yaws, float minM, float maxM, out PhLocalFit.Fit[] fits, out Vector3 size, out string why)
        {
            return PhLocalFit.StackBooks(mins, maxs, yaws, minM, maxM, out fits, out size, out why);
        }

        private static Vector3[] Many(Vector3 v, int n) { var a = new Vector3[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        [Test]
        public void StackBooks_PilesTheBooksOnTopOfEachOther_AndPutsThePivotAtTheBottomCentre()
        {
            PhLocalFit.Fit[] f; Vector3 size; string why;
            var yaws = new[] { 0f, 8f, -6f, 11f };
            Assert.IsTrue(Stack(Many(FlatMin, 4), Many(FlatMax, 4), yaws, 0.05f, 0.40f, out f, out size, out why), why);
            // 4 x 3 cm; the widest footprint is the one turned by 11 degrees: 0.22 cos + 0.15 sin by 0.22 sin + 0.15 cos
            float c = Mathf.Cos(11f * Mathf.Deg2Rad), s = Mathf.Sin(11f * Mathf.Deg2Rad);
            AssertV(new Vector3(0.22f * c + 0.15f * s, 0.12f, 0.22f * s + 0.15f * c), size, 1e-4f, "stack size");
            for (int k = 0; k < 4; k++)
            {
                Vector3 lo, hi; PhLocalFit.MappedBounds(f[k], FlatMin, FlatMax, out lo, out hi);
                Assert.AreEqual(0.03f * k, lo.y, Tol, "book " + k + " rests on the one below"); Assert.AreEqual(0.03f * (k + 1), hi.y, Tol, "book " + k + " top");
                AssertProperRotation(f[k], "book " + k);
            }
            Vector3 slo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), shi = -slo;
            for (int k = 0; k < 4; k++) { Vector3 lo, hi; PhLocalFit.MappedBounds(f[k], FlatMin, FlatMax, out lo, out hi); slo = Vector3.Min(slo, lo); shi = Vector3.Max(shi, hi); }
            Assert.AreEqual(0f, slo.y, Tol, "bottom at y = 0");
            Assert.AreEqual(-shi.x, slo.x, Tol, "centred in x"); Assert.AreEqual(-shi.z, slo.z, Tol, "centred in z");
        }

        [Test]
        public void StackBooks_ABookStandingOnItsEdge_IsLaidOnItsThinnestSide()
        {
            PhLocalFit.Fit[] f; Vector3 size; string why;
            var min = new Vector3(0f, 0f, 0f); var max = new Vector3(0.03f, 0.22f, 0.15f);                       // 3 cm thick along x: a book standing on its spine
            Assert.IsTrue(Stack(new[] { min }, new[] { max }, new[] { 0f }, 0.01f, 0.40f, out f, out size, out why), why);
            AssertV(new Vector3(0.22f, 0.03f, 0.15f), size, Tol, "laid flat");
            AssertProperRotation(f[0], "standing book");
            Assert.AreEqual(1, PhLocalFit.ThinAxis(new Vector3(0.22f, 0.03f, 0.15f)));
            Assert.AreEqual(0, PhLocalFit.ThinAxis(new Vector3(0.03f, 0.22f, 0.15f)));
            Assert.AreEqual(2, PhLocalFit.ThinAxis(new Vector3(0.22f, 0.15f, 0.03f)));
            Assert.AreEqual(1, PhLocalFit.ThinAxis(new Vector3(0.1f, 0.1f, 0.1f)), "a tie keeps y");
        }

        [Test]
        public void StackBooks_ABookThatIsAlreadyFlat_KeepsItsOrientation()
        {
            PhLocalFit.Fit[] f; Vector3 size; string why;
            Assert.IsTrue(Stack(new[] { FlatMin }, new[] { FlatMax }, new[] { 0f }, 0.01f, 0.40f, out f, out size, out why), why);
            AssertV(Vector3.right, f[0].XSrc, 0f, "x"); AssertV(Vector3.up, f[0].YSrc, 0f, "y"); AssertV(Vector3.forward, f[0].ZSrc, 0f, "z");
        }

        [Test]
        public void StackBooks_APositiveYaw_TurnsTheBookTheWayQuaternionEulerDoes()
        {
            PhLocalFit.Fit[] f; Vector3 size; string why;
            Assert.IsTrue(Stack(new[] { FlatMin }, new[] { FlatMax }, new[] { 90f }, 0.01f, 0.40f, out f, out size, out why), why);
            AssertV(new Vector3(0.15f, 0.03f, 0.22f), size, Tol, "turned a quarter");
            // Quaternion.Euler(0, 90, 0) takes +x to -z (and +z to +x): the long end of the book at +x lands on -z
            AssertV(new Vector3(0f, 0.015f, -0.11f), f[0].Map(new Vector3(0.11f, 0.015f, 0f)), Tol, "+x end");
            AssertV(new Vector3(0.075f, 0.015f, 0f), f[0].Map(new Vector3(0f, 0.015f, 0.075f)), Tol, "+z side");
        }

        [Test]
        public void StackBooks_RefusesAStackThatIsNotBetween5And40cm_InEveryExtent()
        {
            PhLocalFit.Fit[] f; Vector3 size; string why;
            Assert.IsFalse(Stack(Many(FlatMin, 1), Many(FlatMax, 1), new[] { 0f }, 0.05f, 0.40f, out f, out size, out why), "one 3 cm book is lower than 5 cm"); StringAssert.Contains("5 to 40 cm", why); Assert.IsNull(f);
            Assert.IsTrue(Stack(Many(FlatMin, 2), Many(FlatMax, 2), new[] { 0f, 0f }, 0.05f, 0.40f, out f, out size, out why), "two books are 6 cm");
            Assert.IsTrue(Stack(Many(FlatMin, 13), Many(FlatMax, 13), new float[13], 0.05f, 0.40f, out f, out size, out why), "13 books are 39 cm");
            Assert.IsFalse(Stack(Many(FlatMin, 14), Many(FlatMax, 14), new float[14], 0.05f, 0.40f, out f, out size, out why), "14 books are 42 cm high");
            var atlasMin = new Vector3(-0.3f, 0f, -0.225f); var atlasMax = new Vector3(0.3f, 0.06f, 0.225f);       // 60 x 45 cm: too big a footprint
            Assert.IsFalse(Stack(new[] { atlasMin }, new[] { atlasMax }, new[] { 0f }, 0.05f, 0.40f, out f, out size, out why), "an atlas");
        }

        [Test]
        public void StackBooks_RefusesNothingAndNonsense()
        {
            PhLocalFit.Fit[] f; Vector3 size; string why;
            Assert.IsFalse(Stack(new Vector3[0], new Vector3[0], new float[0], 0.05f, 0.40f, out f, out size, out why));
            Assert.IsFalse(Stack(null, null, null, 0.05f, 0.40f, out f, out size, out why));
            Assert.IsFalse(Stack(Many(FlatMin, 3), Many(FlatMax, 2), new float[3], 0.05f, 0.40f, out f, out size, out why), "arrays of different length");
            Assert.IsFalse(Stack(new[] { Vector3.zero }, new[] { new Vector3(float.NaN, 1f, 1f) }, new[] { 0f }, 0.05f, 0.40f, out f, out size, out why));
        }
    }
}
