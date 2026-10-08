using UnityEngine;

namespace Opus.Shell
{
    /// <summary>Marks a fruit instance so <see cref="BasketTrigger"/> can identify it without a Unity tag
    /// (tags require editing TagManager.asset, which is unnecessary churn for a single marker check).
    /// Run16 (stage D sorting + stage C precision): also carries the fruit's own color/ripeness tags (cosmetic
    /// material tint only -- no new mesh/asset) so the scene can report them to
    /// <see cref="Opus.Games.OrchardReach.OrchardReachModule.ConfirmPlacedInContainer"/> without re-deriving them
    /// from anything visual. Defaults (red/ripe) match every pre-run16 scene's implicit behaviour exactly.</summary>
    public sealed class FruitMarker : MonoBehaviour
    {
        /// <summary>"red" | "green" -- SortingRuleEvaluator's only two recognised fruit colors.</summary>
        public string Color = "red";
        public bool Ripe = true;

        /// <summary>Tints every Renderer under this fruit to a flat color standing in for Color/Ripe (no new
        /// material/texture asset -- MaterialPropertyBlock so the shared material asset itself is never mutated,
        /// same "don't dirty the asset" discipline as everything else this project tints at runtime). Ripe fruit
        /// is fully saturated; unripe is paled toward white so the same red/green hue reads as "not ready" at a
        /// glance without needing a third color.</summary>
        public void ApplyTint()
        {
            Color32 baseColor = Color == "green" ? new Color32(90, 170, 60, 255) : new Color32(190, 40, 40, 255);
            var tint = (UnityEngine.Color)baseColor;
            if (!Ripe) tint = UnityEngine.Color.Lerp(tint, UnityEngine.Color.white, 0.55f);
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint); // covers non-URP/Standard shaders too
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
