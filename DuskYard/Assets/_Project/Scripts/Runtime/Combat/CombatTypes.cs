using UnityEngine;

namespace DuskYard.Combat
{
    public enum SurfaceType { Stone = 0, Wood = 1, Straw = 2, Metal = 3 }

    /// <summary>Everything the feedback layer needs to place and orient a hit reaction.</summary>
    public struct HitInfo
    {
        public Vector3 Point;
        public Vector3 Normal;
        /// <summary>Normalized blade velocity at the moment of contact.</summary>
        public Vector3 Direction;
        public float Speed;
        public Collider Collider;
        public SurfaceType Surface;
        public int SwingIndex;
    }

    public interface IHittable
    {
        void ReceiveHit(in HitInfo hit);
    }
}
