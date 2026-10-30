using UnityEngine;

namespace DuskYard.Combat
{
    /// <summary>
    /// Very short global time-scale dip on impact (a few frames). Sells the contact without any animation.
    /// Overlapping requests extend the current stop instead of stacking.
    /// </summary>
    public sealed class HitStop : MonoBehaviour
    {
        static HitStop s_Instance;
        float _remaining;

        public static bool Enabled { get; set; } = true;

        /// <summary>Creates the runner up front so the first hit does not add a GameObject mid-play.</summary>
        public static void Prewarm()
        {
            if (s_Instance != null) return;
            var go = new GameObject("[HitStop]");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<HitStop>();
        }

        public static void Trigger(float duration, float timeScale = 0.06f)
        {
            if (!Enabled || duration <= 0f) return;
            Prewarm();
            s_Instance._remaining = Mathf.Max(s_Instance._remaining, duration);
            Time.timeScale = timeScale;
        }

        void Update()
        {
            if (_remaining <= 0f) return;
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining <= 0f) Time.timeScale = 1f;
        }

        void OnDisable()
        {
            if (_remaining > 0f) Time.timeScale = 1f;
            _remaining = 0f;
        }
    }
}
