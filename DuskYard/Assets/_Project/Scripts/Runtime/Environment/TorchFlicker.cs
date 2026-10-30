using UnityEngine;

namespace DuskYard.Environment
{
    /// <summary>
    /// Flame flicker for a realtime light (brazier / torch): low-frequency Perlin intensity + tiny positional wobble so
    /// shadows breathe. Baked torches do not use this (their light is in the lightmap / probes).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class TorchFlicker : MonoBehaviour
    {
        [SerializeField] float intensityVariation = 0.22f;
        [SerializeField] float speed = 7f;
        [SerializeField] float wobble = 0.025f;
        [SerializeField] Transform flameVisual;

        Light _light;
        float _baseIntensity, _seed;
        Vector3 _basePos, _flameScale;

        void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _basePos = transform.localPosition;
            _seed = Random.value * 100f;
            if (flameVisual) _flameScale = flameVisual.localScale;
        }

        void Update()
        {
            float t = Time.time * speed + _seed;
            float n = Mathf.PerlinNoise(t, 0.37f) * 0.7f + Mathf.PerlinNoise(t * 2.3f, 4.1f) * 0.3f; // 0..1
            float k = 1f + (n - 0.5f) * 2f * intensityVariation;
            _light.intensity = _baseIntensity * k;
            transform.localPosition = _basePos + new Vector3(Mathf.PerlinNoise(t, 9f) - 0.5f, Mathf.PerlinNoise(3f, t) - 0.5f, Mathf.PerlinNoise(t, 17f) - 0.5f) * wobble;
            if (flameVisual) flameVisual.localScale = _flameScale * (0.9f + 0.2f * n);
        }
    }
}
