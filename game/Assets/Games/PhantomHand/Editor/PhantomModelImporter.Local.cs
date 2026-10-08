using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Opus.Art;
using Opus.Games.PhantomHand.Presentation;
using UnityEditor;
using UnityEngine;

namespace Opus.Games.PhantomHand.EditorTools
{
    /// <summary>
    /// The Unity Asset Store packs on the build PC -> wrapper prefabs in a GIT-IGNORED folder (Standard Asset Store EULA: usable in the built game, never published; the repository is public).
    /// Menu: Tools/OPUS/Bake local Asset Store props (BuildLocal). The game looks there first (PhModels.Load / PhLocalProps), a clone without the packs finds nothing there and looks exactly as before.
    ///
    ///   Stones (Pixelcloud) Stone_3.fbx, else the first Stone_*.fbx   -> PH_Stone     centred, largest extent 1 m (ThreatDrop scales it to 11 cm). ThreatDrop reads only the wrapper's
    ///           mesh and material, so this one mesh is baked (normalised) into Local/Meshes; every other mesh stays in the pack.
    ///   Dark Wave Paint Table 01 (URP prefab)                           -> PH_Table     BuildTable's convention: top surface centred at y = 0, mapped to 1.2 x 0.7 x 0.75 m, the same top collider.
    ///   Mobile Books book_0001a..d                                      -> PH_Books     the books that exist, stacked with small yaw offsets, each laid on its thinnest side, authored size,
    ///           pivot at the bottom centre; rejected unless the stack is 5 to 40 cm in every extent.
    ///   Pack Gesta Furniture #1 tumba_fur.FBX, else sek1.FBX            -> PH_Sideboard authored size, pivot at the bottom centre of the back face, front +z; rejected unless width 0.4-2.5,
    ///           height 0.3-2.2, depth 0.2-0.9 m (a file that came out x100 or x0.01 is corrected once).
    ///
    /// Orientation and size of the sources are unknown in advance: each is instantiated at the origin, its mesh bounds are measured, PhLocalFit decides axes and scale, the wrapper's "Fit"
    /// container carries that transform (the copy of the pack object inside it keeps its own node chain), and the finished wrapper is measured again as a check. Materials that are not
    /// URP Lit are replaced by URP Lit copies (main texture, colour, tiling and normal map kept) in Local/Materials. No colliders except the table's, no shadows, no pack scripts.
    /// A missing pack only skips its wrapper; a rejected one is not written, and an earlier wrapper of the same name is removed so the game falls back to the committed look.
    /// </summary>
    public static partial class PhantomModelImporter
    {
        /// <summary>Everything below this folder is git-ignored (game/.gitignore) and holds the only references to the packs.</summary>
        public const string LocalRoot = OutRoot + "/Local";
        public const string LocalResourcesDir = LocalRoot + "/Resources/PhantomModelsLocal";    // = Resources.Load("PhantomModelsLocal/<name>"), PhModels.LocalFolder
        private const string LocalMeshDir = LocalRoot + "/Meshes", LocalMatDir = LocalRoot + "/Materials";

        private const float LocalSmoothness = 0.15f;       // converted materials are matte like the Meta bakes (0.08 - 0.25)
        private const int LocalTexMax = 1024;              // Android ASTC 6x6 cap for every texture of a local wrapper
        public const float LocalBookMinM = 0.05f, LocalBookMaxM = 0.40f;
        /// <summary>Footprint the book stack's slot in the scene is cleared for (PhantomHandSceneBuilder). A bigger stack is still baked, with a warning in the log line.</summary>
        public const float LocalBookDesignM = 0.30f;
        private static readonly float[] LocalBookYawDeg = { 0f, 8f, -6f, 11f };

        /// <summary>Which end of the sideboard file is its front cannot be read from its bounds: the bake takes the authored +z of the copy (the log line says which source direction became the front).
        /// If the cabinet then stands with its back to the room (room_eye_right), set this to true and bake again; the slot's yaw must NOT be changed, the body would go through the wall.</summary>
        public static bool LocalSideboardFlipFront = false;

        private const string LocalStoneDir = "Assets/Free/Stones/Mesh", LocalStonePreferred = LocalStoneDir + "/Stone_3.fbx", LocalStoneMatDir = "Assets/Free/Stones/Materials";
        private const string LocalTablePath = "Assets/Dark Wave Paint/DWP_Table_01/Prefabs/DWP_Table_01_URP_Metallic.prefab";
        private static readonly string[] LocalBookPaths =
        {
            "Assets/Books/Prefabs/book_0001a.prefab", "Assets/Books/Prefabs/book_0001b.prefab", "Assets/Books/Prefabs/book_0001c.prefab", "Assets/Books/Prefabs/book_0001d.prefab",
        };
        private static readonly string[] LocalSideboardPaths = { "Assets/Furniture_ges1/tumba_fur/tumba_fur.FBX", "Assets/Furniture_ges1/sek/sek1.FBX" };

        // ---- entry point -------------------------------------------------------------------------------------------------

        /// <summary>Bakes every pack that exists; returns one "[local] ..." line per wrapper (also written to the console).</summary>
        [MenuItem("Tools/OPUS/Bake local Asset Store props")]
        public static string BuildLocal()
        {
            var log = new List<string>();
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) { Debug.LogError("[PhantomModelImporter] URP Lit shader not found; the local bake is aborted."); return "[local] URP Lit shader not found; aborted."; }
            Run1("local stone", () => BakeLocalStone(urp, log), log);
            Run1("local table", () => BakeLocalTable(urp, log), log);
            Run1("local books", () => BakeLocalBooks(urp, log), log);
            Run1("local sideboard", () => BakeLocalSideboard(urp, log), log);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string all = string.Join("\n", log.ToArray());
            Debug.Log("[PhantomModelImporter] local props:\n" + all);
            return all;
        }

        // ---- the four wrappers -------------------------------------------------------------------------------------------

        private static void BakeLocalStone(Shader urp, List<string> log)
        {
            const string w = PhModels.Stone;
            string path = File.Exists(LocalStonePreferred) ? LocalStonePreferred : FirstLocalStone();
            if (path == null) { log.Add("[local] " + w + ": " + LocalStonePreferred + " missing, skipped" + DropStaleLocal(w)); return; }
            EnsureLocalDirs();
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;        // the vertices are read and normalised, so the FBX must be CPU-readable (a setting of the pack's import only)
            if (mi != null && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); log.Add("[local] " + Path.GetFileName(path) + " import: Read/Write enabled"); }
            var root = new GameObject(w);
            try
            {
                var copy = LocalCopy(path, root.transform);
                var mf = copy.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null);
                if (mf == null) throw new InvalidOperationException(path + " has no mesh with a renderer");
                var mesh = mf.sharedMesh;
                Matrix4x4 m = mf.transform.localToWorldMatrix;            // the node chain (root rotation / scale of the import) at the origin
                var pos = mesh.vertices;
                if (pos.Length == 0) throw new InvalidOperationException(path + ": the mesh has no readable vertices");
                Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
                for (int i = 0; i < pos.Length; i++) { pos[i] = m.MultiplyPoint3x4(pos[i]); min = Vector3.Min(min, pos[i]); max = Vector3.Max(max, pos[i]); }
                Vector3 size = max - min;
                float k = PhLocalFit.StoneScale(size);
                if (k <= 0f) { log.Add(RejectedLine(w, Path.GetFileName(path), min, max, "empty bounds") + DropStaleLocal(w)); return; }
                Vector3 centre = (min + max) * 0.5f;
                for (int i = 0; i < pos.Length; i++) pos[i] = (pos[i] - centre) * k;

                bool mirrored = m.determinant < 0f;
                var baked = new Mesh { name = "PHL_Stone", indexFormat = pos.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                baked.vertices = pos;
                var uv = mesh.uv; if (uv.Length == pos.Length) baked.uv = uv;
                var nrm = mesh.normals; bool hasNormals = nrm.Length == pos.Length;
                if (hasNormals) { Matrix4x4 nm = m.inverse.transpose; for (int i = 0; i < nrm.Length; i++) nrm[i] = nm.MultiplyVector(nrm[i]).normalized; baked.normals = nrm; }
                var tan = mesh.tangents;
                if (tan.Length == pos.Length)
                {
                    for (int i = 0; i < tan.Length; i++) { Vector3 tv = m.MultiplyVector(tan[i]).normalized; tan[i] = new Vector4(tv.x, tv.y, tv.z, mirrored ? -tan[i].w : tan[i].w); }
                    baked.tangents = tan;
                }
                baked.subMeshCount = mesh.subMeshCount;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var tris = mesh.GetTriangles(s);
                    if (mirrored) for (int i = 0; i + 2 < tris.Length; i += 3) { int swap = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = swap; }
                    baked.SetTriangles(tris, s);
                }
                if (!hasNormals) baked.RecalculateNormals();
                baked.RecalculateBounds();
                var st = new LocalMats();
                var srcMats = mf.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < srcMats.Length; i++)     // the FBX carries an untextured stand-in of the pack's material (the stone came out white): take the pack's own, which has the pictures
                {
                    var own = srcMats[i] == null ? null : AssetDatabase.LoadAssetAtPath<Material>(LocalStoneMatDir + "/" + srcMats[i].name + ".mat");
                    if (own != null) srcMats[i] = own;
                }
                var mats = LocalMaterials(srcMats, w, urp, st, log);
                if (mats.Length < baked.subMeshCount) { var padded = new Material[baked.subMeshCount]; for (int i = 0; i < padded.Length; i++) padded[i] = mats[Mathf.Min(i, mats.Length - 1)]; mats = padded; }
                UnityEngine.Object.DestroyImmediate(copy);

                var saved = SaveLocalMesh(baked);
                var child = new GameObject("StoneModel"); child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = saved;
                var mr = child.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                AddLocalSlot(root, w, 1f, PivotConvention.Center);
                SaveLocalWrapper(root, w);
                float mean = (size.x + size.y + size.z) / 3f * k;
                log.Add(LocalLine(w, Path.GetFileName(path), min, max, "x" + k.ToString("F4") + " on every axis (largest extent 1 m, mean " + mean.ToString("F3") + " m, centred)", "n/a (a stone)", st)
                        + ", mesh PHL_Stone baked (" + saved.vertexCount + " verts, " + saved.subMeshCount + " submesh" + (saved.subMeshCount == 1 ? "" : "es") + ")");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void BakeLocalTable(Shader urp, List<string> log)
        {
            const string w = PhModels.Table;
            string path = LocalTablePath;
            if (!File.Exists(path)) { log.Add("[local] " + w + ": " + path + " missing, skipped" + DropStaleLocal(w)); return; }
            EnsureLocalDirs();
            var root = new GameObject(w);
            try
            {
                var fit = NewFitContainer(root.transform, "Fit");
                var copy = LocalCopy(path, fit);
                StripLocal(copy);
                Vector3 min, max;
                if (!LocalBounds(copy, out min, out max)) throw new InvalidOperationException(path + " has no visible mesh");
                PhLocalFit.TableResult res; string why;
                if (!PhLocalFit.FitTable(min, max, TableW, TableD, TableH, out res, out why)) { log.Add(RejectedLine(w, Path.GetFileName(path), min, max, why) + DropStaleLocal(w)); return; }
                var st = new LocalMats();
                ConvertLocalMaterials(copy, w, urp, st, log);
                ApplyFit(fit, res.Fit);
                CheckFitted(root, new Vector3(-TableW / 2f, -TableH, -TableD / 2f), new Vector3(TableW / 2f, 0f, TableD / 2f), false);
                AddLocalSlot(root, w, TableW, PivotConvention.TopCenter);
                var c = new GameObject("TopCollider"); c.transform.SetParent(root.transform, false);          // the same top collider as the Meta wrapper
                var box = c.AddComponent<BoxCollider>(); box.center = new Vector3(0f, -0.02f, 0f); box.size = new Vector3(TableW, 0.04f, TableD);
                SaveLocalWrapper(root, w);
                log.Add(LocalLine(w, Path.GetFileName(path), min, max, "per source axis " + res.Fit.Scale.ToString("F3") + " (width x" + res.Factors.x.ToString("F3") + " on " + "xyz"[res.WidthAxis]
                        + ", height x" + res.Factors.y.ToString("F3") + " on " + "xyz"[res.UpAxis] + ", depth x" + res.Factors.z.ToString("F3") + " on " + "xyz"[res.DepthAxis] + "; top surface at y = 0, 1.2 x 0.7 x 0.75 m)",
                        "+" + "xyz"[res.UpAxis] + " of the source (width axis " + "xyz"[res.WidthAxis] + ", depth axis " + "xyz"[res.DepthAxis] + ")", st));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void BakeLocalBooks(Shader urp, List<string> log)
        {
            const string w = PhModels.Books;
            var present = LocalBookPaths.Where(File.Exists).ToArray();
            if (present.Length == 0) { log.Add("[local] " + w + ": " + LocalBookPaths[0] + " missing, skipped" + DropStaleLocal(w)); return; }
            EnsureLocalDirs();
            var root = new GameObject(w);
            try
            {
                int n = present.Length;
                var fits = new Transform[n]; var copies = new GameObject[n]; var mins = new Vector3[n]; var maxs = new Vector3[n]; var yaws = new float[n];
                for (int i = 0; i < n; i++)
                {
                    fits[i] = NewFitContainer(root.transform, "Book_" + i);
                    copies[i] = LocalCopy(present[i], fits[i]);
                    StripLocal(copies[i]);
                    if (!LocalBounds(copies[i], out mins[i], out maxs[i])) throw new InvalidOperationException(present[i] + " has no visible mesh");
                    yaws[i] = LocalBookYawDeg[i % LocalBookYawDeg.Length];
                }
                PhLocalFit.Fit[] f; Vector3 size; string why;
                string names = string.Join(", ", present.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray());
                if (!PhLocalFit.StackBooks(mins, maxs, yaws, LocalBookMinM, LocalBookMaxM, out f, out size, out why))
                {
                    log.Add("[local] " + w + " <- " + names + ": REJECTED (" + why + "), nothing baked" + DropStaleLocal(w)); return;
                }
                var st = new LocalMats();
                var thin = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    ConvertLocalMaterials(copies[i], w, urp, st, log);
                    ApplyFit(fits[i], f[i]);
                    thin.Add("+" + "xyz"[PhLocalFit.ThinAxis(maxs[i] - mins[i])]);
                }
                CheckFitted(root, new Vector3(-size.x / 2f, 0f, -size.z / 2f), new Vector3(size.x / 2f, size.y, size.z / 2f), true);
                AddLocalSlot(root, w, Mathf.Max(size.x, size.y, size.z), PivotConvention.BottomCenter);
                SaveLocalWrapper(root, w);
                Vector3 lo, hi; LocalBounds(root, out lo, out hi);
                float foot = Mathf.Max(size.x, size.z);
                log.Add("[local] " + w + " <- " + n + " of " + LocalBookPaths.Length + " books (" + names + "): stack bounds min " + lo.ToString("F3") + " max " + hi.ToString("F3") + " size " + size.ToString("F3")
                        + " m (5-40 cm each), scale applied 1 (authored size), yaw offsets " + string.Join("/", yaws.Select(y => y.ToString("F0")).ToArray()) + ", up axis per book: the thin axis " + string.Join(",", thin.ToArray())
                        + " laid on y, " + st.Summary + (foot > LocalBookDesignM ? ", WARNING: footprint " + foot.ToString("F2") + " m is over the " + LocalBookDesignM.ToString("F2") + " m the scene slot is cleared for" : ""));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void BakeLocalSideboard(Shader urp, List<string> log)
        {
            const string w = PhModels.Sideboard;
            var present = LocalSideboardPaths.Where(File.Exists).ToArray();
            if (present.Length == 0) { log.Add("[local] " + w + ": " + LocalSideboardPaths[0] + " missing, skipped" + DropStaleLocal(w)); return; }
            EnsureLocalDirs();
            foreach (string path in present)                      // the first file that fits the ranges wins
            {
                var root = new GameObject(w);
                try
                {
                    var fit = NewFitContainer(root.transform, "Fit");
                    var copy = LocalCopy(path, fit);
                    StripLocal(copy);
                    Vector3 min, max;
                    if (!LocalBounds(copy, out min, out max)) throw new InvalidOperationException(path + " has no visible mesh");
                    PhLocalFit.FurnitureResult res; string why;
                    if (!PhLocalFit.FitFurniture(min, max, PhLocalFit.Sideboard, LocalSideboardFlipFront, out res, out why)) { log.Add(RejectedLine(w, Path.GetFileName(path), min, max, why)); continue; }
                    var st = new LocalMats();
                    ConvertLocalMaterials(copy, w, urp, st, log);
                    ApplyFit(fit, res.Fit);
                    CheckFitted(root, new Vector3(-res.Size.x / 2f, 0f, 0f), new Vector3(res.Size.x / 2f, res.Size.y, res.Size.z), false);
                    AddLocalSlot(root, w, Mathf.Max(res.Size.x, res.Size.y, res.Size.z), PivotConvention.BottomCenter);
                    SaveLocalWrapper(root, w);
                    string unit = res.Unit == 1f ? "1 (authored size)"
                        : res.Unit == 0.01f ? "x0.01 (the file came out 100 times too big: centimetres read as metres)"
                        : res.Unit == 100f ? "x100 (the file came out 100 times too small)"
                        : "x" + res.Unit.ToString("G4") + " (feet read as metres)";
                    log.Add(LocalLine(w, Path.GetFileName(path), min, max, unit, "+" + "xyz"[res.UpAxis] + " of the source (width axis " + "xyz"[res.WidthAxis] + ", depth axis " + "xyz"[res.DepthAxis]
                            + ", front = source " + PhLocalFit.AxisName(res.Fit.ZSrc) + (LocalSideboardFlipFront ? " (flipped)" : " (authored forward)") + "; baked size " + res.Size.ToString("F3")
                            + " m, back face on wrapper z = 0, bottom at y = 0)", st));
                    return;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            log.Add("[local] " + w + ": no file of the pack fits the ranges, nothing baked" + DropStaleLocal(w));
        }

        // ---- shared steps ------------------------------------------------------------------------------------------------

        private static void EnsureLocalDirs()
        {
            Directory.CreateDirectory(LocalResourcesDir); Directory.CreateDirectory(LocalMeshDir); Directory.CreateDirectory(LocalMatDir);
            AssetDatabase.Refresh();
        }

        private static string FirstLocalStone()
        {
            if (!Directory.Exists(LocalStoneDir)) return null;
            var files = Directory.GetFiles(LocalStoneDir, "Stone_*.fbx");
            Array.Sort(files, StringComparer.Ordinal);
            return files.Length > 0 ? files[0].Replace('\\', '/') : null;
        }

        /// <summary>A wrapper written by an earlier bake whose source is now missing or rejected is removed, so the game falls back to the committed look. Returns the log suffix.</summary>
        private static string DropStaleLocal(string wrapper)
        {
            string p = LocalResourcesDir + "/" + wrapper + ".prefab";
            if (!File.Exists(p)) return "";
            AssetDatabase.DeleteAsset(p);
            return " (the wrapper of an earlier bake was removed)";
        }

        /// <summary>A plain copy of a pack prefab / model under parent, keeping the local transform of its root (the importer's axis and unit conversion lives there).</summary>
        private static GameObject LocalCopy(string path, Transform parent)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) throw new InvalidOperationException(path + " is not imported as a GameObject (is the pack imported?)");
            var go = UnityEngine.Object.Instantiate(src, parent, false);
            go.name = Path.GetFileNameWithoutExtension(path);
            return go;
        }

        private static Transform NewFitContainer(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>The container carries the fit exactly as PhLocalFit defines it: localScale along the source axes, then the rotation that takes the source frame to the wrapper frame, then the offset.</summary>
        private static void ApplyFit(Transform container, PhLocalFit.Fit f)
        {
            container.localScale = f.Scale;
            container.localRotation = Quaternion.Inverse(Quaternion.LookRotation(f.ZSrc, f.YSrc));
            container.localPosition = f.Offset;
        }

        /// <summary>The wrapper is a picture, not a thing: none of the pack's scripts, joints, colliders or bodies; no shadows. A component goes before the ones it depends on
        /// (a script may require a collider, a joint requires its body), otherwise Unity refuses to remove it.</summary>
        private static void StripLocal(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Joint>(true)) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        /// <summary>World min / max of the enabled mesh renderers below go (the corners of each mesh's bounds through its transform; needs no readable mesh).</summary>
        private static bool LocalBounds(GameObject go, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!r.enabled || mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = mf.transform.TransformPoint(corner);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true;
                }
            }
            return any;
        }

        /// <summary>Measures the finished wrapper and compares it with what PhLocalFit predicted (y only for the books: their yaw makes the other axes a looser match). A mismatch means the
        /// fit was applied wrongly; nothing is saved then.</summary>
        private static void CheckFitted(GameObject root, Vector3 expectedMin, Vector3 expectedMax, bool yOnly)
        {
            Vector3 lo, hi;
            if (!LocalBounds(root, out lo, out hi)) throw new InvalidOperationException("self-check: the wrapper has no mesh left");
            const float tol = 0.002f;
            bool ok = Mathf.Abs(lo.y - expectedMin.y) <= tol && Mathf.Abs(hi.y - expectedMax.y) <= tol;
            if (!yOnly)
                ok = ok && Mathf.Abs(lo.x - expectedMin.x) <= tol && Mathf.Abs(hi.x - expectedMax.x) <= tol && Mathf.Abs(lo.z - expectedMin.z) <= tol && Mathf.Abs(hi.z - expectedMax.z) <= tol;
            if (!ok)
                throw new InvalidOperationException("self-check failed for " + root.name + ": measured min " + lo.ToString("F4") + " max " + hi.ToString("F4") + ", expected min " + expectedMin.ToString("F4") + " max "
                                                    + expectedMax.ToString("F4") + (yOnly ? " (y only)" : "") + "; nothing saved");
        }

        private static void AddLocalSlot(GameObject root, string wrapper, float sizeM, PivotConvention pivot)
        {
            var slot = root.AddComponent<ModelSlot>();
            slot.slotId = wrapper; slot.realWorldSizeM = sizeM; slot.pivot = pivot; slot.axisFixApplied = true;
        }

        private static void SaveLocalWrapper(GameObject root, string wrapper)
        {
            string path = LocalResourcesDir + "/" + wrapper + ".prefab";
            bool ok; PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
            if (!ok) throw new IOException("SaveAsPrefabAsset failed: " + path);
        }

        private static Mesh SaveLocalMesh(Mesh m)
        {
            string path = LocalMeshDir + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---- materials ---------------------------------------------------------------------------------------------------

        private sealed class LocalMats
        {
            public int Converted;
            public readonly List<string> Kept = new List<string>();                                             // URP Lit materials of the pack, used as they are
            public readonly Dictionary<Material, Material> Done = new Dictionary<Material, Material>();
            public readonly HashSet<string> Names = new HashSet<string>();
            public string Summary { get { return "materials converted " + Converted + (Kept.Count > 0 ? ", kept URP Lit " + string.Join(" + ", Kept.ToArray()) : ""); } }
        }

        private static void ConvertLocalMaterials(GameObject go, string wrapper, Shader urp, LocalMats st, List<string> log)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = LocalMaterials(r.sharedMaterials, wrapper, urp, st, log);
        }

        private static Material[] LocalMaterials(Material[] src, string wrapper, Shader urp, LocalMats st, List<string> log)
        {
            var res = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) res[i] = LocalMaterial(src[i], wrapper, urp, st, log);
            return res;
        }

        /// <summary>URP Lit materials stay as the pack made them; any other shader (the Standard shader of the 2017 packs) becomes a URP Lit copy that keeps the main texture with its tiling, the
        /// colour and the normal map. Every texture of the result is capped at 1024 / Android ASTC 6x6 (SetAstc), like the Meta bakes.</summary>
        private static Material LocalMaterial(Material src, string wrapper, Shader urp, LocalMats st, List<string> log)
        {
            if (src == null) return null;
            Material done;
            if (st.Done.TryGetValue(src, out done)) return done;
            bool urpLit = src.shader != null && src.shader.name == urp.name;
            // a pack material switched to URP Lit with its picture still in _MainTex and nothing in _BaseMap renders white (the Stones pack): convert it like a non-URP one
            bool lostBaseMap = urpLit && src.GetTexture("_BaseMap") == null && src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null;
            if (urpLit && !lostBaseMap)
            {
                foreach (var p in src.GetTexturePropertyNames()) { var t = src.GetTexture(p); if (t != null) SetAstc(t, LocalTexMax, log); }
                st.Kept.Add(src.name + " (smoothness " + (src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness").ToString("F2") : "-") + ", metallic " + (src.HasProperty("_Metallic") ? src.GetFloat("_Metallic").ToString("F2") : "-") + ")");
                st.Done[src] = src;
                return src;
            }
            string baseName = "PHL_" + wrapper.Substring(3) + "_" + SafeLocalName(src.name), name = baseName;
            for (int n = 2; !st.Names.Add(name); n++) name = baseName + "_" + n;
            Texture main = FirstLocalTexture(src, "_BaseMap", "_MainTex"), bump = FirstLocalTexture(src, "_BumpMap");
            Color color = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            string path = LocalMatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(urp) { name = name }; else mat.shader = urp;
            mat.SetTexture("_BaseMap", main);
            if (main != null)
            {
                string tiled = src.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap";
                mat.SetTextureScale("_BaseMap", src.GetTextureScale(tiled)); mat.SetTextureOffset("_BaseMap", src.GetTextureOffset(tiled));
            }
            mat.SetColor("_BaseColor", color); mat.color = color;
            mat.SetFloat("_Smoothness", LocalSmoothness); mat.SetFloat("_Metallic", 0f);
            mat.SetTexture("_BumpMap", bump);
            if (bump != null) { mat.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f); mat.EnableKeyword("_NORMALMAP"); }
            else mat.DisableKeyword("_NORMALMAP");
            mat.enableInstancing = true;
            if (created) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            if (main != null) SetAstc(main, LocalTexMax, log);
            if (bump != null) SetAstc(bump, LocalTexMax, log);
            st.Converted++;
            st.Done[src] = mat;
            return mat;
        }

        private static Texture FirstLocalTexture(Material m, params string[] props)
        {
            foreach (var p in props)
                if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return null;
        }

        private static string SafeLocalName(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        // ---- log lines ---------------------------------------------------------------------------------------------------

        private static string LocalLine(string wrapper, string source, Vector3 min, Vector3 max, string scale, string up, LocalMats st)
        {
            return "[local] " + wrapper + " <- " + source + ": bounds min " + min.ToString("F3") + " max " + max.ToString("F3") + " size " + (max - min).ToString("F3") + " m, scale applied " + scale
                   + ", up axis " + up + ", " + st.Summary;
        }

        private static string RejectedLine(string wrapper, string source, Vector3 min, Vector3 max, string why)
        {
            return "[local] " + wrapper + " <- " + source + ": REJECTED (" + why + "); bounds min " + min.ToString("F3") + " max " + max.ToString("F3") + " size " + (max - min).ToString("F3") + " m, nothing baked";
        }
    }
}
