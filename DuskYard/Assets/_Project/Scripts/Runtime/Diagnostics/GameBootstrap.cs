using UnityEngine;

namespace DuskYard.Diagnostics
{
    /// <summary>Applies runtime settings required by the measurement protocol (VSync off, uncapped frame rate).</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] bool disableVSync = true;
        [SerializeField] int targetFrameRate = -1;

        void Awake()
        {
            if (disableVSync) QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }
    }
}
