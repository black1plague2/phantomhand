using System;
using UnityEngine;

namespace Opus.Shell
{
    /// <summary>Trigger volume over the basket opening. Fires <see cref="OnFruitEntered"/> when a
    /// <see cref="FruitMarker"/>-tagged collider enters; <see cref="OrchardReachSceneController"/> decides
    /// whether the current trial state (grasp held long enough) counts this as a successful placement.
    /// Run16 (stage D sorting): <see cref="ContainerId"/> is empty/null for the original single-basket scenes
    /// (stages A-C) -- unchanged behaviour. Stage D's 3 sorting containers each set it to one of
    /// SortingRuleEvaluator's known ids ("red_basket" | "green_basket" | "compost") so the scene controller can
    /// tell which physical container a placement landed in.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BasketTrigger : MonoBehaviour
    {
        [SerializeField] private string containerId;
        public string ContainerId { get => containerId; set => containerId = value; }

        public event Action<GameObject> OnFruitEntered;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var marker = other.GetComponentInParent<FruitMarker>();
            if (marker != null) OnFruitEntered?.Invoke(marker.gameObject);
        }
    }
}
