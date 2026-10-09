using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
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
            pipeline.shadowDistance = 60f;
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

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader not found.");
            AssetDatabase.CreateAsset(new Material(shader), dir + "/Lit.mat");
        }

        static string CreateBootScene()
        {
            string path = GeneratedDir + "/Boot.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
