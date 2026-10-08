using System;
using System.Collections.Generic;
using System.IO;
using Opus.Art;
using Opus.Games.PhantomHand.Presentation;
using UnityEditor;
using UnityEngine;

namespace Opus.Games.PhantomHand.EditorTools
{
    /// <summary>
    /// Builds the Phantom Hand model wrappers from the user's Assets/MetaAssets models (docs/MODEL_REQUESTS.md pattern: URP Lit material
    /// copies, ASTC textures, ModelSlot wrapper prefabs, single LOD per role). Everything is baked into the MESH DATA so the wrapper
    /// prefabs have identity child transforms and the presenters only instantiate them (no per-frame or runtime fitting):
    ///
    ///   handRig_02 rigged right hand (68 bones, skin textures) -> PH_Hand. The rigged hand replaces the glove whenever its FBX exists (see
    ///           PhantomModelImporter.RiggedHand.cs): skinned mesh kept, root rotated / scaled from the measured bones, no mirroring.
    ///   324213  left glove (no bones)        -> PH_Hand     L mesh. FALLBACK when the rigged FBX is missing. Orientation from the mesh itself (finger axis from
    ///           the end slices, palm normal constant), mirrored (the source is a LEFT hand), scaled so wrist crease -> fingertip = 19 cm, wrist crease at
    ///           the arm origin, +z toward the fingers, +y dorsal, underside flush with the table plane; the glove cuff is lofted down to the
    ///           arm's wrist section and trimmed. Skin-tinted flat material (the source texture is a black glove).
    ///   553886  padded arm guard               -> PH_Forearm M mesh. Straightened and re-lofted to 0.90 x ArmGeometry (dome-closed at the elbow),
    ///           baked for a 25 cm forearm (runtime scales z by forearm_length / 25 cm). Skin-tinted flat material.
    ///   935160  arm sleeve (tube, cuffs)       -> PH_Sleeve  M mesh. Re-lofted to 1.08 x ArmGeometry over d = 1.2 .. 19.5 cm; keeps its texture (logo on the right side).
    ///   997491  paint brush                    -> PH_Brush   L mesh. 22 cm long, bristle tip at the origin, handle +y, head wide along x.
    ///   182879  round stone                    -> PH_Stone   M mesh. Unit mean diameter, centred (ThreatDrop scales it to 11 cm).
    ///   1746344 table                          -> PH_Table   M mesh. Top surface at y = 0 (pivot top centre), 1.2 x 0.7 m top, 0.75 m tall, top collider.
    ///
    /// The motor bands stay procedural overlay rings (the sleeve mesh has none). Idempotent: re-running updates the same assets.
    /// Scene-builder hook (the manager wires it):  PhantomModelImporter.EnsureWrappers();  before the presenters are built.
    /// </summary>
    public static partial class PhantomModelImporter
    {
        public const string SrcRoot = "Assets/MetaAssets";
        public const string OutRoot = "Assets/Art/PhantomHand/Models";
        public const string ResourcesDir = OutRoot + "/Resources/PhantomModels";
        private const string MeshDir = OutRoot + "/Meshes", MatDir = OutRoot + "/Materials";

        // Palm normal (palm side) of 324213 in Unity-imported space (post FBX import: world = Rx(-90) * (-x, y, z)); logo direction of 935160.
        private static readonly Vector3 HandPalmNormal = new Vector3(0.6f, -0.76f, -0.23f);
        private static readonly Vector3 SleeveLogoDir = new Vector3(0.949f, 0.161f, -0.271f);

        private const float RefForearmM = 0.25f, SleeveFromM = 0.012f, SleeveToM = 0.195f;
        private const float ForearmFit = 0.90f, SleeveFit = 1.08f;
        private const float HandLenM = 0.19f, HandFlatten = 0.75f, TableFloorY = -0.0205f;
        private const float BrushLenM = 0.22f;
        private const float TableW = 1.2f, TableD = 0.7f, TableH = 0.75f;

        private static readonly string[] Wrappers = { PhModels.Hand, PhModels.Forearm, PhModels.Sleeve, PhModels.Brush, PhModels.Stone, PhModels.Table };

        // ---- entry points ------------------------------------------------------------------------------------------------

        [MenuItem("Tools/OPUS/Phantom Hand/Build Model Wrappers")]
        public static void Run() { Build(true); }

        /// <summary>-executeMethod entry for batch mode.</summary>
        public static void RunBatch() { Build(true); }

        /// <summary>Scene-builder hook: builds any missing wrapper prefab. Returns true when every wrapper exists afterwards
        /// (false when the MetaAssets sources are absent, in which case the presenters keep their procedural geometry).</summary>
        public static bool EnsureWrappers()
        {
            if (AllPresent() && !HandNeedsRebuild()) return true;
            Build(false);
            return AllPresent();
        }

        public static bool AllPresent()
        {
            foreach (var w in Wrappers)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ResourcesDir + "/" + w + ".prefab") == null) return false;
            return true;
        }

        // ---- build -------------------------------------------------------------------------------------------------------

        private static void Build(bool force)
        {
            var log = new List<string>();
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) { Debug.LogError("[PhantomModelImporter] URP Lit shader not found; aborting."); return; }
            Directory.CreateDirectory(MeshDir); Directory.CreateDirectory(MatDir); Directory.CreateDirectory(ResourcesDir);
            AssetDatabase.Refresh();

            Run1("hand", () => BuildHandWrapper(urp, log), log);
            Run1("forearm", () => BuildTube(urp, log, "553886", PhModels.Forearm, "Forearm", "M", false), log);
            Run1("sleeve", () => BuildTube(urp, log, "935160", PhModels.Sleeve, "Sleeve", "M", true), log);
            Run1("brush", () => BuildBrush(urp, log), log);
            Run1("stone", () => BuildStone(urp, log), log);
            Run1("table", () => BuildTable(urp, log), log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PhantomModelImporter] done (force=" + force + "):\n" + string.Join("\n", log));
        }

        private static void Run1(string what, Action a, List<string> log)
        {
            try { a(); }
            catch (Exception e) { log.Add("[" + what + "] FAILED: " + e.Message); Debug.LogError("[PhantomModelImporter] " + what + ": " + e); }
        }

        // ---- source loading ----------------------------------------------------------------------------------------------

        private sealed class Geo
        {
            public Vector3[] V; public Vector2[] UV; public int[] T; public Texture Tex; public Color BaseColor = Color.white;
        }

        private static Geo LoadGeo(string id, string lod, List<string> log)
        {
            string path = SrcRoot + "/Prefabs/" + id + "/" + id + "_" + lod + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { log.Add("[" + id + "] source missing: " + path); throw new FileNotFoundException(path); }
            // the importer reads vertex data, so the source FBX must be CPU-readable (the baked wrapper meshes are what ships)
            var mi = AssetImporter.GetAtPath(SrcRoot + "/Prefabs/" + id + "/" + id + "_" + lod + ".fbx") as ModelImporter;
            if (mi != null && !mi.isReadable)
            {
                mi.isReadable = true; mi.SaveAndReimport();
                log.Add("[" + id + "_" + lod + "] FBX import: Read/Write enabled");
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var mf = root.GetComponentInChildren<MeshFilter>(true);
                var mr = root.GetComponentInChildren<MeshRenderer>(true);
                if (mf == null || mf.sharedMesh == null) throw new InvalidOperationException("no mesh in " + path);
                var mesh = mf.sharedMesh;
                // geometry as the prefab renders when dropped at the origin: the Meta prefabs keep the importer's -90 X / x100 on the ROOT itself (the mesh sits on the
                // root), so the root's own rotation and scale must stay in. Run 2 cancelled them and measured raw FBX space (Z up, 2 cm): table on its side, brush 59 cm long
                Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, root.transform.localRotation, root.transform.localScale) * root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var v = mesh.vertices;
                for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
                var g = new Geo { V = v, UV = mesh.uv, T = mesh.triangles };
                if (g.UV == null || g.UV.Length != v.Length) g.UV = new Vector2[v.Length];
                if (mr != null && mr.sharedMaterial != null)
                {
                    var sm = mr.sharedMaterial;
                    if (sm.HasProperty("_BaseMap")) g.Tex = sm.GetTexture("_BaseMap");
                    if (g.Tex == null && sm.HasProperty("_MainTex")) g.Tex = sm.GetTexture("_MainTex");
                }
                log.Add("[" + id + "_" + lod + "] " + v.Length + " verts, " + g.T.Length / 3 + " tris, world bounds size " + Bounds3(v).size.ToString("F3") + (mesh.isReadable ? "" : " (mesh not readable)"));
                return g;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Bounds Bounds3(IList<Vector3> v)
        {
            var b = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Count; i++) b.Encapsulate(v[i]);
            return b;
        }

        // ---- math helpers ------------------------------------------------------------------------------------------------

        private struct Frame
        {
            public Vector3 X, Y, Z;
            public Vector3 Apply(Vector3 v) { return new Vector3(Vector3.Dot(X, v), Vector3.Dot(Y, v), Vector3.Dot(Z, v)); }
        }

        private static Frame FrameZY(Vector3 z, Vector3 y)
        {
            z.Normalize(); y -= z * Vector3.Dot(y, z); y.Normalize();
            return new Frame { X = Vector3.Cross(y, z), Y = y, Z = z };
        }

        private static Frame FrameZX(Vector3 z, Vector3 x)
        {
            z.Normalize(); x -= z * Vector3.Dot(x, z); x.Normalize();
            return new Frame { X = x, Y = Vector3.Cross(z, x), Z = z };
        }

        private static Frame FrameYX(Vector3 y, Vector3 x)
        {
            y.Normalize(); x -= y * Vector3.Dot(y, x); x.Normalize();
            return new Frame { X = x, Y = y, Z = Vector3.Cross(x, y) };
        }

        /// <summary>Centroids of the bottom and top slices (by native Y, fraction frac of the height each).</summary>
        private static void Ends(Vector3[] v, float frac, out Vector3 lo, out Vector3 hi)
        {
            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in v) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            Vector3 sl = Vector3.zero, sh = Vector3.zero; int nl = 0, nh = 0;
            foreach (var p in v)
            {
                if (p.y < ymin + frac * (ymax - ymin)) { sl += p; nl++; }
                if (p.y > ymax - frac * (ymax - ymin)) { sh += p; nh++; }
            }
            lo = sl / Mathf.Max(1, nl); hi = sh / Mathf.Max(1, nh);
        }

        private static float Percentile(List<float> s, float p)
        {
            if (s.Count == 0) return 0f;
            s.Sort();
            float f = p * (s.Count - 1); int i = Mathf.FloorToInt(f); int j = Mathf.Min(s.Count - 1, i + 1);
            return Mathf.Lerp(s[i], s[j], f - i);
        }

        private sealed class Stations
        {
            public float[] Z, Cx, Cy, Hx, Hy;
            public void At(float z, out float cx, out float cy, out float hx, out float hy)
            {
                int n = Z.Length;
                if (z <= Z[0]) { cx = Cx[0]; cy = Cy[0]; hx = Hx[0]; hy = Hy[0]; return; }
                if (z >= Z[n - 1]) { cx = Cx[n - 1]; cy = Cy[n - 1]; hx = Hx[n - 1]; hy = Hy[n - 1]; return; }
                int i = 0; while (i < n - 2 && Z[i + 1] < z) i++;
                float t = (z - Z[i]) / Mathf.Max(1e-9f, Z[i + 1] - Z[i]);
                cx = Mathf.Lerp(Cx[i], Cx[i + 1], t); cy = Mathf.Lerp(Cy[i], Cy[i + 1], t);
                hx = Mathf.Lerp(Hx[i], Hx[i + 1], t); hy = Mathf.Lerp(Hy[i], Hy[i + 1], t);
            }
        }

        /// <summary>Robust (3..97 percentile) cross-section centre/half-extent at n+1 stations along z.</summary>
        private static Stations MakeStations(List<Vector3> w, int n)
        {
            float zmin = float.MaxValue, zmax = float.MinValue;
            foreach (var p in w) { zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z); }
            float step = (zmax - zmin) / n;
            var st = new Stations { Z = new float[n + 1], Cx = new float[n + 1], Cy = new float[n + 1], Hx = new float[n + 1], Hy = new float[n + 1] };
            var xs = new List<float>(); var ys = new List<float>();
            for (int k = 0; k <= n; k++)
            {
                float z = zmin + step * k;
                xs.Clear(); ys.Clear();
                foreach (var p in w) if (Mathf.Abs(p.z - z) <= 0.75f * step) { xs.Add(p.x); ys.Add(p.y); }
                if (xs.Count < 8)
                {
                    // sparse slice: take the 40 vertices nearest in z
                    var idx = new List<int>(); for (int i = 0; i < w.Count; i++) idx.Add(i);
                    idx.Sort((a, b) => Mathf.Abs(w[a].z - z).CompareTo(Mathf.Abs(w[b].z - z)));
                    xs.Clear(); ys.Clear();
                    for (int i = 0; i < Mathf.Min(40, idx.Count); i++) { xs.Add(w[idx[i]].x); ys.Add(w[idx[i]].y); }
                }
                float x0 = Percentile(xs, 0.03f), x1 = Percentile(xs, 0.97f), y0 = Percentile(ys, 0.03f), y1 = Percentile(ys, 0.97f);
                st.Z[k] = z; st.Cx[k] = (x0 + x1) * 0.5f; st.Cy[k] = (y0 + y1) * 0.5f;
                st.Hx[k] = Mathf.Max(1e-4f, (x1 - x0) * 0.5f); st.Hy[k] = Mathf.Max(1e-4f, (y1 - y0) * 0.5f);
            }
            return st;
        }

        private static List<Vector3> Apply(Vector3[] v, Frame f)
        {
            var w = new List<Vector3>(v.Length);
            foreach (var p in v) w.Add(f.Apply(p));
            return w;
        }

        /// <summary>Welded smooth normals (vertices at the same position share a normal, so UV seams do not show).</summary>
        private static Vector3[] WeldedNormals(Vector3[] v, int[] t)
        {
            var acc = new Dictionary<long, Vector3>();
            Func<Vector3, long> key = p =>
            {
                long x = Mathf.RoundToInt(p.x * 50000f), y = Mathf.RoundToInt(p.y * 50000f), z = Mathf.RoundToInt(p.z * 50000f);
                return (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
            };
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                for (int k = 0; k < 3; k++)
                {
                    long kk = key(v[t[i + k]]); Vector3 cur;
                    acc.TryGetValue(kk, out cur); acc[kk] = cur + n;
                }
            }
            var nn = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 n; acc.TryGetValue(key(v[i]), out n);
                nn[i] = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
            }
            return nn;
        }

        // ---- assets ------------------------------------------------------------------------------------------------------

        private static Mesh SaveMesh(string name, Vector3[] v, Vector2[] uv, int[] t)
        {
            var m = new Mesh { name = name };
            m.indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = v; m.uv = uv; m.triangles = t; m.normals = WeldedNormals(v, t);
            m.RecalculateBounds();
            m.Optimize();
            string path = MeshDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Material SaveMaterial(string name, Shader urp, Texture tex, Color color, float smooth, int texMax, List<string> log)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(urp) { name = name }; else mat.shader = urp;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            if (created) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            if (tex != null) SetAstc(tex, texMax, log);
            return mat;
        }

        private static void SetAstc(Texture tex, int maxSize, List<string> log)
        {
            string tp = AssetDatabase.GetAssetPath(tex);
            var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti == null) return;
            var ps = ti.GetPlatformTextureSettings("Android");
            if (ps.overridden && ps.maxTextureSize == maxSize && ps.format == TextureImporterFormat.ASTC_6x6) return;
            ps.overridden = true; ps.maxTextureSize = maxSize; ps.format = TextureImporterFormat.ASTC_6x6;
            ps.compressionQuality = (int)TextureCompressionQuality.Normal;
            ti.SetPlatformTextureSettings(ps);
            EditorUtility.SetDirty(ti); ti.SaveAndReimport();
            log.Add("texture " + Path.GetFileName(tp) + " -> Android ASTC_6x6 @ " + maxSize);
        }

        private static Material SkinMaterial(Shader urp, List<string> log)
        {
            return SaveMaterial("PHM_Skin", urp, null, _skinTone, 0.25f, 0, log);      // the flat colour, or the rigged hand's average skin tone (set by the hand build)
        }

        private static GameObject SaveWrapper(string wrapperName, string childName, Mesh mesh, Material mat, PivotConvention pivot, float sizeM, Action<GameObject> extra)
        {
            var root = new GameObject(wrapperName);
            try
            {
                var slot = root.AddComponent<ModelSlot>();
                slot.slotId = wrapperName; slot.realWorldSizeM = sizeM; slot.pivot = pivot; slot.axisFixApplied = true;
                var child = new GameObject(childName);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                if (extra != null) extra(root);
                string path = ResourcesDir + "/" + wrapperName + ".prefab";
                bool ok; PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
                if (!ok) throw new IOException("SaveAsPrefabAsset failed: " + path);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ---- hand --------------------------------------------------------------------------------------------------------

        private static void BuildHand(Shader urp, List<string> log)
        {
            var g = LoadGeo("324213", "L", log);
            Vector3 lo, hi; Ends(g.V, 0.12f, out lo, out hi);
            var f = FrameZY(lo - hi, -HandPalmNormal);             // fingers (bottom of the model) -> +z, palm side -> -y (palm down)
            var w = Apply(g.V, f);
            for (int i = 0; i < w.Count; i++) w[i] = new Vector3(-w[i].x, w[i].y, w[i].z);   // source is a left hand: mirror x (thumb to -x)
            var tris = (int[])g.T.Clone();
            for (int i = 0; i + 2 < tris.Length; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }

            float zmin = float.MaxValue, zmax = float.MinValue;
            foreach (var p in w) { zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z); }
            float zc = zmin + 0.34f * (zmax - zmin);               // wrist crease (below the glove strap)
            float k = HandLenM / (zmax - zc);
            for (int i = 0; i < w.Count; i++) w[i] = new Vector3(w[i].x * k, w[i].y * k, (w[i].z - zc) * k);

            // finger underside flush with the table plane, palm centred on the arm axis, then flatten the glove's bulk
            var ys = new List<float>(); var xs = new List<float>();
            foreach (var p in w) { if (p.z > 0.10f && p.z < 0.17f) ys.Add(p.y); if (p.z > 0.06f && p.z < 0.14f) xs.Add(p.x); }
            float yShift = TableFloorY - Percentile(ys, 0.03f);
            float xShift = -(Percentile(xs, 0.03f) + Percentile(xs, 0.97f)) * 0.5f;
            for (int i = 0; i < w.Count; i++)
            {
                float y = w[i].y + yShift;
                y = TableFloorY + (y - TableFloorY) * HandFlatten;
                w[i] = new Vector3(w[i].x + xShift, y, w[i].z);
            }

            // loft the cuff (z <= 0) down to the arm's wrist section and blend back to the hand by z = 4 cm
            var cuff = new List<Vector3>(); foreach (var p in w) if (p.z < 0.06f) cuff.Add(p);
            var st = MakeStations(cuff, 10);
            for (int i = 0; i < w.Count; i++)
            {
                var p = w[i];
                if (p.z >= 0.04f) continue;
                float cx, cy, hx, hy; st.At(p.z, out cx, out cy, out hx, out hy);
                float t = p.z > 0f ? 1f - Smooth(p.z / 0.04f) : 1f;
                float tx = (p.x - cx) / hx * ArmGeometry.WristHalfWidth, ty = (p.y - cy) / hy * ArmGeometry.WristHalfHeight;
                w[i] = new Vector3(p.x * (1f - t) + tx * t, p.y * (1f - t) + ty * t, p.z);
            }

            // trim the part of the cuff that is hidden inside the forearm / sleeve, compacting the vertex list
            var map = new int[w.Count]; for (int i = 0; i < map.Length; i++) map[i] = -1;
            var nv = new List<Vector3>(); var nuv = new List<Vector2>(); var nt = new List<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (w[a].z <= -0.045f && w[b].z <= -0.045f && w[c].z <= -0.045f) continue;
                foreach (int idx in new[] { a, b, c })
                {
                    if (map[idx] < 0) { map[idx] = nv.Count; nv.Add(w[idx]); nuv.Add(g.UV[idx]); }
                    nt.Add(map[idx]);
                }
            }
            var verts = nv.ToArray();
            var mesh = SaveMesh("PHM_Hand", verts, nuv.ToArray(), nt.ToArray());

            var palm = new List<float>(); var fing = new List<float>(); float xminP = float.MaxValue, xmaxP = float.MinValue;
            foreach (var p in verts)
            {
                if (p.z > 0.04f && p.z < 0.10f && Mathf.Abs(p.x) < 0.045f) { palm.Add(p.y); xminP = Mathf.Min(xminP, p.x); xmaxP = Mathf.Max(xmaxP, p.x); }
                if (p.z > 0.11f && p.z < 0.17f) fing.Add(p.y);
            }
            float palmTop = Percentile(palm, 0.97f), fingTop = Percentile(fing, 0.97f);
            var b3 = Bounds3(verts);
            // thumb side check: the mirrored right hand must reach further toward -x than +x around the palm base
            var lateral = new List<float>(); foreach (var p in verts) if (p.z > 0.0f && p.z < 0.08f) lateral.Add(p.x);
            float thumbSide = Percentile(lateral, 0.02f) + Percentile(lateral, 0.98f);
            log.Add("[hand] bounds min " + b3.min.ToString("F3") + " max " + b3.max.ToString("F3") + "; palmTopY " + palmTop.ToString("F4") + " fingerTopY " + fingTop.ToString("F4")
                    + (thumbSide > 0f ? "; WARNING thumb is on +x (expected -x for a right hand): flip the mirror in BuildHand" : "; thumb on -x OK"));
            // never bake a degenerate hand: no wrapper is written and the arm keeps its procedural hand
            string whyNot;
            if (!PhHandFit.BoundsPlausible(b3.min, b3.max, HandLenM, out whyNot))
                throw new InvalidOperationException("implausible glove hand bounds (" + whyNot + "): min " + b3.min.ToString("F4") + " max " + b3.max.ToString("F4"));
            var mat = SkinMaterial(urp, log);
            SaveWrapper(PhModels.Hand, "Hand", mesh, mat, PivotConvention.Joint, HandLenM, root =>
            {
                var info = root.AddComponent<PhModelInfo>();
                info.palmTopY = palmTop; info.fingerTopY = fingTop; info.palmLenM = 0.098f; info.handLenM = HandLenM;
                info.boundsMin = b3.min; info.boundsMax = b3.max;
            });
            log.Add("[hand] wrapper " + PhModels.Hand + " built");
        }

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // ---- forearm and sleeve ------------------------------------------------------------------------------------------

        private static void BuildTube(Shader urp, List<string> log, string id, string wrapper, string childName, string lod, bool sleeve)
        {
            var g = LoadGeo(id, lod, log);
            Vector3 lo, hi; Ends(g.V, 0.12f, out lo, out hi);
            Vector3 a = (hi - lo).normalized;                       // wrist (bottom, narrow) -> elbow (top, wide)
            Frame f;
            if (sleeve) f = FrameZX(-a, SleeveLogoDir);             // z toward the wrist, logo to +x (right side of the arm)
            else
            {
                // dorsal direction = where the padded bulge is (largest radius), weighted r^4
                Vector3 b = Vector3.zero;
                foreach (var p in g.V)
                {
                    Vector3 r = p - lo; r -= a * Vector3.Dot(r, a);
                    float m = r.magnitude; if (m < 1e-6f) continue;
                    b += (r / m) * (m * m * m * m);
                }
                f = FrameZY(-a, b);
            }
            var w = Apply(g.V, f);
            float zmin = float.MaxValue, zmax = float.MinValue;
            foreach (var p in w) { zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z); }
            float d0 = sleeve ? SleeveFromM : 0f, d1 = sleeve ? SleeveToM : RefForearmM;
            float kw = sleeve ? SleeveFit : ForearmFit;
            var st = MakeStations(w, 24);
            var o = new Vector3[w.Count];
            for (int i = 0; i < o.Length; i++)
            {
                var p = w[i];
                float cx, cy, hx, hy; st.At(p.z, out cx, out cy, out hx, out hy);
                float fr = (zmax - p.z) / (zmax - zmin);            // 0 at the wrist end, 1 at the elbow end
                float d = d0 + fr * (d1 - d0);
                float tx = ArmGeometry.HalfWidth(d, RefForearmM) * kw, ty = ArmGeometry.HalfHeight(d, RefForearmM) * kw;
                float dome = 1f;
                if (!sleeve && fr > 0.9f) dome = Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow((fr - 0.9f) / 0.1f, 2f) * 0.95f));
                o[i] = new Vector3((p.x - cx) / hx * tx * dome, ArmGeometry.AxisY(d, RefForearmM) + (p.y - cy) / hy * ty * dome, -d);
            }
            var mesh = SaveMesh("PHM_" + childName, o, g.UV, g.T);
            Material mat;
            if (sleeve) mat = SaveMaterial("PHM_Sleeve", urp, g.Tex, Color.white, 0.20f, 1024, log);
            else mat = SkinMaterial(urp, log);
            SaveWrapper(wrapper, childName, mesh, mat, PivotConvention.Joint, d1 - d0, root =>
            {
                var info = root.AddComponent<PhModelInfo>();
                info.referenceForearmM = RefForearmM; info.rangeFromM = d0; info.rangeToM = d1;
            });
            var bb = Bounds3(o);
            log.Add("[" + childName + "] wrapper " + wrapper + " built; bounds min " + bb.min.ToString("F3") + " max " + bb.max.ToString("F3"));
        }

        // ---- brush -------------------------------------------------------------------------------------------------------

        private static void BuildBrush(Shader urp, List<string> log)
        {
            var g = LoadGeo("997491", "L", log);
            Vector3 lo, hi; Ends(g.V, 0.12f, out lo, out hi);
            Vector3 a = (hi - lo).normalized;                       // handle -> bristle tip
            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in g.V) { float s = Vector3.Dot(p - lo, a); ymin = Mathf.Min(ymin, s); ymax = Mathf.Max(ymax, s); }
            // head principal axis (the wide direction) from the top quarter, in the plane perpendicular to a
            Vector3 e1 = Vector3.Cross(a, Mathf.Abs(a.x) < 0.9f ? Vector3.right : Vector3.forward).normalized, e2 = Vector3.Cross(a, e1);
            double sxx = 0, syy = 0, sxy = 0; int n = 0; Vector3 mean = Vector3.zero;
            foreach (var p in g.V) if (Vector3.Dot(p - lo, a) > ymax - 0.25f * (ymax - ymin)) { mean += p; n++; }
            mean /= Mathf.Max(1, n);
            foreach (var p in g.V)
                if (Vector3.Dot(p - lo, a) > ymax - 0.25f * (ymax - ymin))
                {
                    Vector3 r = p - mean; double x = Vector3.Dot(r, e1), y = Vector3.Dot(r, e2);
                    sxx += x * x; syy += y * y; sxy += x * y;
                }
            double ang = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            Vector3 wide = (e1 * (float)Math.Cos(ang) + e2 * (float)Math.Sin(ang)).normalized;
            var f = FrameYX(-a, wide);                              // handle direction -> +y, head wide axis -> +x
            var w = Apply(g.V, f);
            float wymin = float.MaxValue, wymax = float.MinValue;
            foreach (var p in w) { wymin = Mathf.Min(wymin, p.y); wymax = Mathf.Max(wymax, p.y); }
            float k = BrushLenM / (wymax - wymin);
            // centre x/z on the bristle tip (lowest 6 % of the model)
            Vector3 tip = Vector3.zero; int nt = 0;
            foreach (var p in w) if (p.y < wymin + 0.06f * (wymax - wymin)) { tip += p; nt++; }
            tip /= Mathf.Max(1, nt);
            var o = new Vector3[w.Count];
            for (int i = 0; i < o.Length; i++) o[i] = new Vector3((w[i].x - tip.x) * k, (w[i].y - wymin) * k, (w[i].z - tip.z) * k);
            var mesh = SaveMesh("PHM_Brush", o, g.UV, g.T);
            var mat = SaveMaterial("PHM_Brush", urp, g.Tex, Color.white, 0.15f, 1024, log);
            SaveWrapper(PhModels.Brush, "BrushModel", mesh, mat, PivotConvention.Joint, BrushLenM, null);
            var bb = Bounds3(o);
            log.Add("[brush] wrapper built; bounds min " + bb.min.ToString("F3") + " max " + bb.max.ToString("F3") + " (tip at origin, handle +y, " + (bb.size.y * 100f).ToString("F1") + " cm long)");
        }

        // ---- stone -------------------------------------------------------------------------------------------------------

        private static void BuildStone(Shader urp, List<string> log)
        {
            var g = LoadGeo("182879", "M", log);
            var b = Bounds3(g.V);
            float mean = (b.size.x + b.size.y + b.size.z) / 3f;
            var o = new Vector3[g.V.Length];
            for (int i = 0; i < o.Length; i++) o[i] = (g.V[i] - b.center) / mean;
            var mesh = SaveMesh("PHM_Stone", o, g.UV, g.T);
            var mat = SaveMaterial("PHM_Stone", urp, g.Tex, Color.white, 0.10f, 1024, log);
            SaveWrapper(PhModels.Stone, "StoneModel", mesh, mat, PivotConvention.Center, 1f, null);
            log.Add("[stone] wrapper built; unit-mean-diameter bounds size " + Bounds3(o).size.ToString("F3"));
        }

        // ---- table -------------------------------------------------------------------------------------------------------

        private static void BuildTable(Shader urp, List<string> log)
        {
            var g = LoadGeo("1746344", "M", log);
            var b = Bounds3(g.V);
            var o = new Vector3[g.V.Length];
            for (int i = 0; i < o.Length; i++)
            {
                var p = g.V[i];
                o[i] = new Vector3((p.x - b.center.x) / b.size.x * TableW, (p.y - b.max.y) / b.size.y * TableH, (p.z - b.center.z) / b.size.z * TableD);
            }
            var mesh = SaveMesh("PHM_Table", o, g.UV, g.T);
            var mat = SaveMaterial("PHM_Table", urp, g.Tex, Color.white, 0.08f, 1024, log);   // matte: PhantomHandRigValidatorTest.Table_Is75cmHigh_AndMatte needs smoothness <= 0.1
            SaveWrapper(PhModels.Table, "TableModel", mesh, mat, PivotConvention.TopCenter, TableW, root =>
            {
                var c = new GameObject("TopCollider"); c.transform.SetParent(root.transform, false);
                var box = c.AddComponent<BoxCollider>(); box.center = new Vector3(0f, -0.02f, 0f); box.size = new Vector3(TableW, 0.04f, TableD);
            });
            var bb = Bounds3(o);
            log.Add("[table] wrapper built; bounds min " + bb.min.ToString("F3") + " max " + bb.max.ToString("F3") + " (top at y = 0)");
        }
    }
}
