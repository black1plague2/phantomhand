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
    /// The GLB props (PropFit.Specs: brush, pendant lamp, plant, window, singing bowl, tea cup, framed picture) -> wrapper prefabs in Resources/PhantomModels.
    /// GlbReader reads the file (node transforms applied, Unity axes, one part per material); PropFit moves the pivot and checks the size; this file only turns
    /// the result into Unity assets: ONE mesh with one submesh per material (PHP_Name), URP Lit materials (PHP_Name_Material: linear glTF colours converted for
    /// Unity's gamma-entered colour properties, smoothness = 1 - roughness, normal map, emission = factor x strength, BLEND -> the transparent URP twin, doubleSided ->
    /// cull off), the embedded PNGs written as texture assets (512 max, ASTC 6x6 on Android, sRGB for base colour) and a wrapper whose child has shadows off and
    /// no collider. The authored size is kept. A missing .glb only skips its wrapper (the scene builder keeps its primitive or leaves the prop out). The brush wrapper
    /// comes from PH_Brush.glb when that file exists, else from the Meta paint brush (BuildBrush).
    /// </summary>
    public static partial class PhantomModelImporter
    {
        private const string TexDir = OutRoot + "/Textures";
        private const string PropAssetPrefix = "PHP_";
        private const int PropTexMax = 512;

        private static PropSpec BrushSpec { get { return Array.Find(PropFit.Specs, s => s.Wrapper == PhModels.Brush); } }

        private static bool GlbPresent(PropSpec s) { return File.Exists(s.Glb); }

        private static bool WrapperExists(string wrapper) { return AssetDatabase.LoadAssetAtPath<GameObject>(ResourcesDir + "/" + wrapper + ".prefab") != null; }

        /// <summary>True when PH_Brush.glb exists but the saved PH_Brush is still the Meta paint brush of an earlier bake: EnsureWrappers rebuilds it.</summary>
        private static bool BrushNeedsRebuild()
        {
            if (!GlbPresent(BrushSpec)) return false;
            var w = AssetDatabase.LoadAssetAtPath<GameObject>(ResourcesDir + "/" + PhModels.Brush + ".prefab");
            var mf = w != null ? w.GetComponentInChildren<MeshFilter>(true) : null;
            return mf == null || mf.sharedMesh == null || mf.sharedMesh.name != PropAssetPrefix + "Brush";
        }

        private static void BuildProp(Shader urp, List<string> log, PropSpec spec)
        {
            if (!GlbPresent(spec)) { log.Add("[prop] " + spec.Wrapper + ": " + spec.Glb + " missing, skipped"); return; }
            var model = GlbReader.Read(spec.Glb);
            float[] origin = PropFit.Recentre(model, spec.Pivot), min, max;
            model.Bounds(out min, out max);
            var size = new[] { max[0] - min[0], max[1] - min[1], max[2] - min[2] };
            string why;
            if (!PropFit.SizePlausible(size, spec.Size, out why))
                throw new InvalidOperationException(spec.Glb + ": implausible size (" + why + "); bounds min " + Fmt(min) + " max " + Fmt(max) + ", nothing baked");

            string name = spec.Wrapper.Substring(3);                        // PH_PendantLamp -> PendantLamp
            bool normalMaps = model.Parts.Any(p => p.Material.NormalPng != null);
            var mesh = SavePropMesh(PropAssetPrefix + name, model, normalMaps);
            var mats = new Material[model.Parts.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = SavePropMaterial(PropAssetPrefix + name + "_" + model.Parts[i].Material.Name, urp, model.Parts[i].Material, log);
            var convention = spec.Pivot == PropPivot.TopCentre ? PivotConvention.TopCenter : spec.Pivot == PropPivot.BottomCentre ? PivotConvention.BottomCenter : PivotConvention.Joint;
            SaveWrapper(spec.Wrapper, name + "Model", mesh, mats, convention, Mathf.Max(size[0], size[1], size[2]), null);

            int verts = model.Parts.Sum(p => p.VertexCount);
            log.Add("[prop] " + spec.Wrapper + " <- " + Path.GetFileName(spec.Glb) + ": " + model.TriangleCount + " tris, " + verts + " verts, " + mats.Length + " materials ("
                    + string.Join(", ", model.Parts.Select(p => p.Material.Name).ToArray()) + "); bounds min " + Fmt(min) + " max " + Fmt(max) + " size " + Fmt(size)
                    + " m, expected " + Fmt(spec.Size) + " +-" + (PropFit.Tolerance * 100f).ToString("F0") + " %: plausible; pivot " + spec.Pivot + " (was at " + Fmt(origin) + ")");
        }

        private static string Fmt(float[] v) { return "(" + v[0].ToString("F4") + ", " + v[1].ToString("F4") + ", " + v[2].ToString("F4") + ")"; }

        // ---- assets ------------------------------------------------------------------------------------------------------

        private static Mesh SavePropMesh(string name, GlbModel model, bool tangents)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int[]>();
            foreach (var p in model.Parts)
            {
                int baseVertex = v.Count;
                for (int i = 0; i < p.VertexCount; i++)
                {
                    v.Add(new Vector3(p.Positions[i * 3], p.Positions[i * 3 + 1], p.Positions[i * 3 + 2]));
                    n.Add(new Vector3(p.Normals[i * 3], p.Normals[i * 3 + 1], p.Normals[i * 3 + 2]));
                    uv.Add(new Vector2(p.Uvs[i * 2], p.Uvs[i * 2 + 1]));
                }
                tris.Add(p.Indices.Select(k => k + baseVertex).ToArray());
            }
            var m = new Mesh { name = name };
            m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = tris.Count;
            for (int i = 0; i < tris.Count; i++) m.SetTriangles(tris[i], i);
            m.RecalculateBounds();
            if (tangents) m.RecalculateTangents();                          // the normal maps need them (the file has none)
            string path = MeshDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Material SavePropMaterial(string name, Shader urp, GlbMaterial g, List<string> log)
        {
            var m = new Material(urp) { name = name };
            m.SetColor("_BaseColor", new Color(g.BaseColor[0], g.BaseColor[1], g.BaseColor[2], g.BaseColor[3]).gamma);   // glTF factors are linear; this project is in linear colour space
            if (g.BaseColorPng != null) m.SetTexture("_BaseMap", SaveTexture(name + "_BaseColor", g.BaseColorPng, false, log));
            m.SetFloat("_Metallic", g.Metallic); m.SetFloat("_Smoothness", 1f - g.Roughness);
            if (g.NormalPng != null)
            {
                m.SetTexture("_BumpMap", SaveTexture(name + "_Normal", g.NormalPng, true, log));
                m.SetFloat("_BumpScale", g.NormalScale); m.EnableKeyword("_NORMALMAP");
            }
            if (g.Emissive[0] > 0f || g.Emissive[1] > 0f || g.Emissive[2] > 0f)
            {
                m.SetColor("_EmissionColor", new Color(g.Emissive[0], g.Emissive[1], g.Emissive[2])); m.EnableKeyword("_EMISSION");   // an [HDR] property: taken as linear, like the file's value
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (g.DoubleSided) m.SetFloat("_Cull", 0f);
            if (g.Blend) { var opaque = m; m = PhMaterials.FadeCopy(opaque); UnityEngine.Object.DestroyImmediate(opaque); }
            m.name = name; m.enableInstancing = true;
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Writes the embedded PNG as an asset (untouched when the bytes are the same), 512 max, mipmaps, sRGB for colour and Normal map type for normals, ASTC 6x6 on Android.</summary>
        private static Texture2D SaveTexture(string name, byte[] png, bool normalMap, List<string> log)
        {
            string path = TexDir + "/" + name + ".png";
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png)) File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) throw new IOException("not imported as a texture: " + path);
            var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.maxTextureSize != PropTexMax || !ti.mipmapEnabled || (!normalMap && !ti.sRGBTexture))
            {
                ti.textureType = type; ti.maxTextureSize = PropTexMax; ti.mipmapEnabled = true;
                if (!normalMap) ti.sRGBTexture = true;
                ti.SaveAndReimport();
                log.Add("texture " + Path.GetFileName(path) + " -> " + type + ", max " + PropTexMax + (normalMap ? "" : ", sRGB"));
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            SetAstc(tex, PropTexMax, log);
            return tex;
        }
    }
}
