using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// DarkenController's renderer list (the pendant lamp's unlit shade and emissive bulb, the unlit window glow go black in the probe phases with the lights).
    /// The colour maths is pure (Color only) and also runs outside Unity; the tests with GameObjects, materials and property blocks are marked UnityEngine and need the engine.
    /// </summary>
    public class DarkenControllerTests
    {
        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        // ---- pure ----------------------------------------------------------------------------------------------------------------

        private static void AssertColor(Color expected, Color actual, float tol, string what)
        {
            Assert.AreEqual(expected.r, actual.r, tol, what + " r"); Assert.AreEqual(expected.g, actual.g, tol, what + " g");
            Assert.AreEqual(expected.b, actual.b, tol, what + " b"); Assert.AreEqual(expected.a, actual.a, tol, what + " a");
        }

        [Test]
        public void Scale_AtFullLevel_KeepsTheColour()
        {
            var c = new Color(0.74f, 0.47f, 0.33f, 0.8f);
            AssertColor(c, DarkenController.Scale(c, 1f), 0f, "unlit warm");
            var hdr = new Color(1.6f, 0.863f, 0.311f, 1f);
            AssertColor(hdr, DarkenController.Scale(hdr, 1f), 0f, "emission above 1");
        }

        [Test]
        public void Scale_AtZero_IsBlack_AndKeepsTheAlpha()
        {
            var c = new Color(0.74f, 0.47f, 0.33f, 0.25f);
            AssertColor(new Color(0f, 0f, 0f, 0.25f), DarkenController.Scale(c, 0f), 0f, "glass-like alpha stays");
        }

        [Test]
        public void Scale_MultipliesRgb_LikeTheAmbientColoursAndTheFog()
        {
            var c = new Color(0.74f, 0.47f, 0.33f, 0.6f);
            AssertColor(new Color(0.37f, 0.235f, 0.165f, 0.6f), DarkenController.Scale(c, 0.5f), 1e-6f, "half");
            AssertColor(new Color(0.8f, 0.4315f, 0.1555f, 1f), DarkenController.Scale(new Color(1.6f, 0.863f, 0.311f, 1f), 0.5f), 1e-4f, "emission above 1 scales too");
            float last = float.MaxValue;
            foreach (var level in new[] { 1f, 0.75f, 0.5f, 0.25f, 0.1f, 0f })
            {
                float v = DarkenController.Scale(c, level).r;
                Assert.Less(v, last + 1e-7f, "never brighter while fading out"); last = v;
            }
        }

        // ---- with the engine -----------------------------------------------------------------------------------------------------------

        private Material Make(string shader, Color baseColor)
        {
            var sh = Shader.Find(shader);
            Assert.IsNotNull(sh, shader);
            var m = new Material(sh);
            m.SetColor("_BaseColor", baseColor);
            _made.Add(m);
            return m;
        }

        private MeshRenderer Cube(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; _made.Add(go);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            return mr;
        }

        [Test, Category("UnityEngine")]
        public void UnlitAndEmissiveRenderers_GoBlackWithTheLights_AndComeBack()
        {
            var unlit = Make("Universal Render Pipeline/Unlit", new Color(0.74f, 0.47f, 0.33f, 1f));
            var lit = Make("Universal Render Pipeline/Lit", new Color(0.5f, 0.5f, 0.5f, 1f));
            lit.EnableKeyword("_EMISSION"); lit.SetColor("_EmissionColor", new Color(1.6f, 0.863f, 0.311f));
            var window = Cube("glow", unlit); var bulb = Cube("bulb", lit);
            var lightGo = new GameObject("light"); _made.Add(lightGo);
            var light = lightGo.AddComponent<Light>(); light.intensity = 0.7f;
            var ctrlGo = new GameObject("darken"); _made.Add(ctrlGo);
            var dark = ctrlGo.AddComponent<DarkenController>();
            dark.lights = new[] { light }; dark.renderers = new Renderer[] { window, bulb };
            dark.Capture();

            dark.SetDark(true, immediate: true);
            try
            {
                Assert.AreEqual(0f, dark.Level01); Assert.AreEqual(0f, light.intensity, 1e-6f);
                var block = new MaterialPropertyBlock();
                window.GetPropertyBlock(block, 0);
                AssertColor(new Color(0f, 0f, 0f, 1f), block.GetColor("_BaseColor"), 1e-5f, "unlit base colour in the dark");
                bulb.GetPropertyBlock(block, 0);
                AssertColor(new Color(0f, 0f, 0f, 1f), block.GetColor("_EmissionColor"), 1e-5f, "emission in the dark");
                AssertColor(new Color(0.74f, 0.47f, 0.33f, 1f), unlit.GetColor("_BaseColor"), 1e-6f, "the shared material is never touched");
                AssertColor(new Color(1.6f, 0.863f, 0.311f, 1f), lit.GetColor("_EmissionColor"), 1e-5f, "nor the emission of the shared material");
            }
            finally { dark.SetDark(false, immediate: true); }

            Assert.AreEqual(1f, dark.Level01); Assert.AreEqual(0.7f, light.intensity, 1e-6f);
            Assert.IsFalse(window.HasPropertyBlock(), "lit again: the property block is gone, the renderer batches as before");
            Assert.IsFalse(bulb.HasPropertyBlock());
        }

        [Test, Category("UnityEngine")]
        public void EveryMaterialSlot_IsDarkened_NotOnlyTheFirst()
        {
            var a = Make("Universal Render Pipeline/Lit", Color.white); var b = Make("Universal Render Pipeline/Unlit", new Color(1f, 0.88f, 0.68f, 1f));
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); _made.Add(go);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterials = new[] { a, b };
            var ctrl = new GameObject("darken"); _made.Add(ctrl);
            var dark = ctrl.AddComponent<DarkenController>(); dark.renderers = new Renderer[] { mr }; dark.Capture();
            dark.SetDark(true, immediate: true);
            try
            {
                var block = new MaterialPropertyBlock();
                mr.GetPropertyBlock(block, 1);
                AssertColor(new Color(0f, 0f, 0f, 1f), block.GetColor("_BaseColor"), 1e-5f, "second slot (the lamp's unlit shade)");
            }
            finally { dark.SetDark(false, immediate: true); }
            Assert.IsFalse(mr.HasPropertyBlock());
        }

        [Test, Category("UnityEngine")]
        public void WithoutRenderers_ItStillDarkensTheLights_AsBefore()
        {
            var lightGo = new GameObject("light"); _made.Add(lightGo);
            var light = lightGo.AddComponent<Light>(); light.intensity = 1.05f;
            var ctrl = new GameObject("darken"); _made.Add(ctrl);
            var dark = ctrl.AddComponent<DarkenController>(); dark.lights = new[] { light };
            Assert.AreEqual(0, dark.renderers.Length);
            dark.Capture(); dark.SetDark(true, immediate: true);
            try { Assert.AreEqual(0f, light.intensity, 1e-6f); }
            finally { dark.SetDark(false, immediate: true); }
            Assert.AreEqual(1.05f, light.intensity, 1e-6f);
        }
    }
}
