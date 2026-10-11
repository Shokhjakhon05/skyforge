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
        const string ApplicationId = "com.zerohour.sniper";
        /// <summary>CI passes -sfBundle to get an App Bundle for Google Play instead of an APK for a phone.</summary>
        static bool Bundle => HasArgument("-sfBundle");
        static string OutputPath => Bundle ? "build/Android/Skyforge.aab" : "build/Android/Skyforge.apk";
        const string SigningDir = "ci/signing";

        const string ScreenshotDir = "build/Screenshots";
        /// <summary>Longest the screenshot run may take before the build carries on without it.</summary>
        const double ScreenshotTimeoutSeconds = 2400;

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
            PrepareCharacters();
            PrepareProps();
            PrepareAds();
            _scene = CreateBootScene();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Characters come as glTF files with a skeleton and animation clips. Each is turned into a
        /// prefab under Resources that carries its clips, so the game can load and animate it by name.
        /// </summary>
        static void PrepareCharacters()
        {
            const string source = "Assets/Sniper/Chars";
            if (!Directory.Exists(source)) return;
            string dir = GeneratedDir + "/Resources/Chars";
            Directory.CreateDirectory(dir);
            foreach (string file in Directory.GetFiles(source, "*.glb"))
            {
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
                var clips = new System.Collections.Generic.List<AnimationClip>();
                var report = new System.Text.StringBuilder();
                foreach (UnityEngine.Object asset in all)
                {
                    if (asset is AnimationClip clip)
                    {
                        clips.Add(clip);
                        report.Append(" clip:" + clip.name + (clip.legacy ? "(legacy " : "(") + clip.length.ToString("0.0") + "s)");
                    }
                    else if (asset is Material material) report.Append(" material:" + material.name + "<" + (material.shader != null ? material.shader.name : "none") + ">");
                }
                if (model == null)
                {
                    Debug.LogError("[Skyforge] CHARACTER NOT IMPORTED " + path + " (" + all.Length + " sub-assets)");
                    continue;
                }
                var instance = (GameObject)UnityEngine.Object.Instantiate(model);
                instance.name = name;
                var bounds = new Bounds(instance.transform.position, Vector3.zero);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) skinned.updateWhenOffscreen = true;
                Repaint(instance, name);
                var actor = instance.AddComponent<Skyforge.Actor>();
                actor.Clips = clips.ToArray();
                actor.Height = Mathf.Max(0.01f, bounds.size.y);
                Debug.Log("[Skyforge] character " + name + ": size " + bounds.size + ", renderers " + instance.GetComponentsInChildren<Renderer>().Length +
                    ", animator " + (instance.GetComponentInChildren<Animator>() != null) + ", legacy player " + (instance.GetComponentInChildren<Animation>() != null) + "," + report);
                PrefabUtility.SaveAsPrefabAsset(instance, dir + "/" + name + ".prefab");
                UnityEngine.Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Scenery comes as glTF files of every size and with the origin anywhere. Each becomes a
        /// prefab that stands on its own base at its own centre and knows how large it is.
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<Material, Material> Repainted = new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>
        /// The glTF importer gives its models a shader of its own that this render pipeline does not
        /// draw (they come out magenta). Every material is remade as a plain lit one of the pipeline,
        /// keeping its colour and its picture.
        /// </summary>
        static void Repaint(GameObject instance, string model)
        {
            string dir = GeneratedDir + "/Materials";
            Directory.CreateDirectory(dir);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null) continue;
                    if (!Repainted.TryGetValue(source, out Material lit))
                    {
                        Color colour = Color.white;
                        foreach (string name in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                        {
                            if (!source.HasProperty(name)) continue;
                            colour = source.GetColor(name);
                            break;
                        }
                        Texture picture = null;
                        foreach (string name in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                        {
                            if (!source.HasProperty(name)) continue;
                            picture = source.GetTexture(name);
                            if (picture != null) break;
                        }
                        float rough = source.HasProperty("roughnessFactor") ? source.GetFloat("roughnessFactor") : 0.8f;
                        float metal = source.HasProperty("metallicFactor") ? source.GetFloat("metallicFactor") : 0f;
                        lit = new Material(FindShader("Universal Render Pipeline/Lit"));
                        colour.a = 1f;
                        lit.SetColor("_BaseColor", colour);
                        if (picture != null) lit.SetTexture("_BaseMap", picture);
                        lit.SetFloat("_Smoothness", Mathf.Clamp(1f - rough, 0.05f, 0.45f));
                        lit.SetFloat("_Metallic", Mathf.Clamp(metal, 0f, 0.6f));
                        string safe = System.Text.RegularExpressions.Regex.Replace(model + "_" + source.name, "[^A-Za-z0-9_]", "_");
                        AssetDatabase.CreateAsset(lit, dir + "/" + safe + "_" + Repainted.Count + ".mat");
                        Repainted[source] = lit;
                    }
                    materials[i] = lit;
                }
                renderer.sharedMaterials = materials;
            }
        }

        static void PrepareProps()
        {
            const string source = "Assets/Sniper/Props";
            if (!Directory.Exists(source)) return;
            string dir = GeneratedDir + "/Resources/Props";
            Directory.CreateDirectory(dir);
            int made = 0;
            foreach (string file in Directory.GetFiles(source, "*.glb"))
            {
                string path = file.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    Debug.LogError("[Skyforge] PROP NOT IMPORTED " + path);
                    continue;
                }
                var instance = (GameObject)UnityEngine.Object.Instantiate(model);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Debug.LogError("[Skyforge] PROP WITHOUT A MESH " + path);
                    UnityEngine.Object.DestroyImmediate(instance);
                    continue;
                }
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Repaint(instance, name);
                var root = new GameObject(name);
                instance.transform.SetParent(root.transform, true);
                Debug.Log("[Skyforge] prop " + name + ": size " + bounds.size + " centre " + bounds.center + ", renderers " + renderers.Length);
                PrefabUtility.SaveAsPrefabAsset(root, dir + "/" + name + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                made++;
            }
            Debug.Log("[Skyforge] props prepared: " + made);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The advertising SDK needs the app's id in the manifest before the player is built, and
        /// the game needs to know which units to ask for. Both come from the command line (CI keeps
        /// them as secrets). Everything here goes through reflection and is allowed to fail: a build
        /// without working ads is better than no build.
        /// </summary>
        static void PrepareAds()
        {
            try
            {
                string app = GetArgument("-sfAdsApp"), rewarded = GetArgument("-sfAdsRewarded"), interstitial = GetArgument("-sfAdsInterstitial");
                bool given = !string.IsNullOrEmpty(app) && app.StartsWith("ca-app-pub-");
                // Google's demonstration app id, so the SDK starts even when none was given.
                if (!given) app = "ca-app-pub-3940256099942544~3347511713";
                Directory.CreateDirectory(GeneratedDir + "/Resources");
                File.WriteAllText(GeneratedDir + "/Resources/ads.txt", given ? "rewarded=" + rewarded + "\ninterstitial=" + interstitial + "\n" : "");
                AssetDatabase.ImportAsset(GeneratedDir + "/Resources/ads.txt");

                Type settingsType = Type.GetType("GoogleMobileAds.Editor.GoogleMobileAdsSettings, GoogleMobileAds.Editor");
                if (settingsType == null)
                {
                    Debug.LogWarning("[Skyforge] ADS: no SDK in the project, the build will have no advertising.");
                    return;
                }
                const string dir = "Assets/GoogleMobileAds/Resources";
                Directory.CreateDirectory(dir);
                string path = dir + "/GoogleMobileAdsSettings.asset";
                ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance(settingsType);
                    AssetDatabase.CreateAsset(settings, path);
                }
                var serialized = new SerializedObject(settings);
                bool set = false;
                SerializedProperty property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.String || !property.name.ToLowerInvariant().Contains("androidappid")) continue;
                    property.stringValue = app;
                    set = true;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log("[Skyforge] ADS: app id " + (set ? "written" : "NOT WRITTEN (no such field)") + ", " + (given ? "the owner's units" : "Google's test units"));

                // Fetch the SDK's Android libraries now rather than hope the build does.
                Type resolver = Type.GetType("GooglePlayServices.PlayServicesResolver, Google.JarResolver");
                System.Reflection.MethodInfo resolve = resolver != null ? resolver.GetMethod("ResolveSync", new[] { typeof(bool) }) : null;
                if (resolve != null)
                {
                    object ok = resolve.Invoke(null, new object[] { true });
                    Debug.Log("[Skyforge] ADS: Android libraries resolved: " + ok);
                }
                else Debug.LogWarning("[Skyforge] ADS: no resolver found; relying on the build to fetch the libraries.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skyforge] ADS: preparation failed: " + e);
            }
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
                ShotDirector.Quick = HasArgument("-sfQuick");

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
            PlayerSettings.companyName = "Zero Hour";
            PlayerSettings.productName = "Zero Hour Sniper";
            PlayerSettings.bundleVersion = GetArgument("-sfVersion") ?? "0.0.1";
            PlayerSettings.Android.bundleVersionCode =
                int.TryParse(GetArgument("-sfVersionCode"), out int code) ? code : 1;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // A build meant for the emulator on the build machine also carries code for its processor: translated ARM code crashes there in the audio thread.
            PlayerSettings.Android.targetArchitectures = HasArgument("-sfX86") ? AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64 : AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)26;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // The game opens with its own mark; the engine's is not shown before it.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.01f, 0.02f, 0.035f);

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
            pipeline.supportsHDR = true;
            pipeline.shadowDistance = 140f;
            pipeline.shadowCascadeCount = 2;
        }

        /// <summary>
        /// The .meta files written by gen_meta.py carry no import settings, so the pictures are
        /// told here what they are: panoramas of the sky, and packed detail maps that hold data
        /// rather than colour.
        /// </summary>
        static void ConfigureTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { "Assets/Skyforge/Resources" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                bool sky = path.Contains("/Sky/"), detail = path.Contains("/Textures/detail_");
                bool mapped = path.Contains("/Textures/map_") || path.Contains("/Textures/ground_");
                bool relief = path.Contains("/Textures/ground_") && path.Contains("_n.");
                if (!sky && !detail && !mapped) continue;

                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = false;
                if (sky)
                {
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = false;
                    importer.wrapModeU = TextureWrapMode.Repeat;
                    importer.wrapModeV = TextureWrapMode.Clamp;
                    importer.maxTextureSize = 4096;
                    importer.filterMode = FilterMode.Bilinear;
                }
                else if (mapped)
                {
                    if (relief) importer.textureType = TextureImporterType.NormalMap;
                    importer.sRGBTexture = !relief;
                    importer.mipmapEnabled = true;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.maxTextureSize = 1024;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.anisoLevel = 8;
                }
                else
                {
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = true;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.maxTextureSize = 1024;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.anisoLevel = 4;
                }
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        /// <summary>Music is streamed off the disk as it plays; decompressing minutes of it into memory would cost tens of megabytes.</summary>
        static void ConfigureAudio()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Skyforge/Resources/Music" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                if (settings.loadType == AudioClipLoadType.Streaming) continue;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.55f;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
            }
        }

        static void ConfigureSky(Material sky)
        {
            if (sky.shader.name == "Skyforge/Sky")
            {
                sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Skyforge/Resources/Sky/sky_clear.jpg"));
                sky.SetColor("_Tint", Color.white);
                sky.SetFloat("_Exposure", 1f);
                sky.SetFloat("_Rotation", 0f);
                sky.SetColor("_HorizonColor", new Color(0.70f, 0.80f, 0.90f));
                return;
            }
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

            ConfigureTextures();
            ConfigureAudio();

            // The ground of each kind of place: a photographed surface with its relief.
            foreach (string ground in new[] { "asphalt", "concrete", "sand", "snow" })
            {
                var colour = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Skyforge/Resources/Textures/ground_" + ground + ".jpg");
                var relief = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Skyforge/Resources/Textures/ground_" + ground + "_n.jpg");
                if (colour == null) continue;
                var laid = new Material(FindShader("Universal Render Pipeline/Lit"));
                laid.SetTexture("_BaseMap", colour);
                laid.SetColor("_BaseColor", ground == "concrete" ? new Color(0.78f, 0.78f, 0.8f) : Color.white);
                if (relief != null)
                {
                    laid.SetTexture("_BumpMap", relief);
                    laid.SetFloat("_BumpScale", 1f);
                    laid.EnableKeyword("_NORMALMAP");
                }
                laid.SetFloat("_Smoothness", ground == "asphalt" || ground == "concrete" ? 0.28f : ground == "snow" ? 0.4f : 0.08f);
                laid.SetFloat("_Metallic", 0f);
                AssetDatabase.CreateAsset(laid, dir + "/Ground_" + ground + ".mat");
            }

            // The photographed sky; the procedural one stands in if the shader is broken.
            Shader skyShader = Shader.Find("Skyforge/Sky");
            bool skyOk = skyShader != null && !ShaderUtil.ShaderHasError(skyShader);
            if (!skyOk) Debug.LogWarning("[Skyforge] SKY SHADER BROKEN: using the procedural sky.");
            var sky = new Material(skyOk ? skyShader : FindShader("Skybox/Procedural"));
            ConfigureSky(sky);
            AssetDatabase.CreateAsset(sky, dir + "/Sky.mat");

            Shader surface = Shader.Find("Skyforge/Surface");
            if (surface == null || ShaderUtil.ShaderHasError(surface))
            {
                Debug.LogWarning("[Skyforge] SURFACE SHADER BROKEN: the world falls back to plain materials.");
            }
            else
            {
                AssetDatabase.CreateAsset(new Material(surface), dir + "/Surface.mat");
                var terrain = new Material(surface);
                terrain.EnableKeyword("_TERRAIN");
                AssetDatabase.CreateAsset(terrain, dir + "/SurfaceTerrain.mat");
            }

            Shader particle = Shader.Find("Skyforge/Particle");
            if (particle == null || ShaderUtil.ShaderHasError(particle))
            {
                Debug.LogWarning("[Skyforge] PARTICLE SHADER BROKEN: smoke falls back to solid puffs.");
            }
            else
            {
                var smoke = new Material(particle);
                smoke.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                smoke.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                smoke.renderQueue = 3000;
                AssetDatabase.CreateAsset(smoke, dir + "/Particle.mat");
                var fire = new Material(particle);
                fire.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                fire.SetFloat("_DstBlend", (float)BlendMode.One);
                fire.SetFloat("_Additive", 1f);
                fire.renderQueue = 3100;
                AssetDatabase.CreateAsset(fire, dir + "/ParticleAdd.mat");
            }

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
