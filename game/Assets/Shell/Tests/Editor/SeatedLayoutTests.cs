using System;
using System.Reflection;
using NUnit.Framework;
using Opus.Games.OrchardReach;
using Opus.Sdk;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Shell.Tests
{
    /// <summary>
    /// Run8 (Opus's rejection of run7's "confirmed correct by eye" screenshot claim): "prove layout with numbers
    /// before screenshots." Opens the dressed Assets/Scenes/OrchardReach.unity and asserts, in world space with
    /// explicit tolerances, the seated-patient geometry the screenshots are supposed to represent. Every number
    /// here is read from the real scene/prefab assets (renderer bounds, transform positions, serialized fields),
    /// not asserted against a hand-typed expectation of what the dressing tool "should" have produced — so a
    /// regression in OrchardSceneDressingTool/ImportMetaAssetsTool fails this test even if nobody re-runs those
    /// tools' own manual review.
    /// </summary>
    public class SeatedLayoutTests
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const string FruitApplePath = "Assets/Art/Prefabs/fruit_apple/fruit_apple.prefab";
        private const string RigPath = "[BuildingBlock] Camera Rig";

        // "Default Orchard params" per Assets/Games/OrchardReach/manifest.json's paramSchema defaults (also
        // OrchardReachParams.From's fallback values) — the same ranges a fresh/unconfigured session uses.
        private const double ReachPercentMin = 50, ReachPercentMax = 85;
        private const double AzimuthMinDeg = -45, AzimuthMaxDeg = 45;
        private const double ElevationMinDeg = 0, ElevationMaxDeg = 40;

        private Scene _scene;
        private Transform _chestReference;
        private OrchardReachSceneController _controller;

        [OneTimeSetUp]
        public void OpenScene()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var chestGo = GameObject.Find("ChestReference");
            Assert.IsNotNull(chestGo, "ChestReference GameObject not found — OrchardSceneDressingTool must run first.");
            _chestReference = chestGo.transform;

            var controllerGo = GameObject.Find("OrchardReachSceneController");
            Assert.IsNotNull(controllerGo, "OrchardReachSceneController not found in the dressed scene.");
            _controller = controllerGo.GetComponent<OrchardReachSceneController>();
            Assert.IsNotNull(_controller);
        }

        [Test]
        public void Eye_HeightAndDownwardPitch_AreInSeatedRange()
        {
            var centerEye = GameObject.Find("CenterEyeAnchor");
            Assert.IsNotNull(centerEye, "CenterEyeAnchor not found in the rig.");
            var t = centerEye.transform;

            Assert.That(t.position.y, Is.InRange(1.10f, 1.20f),
                $"eye height {t.position.y:F3}m must be 1.10-1.20m for a seated patient.");

            // forward.y = -sin(pitchDown); positive pitchDown means looking below horizontal.
            float pitchDownDeg = -Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            Assert.That(pitchDownDeg, Is.InRange(20f, 40f),
                $"camera forward {t.forward} implies {pitchDownDeg:F1} deg downward pitch; must be 20-40 deg toward the table.");
        }

        [Test]
        public void Table_TopHeight_AndNearEdgeDistanceFromChest_AreInRange()
        {
            var table = GameObject.Find("OrchardTable");
            Assert.IsNotNull(table, "OrchardTable not found.");
            var bounds = CombinedRendererBounds(table);

            Assert.That(bounds.max.y, Is.InRange(0.58f, 0.62f),
                $"table top surface {bounds.max.y:F3}m must be 0.58-0.62m.");

            float nearEdgeDist = bounds.min.z - _chestReference.position.z;
            Assert.That(nearEdgeDist, Is.InRange(0.25f, 0.45f),
                $"table near edge is {nearEdgeDist:F3}m in front of chest (z); must be 0.25-0.45m.");
        }

        [Test]
        public void Basket_HorizontalDistanceFromChest_AndRestsOnTable()
        {
            var basket = GameObject.Find("Basket");
            Assert.IsNotNull(basket, "Basket not found.");
            var basketBounds = CombinedRendererBounds(basket);

            var chestXZ = new Vector2(_chestReference.position.x, _chestReference.position.z);
            var rimCenterXZ = new Vector2(basketBounds.center.x, basketBounds.center.z);
            float horizDist = Vector2.Distance(chestXZ, rimCenterXZ);
            Assert.That(horizDist, Is.InRange(0.30f, 0.50f),
                $"basket rim center is {horizDist:F3}m from chest horizontally; must be 0.30-0.50m.");

            var table = GameObject.Find("OrchardTable");
            var tableBounds = CombinedRendererBounds(table);
            float restDelta = Mathf.Abs(basketBounds.min.y - tableBounds.max.y);
            Assert.That(restDelta, Is.LessThanOrEqualTo(0.01f),
                $"basket bottom ({basketBounds.min.y:F4}) must rest on the tabletop ({tableBounds.max.y:F4}) within +/-1cm, delta={restDelta:F4}.");
        }

        [Test]
        public void Targets_200Samples_AreAboveTableAndWithinReachEnvelope()
        {
            var table = GameObject.Find("OrchardTable");
            var tableTop = CombinedRendererBounds(table).max.y;

            double armLengthM = (double)GetPrivateField(_controller, "armLengthM");
            var chest = _chestReference.position;
            double[] chestArr = { chest.x, chest.y, chest.z };

            var rng = new System.Random(20260917); // fixed seed: deterministic, reproducible failures
            const int sampleCount = 200;
            for (int i = 0; i < sampleCount; i++)
            {
                double az = AzimuthMinDeg + rng.NextDouble() * (AzimuthMaxDeg - AzimuthMinDeg);
                double el = ElevationMinDeg + rng.NextDouble() * (ElevationMaxDeg - ElevationMinDeg);
                double reach = ReachPercentMin + rng.NextDouble() * (ReachPercentMax - ReachPercentMin);

                var target = TargetPlacement.Place(chestArr, armLengthM, az, el, reach);

                Assert.That(target.Y, Is.GreaterThan(tableTop + 0.05),
                    $"sample {i} (az={az:F1} el={el:F1} reach={reach:F1}%): target y={target.Y:F3} must be > tableTop+0.05={tableTop + 0.05:F3}.");

                double horizDist = Math.Sqrt(
                    (target.X - chestArr[0]) * (target.X - chestArr[0]) +
                    (target.Z - chestArr[2]) * (target.Z - chestArr[2]));
                double maxHoriz = (reach / 100.0) * armLengthM + 0.02;
                Assert.That(horizDist, Is.LessThanOrEqualTo(maxHoriz),
                    $"sample {i} (az={az:F1} el={el:F1} reach={reach:F1}%): horizontal dist {horizDist:F3}m from shoulder exceeds envelope {maxHoriz:F3}m.");
            }
        }

        [Test]
        public void FruitApple_RigidbodyDoesNotFallAtSpawn_AndUsesCorrectAppleMaterial()
        {
            var prefabRoot = UnityEditor.PrefabUtility.LoadPrefabContents(FruitApplePath);
            try
            {
                var rb = prefabRoot.GetComponentInChildren<Rigidbody>();
                Assert.IsNotNull(rb, "fruit_apple has no Rigidbody.");
                Assert.IsTrue(rb.isKinematic || !rb.useGravity,
                    "fruit_apple's Rigidbody must not fall at spawn: expected isKinematic=true (or useGravity=false as a fallback), " +
                    $"got isKinematic={rb.isKinematic} useGravity={rb.useGravity}.");

                var renderer = prefabRoot.GetComponentInChildren<Renderer>();
                Assert.IsNotNull(renderer, "fruit_apple has no Renderer.");
                var mat = renderer.sharedMaterial;
                Assert.IsNotNull(mat, "fruit_apple's renderer has no material assigned.");
                string matPath = UnityEditor.AssetDatabase.GetAssetPath(mat);
                StringAssert.DoesNotContain("632623", matPath,
                    $"fruit_apple must NOT use the 632623 (green distractor) material; got '{matPath}'.");
                StringAssert.Contains("12913", matPath,
                    $"fruit_apple must use the 12913 (red apple) material; got '{matPath}'.");
                StringAssert.Contains("Universal Render Pipeline", mat.shader.name,
                    $"fruit_apple's material must use a URP shader; got '{mat.shader.name}' (a non-URP shader renders pink).");
            }
            finally
            {
                UnityEditor.PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        [Test]
        public void Ground_IsAtLeast40x40_AndCenteredUnderPlayer()
        {
            var ground = GameObject.Find("OrchardGround");
            Assert.IsNotNull(ground, "OrchardGround not found.");
            var bounds = CombinedRendererBounds(ground);

            Assert.That(bounds.size.x, Is.GreaterThanOrEqualTo(40f), $"ground X size {bounds.size.x:F1}m must be >= 40m.");
            Assert.That(bounds.size.z, Is.GreaterThanOrEqualTo(40f), $"ground Z size {bounds.size.z:F1}m must be >= 40m.");

            var chestXZ = new Vector2(_chestReference.position.x, _chestReference.position.z);
            var groundCenterXZ = new Vector2(bounds.center.x, bounds.center.z);
            Assert.That(Vector2.Distance(chestXZ, groundCenterXZ), Is.LessThanOrEqualTo(5f),
                "ground plane must be centered under the player (within 5m of the chest reference), not off to a corner.");
        }

        /// <summary>
        /// The only bullet that needs actual runtime behaviour (the demo replay's "first frame"). Rather than
        /// entering Play mode (this is an EditMode test, per the brief), invokes the controller's own private
        /// Awake() once via reflection — the exact same production code path Play mode would run — so this
        /// exercises the real Awake()/UpdateDemoHandProxy() logic, not a reimplementation of it in the test.
        /// Cleans up the one side effect (an instantiated "FruitInstance" GameObject) afterward since the scene
        /// is never saved by this test anyway, but leaving stray objects around would be sloppy.
        /// </summary>
        [Test]
        public void HandVisual_EnabledUnderRig_AtPlausibleWristDistance_OnFirstDemoFrame()
        {
            var awake = typeof(OrchardReachSceneController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(awake, "OrchardReachSceneController.Awake() not found via reflection.");
            awake.Invoke(_controller, null);
            try
            {
                var proxy = (Transform)GetPrivateField(_controller, "demoHandProxyR");
                Assert.IsNotNull(proxy, "demoHandProxyR is not wired on the controller.");
                Assert.IsTrue(proxy.gameObject.activeInHierarchy, "hand visual GameObject must be active after the first demo frame.");

                // Run11: the visible hand is the ghost MESH when the scene has one; demoHandProxyR is then only
                // the (deliberately hidden) index-tip marker that positions it -- see
                // OrchardReachSceneController.DriveDemoHandAndFruit's Run11 comment. Assert on whichever object
                // is actually the hand visual in this scene, so this keeps testing "the patient can see a hand"
                // rather than "one specific legacy GameObject is drawn".
                var ghost = (Transform)GetPrivateField(_controller, "demoHandGhostVisual");
                if (ghost != null)
                {
                    var ghostRenderers = ghost.GetComponentsInChildren<Renderer>(true);
                    Assert.That(ghostRenderers.Length, Is.GreaterThan(0), "ghost hand visual has no renderers.");
                    Assert.IsTrue(Array.TrueForAll(ghostRenderers, r => r.enabled),
                        "ghost hand mesh must be visible after the first demo frame.");
                    var markerRend = proxy.GetComponent<Renderer>();
                    Assert.IsTrue(markerRend == null || !markerRend.enabled,
                        "the cyan index-tip debug marker must be hidden once the real hand mesh is drawn (run10 screenshot review).");
                }
                else
                {
                    var rend = proxy.GetComponent<Renderer>();
                    Assert.IsNotNull(rend, "hand visual has no Renderer.");
                    Assert.IsTrue(rend.enabled, "with no ghost mesh in the scene, the marker is the fallback hand visual and must be visible.");
                }

                var rig = GameObject.Find(RigPath);
                Assert.IsNotNull(rig, "camera rig not found.");
                Assert.IsTrue(proxy.IsChildOf(rig.transform), "hand visual must be parented under the camera rig.");

                float dist = Vector3.Distance(proxy.position, _chestReference.position);
                Assert.That(dist, Is.LessThanOrEqualTo(0.8f),
                    $"hand visual is {dist:F3}m from chest on the first demo frame; expected a plausible wrist position (<=0.8m).");
            }
            finally
            {
                var fruit = GameObject.Find("FruitInstance");
                if (fruit != null) UnityEngine.Object.DestroyImmediate(fruit);
            }
        }

        private static object GetPrivateField(object instance, string name)
        {
            var field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"field '{name}' not found on {instance.GetType().Name} via reflection.");
            return field.GetValue(instance);
        }

        private static Bounds CombinedRendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0), $"{go.name} has no renderers to compute bounds from.");
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
