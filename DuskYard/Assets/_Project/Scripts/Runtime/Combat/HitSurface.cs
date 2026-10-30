using UnityEngine;

namespace DuskYard.Combat
{
    /// <summary>Tags a collider with the surface used to pick VFX / decal / light / shake.</summary>
    public sealed class HitSurface : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.Stone;

        public static SurfaceType Resolve(Collider c)
        {
            if (c == null) return SurfaceType.Stone;
            var s = c.GetComponentInParent<HitSurface>();
            return s ? s.surface : SurfaceType.Stone;
        }
    }
}
