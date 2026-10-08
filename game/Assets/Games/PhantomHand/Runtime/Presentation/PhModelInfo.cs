using UnityEngine;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Metadata the model importer bakes onto each wrapper prefab (Assets/Art/PhantomHand/Models/Resources/PhantomModels). The presenters
    /// read it to scale the arm parts to forearm_length_cm and to keep the hit proxy flush with the visible hand.
    /// Lives in its own file: Unity can only serialize a MonoBehaviour into a prefab when the file is named after the class (it used to sit in
    /// PhMaterials.cs, where the baked wrappers would have lost it as a missing script).
    /// </summary>
    public sealed class PhModelInfo : MonoBehaviour
    {
        public float referenceForearmM = 0.25f;   // forearm/sleeve meshes were baked for this forearm length (scaled along z at runtime)
        public float rangeFromM, rangeToM;        // sleeve: baked distance range from the wrist (m)
        public float palmTopY = 0.025f;           // hand: dorsal height over the palm / fingers in arm-local space (m)
        public float fingerTopY = 0.021f;
        public float palmLenM = 0.098f, handLenM = 0.19f;
        // hand, measured by the importer for the rigged hand (the defaults are the glove's constants, so the glove path is unchanged)
        public bool rigged;                       // skinned, bone-driven hand (PhRiggedHand on the wrapper)
        public bool texturedSkin;                 // the hand material carries its own skin texture: never override it with the flat skin colour
        public Color skinTone = new Color(0.74f, 0.58f, 0.48f);   // average skin colour of the hand; the forearm wrapper uses the same
        public float palmWidthM = 0.092f;         // across the knuckle line (hit proxy)
        public float palmCenterX;                 // lateral centre of the palm in arm-local x (thumb side is -x)
        public float fingerLenM = 0.08f;          // hit proxy: finger box length along z, starting at palmLenM
        public Vector2 thumbCenterXZ = new Vector2(-0.055f, 0.05f), thumbSizeXZ = new Vector2(0.04f, 0.07f);   // hit proxy: thumb box
        public Vector3 boundsMin, boundsMax;      // rigged hand: the fitted skinned mesh bounds in arm-local space (m), written by the importer; zero = a bake that never recorded them
    }
}
