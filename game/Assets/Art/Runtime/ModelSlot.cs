using UnityEngine;

namespace Opus.Art
{
    /// <summary>
    /// Marks the root of an art asset wrapper prefab so gameplay code (TargetPlacement, grasp/placement math,
    /// IGameModule) can find real-world-scale anchors without ever touching mesh geometry, per
    /// docs/MODEL_REQUESTS.md: "IGameModule/TargetPlacement only look at the ModelSlot root transform and named
    /// anchors, never the renderer." Swapping a placeholder mesh for a real model (or an LOD variant) means editing
    /// this prefab's children only — no C# changes.
    /// </summary>
    public enum PivotConvention
    {
        /// <summary>Root transform sits at the geometric center of the model (e.g. fruit_apple).</summary>
        Center,
        /// <summary>Root transform sits at the floor/table contact point, centered on X/Z (e.g. basket, orchard_tree).</summary>
        BottomCenter,
        /// <summary>Root transform sits at the top-center of the model (e.g. orchard_table's tabletop).</summary>
        TopCenter,
        /// <summary>Root transform sits at a named rig joint, not a bounding-box derived point (e.g. ghost_hand's wrist).</summary>
        Joint,
    }

    [DisallowMultipleComponent]
    public class ModelSlot : MonoBehaviour
    {
        [Tooltip("Asset id from docs/MODEL_REQUESTS.md, e.g. \"fruit_apple\", \"basket\".")]
        public string slotId;

        [Tooltip("Real-world size used to derive the import scale factor, meters. Matches the MODEL_REQUESTS " +
                 "\"real-world size (m)\" column (diameter, height, etc. depending on the asset).")]
        public float realWorldSizeM;

        public PivotConvention pivot = PivotConvention.Center;

        [Tooltip("True if the source model needed an axis fix on import (e.g. orchard_tree's Z-up export, " +
                 "corrected with a -90 deg X rotation on the Model child).")]
        public bool axisFixApplied;

        [Header("Named anchors (optional; empty if the slot has none)")]
        [Tooltip("e.g. basket's opening plane, orchard_table's placement surface. Gameplay reads these transforms " +
                 "directly instead of computing anything from renderer bounds.")]
        public Transform[] anchors;

        /// <summary>Look up a named anchor child by GameObject name; returns null if absent (callers should treat a
        /// missing anchor as "use the root transform" rather than throw, since not every slot needs anchors).</summary>
        public Transform GetAnchor(string anchorName)
        {
            if (anchors == null) return null;
            foreach (var a in anchors)
            {
                if (a != null && a.name == anchorName) return a;
            }
            return null;
        }
    }
}
