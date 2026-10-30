using DuskYard.Combat;
using DuskYard.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuskYard.VFX
{
    /// <summary>
    /// Single entry point for contact feedback. For one <see cref="HitInfo"/> it fires, in the same frame:
    ///  1. contact flash + secondary particles (pooled, oriented between surface normal and reflected blade direction)
    ///  2. a decal (pooled, oriented along the slash, parented to moving targets)
    ///  3. a short dynamic point light at the contact (pooled, no shadows)
    ///  4. camera impulse + hit-stop
    /// Everything is pre-allocated in Awake; Play() does not allocate.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class HitFeedbackSystem : MonoBehaviour
    {
        public static HitFeedbackSystem Instance { get; private set; }

        [SerializeField] SurfaceFxProfile[] profiles;
        [SerializeField] int contactLightCount = 2;
        [SerializeField] bool enableHitStop = true;

        ParticleFxPool[] _pools;            // indexed by SurfaceType
        SurfaceFxProfile[] _bySurface;
        DecalPool[] _decals;                // indexed by SurfaceType, one material per pool
        Light[] _lights;
        float[] _lightAge, _lightDuration, _lightPeak;
        static readonly Vector3 ParkedLightPosition = new Vector3(0f, -1000f, 0f);
        int _nextLight;

        public int HitsPlayed { get; private set; }
        public HitInfo LastHit { get; private set; }

        void Awake()
        {
            Instance = this;
            HitStop.Enabled = enableHitStop;
            HitStop.Prewarm();
            int surfaceCount = System.Enum.GetValues(typeof(SurfaceType)).Length;
            _pools = new ParticleFxPool[surfaceCount];
            _bySurface = new SurfaceFxProfile[surfaceCount];
            _decals = new DecalPool[surfaceCount];

            var poolRoot = new GameObject("ParticlePools").transform;
            poolRoot.SetParent(transform, false);
            var decalRoot = new GameObject("Decals").transform;
            decalRoot.SetParent(transform, false);
            foreach (var p in profiles)
            {
                if (!p) continue;
                _bySurface[(int)p.surface] = p;
                if (p.hitPrefab) _pools[(int)p.surface] = new ParticleFxPool(p.hitPrefab, p.poolSize, poolRoot);
                if (p.decalMaterial) _decals[(int)p.surface] = new DecalPool(p.decalMaterial, p.decalPoolSize, decalRoot);
            }

            _lights = new Light[contactLightCount];
            _lightAge = new float[contactLightCount];
            _lightDuration = new float[contactLightCount];
            _lightPeak = new float[contactLightCount];
            var lightRoot = new GameObject("ContactLights").transform;
            lightRoot.SetParent(transform, false);
            for (int i = 0; i < contactLightCount; i++)
            {
                var go = new GameObject($"ContactLight_{i}");
                go.transform.SetParent(lightRoot, false);
                go.AddHDLight(LightType.Point);
                var l = go.GetComponent<Light>();
                l.shadows = LightShadows.None;
                l.lightUnit = LightUnit.Lumen;
                // lights stay enabled for their whole life: toggling a HD light re-registers it with the render
                // database (allocates). An idle light has zero intensity and is parked far outside the frustum.
                l.intensity = 0f;
                go.transform.position = ParkedLightPosition;
                _lights[i] = l;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        SurfaceFxProfile ProfileFor(SurfaceType s) => _bySurface[(int)s] ? _bySurface[(int)s] : _bySurface[(int)SurfaceType.Stone];

        public void Play(in HitInfo hit)
        {
            var profile = ProfileFor(hit.Surface);
            if (!profile) return;
            HitsPlayed++;
            LastHit = hit;

            // particles: spray between the normal and the reflected blade direction so debris reads as "knocked off"
            Vector3 reflected = Vector3.Reflect(hit.Direction, hit.Normal);
            Vector3 spray = Vector3.Slerp(hit.Normal, (reflected + hit.Direction * 0.35f).normalized, profile.sprayAlongSwing);
            if (spray.sqrMagnitude < 1e-4f) spray = hit.Normal;
            var pool = _pools[(int)profile.surface];
            pool?.Spawn(hit.Point + hit.Normal * 0.015f, Quaternion.LookRotation(spray, Vector3.up));

            // decal: only parent to moving (rigidbody) colliders; static world keeps decals under the pool root
            Transform attach = hit.Collider && hit.Collider.attachedRigidbody ? hit.Collider.transform : null;
            _decals[(int)profile.surface]?.Spawn(hit.Point, hit.Normal, hit.Direction, profile.decalSize, profile.decalDepth,
                profile.decalLifetime, profile.decalFadeTime, attach);

            // dynamic contact light
            var l = _lights[_nextLight];
            l.transform.position = hit.Point + hit.Normal * 0.2f;
            l.color = profile.lightColor;
            l.range = profile.lightRange;
            _lightAge[_nextLight] = 0f;
            _lightDuration[_nextLight] = profile.lightDuration;
            _lightPeak[_nextLight] = profile.lightLumen;
            _nextLight = (_nextLight + 1) % _lights.Length;

            if (ThirdPersonCamera.Active) ThirdPersonCamera.Active.AddShake(profile.shakeAmplitude, profile.shakeDuration);
            HitStop.Trigger(profile.hitStop);
        }

        void Update()
        {
            // unscaled: the light flash must not freeze during hit-stop
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lightDuration[i] <= 0f) continue;
                var l = _lights[i];
                _lightAge[i] += dt;
                float t = _lightAge[i] / _lightDuration[i];
                if (t >= 1f)
                {
                    l.intensity = 0f;
                    l.transform.position = ParkedLightPosition;
                    _lightDuration[i] = 0f;
                    continue;
                }
                // instant attack, quadratic falloff
                float k = (1f - t) * (1f - t);
                l.intensity = _lightPeak[i] * k;
            }
            float gameDt = Time.deltaTime;
            foreach (var d in _decals) d?.Tick(gameDt);
        }

        public int ActiveDecals()
        {
            int n = 0;
            foreach (var d in _decals) if (d != null) n += d.ActiveCount;
            return n;
        }

        public int DecalCapacity()
        {
            int n = 0;
            foreach (var d in _decals) if (d != null) n += d.Capacity;
            return n;
        }

        public int AliveParticles()
        {
            int n = 0;
            foreach (var p in _pools) if (p != null) n += p.AliveParticles();
            return n;
        }

        public int ParticleBudget()
        {
            int n = 0;
            foreach (var p in _pools) if (p != null) n += p.MaxParticles();
            return n;
        }
    }
}
