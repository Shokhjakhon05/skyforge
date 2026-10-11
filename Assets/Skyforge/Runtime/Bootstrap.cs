using UnityEngine;
using UnityEngine.SceneManagement;

namespace Skyforge
{
    /// <summary>
    /// Entry point. The build ships one empty scene; everything in the game is created from code.
    /// </summary>
    public static class Bootstrap
    {
        static bool _listening;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            CreateGame();

            // A restart reloads the scene, which takes the old game object with it; build a new one then.
            if (_listening) return;
            _listening = true;
            SceneManager.sceneLoaded += (scene, mode) => CreateGame();
        }

        /// <summary>Starts the game over from the save, for changes that every screen depends on, like the language.</summary>
        public static void Restart() => SceneManager.LoadScene(0);

        static void CreateGame()
        {
            // The sniper game is what ships; the old drone game stays in the code but is not started.
            var root = new GameObject("Sniper");
            root.AddComponent<Sniper.SniperGame>();
            if (ShotDirector.OutputDir != null) root.AddComponent<Sniper.SniperShots>();
        }
    }
}
