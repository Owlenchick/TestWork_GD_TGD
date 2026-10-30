using UnityEngine;

namespace DuskYard.VFX
{
    /// <summary>
    /// Fixed-size ring pool of one-shot particle effects. Instances are created once; when the pool wraps the oldest
    /// effect is restarted. Memory and particle count are therefore bounded by construction (no Instantiate at runtime).
    /// </summary>
    public sealed class ParticleFxPool
    {
        readonly ParticleSystem[] _roots;
        readonly ParticleSystem[][] _all;
        int _next;

        public ParticleFxPool(ParticleSystem prefab, int size, Transform parent)
        {
            _roots = new ParticleSystem[size];
            _all = new ParticleSystem[size][];
            for (int i = 0; i < size; i++)
            {
                var ps = Object.Instantiate(prefab, parent);
                ps.name = $"{prefab.name}_{i:00}";
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _roots[i] = ps;
                _all[i] = ps.GetComponentsInChildren<ParticleSystem>(true);
            }
        }

        public int Capacity => _roots.Length;

        public ParticleSystem Spawn(Vector3 position, Quaternion rotation)
        {
            var ps = _roots[_next];
            _next = (_next + 1) % _roots.Length;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.transform.SetPositionAndRotation(position, rotation);
            ps.Play(true);
            return ps;
        }

        public int AliveParticles()
        {
            int n = 0;
            foreach (var group in _all)
                foreach (var ps in group)
                    n += ps.particleCount;
            return n;
        }

        public int MaxParticles()
        {
            int n = 0;
            foreach (var group in _all)
                foreach (var ps in group)
                    n += ps.main.maxParticles;
            return n;
        }
    }
}
