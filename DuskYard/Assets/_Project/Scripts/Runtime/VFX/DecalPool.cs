using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DuskYard.VFX
{
    /// <summary>
    /// Ring buffer of HDRP DecalProjectors sharing one material. A decal is parented to the collider it was placed on,
    /// so marks on the swinging dummy move with it. Each decal holds, then fades through fadeFactor.
    ///
    /// Projectors are enabled once and never toggled and never change material: in HDRP both OnEnable/OnDisable and a
    /// material swap remove/re-add the decal in DecalSystem, which allocates. Idle slots are hidden with fadeFactor = 0
    /// and drawDistance = 0 (culled by distance), so they cost nothing to render and the steady state is allocation-free.
    /// </summary>
    public sealed class DecalPool
    {
        const float VisibleDrawDistance = 40f;

        struct Slot
        {
            public DecalProjector Projector;
            public float Age, Lifetime, FadeTime;
            public bool Active;
        }

        readonly Slot[] _slots;
        readonly Transform _home;
        int _next;

        public DecalPool(Material material, int size, Transform home)
        {
            _home = home;
            _slots = new Slot[size];
            for (int i = 0; i < size; i++)
            {
                var go = new GameObject($"Decal_{material.name}_{i:00}");
                go.SetActive(false); // configure before OnEnable registers the projector (avoids a null-material warning)
                go.transform.SetParent(home, false);
                var p = go.AddComponent<DecalProjector>();
                p.material = material;
                p.scaleMode = DecalScaleMode.ScaleInvariant;
                p.pivot = Vector3.zero;
                p.fadeScale = 0.9f;
                p.startAngleFade = 60f;
                p.endAngleFade = 85f;
                p.fadeFactor = 0f;
                p.drawDistance = 0f;
                go.SetActive(true);
                _slots[i].Projector = p;
            }
        }

        public int Capacity => _slots.Length;

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var s in _slots) if (s.Active) n++;
                return n;
            }
        }

        public void Spawn(Vector3 point, Vector3 normal, Vector3 swingDir, Vector2 size, float depth, float lifetime, float fadeTime,
            Transform attachTo)
        {
            ref Slot s = ref _slots[_next];
            _next = (_next + 1) % _slots.Length;

            var p = s.Projector;
            var t = p.transform;
            // the slash runs along the blade direction projected onto the surface
            Vector3 along = Vector3.ProjectOnPlane(swingDir, normal);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(Vector3.up, normal);
            Quaternion rot = Quaternion.LookRotation(-normal, Vector3.Cross(normal, along.normalized));
            t.SetParent(attachTo ? attachTo : _home, true);
            // projection box centred on the surface so curved targets stay inside it on both sides of the contact
            t.SetPositionAndRotation(point, rot);
            p.size = new Vector3(size.x, size.y, depth);
            p.fadeFactor = 1f;
            p.drawDistance = VisibleDrawDistance;
            s.Age = 0f;
            s.Lifetime = lifetime;
            s.FadeTime = Mathf.Max(0.01f, fadeTime);
            s.Active = true;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (!s.Active) continue;
                s.Age += dt;
                float fadeStart = s.Lifetime - s.FadeTime;
                if (s.Age >= s.Lifetime)
                {
                    s.Active = false;
                    s.Projector.fadeFactor = 0f;
                    s.Projector.drawDistance = 0f;
                    s.Projector.transform.SetParent(_home, false);
                }
                else if (s.Age > fadeStart)
                {
                    s.Projector.fadeFactor = 1f - (s.Age - fadeStart) / s.FadeTime;
                }
            }
        }
    }
}
