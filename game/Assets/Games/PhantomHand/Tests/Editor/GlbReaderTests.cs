using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opus.Games.PhantomHand.EditorTools;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// GlbReader on the seven real prop files and on small GLBs built in the test. Pure C# (no GameObjects), so it also runs outside Unity.
    /// Real files are read from the project folder (the working directory of the editor). The expected numbers come from a separate numpy reader
    /// (accessors, strides, node matrices) of the same files: triangle and vertex counts per material, and the bounds of the node-transformed geometry in glTF axes.
    /// The brief's triangle table counted indexed primitives only; the plant (stems, leaves), the window (sill) and the picture (moulding) also have NON-indexed
    /// primitives, which the reader has to draw too, so their totals are larger than the table's.
    /// </summary>
    public class GlbReaderTests
    {
        private sealed class Real
        {
            public string File; public int TableTris, NonIndexedTris;
            public string[] Materials; public int[] Tris, Verts; public float[] Min, Max;     // glTF axes, metres
            public Real(string file, int table, int nonIndexed, string[] mats, int[] tris, int[] verts, float[] min, float[] max)
            { File = file; TableTris = table; NonIndexedTris = nonIndexed; Materials = mats; Tris = tris; Verts = verts; Min = min; Max = max; }
        }

        private static readonly Real[] Files =
        {
            new Real("Assets/Art/PhantomHand/Models/Brush/PH_Brush.glb", 2112, 0, new[] { "PH_Bristle", "PH_Brass", "PH_Wood_Walnut" }, new[] { 640, 960, 512 }, new[] { 369, 574, 297 },
                     new[] { -0.016f, 0f, -0.016f }, new[] { 0.016f, 0.193f, 0.016f }),
            new Real("Assets/Art/PhantomHand/Models/Lamp/PH_PendantLamp.glb", 2304, 0, new[] { "PH_Brass", "PH_Cord", "PH_Linen_Shade", "PH_Bulb_Emissive" }, new[] { 592, 32, 960, 720 }, new[] { 371, 52, 539, 425 },
                     new[] { -0.192f, -0.882f, -0.192f }, new[] { 0.192f, 0f, 0.192f }),
            new Real("Assets/Art/PhantomHand/Models/Plant/PH_Plant.glb", 848, 396, new[] { "PH_Terracotta", "PH_Soil", "PH_Stem", "PH_Leaf" }, new[] { 720, 128, 216, 180 }, new[] { 410, 196, 648, 540 },
                     new[] { -0.2526f, 0f, -0.2817f }, new[] { 0.3105f, 0.5788f, 0.29f }),
            new Real("Assets/Art/PhantomHand/Models/Window/PH_Window.glb", 86, 236, new[] { "PH_Paint_Frame", "PH_Glass" }, new[] { 320, 2 }, new[] { 876, 4 },
                     new[] { -0.51f, 0f, -0.06f }, new[] { 0.51f, 1.2f, 0.12f }),
            new Real("Assets/Art/PhantomHand/Models/Props/PH_SingingBowl.glb", 2080, 0, new[] { "PH_Fabric_Oxblood", "PH_Brass", "PH_Wood_Walnut" }, new[] { 768, 1152, 160 }, new[] { 429, 637, 102 },
                     new[] { -0.0806f, 0f, -0.07f }, new[] { 0.0729f, 0.09f, 0.1075f }),
            new Real("Assets/Art/PhantomHand/Models/Props/PH_TeaCup.glb", 1504, 0, new[] { "PH_Ceramic_Glaze" }, new[] { 1504 }, new[] { 864 },
                     new[] { -0.072f, 0f, -0.072f }, new[] { 0.072f, 0.0685f, 0.072f }),
            new Real("Assets/Art/PhantomHand/Models/Props/PH_FramedPicture.glb", 16, 240, new[] { "PH_Wood_Walnut", "PH_Passepartout", "PH_Art_Canvas", "PH_Glass" }, new[] { 240, 12, 2, 2 }, new[] { 720, 24, 4, 4 },
                     new[] { -0.25f, -0.2f, 0f }, new[] { 0.25f, 0.2f, 0.025f }),
        };

        // ---- the seven real files ------------------------------------------------------------------------------------------------

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Triangles_AreAllThere_IndexedAndNot(int file)
        {
            var f = Files[file];
            int indexed, nonIndexed; RawTriangles(File.ReadAllBytes(f.File), out indexed, out nonIndexed);
            Assert.AreEqual(f.TableTris, indexed, f.File + ": the brief's table counts the indexed primitives");
            Assert.AreEqual(f.NonIndexedTris, nonIndexed, f.File + ": primitives without indices");
            var m = GlbReader.Read(f.File);
            Assert.AreEqual(f.TableTris + f.NonIndexedTris, m.TriangleCount, f.File);
            Assert.AreEqual(f.Tris.Sum(), m.TriangleCount);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Materials_AreMergedPerMaterial_InFileOrder(int file)
        {
            var f = Files[file]; var m = GlbReader.Read(f.File);
            CollectionAssert.AreEqual(f.Materials, m.Parts.Select(p => p.Material.Name).ToArray(), f.File);
            CollectionAssert.AreEqual(f.Tris, m.Parts.Select(p => p.TriangleCount).ToArray(), f.File + " triangles per material");
            CollectionAssert.AreEqual(f.Verts, m.Parts.Select(p => p.VertexCount).ToArray(), f.File + " vertices per material");
            foreach (var p in m.Parts)
            {
                Assert.AreEqual(p.VertexCount * 3, p.Normals.Length); Assert.AreEqual(p.VertexCount * 2, p.Uvs.Length);
                Assert.That(p.Indices.All(i => i >= 0 && i < p.VertexCount), "indices stay inside the part");
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Bounds_AreTheNodeTransformedOnes_MirroredInX(int file)
        {
            var f = Files[file]; var m = GlbReader.Read(f.File);
            float[] min, max; m.Bounds(out min, out max);
            // x is mirrored for Unity: [-xmax, -xmin]; y and z are the glTF values
            var expMin = new[] { -f.Max[0], f.Min[1], f.Min[2] }; var expMax = new[] { -f.Min[0], f.Max[1], f.Max[2] };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expMin[i], min[i], 1.5e-4f, f.File + " min " + "xyz"[i]);
                Assert.AreEqual(expMax[i], max[i], 1.5e-4f, f.File + " max " + "xyz"[i]);
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Winding_FollowsTheNormals_AfterTheHandednessFlip(int file)
        {
            var f = Files[file]; var m = GlbReader.Read(f.File);
            foreach (var p in m.Parts)
            {
                int ok, bad, degenerate; Agreement(p, false, out ok, out bad, out degenerate);
                Assert.Greater(ok + bad, 0, f.File + " " + p.Material.Name + ": no usable triangles");
                Assert.GreaterOrEqual(ok / (float)(ok + bad), 0.999f, f.File + " " + p.Material.Name + ": face normal against vertex normals (ok " + ok + ", bad " + bad + ", degenerate " + degenerate + ")");
                // the same measure with the winding left as in the file would see inside-out faces: the check is able to fail
                int ok2, bad2, deg2; Agreement(p, true, out ok2, out bad2, out deg2);
                Assert.LessOrEqual(ok2 / (float)(ok2 + bad2), 0.001f, f.File + " " + p.Material.Name + ": a reversed winding must be caught");
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Normals_AreUnitLength_AndUvsAreFinite(int file)
        {
            foreach (var p in GlbReader.Read(Files[file].File).Parts)
            {
                for (int i = 0; i < p.Normals.Length; i += 3)
                    Assert.AreEqual(1f, (float)Math.Sqrt(p.Normals[i] * p.Normals[i] + p.Normals[i + 1] * p.Normals[i + 1] + p.Normals[i + 2] * p.Normals[i + 2]), 1e-3f, p.Material.Name);
                Assert.That(p.Uvs.All(u => !float.IsNaN(u) && !float.IsInfinity(u)));
            }
        }

        [Test]
        public void Materials_CarryTheirFactorsAndImages()
        {
            var lamp = GlbReader.Read(Files[1].File).Parts.ToDictionary(p => p.Material.Name, p => p.Material);
            var bulb = lamp["PH_Bulb_Emissive"];
            Assert.AreEqual(1.6f, bulb.Emissive[0], 1e-5f, "emissiveFactor x KHR_materials_emissive_strength");
            Assert.AreEqual(0.5394795f * 1.6f, bulb.Emissive[1], 1e-4f); Assert.AreEqual(0.1946178f * 1.6f, bulb.Emissive[2], 1e-4f);
            Assert.AreEqual(0.4f, bulb.Roughness, 1e-6f); Assert.AreEqual(0f, bulb.Metallic);
            Assert.IsTrue(lamp["PH_Linen_Shade"].DoubleSided); Assert.IsFalse(lamp["PH_Brass"].DoubleSided);
            Assert.AreEqual(0.35f, lamp["PH_Brass"].Metallic, 1e-6f); Assert.AreEqual(0.38f, lamp["PH_Brass"].Roughness, 1e-6f);
            Assert.AreEqual(0f, lamp["PH_Brass"].Emissive[0]);
            Assert.AreEqual(0.5583404f, lamp["PH_Brass"].BaseColor[0], 1e-6f); Assert.AreEqual(1f, lamp["PH_Brass"].BaseColor[3]);

            var shade = lamp["PH_Linen_Shade"];
            AssertPng512(shade.BaseColorPng, "linen base colour"); AssertPng512(shade.NormalPng, "linen normal");
            Assert.AreEqual(0.5f, shade.NormalScale, 1e-6f);
            Assert.IsNull(lamp["PH_Cord"].BaseColorPng); Assert.IsNull(lamp["PH_Cord"].NormalPng);

            var glass = GlbReader.Read(Files[3].File).Parts.Single(p => p.Material.Name == "PH_Glass").Material;
            Assert.IsTrue(glass.Blend, "alphaMode BLEND"); Assert.AreEqual(0.25f, glass.BaseColor[3], 1e-6f); Assert.AreEqual(0.08f, glass.Roughness, 1e-6f);
            Assert.IsFalse(GlbReader.Read(Files[3].File).Parts.Single(p => p.Material.Name == "PH_Paint_Frame").Material.Blend);

            var walnut = GlbReader.Read(Files[0].File).Parts.Single(p => p.Material.Name == "PH_Wood_Walnut").Material;
            AssertPng512(walnut.BaseColorPng, "walnut base colour"); AssertPng512(walnut.NormalPng, "walnut normal"); Assert.AreEqual(0.6f, walnut.NormalScale, 1e-6f);
            Assert.AreEqual(1f, walnut.BaseColor[0], "no baseColorFactor: white");
        }

        private static void AssertPng512(byte[] png, string what)
        {
            Assert.IsNotNull(png, what);
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray(), what + ": PNG signature");
            Assert.AreEqual(512, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], what + ": IHDR width");
            Assert.AreEqual(512, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23], what + ": IHDR height");
        }

        // ---- small GLBs built here -----------------------------------------------------------------------------------------------

        // layout of the BIN chunk of Doc(): positions 0..36, normals 36..72, uvs 72..96, ushort indices 96..102 (+2 padding)
        private const int BinLen = 104;

        private static byte[] Bin(float[] pos, float[] nrm, float[] uv, ushort[] idx)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            foreach (var f in pos) w.Write(f);
            foreach (var f in nrm) w.Write(f);
            foreach (var f in uv) w.Write(f);
            foreach (var i in idx) w.Write(i);
            w.Write((ushort)0);
            return ms.ToArray();
        }

        private static byte[] TriangleBin()
        {
            return Bin(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, new float[] { 0, 0, 1, 0, 0, 1 }, new ushort[] { 0, 1, 2 });
        }

        private static JObject Doc()
        {
            return JObject.Parse(@"{
              'asset': {'version': '2.0'}, 'scene': 0, 'scenes': [{'nodes': [0]}],
              'nodes': [{'mesh': 0}],
              'meshes': [{'primitives': [{'attributes': {'POSITION': 0, 'NORMAL': 1, 'TEXCOORD_0': 2}, 'indices': 3, 'material': 0}]}],
              'materials': [{'name': 'm0'}],
              'accessors': [
                {'bufferView': 0, 'componentType': 5126, 'count': 3, 'type': 'VEC3'},
                {'bufferView': 1, 'componentType': 5126, 'count': 3, 'type': 'VEC3'},
                {'bufferView': 2, 'componentType': 5126, 'count': 3, 'type': 'VEC2'},
                {'bufferView': 3, 'componentType': 5123, 'count': 3, 'type': 'SCALAR'}],
              'bufferViews': [
                {'buffer': 0, 'byteOffset': 0, 'byteLength': 36}, {'buffer': 0, 'byteOffset': 36, 'byteLength': 36},
                {'buffer': 0, 'byteOffset': 72, 'byteLength': 24}, {'buffer': 0, 'byteOffset': 96, 'byteLength': 6}],
              'buffers': [{'byteLength': 104}]}");
        }

        private static byte[] Glb(JObject json, byte[] bin)
        {
            var j = Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)); int jp = (4 - j.Length % 4) % 4, bp = (4 - bin.Length % 4) % 4;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + j.Length + jp + 8 + bin.Length + bp));
            w.Write((uint)(j.Length + jp)); w.Write(0x4E4F534Au); w.Write(j); for (int i = 0; i < jp; i++) w.Write((byte)0x20);
            w.Write((uint)(bin.Length + bp)); w.Write(0x004E4942u); w.Write(bin); for (int i = 0; i < bp; i++) w.Write((byte)0);
            return ms.ToArray();
        }

        private static GlbModel Read(JObject json, byte[] bin) { return GlbReader.Parse(Glb(json, bin), "synthetic.glb"); }

        private static void AssertFloats(float[] expected, float[] actual, string what)
        {
            Assert.AreEqual(expected.Length, actual.Length, what + " length");
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], actual[i], 1e-5f, what + "[" + i + "]");
        }

        [Test]
        public void OneTriangle_IsMirroredInX_WithTheWindingReversed_AndVFlipped()
        {
            var part = Read(Doc(), TriangleBin()).Parts.Single();
            AssertFloats(new float[] { 0, 0, 0, -1, 0, 0, 0, 1, 0 }, part.Positions, "positions (x mirrored)");
            AssertFloats(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, part.Normals, "normals");
            AssertFloats(new float[] { 0, 1, 1, 1, 0, 0 }, part.Uvs, "uvs (v -> 1 - v)");
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, part.Indices, "winding reversed");
            // Unity's front face has the same cross product as the normal: (b - a) x (c - a) with a = v0, b = v2, c = v1 points along +z
            Assert.AreEqual(1f, Cross(part, 0), 1e-5f);
        }

        // z component of the face normal (b - a) x (c - a) of the first triangle
        private static float Cross(GlbPart p, int tri)
        {
            int a = p.Indices[tri * 3] * 3, b = p.Indices[tri * 3 + 1] * 3, c = p.Indices[tri * 3 + 2] * 3;
            float ux = p.Positions[b] - p.Positions[a], uy = p.Positions[b + 1] - p.Positions[a + 1], vx = p.Positions[c] - p.Positions[a], vy = p.Positions[c + 1] - p.Positions[a + 1];
            return ux * vy - uy * vx;
        }

        [Test]
        public void NodeTransforms_MatrixAndTrs_AreComposedDownTheTree()
        {
            var j = Doc();
            // root: translation (1,2,3), 90 degrees about z, scale 2; child: matrix (column-major) translating by (0.5, 0, 0)
            string s = Math.Sqrt(0.5).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            j["nodes"] = JArray.Parse("[{'translation':[1,2,3],'rotation':[0,0," + s + "," + s + "],'scale':[2,2,2],'children':[1]},"
                                      + "{'mesh':0,'matrix':[1,0,0,0, 0,1,0,0, 0,0,1,0, 0.5,0,0,1]}]");
            var part = Read(j, TriangleBin()).Parts.Single();
            // (0,0,0) -> (0.5,0,0) -> (1,0,0) -> (0,1,0) -> (1,3,3) -> x mirrored (-1,3,3); (1,0,0) -> (3,0,0) -> (0,3,0) -> (1,5,3) -> (-1,5,3); (0,1,0) -> (0.5,1,0) -> (1,2,0) -> (-2,1,0) -> (-1,3,3) -> (1,3,3)
            AssertFloats(new float[] { -1, 3, 3, -1, 5, 3, 1, 3, 3 }, part.Positions, "positions");
            AssertFloats(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, part.Normals, "the normal turns with the rotation only (about z: unchanged)");
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, part.Indices);
        }

        [Test]
        public void ARotatedNode_TurnsTheNormalsToo()
        {
            var j = Doc();
            string s = Math.Sqrt(0.5).ToString("R", System.Globalization.CultureInfo.InvariantCulture);       // 90 degrees about x (right-handed): y -> z, z -> -y
            j["nodes"] = JArray.Parse("[{'mesh':0,'rotation':[" + s + ",0,0," + s + "]}]");
            var part = Read(j, TriangleBin()).Parts.Single();
            AssertFloats(new float[] { 0, -1, 0, 0, -1, 0, 0, -1, 0 }, part.Normals, "normals");
            AssertFloats(new float[] { 0, 0, 0, -1, 0, 0, 0, 0, 1 }, part.Positions, "positions: (0,1,0) -> (0,0,1)");
        }

        [Test]
        public void ATrsNode_ScalesFirst_ThenRotates_ThenTranslates()
        {
            var j = Doc();
            string s = Math.Sqrt(0.5).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            j["nodes"] = JArray.Parse("[{'mesh':0,'rotation':[0,0," + s + "," + s + "],'scale':[1,2,1]}]");      // 90 degrees about z, y stretched 2x in the node's own frame
            var part = Read(j, TriangleBin()).Parts.Single();
            // (1,0,0) -> (1,0,0) -> (0,1,0); (0,1,0) -> (0,2,0) -> (-2,0,0); x mirrored for Unity
            AssertFloats(new float[] { 0, 0, 0, 0, 1, 0, 2, 0, 0 }, part.Positions, "scaled before it is rotated");
        }

        [Test]
        public void ANonUniformScale_TransformsNormalsWithTheInverseTranspose()
        {
            var j = Doc(); j["nodes"] = JArray.Parse("[{'mesh':0,'scale':[2,1,1]}]");
            float r = (float)Math.Sqrt(0.5);
            var bin = Bin(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, new float[] { r, r, 0, r, r, 0, r, r, 0 }, new float[] { 0, 0, 1, 0, 0, 1 }, new ushort[] { 0, 1, 2 });
            var part = Read(j, bin).Parts.Single();
            // inverse transpose of diag(2,1,1) is diag(0.5,1,1): (r,r,0) -> (0.5r, r, 0) -> normalised (0.4472, 0.8944, 0); x mirrored: -0.4472
            AssertFloats(new float[] { -0.4472136f, 0.8944272f, 0f, -0.4472136f, 0.8944272f, 0f, -0.4472136f, 0.8944272f, 0f }, part.Normals, "normals");
            AssertFloats(new float[] { 0, 0, 0, -2, 0, 0, 0, 1, 0 }, part.Positions, "positions");
        }

        [Test]
        public void ANodeThatMirrorsItself_KeepsTheFacesPointingOut()
        {
            var j = Doc(); j["nodes"] = JArray.Parse("[{'mesh':0,'scale':[-1,1,1]}]");
            var part = Read(j, TriangleBin()).Parts.Single();
            AssertFloats(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, part.Positions, "two mirrors cancel");
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, part.Indices, "the node's own mirror already reversed the winding");
            Assert.AreEqual(1f, Cross(part, 0), 1e-5f);
        }

        [TestCase(5121)] [TestCase(5123)] [TestCase(5125)]
        public void IndexTypes_UByte_UShort_UInt_AreRead(int componentType)
        {
            var j = Doc(); ((JObject)j["accessors"][3])["componentType"] = componentType;
            int size = componentType == 5121 ? 1 : componentType == 5123 ? 2 : 4;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(TriangleBin(), 0, 96);
            for (int i = 0; i < 3; i++) { if (size == 1) w.Write((byte)i); else if (size == 2) w.Write((ushort)i); else w.Write((uint)i); }
            while (ms.Length % 4 != 0) w.Write((byte)0);
            j["bufferViews"][3]["byteLength"] = 3 * size;
            Assert.AreEqual(new[] { 0, 2, 1 }, Read(j, ms.ToArray()).Parts.Single().Indices);
        }

        [Test]
        public void APrimitiveWithoutIndices_IsADrawArrayList()
        {
            var j = Doc(); ((JObject)j["meshes"][0]["primitives"][0]).Remove("indices");
            var part = Read(j, TriangleBin()).Parts.Single();
            Assert.AreEqual(1, part.TriangleCount);
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, part.Indices);
            j["accessors"][0]["count"] = 2; j["accessors"][1]["count"] = 2; j["accessors"][2]["count"] = 2;
            var ex = Assert.Throws<InvalidDataException>(() => Read(j, TriangleBin()));
            StringAssert.Contains("synthetic.glb", ex.Message);
        }

        [Test]
        public void ByteStrideAndOffsets_AreRespected_ForInterleavedAttributes()
        {
            // one bufferView, stride 32: position (12) normal (12) uv (8) per vertex, view starts 8 bytes into the BIN chunk, accessors at +0 / +12 / +24
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(0xDEADBEEFu); w.Write(0xDEADBEEFu);
            float[][] pos = { new float[] { 0, 0, 0 }, new float[] { 1, 0, 0 }, new float[] { 0, 1, 0 } };
            float[][] uv = { new float[] { 0, 0 }, new float[] { 1, 0 }, new float[] { 0, 1 } };
            for (int i = 0; i < 3; i++) { foreach (var f in pos[i]) w.Write(f); w.Write(0f); w.Write(0f); w.Write(1f); foreach (var f in uv[i]) w.Write(f); }
            foreach (var i in new ushort[] { 0, 1, 2, 0 }) w.Write(i);
            var j = Doc();
            j["bufferViews"] = JArray.Parse("[{'buffer':0,'byteOffset':8,'byteLength':96,'byteStride':32},{'buffer':0,'byteOffset':104,'byteLength':6}]");
            j["accessors"] = JArray.Parse("[{'bufferView':0,'byteOffset':0,'componentType':5126,'count':3,'type':'VEC3'},{'bufferView':0,'byteOffset':12,'componentType':5126,'count':3,'type':'VEC3'},"
                                          + "{'bufferView':0,'byteOffset':24,'componentType':5126,'count':3,'type':'VEC2'},{'bufferView':1,'componentType':5123,'count':3,'type':'SCALAR'}]");
            var part = Read(j, ms.ToArray()).Parts.Single();
            AssertFloats(new float[] { 0, 0, 0, -1, 0, 0, 0, 1, 0 }, part.Positions, "positions");
            AssertFloats(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, part.Normals, "normals");
            AssertFloats(new float[] { 0, 1, 1, 1, 0, 0 }, part.Uvs, "uvs");
        }

        [Test]
        public void PrimitivesOfOneMaterial_AreMerged_AndPartsFollowTheMaterialOrder()
        {
            var j = Doc();
            j["materials"] = JArray.Parse("[{'name':'a'},{'name':'b'}]");
            j["nodes"] = JArray.Parse("[{'children':[1,2,3]},{'mesh':0,'translation':[0,0,5]},{'mesh':1},{'mesh':0,'translation':[0,0,7]}]");
            var prim1 = (JObject)j["meshes"][0]["primitives"][0].DeepClone(); prim1["material"] = 1;
            j["meshes"] = new JArray(j["meshes"][0], new JObject { ["primitives"] = new JArray(prim1) });
            var model = Read(j, TriangleBin());
            CollectionAssert.AreEqual(new[] { "a", "b" }, model.Parts.Select(p => p.Material.Name).ToArray());
            Assert.AreEqual(6, model.Parts[0].VertexCount); Assert.AreEqual(3, model.Parts[1].VertexCount);
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 3, 5, 4 }, model.Parts[0].Indices, "second primitive offset by the first one's vertices");
            Assert.AreEqual(5f, model.Parts[0].Positions[2]); Assert.AreEqual(7f, model.Parts[0].Positions[3 * 3 + 2]);
        }

        [Test]
        public void MaterialFactors_Defaults_BlendAndEmissiveStrength_AreRead()
        {
            var j = Doc();
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
            j["materials"] = JArray.Parse("[{'name':'x','alphaMode':'BLEND','doubleSided':true,'emissiveFactor':[0.5,0.25,0],'extensions':{'KHR_materials_emissive_strength':{'emissiveStrength':4}},"
                                          + "'pbrMetallicRoughness':{'baseColorFactor':[0.1,0.2,0.3,0.4],'metallicFactor':0.7,'roughnessFactor':0.2,'baseColorTexture':{'index':0}},'normalTexture':{'index':1,'scale':0.3}}]");
            j["extensionsUsed"] = new JArray("KHR_materials_emissive_strength");
            j["textures"] = JArray.Parse("[{'source':0},{'source':1}]");
            j["images"] = JArray.Parse("[{'mimeType':'image/png','bufferView':4},{'mimeType':'image/png','bufferView':5}]");
            ((JArray)j["bufferViews"]).Add(JObject.Parse("{'buffer':0,'byteOffset':104,'byteLength':8}")); ((JArray)j["bufferViews"]).Add(JObject.Parse("{'buffer':0,'byteOffset':112,'byteLength':8}"));
            var bin = TriangleBin().Concat(png).Concat(png.Select(b => (byte)(b ^ 1))).ToArray();
            j["buffers"][0]["byteLength"] = bin.Length;
            var m = Read(j, bin).Parts.Single().Material;
            AssertFloats(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, m.BaseColor, "baseColorFactor");
            Assert.AreEqual(0.7f, m.Metallic); Assert.AreEqual(0.2f, m.Roughness);
            AssertFloats(new float[] { 2f, 1f, 0f }, m.Emissive, "emissive x strength");
            Assert.IsTrue(m.Blend); Assert.IsTrue(m.DoubleSided);
            CollectionAssert.AreEqual(png, m.BaseColorPng); CollectionAssert.AreEqual(png.Select(b => (byte)(b ^ 1)).ToArray(), m.NormalPng);
            Assert.AreEqual(0.3f, m.NormalScale, 1e-6f);

            var plain = Read(Doc(), TriangleBin()).Parts.Single().Material;     // a material with nothing but a name: the glTF defaults
            AssertFloats(new float[] { 1, 1, 1, 1 }, plain.BaseColor, "default colour"); Assert.AreEqual(1f, plain.Metallic); Assert.AreEqual(1f, plain.Roughness);
            AssertFloats(new float[] { 0, 0, 0 }, plain.Emissive, "default emissive"); Assert.IsFalse(plain.Blend); Assert.IsFalse(plain.DoubleSided); Assert.IsNull(plain.BaseColorPng);
        }

        // ---- what is not supported must say so, with the file and the feature -----------------------------------------------------------

        private static void AssertUnsupported(Action<JObject> change, string feature)
        {
            var j = Doc(); change(j);
            var ex = Assert.Throws<NotSupportedException>(() => Read(j, TriangleBin()), feature);
            StringAssert.Contains("synthetic.glb", ex.Message);
            StringAssert.Contains(feature, ex.Message);
        }

        [Test]
        public void UnsupportedFeatures_Throw_NamingTheFileAndTheFeature()
        {
            AssertUnsupported(j => j["extensionsRequired"] = new JArray("KHR_draco_mesh_compression"), "KHR_draco_mesh_compression");
            AssertUnsupported(j => j["extensionsUsed"] = new JArray("KHR_texture_transform"), "KHR_texture_transform");
            AssertUnsupported(j => j["meshes"][0]["primitives"][0]["mode"] = 1, "primitive mode 1");
            AssertUnsupported(j => ((JObject)j["meshes"][0]["primitives"][0]["attributes"])["TEXCOORD_1"] = 2, "TEXCOORD_1");
            AssertUnsupported(j => ((JObject)j["meshes"][0]["primitives"][0]["attributes"])["COLOR_0"] = 2, "COLOR_0");
            AssertUnsupported(j => ((JObject)j["meshes"][0]["primitives"][0]).Remove("material"), "without a material");
            AssertUnsupported(j => j["meshes"][0]["primitives"][0]["targets"] = new JArray(new JObject()), "morph targets");
            AssertUnsupported(j => j["nodes"][0]["skin"] = 0, "skinned node");
            AssertUnsupported(j => j["skins"] = new JArray(new JObject()), "skins");
            AssertUnsupported(j => j["animations"] = new JArray(new JObject()), "animations");
            AssertUnsupported(j => j["accessors"][0]["sparse"] = new JObject(), "sparse accessors");
            AssertUnsupported(j => ((JObject)j["accessors"][0]).Remove("bufferView"), "without a bufferView");
            AssertUnsupported(j => j["accessors"][0]["componentType"] = 5123, "not float");
            AssertUnsupported(j => j["accessors"][3]["componentType"] = 5126, "indices that are not");
            AssertUnsupported(j => j["buffers"][0]["uri"] = "x.bin", "buffer");
            AssertUnsupported(j => j["materials"][0]["alphaMode"] = "MASK", "alphaMode MASK");
            AssertUnsupported(j => j["materials"][0]["occlusionTexture"] = new JObject { ["index"] = 0 }, "occlusion");
            AssertUnsupported(j => j["materials"][0]["pbrMetallicRoughness"] = JObject.Parse("{'metallicRoughnessTexture':{'index':0}}"), "metallicRoughnessTexture");
            AssertUnsupported(j =>
            {
                j["textures"] = JArray.Parse("[{'source':0}]"); j["images"] = JArray.Parse("[{'mimeType':'image/jpeg','bufferView':3}]");
                j["materials"][0]["pbrMetallicRoughness"] = JObject.Parse("{'baseColorTexture':{'index':0}}");
            }, "image/jpeg");
            AssertUnsupported(j => j["asset"]["version"] = "1.0", "glTF version");
        }

        [Test]
        public void BrokenFiles_Throw_InvalidData_NamingTheFile()
        {
            var good = Glb(Doc(), TriangleBin());
            var ex1 = Assert.Throws<InvalidDataException>(() => GlbReader.Parse(new byte[40], "zeros.glb")); StringAssert.Contains("zeros.glb", ex1.Message);
            var ex2 = Assert.Throws<InvalidDataException>(() => GlbReader.Parse(good.Take(good.Length - 40).ToArray(), "cut.glb")); StringAssert.Contains("cut.glb", ex2.Message);
            var j = Doc(); j["accessors"][0]["count"] = 1000;
            var ex3 = Assert.Throws<InvalidDataException>(() => Read(j, TriangleBin())); StringAssert.Contains("outside", ex3.Message);
            j = Doc(); j["nodes"] = JArray.Parse("[{'children':[0]}]");
            Assert.Throws<InvalidDataException>(() => Read(j, TriangleBin()));      // a cycle
            var dup = TriangleBin(); dup[96] = 9;                                    // index 9 of 3 vertices
            var ex4 = Assert.Throws<InvalidDataException>(() => Read(Doc(), dup)); StringAssert.Contains("index 9", ex4.Message);
        }

        // ---- helpers ---------------------------------------------------------------------------------------------------------------------

        /// <summary>Triangles counted straight from the JSON: indices / 3 for indexed primitives, POSITION count / 3 for the others (no node tree, no reader).</summary>
        private static void RawTriangles(byte[] glb, out int indexed, out int nonIndexed)
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(glb, 20, (int)BitConverter.ToUInt32(glb, 12)));
            indexed = 0; nonIndexed = 0;
            foreach (var mesh in json["meshes"])
                foreach (var prim in mesh["primitives"])
                {
                    if (prim["indices"] != null) indexed += (int)json["accessors"][(int)prim["indices"]]["count"] / 3;
                    else nonIndexed += (int)json["accessors"][(int)prim["attributes"]["POSITION"]]["count"] / 3;
                }
        }

        /// <summary>Counts triangles whose face normal (b - a) x (c - a) agrees (dot &gt; 0) or disagrees with the sum of their vertex normals; degenerate ones are skipped.
        /// reversed = look at the same triangles with the winding the other way round.</summary>
        private static void Agreement(GlbPart p, bool reversed, out int ok, out int bad, out int degenerate)
        {
            ok = bad = degenerate = 0;
            for (int t = 0; t < p.TriangleCount; t++)
            {
                int a = p.Indices[t * 3], b = p.Indices[t * 3 + (reversed ? 2 : 1)], c = p.Indices[t * 3 + (reversed ? 1 : 2)];
                double ux = p.Positions[b * 3] - p.Positions[a * 3], uy = p.Positions[b * 3 + 1] - p.Positions[a * 3 + 1], uz = p.Positions[b * 3 + 2] - p.Positions[a * 3 + 2];
                double vx = p.Positions[c * 3] - p.Positions[a * 3], vy = p.Positions[c * 3 + 1] - p.Positions[a * 3 + 1], vz = p.Positions[c * 3 + 2] - p.Positions[a * 3 + 2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                if (nx * nx + ny * ny + nz * nz < 1e-18) { degenerate++; continue; }
                double sx = p.Normals[a * 3] + p.Normals[b * 3] + p.Normals[c * 3], sy = p.Normals[a * 3 + 1] + p.Normals[b * 3 + 1] + p.Normals[c * 3 + 1], sz = p.Normals[a * 3 + 2] + p.Normals[b * 3 + 2] + p.Normals[c * 3 + 2];
                if (nx * sx + ny * sy + nz * sz > 0) ok++; else bad++;
            }
        }
    }
}
