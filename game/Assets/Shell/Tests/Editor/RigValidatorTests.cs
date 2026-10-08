using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Opus.Shell.Tests
{
    /// <summary>
    /// R2 (Track U next-run brief): "the reproducibility guarantee" for the Building Blocks rig applied to
    /// <c>Assets/Scenes/OrchardReach.unity</c> (docs/UNITY_PRACTICES.md §3): Camera Rig -> Hand Tracking ->
    /// Interactions Rig (Meta's current name for the OVR comprehensive interaction rig), installed via
    /// `Tools/OPUS` editor automation, not hand-wired, so re-applying it should always reproduce this exact
    /// structure. Paths below are pinned to the concrete hierarchy the Building Blocks installer produced
    /// (verified once via SceneObjectsTools.GetSceneHierarchy against the real installed rig) rather than
    /// guessed, per _COMMON.md rule 5 ("test everything... paste real command output as evidence").
    ///
    /// PointableCanvas/GraphicRaycaster is intentionally NOT checked here yet: no UI canvas exists in the scene
    /// until R4 (shell/flow) adds one. Add that assertion when R4 lands its first world-space canvas.
    /// </summary>
    public class RigValidatorTests
    {
        private const string ScenePath = "Assets/Scenes/OrchardReach.unity";
        private const string CameraRigRoot = "[BuildingBlock] Camera Rig";
        private const string InteractionRigRoot = CameraRigRoot + "/[BuildingBlock] OVRComprehensiveInteractionRig";

        [OneTimeSetUp]
        public void OpenRigScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void Rig_HasExactlyOneOVRManager()
        {
            var managers = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var m in managers)
            {
                if (m != null && m.GetType().Name == "OVRManager") count++;
            }
            Assert.AreEqual(1, count, "Exactly one OVRManager must exist in the rig scene (found on the Camera Rig root).");
        }

        [Test]
        public void Rig_HasExactlyOneEnabledCamera()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            int enabledCount = 0;
            foreach (var c in cameras)
            {
                if (c.enabled && c.gameObject.activeInHierarchy) enabledCount++;
            }
            Assert.AreEqual(1, enabledCount,
                "Exactly one enabled Camera must exist (CenterEyeAnchor) — no duplicate Main Camera, and " +
                "Left/RightEyeAnchor cameras must stay disabled per OVRCameraRig's standard setup.");
        }

        [Test]
        public void Rig_HasBothOVRHandDataSources()
        {
            AssertExists(InteractionRigRoot + "/OVRHands/OVRHandDataSourceLeft");
            AssertExists(InteractionRigRoot + "/OVRHands/OVRHandDataSourceRight");
        }

        [Test]
        public void Rig_HasBothHandGrabInteractors()
        {
            AssertExists(InteractionRigRoot + "/ComprehensiveInteractorsLeft/Interactors/Hand and No Controller/HandGrabInteractor");
            AssertExists(InteractionRigRoot + "/ComprehensiveInteractorsRight/Interactors/Hand and No Controller/HandGrabInteractor");
        }

        [Test]
        public void Rig_HasBothPokeInteractors()
        {
            AssertExists(InteractionRigRoot + "/ComprehensiveInteractorsLeft/Interactors/Hand/HandPokeInteractor");
            AssertExists(InteractionRigRoot + "/ComprehensiveInteractorsRight/Interactors/Hand/HandPokeInteractor");
        }

        [Test]
        public void Rig_HasBothRayInteractors()
        {
            AssertExists(InteractionRigRoot + "/ComprehensiveInteractorsLeft/Interactors/Hand and No Controller/HandRayInteractor");
            AssertExists(InteractionRigRoot + "/ComprehensiveInteractorsRight/Interactors/Hand and No Controller/HandRayInteractor");
        }

        [Test]
        public void Rig_HasNoDuplicateCameraRig()
        {
            var rigs = GameObject.FindGameObjectsWithTag("Untagged"); // placeholder scan avoided; count by name instead
            int count = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.scene.IsValid() && go.name == "[BuildingBlock] Camera Rig" && go.transform.parent == null) count++;
            }
            Assert.AreEqual(1, count, "Exactly one root-level Camera Rig must exist (no duplicate rig install).");
        }

        private static void AssertExists(string path)
        {
            var go = GameObject.Find(path);
            Assert.IsNotNull(go, $"Expected GameObject at path '{path}' but it was not found in the open scene.");
        }
    }
}
