using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Opus.Games.PhantomHand.EditorTools
{
    /// <summary>One glTF material reduced to what the props use. Colours are LINEAR, as in the file (the importer converts them for Unity's colour properties).</summary>
    public sealed class GlbMaterial
    {
        public string Name;
        public float[] BaseColor = { 1f, 1f, 1f, 1f };
        public byte[] BaseColorPng, NormalPng;          // embedded PNG files, or null
        public float NormalScale = 1f;
        public float Metallic = 1f, Roughness = 1f;     // glTF defaults when the file says nothing
        public float[] Emissive = { 0f, 0f, 0f };       // emissiveFactor x KHR_materials_emissive_strength
        public bool Blend, DoubleSided;                 // alphaMode BLEND, doubleSided
    }

    /// <summary>All triangles of one material, merged over the node tree, with every node transform applied, in Unity's axes (see <see cref="GlbReader"/>).</summary>
    public sealed class GlbPart
    {
        public GlbMaterial Material;
        public float[] Positions, Normals, Uvs;         // x y z / x y z / u v per vertex
        public int[] Indices;                           // triangle list
        public int VertexCount { get { return Positions.Length / 3; } }
        public int TriangleCount { get { return Indices.Length / 3; } }
    }

    public sealed class GlbModel
    {
        public string Source;
        public GlbPart[] Parts;

        public int TriangleCount { get { int n = 0; foreach (var p in Parts) n += p.TriangleCount; return n; } }

        public void Bounds(out float[] min, out float[] max)
        {
            min = new[] { float.MaxValue, float.MaxValue, float.MaxValue }; max = new[] { float.MinValue, float.MinValue, float.MinValue };
            foreach (var p in Parts)
                for (int i = 0; i < p.Positions.Length; i++)
                {
                    int k = i % 3;
                    if (p.Positions[i] < min[k]) min[k] = p.Positions[i];
                    if (p.Positions[i] > max[k]) max[k] = p.Positions[i];
                }
        }
    }

    /// <summary>
    /// Reads the part of glTF 2.0 binary (.glb) that the Phantom Hand props use, in plain C# (no UnityEngine types, so it also runs outside the editor):
    /// the default scene's node tree (matrix or TRS), triangle primitives with POSITION / NORMAL / TEXCOORD_0 (float) and ubyte / ushort / uint indices or none,
    /// bufferView offsets and strides, and materials (baseColorFactor and texture, metallic, roughness, normal map, emissive x KHR_materials_emissive_strength,
    /// alphaMode OPAQUE / BLEND, doubleSided) with their PNG images embedded in the BIN chunk. Anything else throws a NotSupportedException that names the file
    /// and the feature (sparse accessors, other attributes, skins, animations, other extensions, MASK, ...), so a new export can never be imported half-right.
    /// Texture samplers are not read (the files use repeat and trilinear filtering).
    ///
    /// Handedness: glTF is right-handed (+y up, front of an asset +z), Unity left-handed. The x axis is mirrored (x -> -x, positions and normals; the front of an
    /// asset stays on +z, Unity's forward, as in glTFast) and the winding of every triangle is reversed, so front faces stay front faces; a node transform that
    /// is itself a mirror (negative determinant) cancels the reversal. Texture v is flipped (glTF's origin is the top left corner of the image, Unity's the bottom left).
    /// Units stay metres. Vertices are not welded or reordered: one part per material, in the order of the glTF materials.
    /// </summary>
    public static class GlbReader
    {
        private const uint MagicGlb = 0x46546C67, ChunkJson = 0x4E4F534A, ChunkBin = 0x004E4942;
        private const int Float32 = 5126, UByte = 5121, UShort = 5123, UInt = 5125;

        public static GlbModel Read(string path) { return Parse(File.ReadAllBytes(path), path); }

        public static GlbModel Parse(byte[] glb, string source) { return new Doc(glb, source).Run(); }

        private sealed class Acc { public List<float> P = new List<float>(), N = new List<float>(), Uv = new List<float>(); public List<int> I = new List<int>(); public int Verts; }

        private sealed class Doc
        {
            private readonly byte[] _glb; private readonly string _src, _file;
            private JObject _json; private int _binOff, _binLen;
            private readonly SortedDictionary<int, Acc> _acc = new SortedDictionary<int, Acc>();

            public Doc(byte[] glb, string source) { _glb = glb; _src = source; _file = Path.GetFileName(source); }

            private Exception Unsupported(string feature) { return new NotSupportedException(_file + ": " + feature + " is not supported by GlbReader"); }
            private Exception Bad(string what) { return new InvalidDataException(_file + ": " + what); }

            public GlbModel Run()
            {
                Container();
                Header();
                var scenes = _json["scenes"] as JArray;
                if (scenes == null || scenes.Count == 0) throw Bad("no scene");
                var roots = scenes[(int?)_json["scene"] ?? 0]["nodes"] as JArray;
                if (roots == null) throw Bad("the scene has no nodes");
                foreach (var r in roots) Visit((int)r, Identity(), 0);
                var model = new GlbModel { Source = _src };
                var parts = new List<GlbPart>();
                foreach (var kv in _acc)
                    parts.Add(new GlbPart { Material = ReadMaterial(kv.Key), Positions = kv.Value.P.ToArray(), Normals = kv.Value.N.ToArray(), Uvs = kv.Value.Uv.ToArray(), Indices = kv.Value.I.ToArray() });
                if (parts.Count == 0) throw Bad("no triangles");
                model.Parts = parts.ToArray();
                return model;
            }

            // ---- container and document-level checks ------------------------------------------------------------------------

            private void Container()
            {
                if (_glb.Length < 20 || BitConverter.ToUInt32(_glb, 0) != MagicGlb) throw Bad("not a binary glTF (.glb) file");
                if (BitConverter.ToUInt32(_glb, 4) != 2) throw Unsupported("glb container version " + BitConverter.ToUInt32(_glb, 4));
                long total = BitConverter.ToUInt32(_glb, 8);
                if (total > _glb.Length) throw Bad("truncated (header says " + total + " bytes, file has " + _glb.Length + ")");
                long off = 12; string json = null;
                while (off + 8 <= total)
                {
                    long len = BitConverter.ToUInt32(_glb, (int)off); uint type = BitConverter.ToUInt32(_glb, (int)off + 4);
                    if (off + 8 + len > total) throw Bad("chunk runs past the end of the file");
                    if (type == ChunkJson && json == null) json = Encoding.UTF8.GetString(_glb, (int)off + 8, (int)len);
                    else if (type == ChunkBin && _binLen == 0) { _binOff = (int)off + 8; _binLen = (int)len; }
                    off += 8 + len;
                }
                if (json == null) throw Bad("no JSON chunk");
                _json = JObject.Parse(json);
            }

            private void Header()
            {
                var asset = _json["asset"]; string ver = asset != null ? (string)asset["version"] : null;
                if (ver == null || !ver.StartsWith("2")) throw Unsupported("glTF version " + ver);
                var required = _json["extensionsRequired"] as JArray;
                if (required != null && required.Count > 0) throw Unsupported("required extension " + required[0]);
                var used = _json["extensionsUsed"] as JArray;
                if (used != null) foreach (var e in used) if ((string)e != "KHR_materials_emissive_strength") throw Unsupported("extension " + e);
                if (Count("skins") > 0) throw Unsupported("skins");
                if (Count("animations") > 0) throw Unsupported("animations");
                var buffers = _json["buffers"] as JArray;
                if (buffers == null || buffers.Count != 1 || buffers[0]["uri"] != null || _binLen == 0) throw Unsupported("anything but one buffer in the BIN chunk (external or data-URI buffers)");
            }

            private int Count(string key) { var a = _json[key] as JArray; return a == null ? 0 : a.Count; }

            // ---- node tree --------------------------------------------------------------------------------------------------

            private void Visit(int index, float[] parent, int depth)
            {
                if (depth > 64) throw Bad("node tree deeper than 64 levels (a cycle?)");
                var nodes = (JArray)_json["nodes"];
                if (index < 0 || index >= nodes.Count) throw Bad("node " + index + " does not exist");
                var n = nodes[index];
                if (n["skin"] != null) throw Unsupported("a skinned node (" + (string)n["name"] + ")");
                var world = Mul(parent, Local(n));
                if (n["mesh"] != null)
                    foreach (var prim in (JArray)_json["meshes"][(int)n["mesh"]]["primitives"]) Primitive(prim, world);
                var kids = n["children"] as JArray;
                if (kids != null) foreach (var c in kids) Visit((int)c, world, depth + 1);
            }

            private static float[] Local(JToken n)
            {
                var m = n["matrix"];
                if (m != null)
                {
                    var c = m.ToObject<float[]>(); var r = new float[16];       // glTF stores matrices column-major
                    for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) r[row * 4 + col] = c[col * 4 + row];
                    return r;
                }
                float[] t = n["translation"] != null ? n["translation"].ToObject<float[]>() : new float[3];
                float[] q = n["rotation"] != null ? n["rotation"].ToObject<float[]>() : new float[] { 0, 0, 0, 1 };
                float[] s = n["scale"] != null ? n["scale"].ToObject<float[]>() : new float[] { 1, 1, 1 };
                float len = (float)Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
                float x = q[0] / len, y = q[1] / len, z = q[2] / len, w = q[3] / len;
                var rot = new[] { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y) };
                var res = Identity();
                for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) res[row * 4 + col] = rot[row * 3 + col] * s[col]; res[row * 4 + 3] = t[row]; }
                return res;
            }

            private static float[] Identity() { return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }; }

            private static float[] Mul(float[] a, float[] b)
            {
                var r = new float[16];
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { float s = 0; for (int k = 0; k < 4; k++) s += a[i * 4 + k] * b[k * 4 + j]; r[i * 4 + j] = s; }
                return r;
            }

            // ---- primitives -------------------------------------------------------------------------------------------------

            private void Primitive(JToken prim, float[] m)
            {
                int mode = (int?)prim["mode"] ?? 4;
                if (mode != 4) throw Unsupported("primitive mode " + mode + " (only triangle lists)");
                if (prim["targets"] != null) throw Unsupported("morph targets");
                var attrs = (JObject)prim["attributes"];
                foreach (var p in attrs.Properties())
                    if (p.Name != "POSITION" && p.Name != "NORMAL" && p.Name != "TEXCOORD_0") throw Unsupported("vertex attribute " + p.Name);
                if (attrs["POSITION"] == null || attrs["NORMAL"] == null || attrs["TEXCOORD_0"] == null) throw Unsupported("a primitive without POSITION, NORMAL and TEXCOORD_0");
                if (prim["material"] == null) throw Unsupported("a primitive without a material");
                float[] pos = Floats((int)attrs["POSITION"], "VEC3", 3), nrm = Floats((int)attrs["NORMAL"], "VEC3", 3), uv = Floats((int)attrs["TEXCOORD_0"], "VEC2", 2);
                int count = pos.Length / 3;
                if (nrm.Length != pos.Length || uv.Length / 2 != count) throw Bad("POSITION, NORMAL and TEXCOORD_0 have different counts");
                int[] idx = prim["indices"] != null ? Indices((int)prim["indices"], count) : null;
                if (idx == null) { if (count % 3 != 0) throw Bad("a non-indexed primitive with " + count + " vertices"); idx = new int[count]; for (int i = 0; i < count; i++) idx[i] = i; }

                // normal matrix = inverse transpose of the upper 3x3 = cofactors / determinant
                float a = m[0], b = m[1], c = m[2], d = m[4], e = m[5], f = m[6], g = m[8], h = m[9], k = m[10];
                float det = a * (e * k - f * h) - b * (d * k - f * g) + c * (d * h - e * g);
                if (Math.Abs(det) < 1e-12f) throw Bad("a node with a singular transform");
                var nm = new[] { (e * k - f * h) / det, -(d * k - f * g) / det, (d * h - e * g) / det, -(b * k - c * h) / det, (a * k - c * g) / det, -(a * h - b * g) / det, (b * f - c * e) / det, -(a * f - c * d) / det, (a * e - b * d) / det };

                int mat = (int)prim["material"];
                Acc acc; if (!_acc.TryGetValue(mat, out acc)) _acc[mat] = acc = new Acc();
                for (int i = 0; i < count; i++)
                {
                    float x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
                    acc.P.Add(-(m[0] * x + m[1] * y + m[2] * z + m[3])); acc.P.Add(m[4] * x + m[5] * y + m[6] * z + m[7]); acc.P.Add(m[8] * x + m[9] * y + m[10] * z + m[11]);
                    float nx = nm[0] * nrm[i * 3] + nm[1] * nrm[i * 3 + 1] + nm[2] * nrm[i * 3 + 2], ny = nm[3] * nrm[i * 3] + nm[4] * nrm[i * 3 + 1] + nm[5] * nrm[i * 3 + 2], nz = nm[6] * nrm[i * 3] + nm[7] * nrm[i * 3 + 1] + nm[8] * nrm[i * 3 + 2];
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl < 1e-12f) nl = 1f;
                    acc.N.Add(-nx / nl); acc.N.Add(ny / nl); acc.N.Add(nz / nl);
                    acc.Uv.Add(uv[i * 2]); acc.Uv.Add(1f - uv[i * 2 + 1]);
                }
                bool swap = det > 0f;      // the x mirror reverses the winding; a mirrored node undoes that
                for (int i = 0; i < idx.Length; i += 3)
                {
                    acc.I.Add(acc.Verts + idx[i]); acc.I.Add(acc.Verts + idx[swap ? i + 2 : i + 1]); acc.I.Add(acc.Verts + idx[swap ? i + 1 : i + 2]);
                }
                acc.Verts += count;
            }

            // ---- accessors --------------------------------------------------------------------------------------------------

            private JToken View(JToken accessor, int elementBytes, int count, out int start, out int stride)
            {
                if (accessor["sparse"] != null) throw Unsupported("sparse accessors");
                if (accessor["bufferView"] == null) throw Unsupported("an accessor without a bufferView");
                if ((bool?)accessor["normalized"] == true) throw Unsupported("normalized accessors");
                var bv = _json["bufferViews"][(int)accessor["bufferView"]];
                if ((int)bv["buffer"] != 0) throw Unsupported("a second buffer");
                int viewOff = (int?)bv["byteOffset"] ?? 0, viewLen = (int)bv["byteLength"];
                start = viewOff + ((int?)accessor["byteOffset"] ?? 0);
                stride = (int?)bv["byteStride"] ?? elementBytes;
                long last = (long)start + (long)(count - 1) * stride + elementBytes;
                if (count < 1 || last > viewOff + viewLen || viewOff + viewLen > _binLen) throw Bad("an accessor reads outside its bufferView / the BIN chunk");
                return bv;
            }

            private float[] Floats(int index, string type, int comps)
            {
                var a = _json["accessors"][index];
                if ((int)a["componentType"] != Float32) throw Unsupported("a " + type + " accessor that is not float");
                if ((string)a["type"] != type) throw Bad("accessor " + index + " is " + (string)a["type"] + ", expected " + type);
                int count = (int)a["count"], start, stride;
                View(a, comps * 4, count, out start, out stride);
                var r = new float[count * comps];
                for (int i = 0; i < count; i++) for (int c = 0; c < comps; c++) r[i * comps + c] = BitConverter.ToSingle(_glb, _binOff + start + i * stride + c * 4);
                return r;
            }

            private int[] Indices(int index, int vertexCount)
            {
                var a = _json["accessors"][index];
                int ct = (int)a["componentType"];
                if ((string)a["type"] != "SCALAR" || (ct != UByte && ct != UShort && ct != UInt)) throw Unsupported("indices that are not ubyte / ushort / uint scalars");
                int count = (int)a["count"], start, stride, size = ct == UByte ? 1 : ct == UShort ? 2 : 4;
                if (count % 3 != 0) throw Bad("an index count of " + count + " is not a multiple of 3");
                View(a, size, count, out start, out stride);
                var r = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int o = _binOff + start + i * stride;
                    long v = ct == UByte ? _glb[o] : ct == UShort ? BitConverter.ToUInt16(_glb, o) : BitConverter.ToUInt32(_glb, o);
                    if (v >= vertexCount) throw Bad("index " + v + " is outside the " + vertexCount + " vertices");
                    r[i] = (int)v;
                }
                return r;
            }

            // ---- materials --------------------------------------------------------------------------------------------------

            private GlbMaterial ReadMaterial(int index)
            {
                var m = _json["materials"][index];
                var r = new GlbMaterial { Name = (string)m["name"] ?? "material" + index };
                string mode = (string)m["alphaMode"] ?? "OPAQUE";
                if (mode != "OPAQUE" && mode != "BLEND") throw Unsupported("alphaMode " + mode + " (material " + r.Name + ")");
                r.Blend = mode == "BLEND"; r.DoubleSided = (bool?)m["doubleSided"] ?? false;
                var pbr = m["pbrMetallicRoughness"];
                if (pbr != null)
                {
                    if (pbr["baseColorFactor"] != null) r.BaseColor = pbr["baseColorFactor"].ToObject<float[]>();
                    r.Metallic = (float?)pbr["metallicFactor"] ?? 1f; r.Roughness = (float?)pbr["roughnessFactor"] ?? 1f;
                    if (pbr["metallicRoughnessTexture"] != null) throw Unsupported("a metallicRoughnessTexture (material " + r.Name + ")");
                    if (pbr["baseColorTexture"] != null) r.BaseColorPng = Image(pbr["baseColorTexture"], r.Name);
                }
                if (m["normalTexture"] != null) { r.NormalPng = Image(m["normalTexture"], r.Name); r.NormalScale = (float?)m["normalTexture"]["scale"] ?? 1f; }
                if (m["occlusionTexture"] != null || m["emissiveTexture"] != null) throw Unsupported("an occlusion or emissive texture (material " + r.Name + ")");
                if (m["emissiveFactor"] != null)
                {
                    var e = m["emissiveFactor"].ToObject<float[]>();
                    var ext = m["extensions"]; var st = ext != null ? ext["KHR_materials_emissive_strength"] : null;
                    float strength = st != null ? (float?)st["emissiveStrength"] ?? 1f : 1f;
                    r.Emissive = new[] { e[0] * strength, e[1] * strength, e[2] * strength };
                }
                return r;
            }

            private byte[] Image(JToken texInfo, string material)
            {
                if (((int?)texInfo["texCoord"] ?? 0) != 0) throw Unsupported("a second UV set (material " + material + ")");
                var tex = _json["textures"][(int)texInfo["index"]];
                if (tex["source"] == null) throw Unsupported("a texture without an image source (material " + material + ")");
                var img = _json["images"][(int)tex["source"]];
                if (img["uri"] != null || img["bufferView"] == null) throw Unsupported("an image outside the BIN chunk (material " + material + ")");
                if ((string)img["mimeType"] != "image/png") throw Unsupported("image type " + (string)img["mimeType"] + " (material " + material + "), only PNG");
                var bv = _json["bufferViews"][(int)img["bufferView"]];
                int off = (int?)bv["byteOffset"] ?? 0, len = (int)bv["byteLength"];
                if ((int)bv["buffer"] != 0 || off < 0 || off + len > _binLen) throw Bad("an image reads outside the BIN chunk");
                var png = new byte[len]; Buffer.BlockCopy(_glb, _binOff + off, png, 0, len);
                return png;
            }
        }
    }
}
