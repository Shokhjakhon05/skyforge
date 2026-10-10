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
        /// <summary>CI passes -sfBundle to get an App Bundle for Google Play instead of an APK for a phone.</summary>
        static bool Bundle => HasArgument("-sfBundle");
        static string OutputPath => Bundle ? "build/Android/Skyforge.aab" : "build/Android/Skyforge.apk";
        const string SigningDir = "ci/signing";

        const string ScreenshotDir = "build/Screenshots";
        /// <summary>Longest the screenshot run may take before the build carries on without it.</summary>
        const double ScreenshotTimeoutSeconds = 1500;

        static string _scene;
        static bool _screenshotsRan;
        static bool _leavingPlayMode;
        static double _screenshotDeadline;

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
                PrepareAssets();

                // Returning from this method lets the editor loop run, which play mode needs.
                if (HasArgument("-sfScreenshots")) StartScreenshots();
                else BuildPlayer();
            }
            catch (Exception e)
            {
                Debug.LogError("[Skyforge] Build failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        static void PrepareAssets()
        {
            Directory.CreateDirectory(GeneratedDir);
            CreateRenderPipeline();
            CreateMaterials();
            _scene = CreateBootScene();
            AssetDatabase.SaveAssets();
        }

        static void BuildPlayer()
        {
            try
            {
                if (_screenshotsRan) RestoreAssetsAfterPlayMode();
                ApplyIcon();

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { _scene },
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

        /// <summary>
        /// Playing the game changes the generated pipeline asset (quality settings) and the sky
        /// material (weather). They are put back in place rather than recreated: replacing the
        /// pipeline asset destroys the object URP's build step still holds.
        /// </summary>
        static void RestoreAssetsAfterPlayMode()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(GeneratedDir + "/Urp.asset");
            if (pipeline != null)
            {
                ConfigurePipeline(pipeline);
                EditorUtility.SetDirty(pipeline);
                GraphicsSettings.defaultRenderPipeline = pipeline;
            }
            var sky = AssetDatabase.LoadAssetAtPath<Material>(GeneratedDir + "/Resources/Materials/Sky.mat");
            if (sky != null)
            {
                ConfigureSky(sky);
                EditorUtility.SetDirty(sky);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Plays the game inside the editor so <see cref="ShotDirector"/> can save pictures of it.
        /// Whatever happens here, the APK build follows: screenshots are a view, not a gate.
        /// </summary>
        static void StartScreenshots()
        {
            _screenshotsRan = true;
            _leavingPlayMode = false;
            _screenshotDeadline = EditorApplication.timeSinceStartup + ScreenshotTimeoutSeconds;
            try
            {
                Directory.CreateDirectory(ScreenshotDir);
                ShotDirector.OutputDir = Path.GetFullPath(ScreenshotDir);
                ShotDirector.Finished = false;
                ShotDirector.Stage = 0;

                // Without a domain reload the statics above and the update hook survive entering play mode.
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions =
                    EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
                EditorApplication.update += ScreenshotUpdate;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skyforge] SCREENSHOTS SKIPPED: " + e.Message);
                EditorApplication.update -= ScreenshotUpdate;
                ShotDirector.OutputDir = null;
                BuildPlayer();
            }
        }

        static void ScreenshotUpdate()
        {
            try
            {
                if (!_leavingPlayMode)
                {
                    bool timedOut = EditorApplication.timeSinceStartup > _screenshotDeadline;
                    if (!ShotDirector.Finished && !timedOut) return;
                    if (timedOut) Debug.LogWarning("[Skyforge] SCREENSHOTS TIMED OUT");
                    _leavingPlayMode = true;
                    if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                    return;
                }
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

                int count = Directory.Exists(ScreenshotDir) ? Directory.GetFiles(ScreenshotDir, "*.png").Length : 0;
                Debug.Log("[Skyforge] Screenshots done: " + count);
            }
            catch (Exception e)
            {
                // An exception left in an update hook would repeat forever and hang the job.
                Debug.LogWarning("[Skyforge] SCREENSHOTS FAILED: " + e.Message);
                if (EditorApplication.isPlaying)
                {
                    // The player cannot be built from play mode; leave it and come back here.
                    _leavingPlayMode = true;
                    EditorApplication.ExitPlaymode();
                    return;
                }
            }

            EditorApplication.update -= ScreenshotUpdate;
            ShotDirector.OutputDir = null;
            BuildPlayer();
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

            EditorUserBuildSettings.buildAppBundle = Bundle;
            UseLegacyInput();
        }

        /// <summary>The icon is drawn by tools/gen_icon.py; one large picture serves every size.</summary>
        static void ApplyIcon()
        {
            const string path = "Assets/Skyforge/Branding/icon.png";
            // The .meta written by gen_meta.py carries no import settings, and Unity guessed "cubemap"
            // for this picture; say what it is.
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                (importer.textureShape != TextureImporterShape.Texture2D || importer.mipmapEnabled))
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon == null)
            {
                UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(path);
                Debug.LogWarning("[Skyforge] ICON MISSING: the build will carry Unity's default icon. The file loads as " +
                                 (main == null ? "nothing" : main.GetType().Name) + ".");
                return;
            }
            Debug.Log("[Skyforge] Icon applied.");
            try
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Android, IconKind.Any);
                var icons = new Texture2D[sizes.Length];
                for (int i = 0; i < icons.Length; i++) icons[i] = icon;
                PlayerSettings.SetIcons(NamedBuildTarget.Android, icons, IconKind.Any);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skyforge] ICON NOT SET: " + e.Message);
            }
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
            // Screen effects need the shaders this asset points to.
            rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (rendererData.postProcessData == null)
                Debug.LogWarning("[Skyforge] PostProcessData not found; screen effects will be off.");
            AssetDatabase.CreateAsset(rendererData, GeneratedDir + "/UrpRenderer.asset");

            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            ConfigurePipeline(pipeline);
            AssetDatabase.CreateAsset(pipeline, GeneratedDir + "/Urp.asset");

            GraphicsSettings.defaultRenderPipeline = pipeline;
        }

        /// <summary>The settings the player is built with; shader variants are kept or stripped to match.</summary>
        static void ConfigurePipeline(UniversalRenderPipelineAsset pipeline)
        {
            pipeline.renderScale = 1f;
            pipeline.msaaSampleCount = 2;
            pipeline.supportsHDR = false;
            pipeline.shadowDistance = 140f;
            pipeline.shadowCascadeCount = 2;
        }

        static void ConfigureSky(Material sky)
        {
            // A material made in code gets no keywords, and without one the sky has no sun disc.
            sky.EnableKeyword("_SUNDISK_SIMPLE");
            sky.SetFloat("_SunDisk", 1f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.95f);
            sky.SetFloat("_Exposure", 1.25f);
            sky.SetColor("_SkyTint", new Color(0.50f, 0.52f, 0.55f));
            sky.SetColor("_GroundColor", new Color(0.70f, 0.80f, 0.90f));
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
            ConfigureSky(sky);
            AssetDatabase.CreateAsset(sky, dir + "/Sky.mat");

            // Without this material the game falls back to a plain glossy sea.
            Shader water = Shader.Find("Skyforge/Water");
            if (water == null || ShaderUtil.ShaderHasError(water))
                Debug.LogWarning("[Skyforge] WATER SHADER BROKEN: using the fallback sea.");
            else
                AssetDatabase.CreateAsset(new Material(water), dir + "/Water.mat");

            Shader globe = Shader.Find("Skyforge/Globe");
            if (globe == null || ShaderUtil.ShaderHasError(globe))
                Debug.LogWarning("[Skyforge] GLOBE SHADER BROKEN: using a plain material.");
            else
                AssetDatabase.CreateAsset(new Material(globe), dir + "/Globe.mat");
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

        static bool HasArgument(string name)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
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
