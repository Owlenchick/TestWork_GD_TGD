using DuskYard.Target;
using DuskYard.VFX;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuskYard.Diagnostics
{
    /// <summary>
    /// Developer overlay (IMGUI, not a game UI): H toggles the control hint, F1 toggles live stats.
    /// Strings are rebuilt at most 4 times per second to keep IMGUI allocations negligible; the benchmark disables it.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        [SerializeField] bool showHelp = true;
        [SerializeField] bool showStats;

        const string Help =
            "WASD move   Shift run   Mouse look (click to capture)\n" +
            "LMB / Space  attack (press again for the backhand combo)\n" +
            "Q / Tab / MMB  lock-on framing   F1 stats   H hide   Esc release cursor";

        string _stats = "";
        float _accum, _nextRefresh;
        int _frames;
        GUIStyle _style;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
                if (kb.f1Key.wasPressedThisFrame) showStats = !showStats;
            }
            if (!showStats) return;
            _accum += Time.unscaledDeltaTime;
            _frames++;
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            float ms = _accum / Mathf.Max(1, _frames) * 1000f;
            _accum = 0f; _frames = 0;
            var fx = HitFeedbackSystem.Instance;
            var dummy = TrainingDummy.All.Count > 0 ? TrainingDummy.All[0] : null;
            _stats = $"{1000f / ms:0} FPS  ({ms:0.00} ms)\n" +
                     $"particles {(fx ? fx.AliveParticles() : 0)} / {(fx ? fx.ParticleBudget() : 0)}\n" +
                     $"decals {(fx ? fx.ActiveDecals() : 0)} / {(fx ? fx.DecalCapacity() : 0)}\n" +
                     $"dummy {(dummy ? dummy.State.ToString() : "-")}  hits {(dummy ? dummy.HitsTaken : 0)}";
        }

        void OnGUI()
        {
            if (!showHelp && !showStats) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false, normal = { textColor = new Color(1f, 0.95f, 0.85f) } };
            if (showHelp) GUI.Label(new Rect(16, Screen.height - 76, 900, 70), Help, _style);
            if (showStats) GUI.Label(new Rect(16, 12, 400, 90), _stats, _style);
        }
    }
}
