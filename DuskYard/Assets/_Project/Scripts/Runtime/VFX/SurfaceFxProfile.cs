using DuskYard.Combat;
using UnityEngine;

namespace DuskYard.VFX
{
    /// <summary>Data-only description of how a surface reacts to a blade contact.</summary>
    [CreateAssetMenu(menuName = "DuskYard/Surface FX Profile", fileName = "SFX_New")]
    public sealed class SurfaceFxProfile : ScriptableObject
    {
        public SurfaceType surface;

        [Header("Particles")]
        public ParticleSystem hitPrefab;
        [Min(1)] public int poolSize = 6;
        [Tooltip("0 = spray along the surface normal, 1 = spray along the reflected blade direction.")]
        [Range(0f, 1f)] public float sprayAlongSwing = 0.55f;

        [Header("Decal")]
        public Material decalMaterial;
        [Min(1)] public int decalPoolSize = 6;
        public Vector2 decalSize = new Vector2(0.35f, 0.12f);
        public float decalDepth = 0.25f;
        public float decalLifetime = 7f;
        public float decalFadeTime = 1.5f;

        [Header("Contact light (dynamic)")]
        public Color lightColor = new Color(1f, 0.72f, 0.45f);
        public float lightLumen = 900f;
        public float lightRange = 3.5f;
        public float lightDuration = 0.12f;

        [Header("Camera / time")]
        public float shakeAmplitude = 0.05f;
        public float shakeDuration = 0.14f;
        public float hitStop = 0.05f;
    }
}
