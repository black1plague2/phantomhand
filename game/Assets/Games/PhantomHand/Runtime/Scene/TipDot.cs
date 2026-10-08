using UnityEngine;

namespace Opus.Games.PhantomHand
{
    /// <summary>The small dot shown on the participant's left index fingertip during probes (hand meshes stay hidden).
    /// The presenter assigns <see cref="target"/> (the tracked index-tip transform) and calls <see cref="SetVisible"/>.
    /// Without a target the dot stays where the scene builder put it.</summary>
    public sealed class TipDot : MonoBehaviour
    {
        public Transform target;
        public Renderer dotRenderer;
        public bool visibleOnStart = false;

        private void Awake() { SetVisible(visibleOnStart); }

        public void SetVisible(bool visible) { if (dotRenderer != null) dotRenderer.enabled = visible; }
        public bool IsVisible { get { return dotRenderer != null && dotRenderer.enabled; } }

        private void LateUpdate()
        {
            if (target != null) transform.position = target.position;
        }
    }
}
