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
    /// PH_Hand from the user's rigged right hand (Assets/Art/PhantomHand/Models/RiggedHand/handRig_02.fbx, 68 bones, hand_Co albedo + hand_No normal).
    /// A skinned mesh cannot be baked into vertex data like the glove, so the wrapper keeps the SkinnedMeshRenderer and its bones and moves the ROOT:
    ///   1. FBX import forced to: no camera, no light, no animation clip (a clip must not fight the Curl bones), read/write on; the Animator the Generic rig adds is removed.
    ///   2. The file's node pose is a clenched fist while the skin is bound to an open hand, so the bones are put back on their bindposes.
    ///   3. The orientation is MEASURED, never assumed: wrist (hand.R) -> middle fingertip = +z, index/pinky knuckles give the thumb side (-x), the palm
    ///      normal follows from the right-hand rule (PhHandFit.TryFrame), palm DOWN. Scale: wrist -> middle fingertip = HandLenM (the file is 1.42 m long).
    ///   4. The wrist band is centred on the arm axis and levelled with the forearm; palm / finger heights, palm length / width and the thumb box
    ///      are measured into PhModelInfo for the hit proxy.
    ///   5. A self-test instantiates the saved wrapper, binds PhRiggedHand and curls it once (the log says whether the fingers closed toward the palm).
    /// Everything it measures is written to the log, so the numbers can be checked against the FBX facts in logs/sessions/2026-10-08-PH-U-MODELS-run2.md.
    /// </summary>
    public static partial class PhantomModelImporter
    {
        public const string RiggedDir = "Assets/Art/PhantomHand/Models/RiggedHand";
        public const string RiggedFbx = RiggedDir + "/handRig_02.fbx", RiggedAlbedo = RiggedDir + "/hand_Co.jpg", RiggedNormal = RiggedDir + "/hand_No.png";
        private const string HandSourceId = "324213";
        private const float KnuckleWidthPadM = 0.018f;          // finger width added to the knuckle-to-knuckle distance for the palm width

        // Skin tone shared by the hand wrapper and the forearm wrapper (the hand build step sets it; the default is the flat PH_Skin colour).
        private static Color _skinTone = FlatSkinTone();

        private static Color FlatSkinTone() { return new Color(0.74f, 0.58f, 0.48f); }

        public static bool RiggedFbxPresent() { return AssetDatabase.LoadAssetAtPath<GameObject>(RiggedFbx) != null; }

        private static bool GloveSourcePresent() { return AssetDatabase.LoadAssetAtPath<GameObject>(SrcRoot + "/Prefabs/" + HandSourceId + "/" + HandSourceId + "_L.prefab") != null; }

        /// <summary>True when the rigged FBX exists but the baked PH_Hand is not rigged yet (an earlier bake used the glove): EnsureWrappers rebuilds the hand then.</summary>
        private static bool HandNeedsRebuild()
        {
            if (!RiggedFbxPresent()) return false;
            var w = AssetDatabase.LoadAssetAtPath<GameObject>(ResourcesDir + "/" + PhModels.Hand + ".prefab");
            return w == null || w.GetComponent<PhRiggedHand>() == null;
        }

        /// <summary>PH_Hand: the rigged hand when its FBX exists (and its bake succeeds), else the 324213 glove, else nothing (the arm keeps its procedural hand).</summary>
        private static void BuildHandWrapper(Shader urp, List<string> log)
        {
            _skinTone = FlatSkinTone();
            var source = PhModels.ChooseHandSource(RiggedFbxPresent(), GloveSourcePresent());
            log.Add("[hand] source: " + source + (source == PhModels.HandSource.Rigged ? " (" + RiggedFbx + ")" :
                    source == PhModels.HandSource.Glove ? " (" + HandSourceId + " glove; " + RiggedFbx + " is missing)" : " (no hand model: procedural hand)"));
            if (source == PhModels.HandSource.Rigged)
            {
                try { BuildRiggedHand(urp, log); return; }
                catch (Exception e)
                {
                    _skinTone = FlatSkinTone();
                    log.Add("[hand] rigged hand FAILED: " + e.Message + (GloveSourcePresent() ? " -> falling back to the glove" : " -> no glove source either: procedural hand"));
                    Debug.LogError("[PhantomModelImporter] rigged hand: " + e);
                }
            }
            if (GloveSourcePresent()) BuildHand(urp, log);
        }

        // ---- import settings ---------------------------------------------------------------------------------------------

        private static void ConfigureRiggedImport(List<string> log)
        {
            var mi = AssetImporter.GetAtPath(RiggedFbx) as ModelImporter;
            if (mi == null) throw new InvalidOperationException("not a model asset: " + RiggedFbx);
            var changed = new List<string>();
            if (mi.importCameras) { mi.importCameras = false; changed.Add("importCameras=0"); }
            if (mi.importLights) { mi.importLights = false; changed.Add("importLights=0"); }
            if (mi.importAnimation) { mi.importAnimation = false; changed.Add("importAnimation=0"); }
            if (!mi.isReadable) { mi.isReadable = true; changed.Add("isReadable=1"); }       // the rig type stays Generic (the user's setting): the Animator it adds is removed from the wrapper
            if (changed.Count > 0) { mi.SaveAndReimport(); log.Add("[hand] FBX import settings forced: " + string.Join(", ", changed)); }
            else log.Add("[hand] FBX import settings already as required (no camera, no light, no animation clip, read/write on)");
            log.Add("[hand] FBX rig type " + mi.animationType + " (left as imported; any Animator it adds is removed from the wrapper)");
        }

        private static void ConfigureTexture(string path, bool normalMap, TextureImporterFormat androidFormat, List<string> log)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) throw new FileNotFoundException(path);
            bool dirty = false;
            if (normalMap && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap; dirty = true;
                log.Add("[hand] " + Path.GetFileName(path) + " -> import type Normal map");
            }
            var ps = ti.GetPlatformTextureSettings("Android");
            if (!(ps.overridden && ps.maxTextureSize == 1024 && ps.format == androidFormat))
            {
                ps.overridden = true; ps.maxTextureSize = 1024; ps.format = androidFormat;
                ps.compressionQuality = (int)TextureCompressionQuality.Normal;
                ti.SetPlatformTextureSettings(ps); dirty = true;
                log.Add("[hand] " + Path.GetFileName(path) + " -> Android " + androidFormat + " @ 1024");
            }
            if (dirty) { EditorUtility.SetDirty(ti); ti.SaveAndReimport(); }
        }

        // ---- the build ---------------------------------------------------------------------------------------------------

        private static void BuildRiggedHand(Shader urp, List<string> log)
        {
            ConfigureRiggedImport(log);
            ConfigureTexture(RiggedAlbedo, false, TextureImporterFormat.ASTC_6x6, log);
            ConfigureTexture(RiggedNormal, true, TextureImporterFormat.ASTC_5x5, log);
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(RiggedAlbedo);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(RiggedNormal);
            if (albedo == null) throw new FileNotFoundException(RiggedAlbedo);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(RiggedFbx);

            var root = new GameObject(PhModels.Hand);
            try
            {
                var container = new GameObject("RiggedHand").transform;
                container.SetParent(root.transform, false);
                var inst = UnityEngine.Object.Instantiate(src, container, false);     // a plain clone (no prefab link): its bones and the skinned mesh reference the FBX assets
                inst.name = "handRig_02";
                int removed = 0, animators = 0;
                foreach (var c in inst.GetComponentsInChildren<Camera>(true)) { UnityEngine.Object.DestroyImmediate(c.gameObject); removed++; }
                foreach (var l in inst.GetComponentsInChildren<Light>(true)) { UnityEngine.Object.DestroyImmediate(l.gameObject); removed++; }
                foreach (var a in inst.GetComponentsInChildren<Animator>(true)) { UnityEngine.Object.DestroyImmediate(a); animators++; }
                log.Add("[hand] camera/light objects removed from the instance: " + removed + (removed == 0 ? " (the import settings already dropped them)" : " (WARNING: the import settings did not drop them)") + "; Animators removed: " + animators);
                // with importCameras / importLights off Unity still keeps the FBX nodes as empty GameObjects named Camera and Light (run 2: 73 transforms instead of 71)
                int placeholders = 0;
                foreach (var nodeName in new[] { "Camera", "Light" })
                {
                    var node = inst.transform.Find(nodeName);
                    if (node != null && node.childCount == 0 && node.GetComponents<Component>().Length == 1) { UnityEngine.Object.DestroyImmediate(node.gameObject); placeholders++; }
                }
                log.Add("[hand] empty Camera / Light placeholder objects removed: " + placeholders + " (expected 2)");

                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null || smr.sharedMesh == null) throw new InvalidOperationException("no SkinnedMeshRenderer with a mesh in " + RiggedFbx);
                smr.gameObject.name = "Hand";
                log.Add("[hand] skinned mesh: " + smr.sharedMesh.vertexCount + " verts, " + smr.sharedMesh.triangles.Length / 3 + " tris, " + smr.bones.Length + " bones in the SMR, "
                        + inst.GetComponentsInChildren<Transform>(true).Length + " transforms in the instance, " + smr.sharedMesh.subMeshCount + " submesh(es), renderer lossyScale "
                        + smr.transform.lossyScale.ToString("F3"));

                RestoreBindPose(smr, log);

                var wrist = FindBone(inst.transform, PhHandFit.WristBone);
                var index1 = FindBone(inst.transform, PhHandFit.Bone("index", 1));
                var pinky1 = FindBone(inst.transform, PhHandFit.Bone("pinky", 1));
                var middle1 = FindBone(inst.transform, PhHandFit.Bone("middle", 1));
                var midEnd = FindBone(inst.transform, PhHandFit.EndBone("middle"));
                if (midEnd == null) midEnd = FindBone(inst.transform, PhHandFit.Bone("middle", 3));
                var missing = new List<string>();
                if (wrist == null) missing.Add(PhHandFit.WristBone);
                if (index1 == null) missing.Add(PhHandFit.Bone("index", 1));
                if (pinky1 == null) missing.Add(PhHandFit.Bone("pinky", 1));
                if (middle1 == null) missing.Add(PhHandFit.Bone("middle", 1));
                if (midEnd == null) missing.Add(PhHandFit.EndBone("middle"));
                if (missing.Count > 0) throw new InvalidOperationException("bones not found in the FBX hierarchy: " + string.Join(", ", missing));

                // ---- measure in whatever space Unity imported the model in, then fit ------------------------------------------
                Vector3[] verts = SkinnedWorldVertices(smr);                  // the container is still at identity here: these are instance-space (= wrapper-space) positions
                Vector3 f0 = (midEnd.position - wrist.position).normalized;
                int tipIdx;
                float reach = PhHandFit.FarthestAlong(verts, wrist.position, f0, out tipIdx);
                float boneReach = (midEnd.position - wrist.position).magnitude;
                var vmin = verts[0]; var vmax = verts[0];
                for (int i = 1; i < verts.Length; i++) { vmin = Vector3.Min(vmin, verts[i]); vmax = Vector3.Max(vmax, verts[i]); }
                log.Add("[hand] skinned mesh bounds (import space, m): min " + vmin.ToString("F4") + " max " + vmax.ToString("F4") + " size " + (vmax - vmin).ToString("F4") + " (bind pose in the file: about 0.42 x 1.48 x 1.02)");
                log.Add("[hand] skin reach along wrist -> middle end: " + reach.ToString("F4") + " m vs the end bone " + boneReach.ToString("F4") + " m (ratio " + (reach / boneReach).ToString("F3") + ", expected 1.001)");
                // the bones are authoritative and the skin tip must lie at the end bone of the middle finger (+-3 %): anything else means vertex space and bone space disagree, and no
                // hand is baked (the glove fallback takes over) instead of one fitted to a wrong measurement
                if (Mathf.Abs(reach / boneReach - 1f) > 0.03f)
                    throw new InvalidOperationException("the skinned mesh and the bones disagree (skin reach " + reach.ToString("F4") + " m, end bone " + boneReach.ToString("F4") + " m): vertex space and bone space do not match");
                var thumbBones = new List<Transform>();
                foreach (var n in new[] { PhHandFit.Bone("thumb", 1), PhHandFit.Bone("thumb", 2), PhHandFit.Bone("thumb", 3), PhHandFit.EndBone("thumb") })
                {
                    var t = FindBone(inst.transform, n);
                    if (t != null) thumbBones.Add(t);
                }
                log.Add("[hand] measured (import space, m): wrist " + wrist.position.ToString("F4") + ", middle end bone " + midEnd.position.ToString("F4") + ", farthest vertex "
                        + verts[tipIdx].ToString("F4") + ", index knuckle " + index1.position.ToString("F4") + ", pinky knuckle " + pinky1.position.ToString("F4"));
                HandFrame fr;
                if (!PhHandFit.TryFrame(wrist.position, verts[tipIdx], index1.position, pinky1.position, HandLenM, out fr))
                    throw new InvalidOperationException("the hand frame is degenerate (wrist, fingertip and knuckles are collinear or coincide)");
                var thumbMid = Vector3.zero;
                foreach (var t in thumbBones) thumbMid += t.position;
                if (thumbBones.Count > 0) thumbMid /= thumbBones.Count;
                float witness = thumbBones.Count > 0 ? PhHandFit.PalmSideWitness(fr, thumbMid, (index1.position + pinky1.position) * 0.5f) * fr.Scale : 0f;
                log.Add("[hand] frame: forward " + fr.Forward.ToString("F3") + ", thumb side " + fr.Thumb.ToString("F3") + ", palm normal " + fr.Palm.ToString("F3") + ", wrist -> fingertip "
                        + fr.MeasuredLength.ToString("F4") + " source units, scale " + fr.Scale.ToString("F5") + " (target hand length " + HandLenM.ToString("F3") + " m)"
                        + (thumbBones.Count == 0 ? "; thumb bones missing: palm side not cross-checked" :
                           witness > 0f ? "; palm side cross-check OK (the thumb sits " + (witness * 100f).ToString("F1") + " cm toward the palm side)"
                                        : "; WARNING the thumb sits on the DORSAL side of the knuckle line: the model may be imported mirrored (palm would come out UP)"));

                Quaternion rot = Quaternion.Inverse(Quaternion.LookRotation(fr.Forward, fr.Dorsal));   // maps thumb -> -x, dorsal -> +y, forward -> +z
                container.localRotation = rot;
                container.localScale = Vector3.one * fr.Scale;
                container.localPosition = -(rot * (fr.Wrist * fr.Scale));                                // wrist at the wrapper pivot
                log.Add("[hand] container: euler " + rot.eulerAngles.ToString("F1") + ", scale " + fr.Scale.ToString("F5"));

                // ---- level and centre the wrist band on the forearm ------------------------------------------------------------
                var local = new Vector3[verts.Length];                           // the same vertices through the container's rotation / scale / offset (the wrapper root is at the origin)
                for (int i = 0; i < local.Length; i++) local[i] = container.TransformPoint(verts[i]);
                var ws = PhHandFit.Wrist(local, PhHandFit.WristBandM);
                float dx = PhHandFit.CenterShiftX(ws.CenterX), dy = PhHandFit.LevelShiftY(ws.Bottom);
                container.localPosition += new Vector3(dx, dy, 0f);
                for (int i = 0; i < local.Length; i++) local[i] += new Vector3(dx, dy, 0f);
                log.Add("[hand] wrist band: cut end z " + ws.MinZ.ToString("F4") + ", width " + (ws.Width * 100f).ToString("F1") + " cm (ArmGeometry wrist " + (ArmGeometry.WristHalfWidth * 200f).ToString("F1")
                        + " cm), underside y " + ws.Bottom.ToString("F4") + ", top y " + ws.Top.ToString("F4") + "; corrected by dx " + dx.ToString("F4") + ", dy " + dy.ToString("F4"));

                // ---- measurements for the hit proxy ----------------------------------------------------------------------------
                Func<Transform, Vector3> loc = t => root.transform.InverseTransformPoint(t.position);
                Vector3 pm = loc(middle1), pi = loc(index1), pp = loc(pinky1);
                float palmLen = Mathf.Max(0.05f, pm.z), palmCx = (pi.x + pp.x) * 0.5f, palmW = Mathf.Abs(pi.x - pp.x) + KnuckleWidthPadM;
                float palmTop = PhHandFit.TopY(local, 0.03f, palmLen * 0.9f, palmCx, 0.03f);
                float fingTop = PhHandFit.TopY(local, palmLen + 0.02f, HandLenM * 0.9f, palmCx, 0.045f);
                var bmin = local[0]; var bmax = local[0];
                for (int i = 1; i < local.Length; i++) { bmin = Vector3.Min(bmin, local[i]); bmax = Vector3.Max(bmax, local[i]); }
                Vector2 thumbC = new Vector2(-0.055f, 0.05f), thumbS = new Vector2(0.04f, 0.07f);     // the glove's thumb box, kept when the thumb bones are missing
                if (thumbBones.Count >= 2)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                    foreach (var t in thumbBones) { var p = loc(t); x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
                    thumbC = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f); thumbS = new Vector2(x1 - x0 + 0.02f, z1 - z0 + 0.02f);
                }
                bool thumbMinusX = thumbBones.Count > 0 && loc(thumbBones[0]).x < 0f;
                log.Add("[hand] final bounds (wrapper m): min " + bmin.ToString("F4") + " max " + bmax.ToString("F4") + " (hand length " + bmax.z.ToString("F4") + ", width " + (bmax.x - bmin.x).ToString("F4") + ", thickness " + (bmax.y - bmin.y).ToString("F4") + ")");
                log.Add("[hand] palm length " + palmLen.ToString("F4") + ", palm width " + palmW.ToString("F4") + ", palm centre x " + palmCx.ToString("F4") + ", palmTopY " + palmTop.ToString("F4") + ", fingerTopY " + fingTop.ToString("F4")
                        + ", thumb box centre " + thumbC.ToString("F4") + " size " + thumbS.ToString("F4") + (thumbMinusX ? "; thumb on -x OK" : "; WARNING thumb is not on -x (expected -x for a right hand)"));
                if (Mathf.Abs(bmax.z - HandLenM) > 0.01f) log.Add("[hand] WARNING the fingertip is not at z = " + HandLenM.ToString("F2") + " m: check the scale");
                // a degenerate hand (run 2: a 2 mm speck) is never baked: the exception sends the bake to the glove fallback
                string why;
                if (!PhHandFit.BoundsPlausible(bmin, bmax, HandLenM, out why))
                    throw new InvalidOperationException("implausible hand bounds (" + why + "): min " + bmin.ToString("F4") + " max " + bmax.ToString("F4"));

                // ---- material, renderer, components ----------------------------------------------------------------------------
                _skinTone = SampleSkinTone(smr.sharedMesh, RiggedAlbedo);
                var mat = SaveHandSkinMaterial(urp, albedo, normal, log);
                smr.sharedMaterial = mat;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; smr.receiveShadows = false;
                smr.quality = SkinQuality.Bone4;                    // the Quest quality level skins with 2 bones: the hand mesh has up to 4 influences per vertex
                smr.skinnedMotionVectors = false;
                smr.updateWhenOffscreen = true;                     // bounds follow the curling fingers (a single small mesh: negligible cost)
                log.Add("[hand] skin tone (average of hand_Co at the mesh UVs) " + _skinTone.ToString("F3") + "; material " + MatDir + "/PHM_HandSkin.mat (URP Lit, hand_Co + hand_No normal map, smoothness 0.25)");

                var slot = root.AddComponent<ModelSlot>();
                slot.slotId = PhModels.Hand; slot.realWorldSizeM = HandLenM; slot.pivot = PivotConvention.Joint; slot.axisFixApplied = true;
                var info = root.AddComponent<PhModelInfo>();
                info.rigged = true; info.texturedSkin = true; info.skinTone = _skinTone;
                info.palmTopY = palmTop; info.fingerTopY = fingTop; info.palmLenM = palmLen; info.handLenM = bmax.z;
                info.palmWidthM = palmW; info.palmCenterX = palmCx; info.fingerLenM = Mathf.Max(0.04f, HandLenM - palmLen - 0.012f);
                info.thumbCenterXZ = thumbC; info.thumbSizeXZ = thumbS;
                info.boundsMin = bmin; info.boundsMax = bmax;      // the arm only uses a rigged hand wrapper whose recorded bounds are plausible (VirtualArmRig)
                root.AddComponent<PhRiggedHand>();

                string path = ResourcesDir + "/" + PhModels.Hand + ".prefab";
                bool ok; PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
                if (!ok) throw new IOException("SaveAsPrefabAsset failed: " + path);
                log.Add("[hand] wrapper " + PhModels.Hand + " built from " + RiggedFbx + " (rigged, textured)");
                SelfTestRiggedWrapper(path, log);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ---- helpers -----------------------------------------------------------------------------------------------------

        private static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static int Depth(Transform t)
        {
            int d = 0;
            while (t.parent != null) { d++; t = t.parent; }
            return d;
        }

        /// <summary>The file's node pose is a fist while the skin is bound to an open hand: put every skinned bone back on its bindpose (parents first). Unity renders
        /// v' = sum(w * bone.localToWorld * bindpose) * v, so bone.localToWorld = rendererToWorld * bindpose^-1 reproduces the bind pose.</summary>
        private static void RestoreBindPose(SkinnedMeshRenderer smr, List<string> log)
        {
            var bones = smr.bones; var bind = smr.sharedMesh.bindposes;
            if (bones.Length == 0 || bones.Length != bind.Length) throw new InvalidOperationException("skin has " + bones.Length + " bones and " + bind.Length + " bindposes");
            Matrix4x4 rendererToWorld = smr.transform.localToWorldMatrix;
            var order = new List<int>();
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null) order.Add(i);
            order.Sort((a, b) => Depth(bones[a]).CompareTo(Depth(bones[b])));
            float moved = 0f; int rotated = 0, mirrored = 0, scaled = 0;
            foreach (int i in order)
            {
                Matrix4x4 m = rendererToWorld * bind[i].inverse;
                if (m.determinant <= 0f) { mirrored++; continue; }
                Vector3 pos = m.GetColumn(3); Quaternion rot = m.rotation;
                moved = Mathf.Max(moved, Vector3.Distance(bones[i].position, pos));
                if (Quaternion.Angle(bones[i].rotation, rot) > 0.1f) rotated++;
                bones[i].SetPositionAndRotation(pos, rot);
                if (Vector3.Distance(m.lossyScale, bones[i].lossyScale) > 0.01f * Mathf.Max(1f, m.lossyScale.x)) scaled++;
            }
            log.Add("[hand] pose restore: " + (order.Count - mirrored) + " of " + bones.Length + " skinned bones set to their bindposes; moved up to " + (moved * 100f).ToString("F1") + " cm, " + rotated
                    + " rotated (0 moved / 0 rotated = Unity had already instantiated the bind pose)"
                    + (mirrored > 0 ? "; WARNING " + mirrored + " bindposes mirrored (negative determinant), left alone" : "")
                    + (scaled > 0 ? "; WARNING " + scaled + " bones differ in scale from their bindpose" : ""));
        }

        /// <summary>The skinned mesh as it renders now, in world space, by explicit CPU skinning over bone.localToWorld * bindpose (PhHandFit.SkinVertices). Not BakeMesh: here
        /// BakeMesh(useScale: true) returned unscaled mesh units for a renderer whose lossy scale is 100 (the file keeps the node scale 100 and bakes the 0.01 unit factor into
        /// the vertices), so position + rotation * v collapsed the whole hand onto the renderer's position (run 2: a 2 mm speck).</summary>
        private static Vector3[] SkinnedWorldVertices(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh; var bones = smr.bones; var bind = mesh.bindposes;
            var m = new Matrix4x4[bones.Length];
            for (int i = 0; i < m.Length; i++) m[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
            return PhHandFit.SkinVertices(mesh.vertices, mesh.boneWeights, m);
        }

        /// <summary>Average colour of the albedo at the mesh UVs, read from the file bytes (raw, as authored): the forearm wrapper uses it so skin tones match.</summary>
        private static Color SampleSkinTone(Mesh mesh, string albedoPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                var uv = mesh.uv;
                string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", albedoPath));
                if (uv == null || uv.Length == 0 || !File.Exists(full) || !tex.LoadImage(File.ReadAllBytes(full))) return FlatSkinTone();
                double r = 0, g = 0, b = 0; int n = 0, step = Mathf.Max(1, uv.Length / 5000);
                for (int i = 0; i < uv.Length; i += step)
                {
                    var c = tex.GetPixelBilinear(uv[i].x, uv[i].y);
                    r += c.r; g += c.g; b += c.b; n++;
                }
                return n == 0 ? FlatSkinTone() : new Color((float)(r / n), (float)(g / n), (float)(b / n), 1f);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static Material SaveHandSkinMaterial(Shader urp, Texture albedo, Texture normal, List<string> log)
        {
            string path = MatDir + "/PHM_HandSkin.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created) mat = new Material(urp) { name = "PHM_HandSkin" }; else mat.shader = urp;
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white); mat.color = Color.white;
            if (normal != null && mat.HasProperty("_BumpMap")) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.25f); mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            if (created) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Instantiates the saved wrapper, binds the rig and curls it: the log reports the joint count and whether the fingertip moved toward the palm (-y).</summary>
        private static void SelfTestRiggedWrapper(string prefabPath, List<string> log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) { log.Add("[hand] self-test: could not reload " + prefabPath); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                // the saved wrapper as the arm will see it: CPU-skinned bounds in wrapper space (same plausibility rule as the bake)
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin != null)
                {
                    var sw = SkinnedWorldVertices(skin);
                    var lo = go.transform.InverseTransformPoint(sw[0]); var hi = lo;
                    for (int i = 1; i < sw.Length; i++) { var q = go.transform.InverseTransformPoint(sw[i]); lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q); }
                    string whyNot;
                    log.Add("[hand] self-test: saved wrapper skinned bounds (wrapper m) min " + lo.ToString("F4") + " max " + hi.ToString("F4")
                            + (PhHandFit.BoundsPlausible(lo, hi, HandLenM, out whyNot) ? "; plausible" : "; WARNING implausible: " + whyNot));
                }
                var rig = go.GetComponent<PhRiggedHand>();
                bool bound = rig != null && rig.Bind();
                var tipBone = FindBone(go.transform, PhHandFit.EndBone("middle"));
                if (!bound || tipBone == null) { log.Add("[hand] self-test: WARNING the rig did not bind (" + (rig != null ? rig.JointCount : 0) + " of 15 joints): Curl will use the squash fallback"); return; }
                float y0 = go.transform.InverseTransformPoint(tipBone.position).y, z0 = go.transform.InverseTransformPoint(tipBone.position).z;
                rig.SetCurl(1f);
                var p1 = go.transform.InverseTransformPoint(tipBone.position);
                rig.SetCurl(0f);
                log.Add("[hand] self-test: " + rig.JointCount + " of 15 joints bound; middle fingertip at curl 0 -> 1 moves dy " + ((p1.y - y0) * 100f).ToString("F1") + " cm, dz " + ((p1.z - z0) * 100f).ToString("F1")
                        + " cm (expected: down, i.e. dy < 0, and back toward the wrist, dz < 0)" + (p1.y - y0 < -0.01f ? "; OK" : "; WARNING the fingers do not close toward the palm: check the flexion axes"));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
