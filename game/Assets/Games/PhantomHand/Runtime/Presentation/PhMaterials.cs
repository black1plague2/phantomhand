using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>Material helpers: runtime fallbacks when the scene builder did not assign one, and URP transparent copies for fades.</summary>
    public static class PhMaterials
    {
        public static Material Make(string name, Color c, float smoothness = 0.25f, bool unlit = false)
        {
            var sh = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = name };
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        /// <summary>A transparent twin of an opaque URP material (alpha-blended, no depth write).</summary>
        /// <summary>A transparent twin of src. depthWrite: for a solid thing that fades as a whole (the arm in the Dissolve). Its parts then hide one another as they do when
        /// opaque; without it the forearm inside the sleeve is drawn over the sleeve the moment the fade starts (seen in the first frame sequence, 8 Oct 2026).</summary>
        public static Material FadeCopy(Material src, bool depthWrite = false)
        {
            var m = new Material(src) { name = src.name + "_fade" };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", depthWrite ? 1f : 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            return m;
        }

        public static void SetAlpha(Material m, float a)
        {
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a = a;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
        }

        public static void Strip(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            var r = go.GetComponent<Renderer>();
            if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        }
    }

    /// <summary>
    /// Optional 3D models for the arm, brush, stone and table, and the room props (pendant lamp, plant, window, singing bowl, tea cup, framed picture; baked from
    /// their .glb files, the brush too when PH_Brush.glb exists). Wrappers live under a Resources folder so the presenters need no
    /// scene references; when a wrapper is missing (or <see cref="UseModels"/> is false) every presenter silently keeps its
    /// procedural geometry. Build the wrappers with Tools/OPUS/Phantom Hand/Build Model Wrappers (PhantomModelImporter).
    /// </summary>
    public static class PhModels
    {
        public const string Hand = "PH_Hand", Forearm = "PH_Forearm", Sleeve = "PH_Sleeve", Brush = "PH_Brush", Stone = "PH_Stone", Table = "PH_Table";
        public const string Lamp = "PH_PendantLamp", Plant = "PH_Plant", Window = "PH_Window", Bowl = "PH_SingingBowl", Cup = "PH_TeaCup", Picture = "PH_FramedPicture";
        public const string ResourceFolder = "PhantomModels/";

        /// <summary>Every wrapper the importer bakes, in build order.</summary>
        public static readonly string[] All = { Hand, Forearm, Sleeve, Brush, Stone, Table, Lamp, Plant, Window, Bowl, Cup, Picture };

        /// <summary>What the PH_Hand wrapper is baked from: the rigged right hand (handRig_02.fbx), else the 324213 glove, else nothing (procedural hand).</summary>
        public enum HandSource { None, Glove, Rigged }

        public static HandSource ChooseHandSource(bool riggedFbxPresent, bool gloveSourcePresent)
        {
            if (riggedFbxPresent) return HandSource.Rigged;
            return gloveSourcePresent ? HandSource.Glove : HandSource.None;
        }

        /// <summary>Master switch (tests / A-B captures can turn the models off).</summary>
        public static bool UseModels = true;

        public static GameObject Load(string name)
        {
            if (!UseModels) return null;
            return Resources.Load<GameObject>(ResourceFolder + name);
        }

        /// <summary>Instantiates a wrapper under parent (or null when the model is not available).</summary>
        public static GameObject Spawn(string name, Transform parent)
        {
            var prefab = Load(name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            return go;
        }

        /// <summary>Scene-builder helper: the table model with its top surface centred at topCentre (the TableTop anchor, y = 0.75). Returns null when unavailable
        /// (the caller keeps its procedural table). The wrapper carries its own top BoxCollider (1.2 x 0.7 m).</summary>
        public static GameObject SpawnTable(Transform parent, Vector3 topCentre)
        {
            var go = Spawn(Table, parent);
            if (go != null) go.transform.position = topCentre;
            return go;
        }

        public static void StripShadows(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }
    }
}
