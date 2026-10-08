using Oculus.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// U4 scene placement in one call: <c>PhantomHandUiInstaller.Install(root, anchors)</c>. Idempotent. Creates any missing
    /// panel anchor (instruction, questionnaire, witness, HUD; world-space RectTransforms, inactive), the EventSystem with the
    /// ISDK PointableCanvasModule (so a fingertip poke becomes a uGUI click), and the <see cref="PhantomHandUiPresenter"/>
    /// on the Presentation child (or the root), wired to the anchors and the <see cref="ArmThreatPresenter"/>.
    /// Panel contents are built at runtime by the presenter, never stored in the scene.
    /// </summary>
    public static class PhantomHandUiInstaller
    {
        public static readonly Vector3 InstructionPos = new Vector3(0f, 1.00f, 0.60f), InstructionEuler = new Vector3(26f, 0f, 0f);
        public static readonly Vector3 QuestionnairePos = new Vector3(0f, 1.06f, 0.47f), QuestionnaireEuler = new Vector3(18f, 0f, 0f);
        public static readonly Vector3 WitnessPos = new Vector3(0f, 1.28f, 0.95f), WitnessEuler = new Vector3(8f, 0f, 0f);
        public static readonly Vector3 HudPos = new Vector3(-0.34f, 1.36f, 0.80f), HudEuler = new Vector3(10f, 0f, 0f);

        public static PhantomHandUiPresenter Install(Transform root, PhantomAnchors anchors)
        {
            if (root == null) throw new System.ArgumentNullException("root");
            if (anchors == null) throw new System.ArgumentNullException("anchors");

            anchors.instructionPanel = EnsurePanel(anchors.instructionPanel, root, "InstructionPanel", InstructionPos, InstructionEuler, PhInstructionPanel.WidthMm, PhInstructionPanel.HeightMm);
            anchors.questionnairePanel = EnsurePanel(anchors.questionnairePanel, root, "QuestionnairePanel", QuestionnairePos, QuestionnaireEuler, PhQuestionnairePanel.WidthMm, PhQuestionnairePanel.HeightMm);
            anchors.witnessPanel = EnsurePanel(anchors.witnessPanel, root, "WitnessPanel", WitnessPos, WitnessEuler, PhWitnessPanel.WidthMm, PhWitnessPanel.HeightMm);
            anchors.hudPanel = EnsurePanel(anchors.hudPanel, root, "HudPanel", HudPos, HudEuler, PhHudPanel.WidthMm, PhHudPanel.HeightMm);

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(root, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<PointableCanvasModule>();
            }
            else
            {
                var existing = Object.FindFirstObjectByType<EventSystem>();
                if (existing.GetComponent<PointableCanvasModule>() == null) existing.gameObject.AddComponent<PointableCanvasModule>();
            }

            var host = root.Find("Presentation");
            var hostGo = host != null ? host.gameObject : root.gameObject;
            var ui = hostGo.GetComponent<PhantomHandUiPresenter>();
            if (ui == null) ui = hostGo.AddComponent<PhantomHandUiPresenter>();
            ui.anchors = anchors;
            ui.armPresenter = hostGo.GetComponent<ArmThreatPresenter>();
            ui.attachPoke = true;
            return ui;
        }

        private static Transform EnsurePanel(Transform existing, Transform root, string name, Vector3 pos, Vector3 euler, float wMm, float hMm)
        {
            Transform t = existing;
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(root, false);
                t = go.transform;
                t.SetPositionAndRotation(root.TransformPoint(pos), root.rotation * Quaternion.Euler(euler));
            }
            var rt = t as RectTransform;
            if (rt != null) { rt.sizeDelta = new Vector2(wMm, hMm); rt.localScale = Vector3.one * 0.001f; }
            t.gameObject.SetActive(false);   // panels must be inactive until the presenter has injected the poke references
            return t;
        }
    }
}
