using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.EditorTools;
using Opus.Games.PhantomHand.Presentation;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// The pure rules of the GLB prop bake (PropFit): pivots on the real files, the 25 % size check, the brush tip from the bristle vertices.
    /// No GameObjects, so it also runs outside Unity. Real files are read from the project folder (the editor's working directory).
    /// </summary>
    public class PropFitTests
    {
        [Test]
        public void Specs_NameTheSevenWrappers_AfterTheirFiles()
        {
            Assert.AreEqual(7, PropFit.Specs.Length);
            foreach (var s in PropFit.Specs)
            {
                Assert.AreEqual(s.Wrapper, Path.GetFileNameWithoutExtension(s.Glb), "wrapper name = file name");
                CollectionAssert.Contains(PhModels.All, s.Wrapper);
                Assert.IsTrue(File.Exists(s.Glb), s.Glb);
            }
            Assert.AreEqual(7, PropFit.Specs.Select(s => s.Wrapper).Distinct().Count());
        }

        // ---- size check ----------------------------------------------------------------------------------------------------------

        private static readonly float[] Lamp = { 0.384f, 0.882f, 0.384f };

        private static bool Plausible(float[] size, float[] expected) { string why; return PropFit.SizePlausible(size, expected, out why); }

        [Test]
        public void SizePlausible_AcceptsWithin25Percent_PerAxis()
        {
            Assert.IsTrue(Plausible(Lamp, Lamp));
            Assert.IsTrue(Plausible(new[] { 0.384f * 1.24f, 0.882f * 0.76f, 0.384f }, Lamp));
            Assert.IsFalse(Plausible(new[] { 0.384f * 1.26f, 0.882f, 0.384f }, Lamp), "x 26 % too big");
            Assert.IsFalse(Plausible(new[] { 0.384f, 0.882f * 0.74f, 0.384f }, Lamp), "y 26 % too small");
            Assert.IsFalse(Plausible(new[] { 0.384f, 0.882f, 0.384f * 1.3f }, Lamp), "z");
        }

        [Test]
        public void SizePlausible_RejectsTheUsualImportMistakes()
        {
            string why;
            Assert.IsFalse(PropFit.SizePlausible(Lamp.Select(v => v * 0.01f).ToArray(), Lamp, out why)); StringAssert.Contains("x is", why);
            Assert.IsFalse(Plausible(Lamp.Select(v => v * 100f).ToArray(), Lamp), "centimetres read as metres");
            Assert.IsFalse(Plausible(new[] { 0.882f, 0.384f, 0.384f }, Lamp), "a lamp lying on its side");
            Assert.IsFalse(Plausible(new[] { 0f, 0f, 0f }, Lamp), "nothing");
            Assert.IsFalse(Plausible(new[] { float.NaN, 0.882f, 0.384f }, Lamp), "NaN");
        }

        [Test]
        public void TheBriefsRawLampHeight_IsNotWithin25PercentOfTheRealBounds()
        {
            // the first brief read 1.22 m from the accessors, ignoring the node matrices; the node-transformed lamp is 0.882 m tall, so the check is made against the real one
            Assert.IsFalse(Plausible(new[] { 0.384f, 1.22f, 0.384f }, Lamp));
            Assert.AreEqual(0.882f, PropFit.Specs.Single(s => s.Wrapper == PhModels.Lamp).Size[1], 1e-6f);
        }

        // ---- pivots on the real files --------------------------------------------------------------------------------------------

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void RealFile_PivotsAsSpecified_AndIsPlausibleInSize(int spec)
        {
            var s = PropFit.Specs[spec];
            var model = GlbReader.Read(s.Glb);
            PropFit.Recentre(model, s.Pivot);
            float[] min, max; model.Bounds(out min, out max);
            float cx = (min[0] + max[0]) * 0.5f, cy = (min[1] + max[1]) * 0.5f, cz = (min[2] + max[2]) * 0.5f;
            switch (s.Pivot)
            {
                case PropPivot.TopCentre: Assert.AreEqual(0f, max[1], 1e-6f, s.Wrapper + " top at the origin"); Assert.AreEqual(0f, cx, 1e-6f); Assert.AreEqual(0f, cz, 1e-6f); break;
                case PropPivot.BottomCentre: Assert.AreEqual(0f, min[1], 1e-6f, s.Wrapper + " bottom at the origin"); Assert.AreEqual(0f, cx, 1e-6f); Assert.AreEqual(0f, cz, 1e-6f); break;
                case PropPivot.BackCentre: Assert.AreEqual(0f, min[2], 1e-6f, s.Wrapper + " back face at the origin"); Assert.AreEqual(0f, cx, 1e-6f); Assert.AreEqual(0f, cy, 1e-6f); break;
                default: Assert.AreEqual(0f, min[1], 1e-6f, s.Wrapper + " bristle tip at the origin"); Assert.AreEqual(0.193f, max[1], 1e-3f, "handle end 19.3 cm up"); break;
            }
            string why;
            Assert.IsTrue(PropFit.SizePlausible(new[] { max[0] - min[0], max[1] - min[1], max[2] - min[2] }, s.Size, out why), s.Wrapper + ": " + why);
        }

        [Test]
        public void TheWindow_StandsOutFromItsBackFace_ByTheSill()
        {
            var s = PropFit.Specs.Single(p => p.Wrapper == PhModels.Window);
            var model = GlbReader.Read(s.Glb); PropFit.Recentre(model, s.Pivot);
            float[] min, max; model.Bounds(out min, out max);
            Assert.AreEqual(0.18f, max[2], 1e-4f, "back face at z 0, sill 18 cm out on +z (the front)");
            Assert.AreEqual(-0.6f, min[1], 1e-4f); Assert.AreEqual(0.6f, max[1], 1e-4f);
            var glass = model.Parts.Single(p => p.Material.Name == "PH_Glass");
            Assert.AreEqual(0.056f, glass.Positions[2], 1e-4f, "the glass is 5.6 cm in front of the wall plane");
        }

        // ---- the brush tip ---------------------------------------------------------------------------------------------------------

        [Test]
        public void TheBrushTip_IsFoundFromTheBristleVertices_WhereverTheFileHasThem()
        {
            var s = PropFit.Specs.Single(p => p.Wrapper == PhModels.Brush);
            var a = GlbReader.Read(s.Glb); var oa = PropFit.Recentre(a, s.Pivot);
            var b = GlbReader.Read(s.Glb);
            foreach (var p in b.Parts) for (int i = 0; i < p.Positions.Length; i += 3) { p.Positions[i] += 0.3f; p.Positions[i + 1] -= 0.5f; p.Positions[i + 2] += 0.2f; }
            var ob = PropFit.Recentre(b, s.Pivot);
            Assert.AreEqual(oa[0] + 0.3f, ob[0], 1e-5f); Assert.AreEqual(oa[1] - 0.5f, ob[1], 1e-5f); Assert.AreEqual(oa[2] + 0.2f, ob[2], 1e-5f);
            for (int k = 0; k < a.Parts.Length; k++)
                for (int i = 0; i < a.Parts[k].Positions.Length; i++) Assert.AreEqual(a.Parts[k].Positions[i], b.Parts[k].Positions[i], 1e-5f);
            var bristle = a.Parts.Single(p => p.Material.Name == "PH_Bristle");
            Assert.AreEqual(0f, bristle.Positions.Where((v, i) => i % 3 == 1).Min(), 1e-6f, "the lowest bristle vertex is the origin");
            Assert.AreEqual(0.048f, bristle.Positions.Where((v, i) => i % 3 == 1).Max(), 1e-3f, "bristles 4.8 cm long");
        }

        [Test]
        public void ABrushWithTheBristlesAtTheTop_IsRefused_AndSoIsOneWithoutBristles()
        {
            var s = PropFit.Specs.Single(p => p.Wrapper == PhModels.Brush);
            var upsideDown = GlbReader.Read(s.Glb);
            foreach (var p in upsideDown.Parts) for (int i = 1; i < p.Positions.Length; i += 3) p.Positions[i] = -p.Positions[i];
            var ex = Assert.Throws<InvalidOperationException>(() => PropFit.Recentre(upsideDown, PropPivot.BristleTip));
            StringAssert.Contains("PH_Bristle", ex.Message);
            var cup = GlbReader.Read(PropFit.Specs.Single(p => p.Wrapper == PhModels.Cup).Glb);
            var ex2 = Assert.Throws<InvalidOperationException>(() => PropFit.Recentre(cup, PropPivot.BristleTip));
            StringAssert.Contains("PH_Bristle", ex2.Message);
        }
    }
}
