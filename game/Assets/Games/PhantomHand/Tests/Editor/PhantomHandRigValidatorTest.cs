using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// Reproducibility guarantee for Assets/Scenes/PhantomHand.unity (built by PhantomHandSceneBuilder):
    /// one camera, hand tracking only, frequency LOW, raw hand data sources present (what MetaHandSource wraps),
    /// hand visuals off, controllers off, poke on both hands, anchors wired, URP. Meta types are reached by name
    /// (reflection) so this test assembly needs no Meta references. D10: at most ONE OVRPassthroughLayer is allowed,
    /// and it must be disabled (it is not in the scene yet).
    /// </summary>
    public class PhantomHandRigValidatorTest
    {
        private const string ScenePath = "Assets/Scenes/PhantomHand.unity";
        private const string Rig = "[BuildingBlock] Camera Rig";
        private const string Interaction = Rig + "/[BuildingBlock] OVRComprehensiveInteractionRig";

        private PhantomAnchors _anchors;

        [OneTimeSetUp]
        public void Open()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            _anchors = UnityEngine.Object.FindAnyObjectByType<PhantomAnchors>();
        }

        private static Type T(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        private static MonoBehaviour[] AllOfTypeName(string typeName, bool includeInactive = true)
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(m => m != null && m.GetType().Name == typeName && m.gameObject.scene.IsValid()).ToArray();
        }

        [Test]
        public void Scene_HasAnchorsComponent_AndEveryAnchorIsWired()
        {
            Assert.IsNotNull(_anchors, "PhantomAnchors missing");
            foreach (var f in typeof(PhantomAnchors).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                    Assert.IsNotNull((UnityEngine.Object)f.GetValue(_anchors), "anchor not wired: " + f.Name);
        }

        [Test]
        public void Rig_HasExactlyOneEnabledCamera_AndNoMainCamera()
        {
            var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToList();
            Assert.AreEqual(1, cams.Count);
            Assert.AreEqual("CenterEyeAnchor", cams[0].gameObject.name);
            // the Building Blocks rig tags CenterEyeAnchor "MainCamera"; what must not exist is a second camera
            Assert.IsFalse(Resources.FindObjectsOfTypeAll<Camera>().Any(c => c.gameObject.scene.IsValid() && c.enabled && c.gameObject.name != "CenterEyeAnchor" && (c.CompareTag("MainCamera") || c.gameObject.name == "Main Camera")));
        }

        [Test]
        public void Rig_HasExactlyOneOVRManager_OneCameraRig_FloorLevel()
        {
            var mgrs = AllOfTypeName("OVRManager");
            Assert.AreEqual(1, mgrs.Length);
            var so = new SerializedObject(mgrs[0]);
            Assert.AreEqual(1, so.FindProperty("_trackingOriginType").intValue, "FloorLevel expected (1)");
            Assert.AreEqual(1, AllOfTypeName("OVRCameraRig").Length);
            Assert.AreEqual(1, Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.scene.IsValid() && g.name == Rig && g.transform.parent == null));
        }

        [Test]
        public void ProjectConfig_HandsOnly_FrequencyLow_FastMotionOff()
        {
            var cfgType = T("OVRProjectConfig");
            Assert.IsNotNull(cfgType);
            var cfg = cfgType.GetProperty("CachedProjectConfig", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            Assert.AreEqual("HandsOnly", cfgType.GetField("handTrackingSupport").GetValue(cfg).ToString());
            Assert.AreEqual("LOW", cfgType.GetField("handTrackingFrequency").GetValue(cfg).ToString());
            var mgr = AllOfTypeName("OVRManager")[0];
            Assert.IsFalse((bool)mgr.GetType().GetField("fastMotionModeHandPosesEnabled").GetValue(mgr));
        }

        [Test]
        public void Rig_HasRawHandSourcesAndHmd_ForMetaHandSource()
        {
            foreach (var n in new[] { "OVRHandDataSourceLeft", "OVRHandDataSourceRight" })
            {
                var go = GameObject.Find(Interaction + "/OVRHands/" + n);
                Assert.IsNotNull(go, n);
                var ihand = T("Oculus.Interaction.Input.IHand");
                Assert.IsNotNull(ihand);
                Assert.IsTrue(go.GetComponents<MonoBehaviour>().Any(m => m != null && ihand.IsAssignableFrom(m.GetType())), n + " must implement IHand (raw, before any HandFilter)");
            }
            var hmd = GameObject.Find(Interaction + "/OVRHmd");   // the Hmd component (IHmd) sits on OVRHmd; OVRHmdDataSource only feeds it
            Assert.IsNotNull(hmd);
            Assert.IsTrue(hmd.GetComponents<MonoBehaviour>().Any(m => m != null && T("Oculus.Interaction.Input.IHmd").IsAssignableFrom(m.GetType())));
            Assert.IsNotNull(GameObject.Find(Interaction + "/OVRHmd/OVRHmdDataSource"));
        }

        [Test]
        public void HandVisualRenderers_AreOff_ButHandsStillTracked()
        {
            var rig = GameObject.Find(Rig);
            var hand = rig.GetComponentsInChildren<Renderer>(true).Where(r =>
            {
                string p = PathOf(r.transform);
                return p.Contains("HandVisual") || p.Contains("Hand Tracking");
            }).ToList();
            Assert.Greater(hand.Count, 0, "expected hand renderers to exist (so that 'off' is meaningful)");
            foreach (var r in hand) Assert.IsFalse(r.enabled, "hand renderer still on: " + PathOf(r.transform));
            // HandVisual.ForceOffVisibility is a runtime-only property (not serialized): the disabled renderers are the persisted guarantee
            Assert.IsNotNull(GameObject.Find(Interaction + "/OVRHands/OVRHandDataSourceLeft"), "tracking sources must remain");
        }

        [Test]
        public void ControllerVisuals_AreOff()
        {
            foreach (var n in new[] { "OVRControllerVisualLeft", "OVRControllerVisualRight" })
            {
                var t = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.gameObject.scene.IsValid() && x.name == n);
                Assert.IsNotNull(t, n);
                Assert.IsFalse(t.gameObject.activeSelf, n + " must be inactive (no controllers)");
            }
        }

        [Test]
        public void PokeInteractors_OnBothHands()
        {
            Assert.IsNotNull(GameObject.Find(Interaction + "/ComprehensiveInteractorsLeft/Interactors/Hand/HandPokeInteractor"));
            Assert.IsNotNull(GameObject.Find(Interaction + "/ComprehensiveInteractorsRight/Interactors/Hand/HandPokeInteractor"));
        }

        [Test]
        public void Passthrough_AtMostOneLayer_AndDisabled()
        {
            var layers = AllOfTypeName("OVRPassthroughLayer");
            Assert.LessOrEqual(layers.Length, 1, "D10 allows exactly one passthrough layer");
            foreach (var l in layers) Assert.IsFalse(l.enabled, "the passthrough layer must be disabled by default");
        }

        [Test]
        public void RenderPipeline_IsUniversal()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            Assert.IsNotNull(rp);
            Assert.AreEqual("UniversalRenderPipelineAsset", rp.GetType().Name);
        }

        [Test]
        public void Table_Is75cmHigh_AndMatte()
        {
            Assert.AreEqual(0.75f, _anchors.tableTop.position.y, 1e-4f);
            var top = GameObject.Find("TableTopSurface");
            Assert.AreEqual(0.75f, top.GetComponent<Renderer>().bounds.max.y, 1e-3f);
            Assert.LessOrEqual(top.GetComponent<Renderer>().sharedMaterial.GetFloat("_Smoothness"), 0.1f);
            Assert.IsNotNull(top.GetComponent<BoxCollider>());
        }

        [Test]
        public void Ruler_Is1m_WithCmTicks_35cmAhead_AtTableHeight()
        {
            var ruler = _anchors.probeRuler;
            Assert.AreEqual(0.35f, ruler.position.z, 1e-3f);
            Assert.AreEqual(0.75f, ruler.position.y, 0.01f);
            var baseMr = ruler.Find("Base").GetComponent<MeshRenderer>();
            Assert.AreEqual(1.0f, baseMr.bounds.size.x, 1e-3f);
            int ticks = ruler.Find("Ticks").GetComponent<MeshFilter>().sharedMesh.vertexCount / 4
                      + ruler.Find("TicksMajor").GetComponent<MeshFilter>().sharedMesh.vertexCount / 4;
            Assert.AreEqual(101, ticks, "one tick per cm from 0 to 100");
            Assert.IsFalse(ruler.gameObject.activeSelf, "ruler is hidden until the probe starts");
        }

        [Test]
        public void VirtualArm_IsOffsetToTheLeft_AndStoneDropsFrom40cm()
        {
            float realX = _anchors.armRestOutline.position.x;
            Assert.AreEqual(0.15f, realX - _anchors.virtualArmAnchor.position.x, 1e-3f);
            Assert.AreEqual(PhantomAnchors.DropHeightM, _anchors.threatDropPoint.position.y - _anchors.virtualArmAnchor.position.y, 1e-3f);
            Assert.AreEqual(_anchors.virtualArmAnchor.position.x, _anchors.threatDropPoint.position.x, 1e-3f);
        }

        [Test]
        public void ArmRestOutline_IsAClosedLoop_OnTheTable()
        {
            var lr = _anchors.armRestOutline.GetComponent<LineRenderer>();
            Assert.IsNotNull(lr);
            Assert.IsTrue(lr.loop);
            Assert.GreaterOrEqual(lr.positionCount, 12);
            Assert.AreEqual(0.753f, _anchors.armRestOutline.position.y, 0.003f);
        }

        [Test]
        public void LeftIndexDot_ExistsAndIsToggleable()
        {
            var tip = _anchors.leftIndexDot.GetComponent<TipDot>();
            Assert.IsNotNull(tip);
            Assert.IsNotNull(tip.dotRenderer);
            Assert.IsFalse(tip.dotRenderer.enabled, "dot hidden by default");
        }

        [Test]
        public void DarkenController_IsWired()
        {
            Assert.IsNotNull(_anchors.darken);
            Assert.GreaterOrEqual(_anchors.darken.lights.Length, 1);
            Assert.IsNotNull(_anchors.darken.cameraToTint);
            Assert.AreEqual(LightType.Directional, _anchors.darken.lights[0].type);
        }

        [Test]
        public void Panels_AreWorldSpaceCanvases_WithPokeSizedAreas()
        {
            foreach (var p in new[] { _anchors.questionnairePanel, _anchors.witnessPanel, _anchors.hudPanel })
            {
                var c = p.GetComponent<Canvas>();
                Assert.IsNotNull(c, p.name);
                Assert.AreEqual(RenderMode.WorldSpace, c.renderMode);
                Assert.IsNotNull(p.GetComponent<UnityEngine.UI.GraphicRaycaster>(), p.name + " needs a GraphicRaycaster");
            }
            // questionnaire panel is ~45 cm from the eyes, slightly below eye level
            var eye = new Vector3(0f, PhantomHandBuilderConstants.SeatedEyeY, 0.02f);
            float d = Vector3.Distance(_anchors.questionnairePanel.position, eye);
            Assert.That(d, Is.InRange(0.40f, 0.55f));
            Assert.Less(_anchors.questionnairePanel.position.y, eye.y);
        }

        [Test]
        public void Lighting_WarmKey_Ambient_LightFog()
        {
            Assert.IsTrue(RenderSettings.fog);
            Assert.Less(RenderSettings.fogDensity, 0.1f);
            Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Trilight, RenderSettings.ambientMode);
            var key = _anchors.darken.lights[0];
            Assert.Greater(key.color.r, key.color.b, "warm key light");
            Assert.AreEqual(LightShadows.None, key.shadows, "Quest: no realtime shadows (stone gets a blob shadow later)");
        }

        [Test]
        public void Environment_IsStaticBatched()
        {
            var env = GameObject.Find("Environment");
            var renderers = env.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(renderers.Length, 10);
            foreach (var r in renderers)
                Assert.IsTrue(GameObjectUtility.GetStaticEditorFlags(r.gameObject).HasFlag(StaticEditorFlags.BatchingStatic), r.name);
        }

        [Test]
        public void Audio_MixerWithSfxAndVoice_AndPlaceholders()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Games/PhantomHand/PhantomHandMixer.mixer");
            Assert.IsNotNull(mixer, "mixer asset");
            Assert.AreEqual(1, mixer.FindMatchingGroups("Sfx").Length);
            Assert.AreEqual(1, mixer.FindMatchingGroups("Voice").Length);
            var sources = _anchors.audioRoot.GetComponentsInChildren<AudioSource>();
            Assert.GreaterOrEqual(sources.Length, 5);
            Assert.IsTrue(sources.All(s => s.outputAudioMixerGroup != null && !s.playOnAwake));
        }

        [Test]
        public void SceneIsInBuildSettings_AfterBootstrap_AndHasNoOrchardObjects()
        {
            // U5 build order (BootstrapSceneBuilder.SetBuildSettings): Bootstrap is build index 0, then the game scenes.
            // OrchardReach may still follow as a fallback until its removal; it is deliberately not asserted here.
            var scenes = EditorBuildSettings.scenes;
            var paths = scenes.Select(s => s.path).ToList();
            Assert.AreEqual("Assets/Scenes/Bootstrap.unity", paths[0], "Bootstrap must be build index 0");
            Assert.Greater(paths.IndexOf(ScenePath), 0, "PhantomHand must be in the build settings after Bootstrap");
            Assert.IsTrue(scenes[paths.IndexOf(ScenePath)].enabled, "PhantomHand must be enabled in the build settings");
            foreach (var n in new[] { "OrchardTable", "OrchardGround", "OrchardReachSceneController", "OrchardSun", "Basket" })
                Assert.IsNull(GameObject.Find(n), n + " must not be in the Phantom Hand scene");
        }

        [Test]
        public void RigRuntimeSettings_At72Hz()
        {
            var s = GameObject.Find(Rig).GetComponent<RigRuntimeSettings>();
            Assert.IsNotNull(s);
            Assert.AreEqual(72, s.displayFrequencyHz);
            Assert.AreSame(GameObject.Find(Rig).transform, s.rigRoot);
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }

    internal static class PhantomHandBuilderConstants
    {
        // mirrors PhantomHandSceneBuilder.SeatedEye (the builder lives in the Shell editor assembly)
        public const float SeatedEyeY = 1.18f;
    }
}
