using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// Runtime guarantees for the Phantom Hand rig: hand tracking stays on but no hand mesh is ever drawn ("the real
    /// hand is never rendered", PRD FR-VR-01), controller visuals are off, and the display runs at 72 Hz. Uses
    /// reflection for the Meta types so this assembly carries no Meta references.
    /// </summary>
    public sealed class RigRuntimeSettings : MonoBehaviour
    {
        public Transform rigRoot;
        public int displayFrequencyHz = 72;
        private readonly List<Renderer> _handRenderers = new List<Renderer>();

        private void Start()
        {
            if (rigRoot == null) return;
            foreach (var mb in rigRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (t.Name == "HandVisual")
                {
                    var p = t.GetProperty("ForceOffVisibility", BindingFlags.Public | BindingFlags.Instance);
                    if (p != null && p.CanWrite) p.SetValue(mb, true);
                }
            }
            CollectHandRenderers();
            SetDisplayFrequency();
        }

        private void CollectHandRenderers()
        {
            _handRenderers.Clear();
            foreach (var r in rigRoot.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathOf(r.transform);
                if (path.Contains("HandVisual") || path.Contains("Hand Tracking") || path.Contains("ControllerVisual") || path.Contains("ControllerPrefab"))
                    _handRenderers.Add(r);
            }
        }

        private void LateUpdate()
        {
            for (int i = 0; i < _handRenderers.Count; i++)
                if (_handRenderers[i] != null && _handRenderers[i].enabled) _handRenderers[i].enabled = false;
        }

        private void SetDisplayFrequency()
        {
            try
            {
                Type ovr = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { ovr = a.GetType("OVRManager"); if (ovr != null) break; }
                if (ovr == null) return;
                var display = ovr.GetProperty("display", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var prop = display?.GetType().GetProperty("displayFrequency");
                if (prop != null && prop.CanWrite) prop.SetValue(display, (float)displayFrequencyHz);
            }
            catch (Exception e) { Debug.LogWarning("[PhantomHand] could not set display frequency: " + e.Message); }
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
