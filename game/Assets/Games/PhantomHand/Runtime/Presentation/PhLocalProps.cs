using System;
using System.Collections.Generic;
using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Room props that come from Unity Asset Store packs. The packs exist only on the build PC and must never be committed (Standard Asset Store EULA: usable in the built game, not
    /// publishable; the repository is public), so the committed scene holds nothing but this component and its slot data: a wrapper NAME, a world position and a yaw per slot. No
    /// reference to a pack asset or to a local wrapper is ever serialized.
    ///
    /// <see cref="Spawn()"/> (called from Awake, so in play mode and in the built game) instantiates every slot whose wrapper exists in <see cref="PhModels.LocalFolder"/> under this object,
    /// and, when a local PH_Table exists, puts it at the pose of the scene's own table model (the PH_Table below this object) and switches that one off. The wrappers are baked on the
    /// build PC by PhantomModelImporter.BuildLocal. In a clone without the packs there is no local wrapper: Spawn() then does nothing at all (nothing created, nothing changed, no log).
    /// Everything it creates has HideFlags.DontSave, so it can never be saved into the scene; <see cref="Clear()"/> removes it and switches the scene's table back on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhLocalProps : MonoBehaviour
    {
        [Serializable]
        public struct Slot
        {
            public string wrapper;        // PhModels.Books, PhModels.Sideboard ...
            public Vector3 position;      // world metres
            public float yaw;             // degrees about +y; 0 faces +z (the front of a wrapper is +z)

            public Slot(string wrapper, Vector3 position, float yaw) { this.wrapper = wrapper; this.position = position; this.yaw = yaw; }
        }

        public Slot[] slots = new Slot[0];

        private readonly List<GameObject> _made = new List<GameObject>();
        private GameObject _tableHidden;
        private bool _tableWasActive;

        private void Awake() { Spawn(); }

        /// <summary>The slots that will be spawned: those whose wrapper exists. Pure (the lookup is passed in), so "no local wrapper: nothing happens" is decided without the engine.</summary>
        public static List<Slot> Plan(Slot[] slots, Func<string, bool> exists)
        {
            var res = new List<Slot>();
            if (slots == null || exists == null) return res;
            foreach (var s in slots)
                if (!string.IsNullOrEmpty(s.wrapper) && exists(s.wrapper)) res.Add(s);
            return res;
        }

        /// <summary>Spawns the local wrappers that exist; returns how many objects it created (0: nothing was touched).</summary>
        public int Spawn() { return Spawn(PhModels.LoadLocal); }

        /// <summary>Same, with the wrapper lookup passed in (the tests give it stand-ins; the game uses <see cref="PhModels.LoadLocal"/>).</summary>
        public int Spawn(Func<string, GameObject> loadLocal)
        {
            Clear();
            if (loadLocal == null) return 0;
            var names = new List<string>();
            var table = loadLocal(PhModels.Table);
            var committed = table != null ? FindSceneTable() : null;
            if (table != null && committed != null)
            {
                var go = Instantiate(table, committed.transform.parent, false);
                go.name = PhModels.Table;
                go.transform.SetPositionAndRotation(committed.transform.position, committed.transform.rotation);
                Own(go);
                _tableHidden = committed; _tableWasActive = committed.activeSelf; committed.SetActive(false);
                names.Add(PhModels.Table);
            }
            foreach (var s in Plan(slots, n => loadLocal(n) != null))
            {
                var go = Instantiate(loadLocal(s.wrapper), transform, false);
                go.name = s.wrapper;
                go.transform.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
                Own(go);
                names.Add(s.wrapper);
            }
            if (names.Count > 0) Debug.Log("[PhLocalProps] " + string.Join(", ", names.ToArray()) + (_tableHidden != null ? " (the scene's table is switched off)" : ""));
            return names.Count;
        }

        /// <summary>Removes what <see cref="Spawn()"/> created and switches the scene's table back on.</summary>
        public void Clear()
        {
            for (int i = _made.Count - 1; i >= 0; i--)
            {
                var go = _made[i];
                if (go == null) continue;
                go.SetActive(false);
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _made.Clear();
            if (_tableHidden != null) _tableHidden.SetActive(_tableWasActive);
            _tableHidden = null;
        }

        /// <summary>The scene's own table model: the PH_Table below this object (Spawn clears first, so it is never one of ours). Null for the box table, which has no model to replace.</summary>
        private GameObject FindSceneTable()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == PhModels.Table) return t.gameObject;
            return null;
        }

        private void Own(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            _made.Add(go);
        }
    }
}
