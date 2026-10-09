using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// Entry point. The build ships one empty scene; everything in the game is created from code.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var root = new GameObject("Skyforge");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<SmokeScene>();
        }
    }
}
