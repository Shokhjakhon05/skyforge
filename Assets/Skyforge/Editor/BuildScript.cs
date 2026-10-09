using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Skyforge.EditorTools
{
    /// <summary>
    /// Command-line build entry point used by CI. The repository holds code only: the boot scene,
    /// the URP assets and the base materials are generated here into Assets/Generated on every build.
    /// </summary>
    public static class BuildScript
    {
        const string GeneratedDir = "Assets/Generated";
        const string ApplicationId = "com.skyforge.globalops";
        const string OutputPath = "build/Android/Skyforge.apk";
        const string SigningDir = "ci/signing";

        public static void BuildAndroid()
        {
            try
            {
                if (!RunTests())
                {
                    EditorApplication.Exit(1);
                    return;
                }
                ApplyPlayerSettings();
                ApplySigning();
                Directory.CreateDirectory(GeneratedDir);
                CreateRenderPipeline();
                CreateMaterials();
                string scene = CreateBootScene();
                AssetDatabase.SaveAssets();

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scene },
                    locationPathName = OutputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                });

                var summary = report.summary;
                Debug.Log($"[Skyforge] Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB, " +
                          $"{summary.totalErrors} errors, {summary.totalTime}");
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("[Skyforge] Build failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Skyforge";
            PlayerSettings.productName = "Skyforge";
            PlayerSettings.bundleVersion = GetArgument("-sfVersion") ?? "0.0.1";
            PlayerSettings.Android.bundleVersionCode =
                int.TryParse(GetArgument("-sfVersionCode"), out int code) ? code : 1;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)26;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            EditorUserBuildSettings.buildAppBundle = false;
            UseLegacyInput();
        }

        /// <summary>
        /// The touch sticks and the uGUI event system read UnityEngine.Input, so the classic input
        /// manager has to be the active handler. The setting has no public API.
        /// </summary>
        static void UseLegacyInput()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[Skyforge] Could not open ProjectSettings to check the input handler.");
                return;
            }
            var settings = new SerializedObject(assets[0]);
            SerializedProperty handler = settings.FindProperty("activeInputHandler");
            if (handler == null)
            {
                Debug.LogWarning("[Skyforge] activeInputHandler property not found.");
                return;
            }
            Debug.Log("[Skyforge] activeInputHandler was " + handler.intValue);
            if (handler.intValue == 0) return;
            handler.intValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        sealed class TestCallbacks : ICallbacks
        {
            public bool Finished;
            public int Passed, Failed;

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    Debug.LogError("[Skyforge] TEST FAILED " + result.FullName + ": " + result.Message);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                Passed = result.PassCount;
                Failed = result.FailCount;
            }
        }

        /// <summary>
        /// Runs the EditMode tests inside the build, which saves starting a second editor in CI.
        /// Returns false when a test failed.
        /// </summary>
        static bool RunTests()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var callbacks = new TestCallbacks();
            api.RegisterCallbacks(callbacks);
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
            api.UnregisterCallbacks(callbacks);

            if (!callbacks.Finished)
            {
                Debug.LogWarning("[Skyforge] TESTS NOT RUN: the test runner did not finish synchronously.");
                return true;
            }
            Debug.Log($"[Skyforge] Tests: {callbacks.Passed} passed, {callbacks.Failed} failed");
            if (callbacks.Passed + callbacks.Failed == 0)
                Debug.LogWarning("[Skyforge] TESTS NOT RUN: no tests were found.");
            return callbacks.Failed == 0;
        }

        /// <summary>
        /// CI writes the keystore and a three-line file (store password, alias, alias password) into
        /// ci/signing. Without them the APK gets Unity's debug signature.
        /// </summary>
        static void ApplySigning()
        {
            string keystore = Path.GetFullPath(Path.Combine(SigningDir, "skyforge.keystore"));
            string secrets = Path.Combine(SigningDir, "signing.txt");
            if (!File.Exists(keystore) || !File.Exists(secrets))
            {
                Debug.LogWarning("[Skyforge] No signing files, building with the debug signature.");
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            string[] lines = File.ReadAllLines(secrets);
            if (lines.Length < 3)
                throw new InvalidOperationException(secrets + " must have three lines.");

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = lines[0];
            PlayerSettings.Android.keyaliasName = lines[1];
            PlayerSettings.Android.keyaliasPass = lines[2];
        }

        static void CreateRenderPipeline()
        {
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, GeneratedDir + "/UrpRenderer.asset");

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            pipeline.msaaSampleCount = 2;
            pipeline.supportsHDR = false;
            pipeline.shadowDistance = 140f;
            pipeline.shadowCascadeCount = 2;
            AssetDatabase.CreateAsset(pipeline, GeneratedDir + "/Urp.asset");

            GraphicsSettings.defaultRenderPipeline = pipeline;
        }

        /// <summary>
        /// Runtime code clones these instead of calling Shader.Find, so the shaders and exactly the
        /// variants in use end up in the build.
        /// </summary>
        static void CreateMaterials()
        {
            string dir = GeneratedDir + "/Resources/Materials";
            Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(new Material(FindShader("Universal Render Pipeline/Lit")), dir + "/Lit.mat");
            AssetDatabase.CreateAsset(new Material(FindShader("Universal Render Pipeline/Unlit")), dir + "/Unlit.mat");

            var sky = new Material(FindShader("Skybox/Procedural"));
            // A material made in code gets no keywords, and without one the sky has no sun disc.
            sky.EnableKeyword("_SUNDISK_SIMPLE");
            sky.SetFloat("_SunDisk", 1f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.95f);
            sky.SetFloat("_Exposure", 1.25f);
            sky.SetColor("_SkyTint", new Color(0.50f, 0.52f, 0.55f));
            AssetDatabase.CreateAsset(sky, dir + "/Sky.mat");
        }

        static Shader FindShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
                throw new InvalidOperationException("Shader not found: " + name);
            return shader;
        }

        static string CreateBootScene()
        {
            string path = GeneratedDir + "/Boot.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Fog shader variants are kept only for the fog modes that scenes in the build use.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException("Could not save " + path);
            return path;
        }

        static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
