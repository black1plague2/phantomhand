using UnityEngine;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// Scene-level handles for everything the presenters (U3/U4) and the composition root (U5) need, so none of
    /// them has to find objects by name. Filled in by PhantomHandSceneBuilder; the rig validator test asserts
    /// every field is set. World space: +x right, +y up, +z forward, origin on the floor under the seat.
    /// </summary>
    public sealed class PhantomAnchors : MonoBehaviour
    {
        [Header("Table and arms")]
        public Transform tableTop;           // centre of the table surface (y = 0.75)
        public Transform armRestOutline;     // glowing outline where the real right forearm rests
        public Transform virtualArmAnchor;   // virtual wrist position (offset_cm to the left of the real wrist)
        public Transform brushRig;           // parent for the brush; path is time-parameterised in code
        public Transform threatDropPoint;    // 40 cm above the virtual hand
        [Header("Probe")]
        public Transform probeRuler;         // 1 m ruler with cm ticks, 35 cm ahead at table height
        public Transform leftIndexDot;       // the dot shown on the left index tip during probes
        [Header("Panels")]
        public Transform instructionPanel;   // calibration + probe text with the progress ring (U4)
        public Transform questionnairePanel;
        public Transform witnessPanel;
        public Transform hudPanel;
        [Header("Rig")]
        public Transform cameraRig;
        public Transform seatedEyePose;      // design eye pose for editor screenshots only (the headset drives the real one)
        public DarkenController darken;
        public Transform audioRoot;          // placeholder AudioSources routed to the Sfx / Voice mixer groups

        public const float TableHeightM = 0.75f;
        public const float RulerDistanceM = 0.35f;
        public const float RulerLengthM = 1.0f;
        public const float DropHeightM = 0.40f;
    }
}
