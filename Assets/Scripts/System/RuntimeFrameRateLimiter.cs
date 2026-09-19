using UnityEngine;

namespace Nekolpos.System
{
    [DefaultExecutionOrder(-30010)]
    public sealed class RuntimeFrameRateLimiter : MonoBehaviour
    {
        private const int TargetFrameRate = 60;

        private static RuntimeFrameRateLimiter instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstance()
        {
            if (Application.isBatchMode || instance != null)
            {
                return;
            }

            GameObject limiterObject = new GameObject(nameof(RuntimeFrameRateLimiter));
            instance = limiterObject.AddComponent<RuntimeFrameRateLimiter>();
            DontDestroyOnLoad(limiterObject);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            ApplyFrameRateLimit();
        }

        private static void ApplyFrameRateLimit()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
