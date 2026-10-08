// Meta Quest mixed-reality setup for the lab room.
//
// Menu: Lab Room > Meta MR
//   1. Configure Project for Quest      XR Plug-in Management (OpenXR + Meta XR feature), player settings, Meta project
//                                        config (passthrough, anchors, hands), Android manifest, build scenes.
//   2. Create or Rebuild MR Scene        copies LabRoom_Overlay.unity to LabRoom_MetaMR.unity (the original is never
//                                        opened or saved) and adds the Meta rig, passthrough windows, avatar and anchors.
//   3. Validate                          checks everything and writes Logs/LabRoomMetaMR_Validation.txt.
//   4. Switch Build Target to Android
//   5. Build Quest APK                   Builds/LabRoomMetaMR.apk
//
//   Test Avatar Tracking (Play Mode)     checks in play mode that the avatar's head follows the headset pose
//                                        (LabRoomMetaMRAvatarPlayTest, Logs/LabRoomMetaMR_AvatarPlayTest.txt).
//
// Automation: writing "configure", "build-scene", "validate", "android", "apk", "avatar-input" (re-wire the avatar
// tracking in the existing MR scene) or "avatar-playtest" (one per line) into <project>/Library/LabRoomMetaMR.command
// runs the same steps (picked up by LabRoomMetaMRDiagnostics); results are appended to Logs/LabRoomMetaMR.log.
// Nothing happens unless that file exists.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LabRoom.MetaMR;
using Meta.XR.MultiplayerBlocks.Shared;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace LabRoom.MetaMR.EditorTools
{
    public static class LabRoomMetaMRSetup
    {
        public const string SourceScenePath = "Assets/Scenes/LabRoom_Overlay.unity";
        public const string MRScenePath = "Assets/Scenes/LabRoom_MetaMR.unity";
        const string RootDir = "Assets/LabRoomMetaMR";
        const string MaterialsDir = RootDir + "/Materials";
        const string PrefabsDir = RootDir + "/Prefabs";
        const string WindowMaterialPath = MaterialsDir + "/PassthroughWindow.mat";
        const string GuardMaterialPath = MaterialsDir + "/PassthroughAlphaGuard.mat";
        const string RemoteAvatarPrefabPath = PrefabsDir + "/LabRemoteAvatar.prefab";
        const string WindowShaderName = "LabRoom/MR/Passthrough Window";
        const string GuardShaderName = "LabRoom/MR/Passthrough Alpha Guard";
        const string CameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        const string AvatarSdkPrefabPath = "Packages/com.meta.xr.sdk.core/Scripts/BuildingBlocks/MultiplayerBlocks/Shared/NetworkedAvatar/AvatarSDK/Prefabs/AvatarSDK.prefab";
        const string AvatarShaderConfigDir = "Packages/com.meta.xr.sdk.avatars/Scripts/Common/Shaders/Configurations/Recommended/";
        const string AvatarShaderConfigPath = AvatarShaderConfigDir + "MetaStyle2ShaderConfiguration.asset";
        const string AvatarFastLoadShaderConfigPath = AvatarShaderConfigDir + "MetaStyle2ShaderConfigurationVertex.asset";
        const string PresetZipSource = "SampleAssets/PresetAvatars"; // relative to the sample assets/StreamingAssets folder
        const string PresetZipDir = "Assets/Oculus/Avatar2_SampleAssets/SampleAssets/SampleAssets";
        const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string BackupHashFile = "_Backup_BeforeMetaMR/original_scene.sha256";
        const string ApkPath = "Builds/LabRoomMetaMR.apk";
        const float WindowInset = 0.025f; // portal sits this far inside the room from the inner face of the glass

        // Generated object names (used to find/replace them on rebuild).
        const string RigName = "OVRCameraRig";
        const string PassthroughName = "MR_Passthrough";
        const string WindowsName = "MR_PassthroughWindows";
        const string GuardName = "MR_PassthroughAlphaGuard";
        const string AvatarSdkName = "AvatarSDK";
        const string LocalAvatarName = "LocalAvatar";
        const string SystemsName = "MR_Systems";

        static readonly string[] AndroidFeatures =
        {
            "com.meta.openxr.feature.metaxr",
            "com.unity.openxr.feature.metaquest",
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.metaquestpro",
            "com.meta.openxr.feature.foveation",
            "com.meta.openxr.feature.subsampledLayout",
        };

        static readonly string[] StandaloneFeatures =
        {
            "com.meta.openxr.feature.metaxr",
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.metaquestpro",
        };

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string LogFile => Path.Combine(ProjectRoot, "Logs", "LabRoomMetaMR.log");
        static string ValidationFile => Path.Combine(ProjectRoot, "Logs", "LabRoomMetaMR_Validation.txt");

        // ------------------------------------------------------------------------------------------------ menus

        [MenuItem("Lab Room/Meta MR/1. Configure Project for Quest", priority = 1)]
        public static void ConfigureProjectMenu() => Run("Configure project", ConfigureProject);

        [MenuItem("Lab Room/Meta MR/2. Create or Rebuild MR Scene", priority = 2)]
        public static void BuildSceneMenu()
        {
            if (File.Exists(Path.Combine(ProjectRoot, MRScenePath)) &&
                !EditorUtility.DisplayDialog("Rebuild LabRoom_MetaMR?",
                    "LabRoom_MetaMR.unity already exists. Rebuilding copies LabRoom_Overlay.unity again and re-adds the Meta MR objects, " +
                    "replacing manual changes made in LabRoom_MetaMR. The original scene is not touched.", "Rebuild", "Cancel"))
                return;
            Run("Build MR scene", BuildMRScene);
        }

        [MenuItem("Lab Room/Meta MR/3. Validate", priority = 3)]
        public static void ValidateMenu() => Run("Validate", () => Validate());

        [MenuItem("Lab Room/Meta MR/4. Switch Build Target to Android (Quest)", priority = 20)]
        public static void SwitchToAndroidMenu() => Run("Switch to Android", SwitchToAndroid);

        [MenuItem("Lab Room/Meta MR/5. Build Quest APK", priority = 21)]
        public static void BuildApkMenu() => Run("Build APK", BuildApk);

        [MenuItem("Lab Room/Meta MR/Open MR Scene", priority = 40)]
        public static void OpenMRScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single);
        }

        // ------------------------------------------------------------------------------------------------ automation

        /// <summary>Runs one setup step by name (used by the automation hook in LabRoomMetaMRDiagnostics).</summary>
        public static void RunCommand(string command)
        {
            switch (command.Trim().ToLowerInvariant())
            {
                case "configure": Run("Configure project", ConfigureProject); break;
                case "build-scene": Run("Build MR scene", BuildMRScene); break;
                case "validate": Run("Validate", () => Validate()); break;
                case "android": Run("Switch to Android", SwitchToAndroid); break;
                case "apk": Run("Build APK", BuildApk); break;
                case "apk-clean": Run("Clean build APK", () => BuildApk(true)); break;
                case "open-mr": Run("Open MR scene", () => EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single)); break;
                case "bake": Run("Bake lighting", LabRoomMetaMRLighting.BakeLighting); break;
                case "quest-quality": Run("Quest quality settings", LabRoomMetaMRLighting.ApplyQuestQuality); break;
                case "stats": Run("Rendering stats", LabRoomMetaMRLighting.ReportStats); break;
                case "avatar-input": Run("Patch avatar input", PatchAvatarInput); break;
                case "collect-windows": Run("Collect passthrough window portals", CollectWindowPortals); break;
                case "avatar-playtest": Run("Avatar tracking play-mode test", LabRoomMetaMRAvatarPlayTest.Begin); break;
                default: Log("Unknown command: " + command); break;
            }
        }

        internal static void Run(string title, Action action)
        {
            Log($"==== {title} ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ====");
            try { action(); Log($"==== {title}: done ===="); }
            catch (Exception e)
            {
                Log($"==== {title}: FAILED: {e}");
                Debug.LogException(e);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal static void Log(string message)
        {
            Debug.Log("[LabRoom MR] " + message);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFile));
                File.AppendAllText(LogFile, message + Environment.NewLine);
            }
            catch (IOException) { }
        }

        // ------------------------------------------------------------------------------------------------ project

        public static void ConfigureProject()
        {
            // XR Plug-in Management: OpenXR loader on Quest (Android) and Link/Simulator (Windows).
            EnsureOpenXRLoader(BuildTargetGroup.Android);
            EnsureOpenXRLoader(BuildTargetGroup.Standalone);
            ConfigureOpenXR(BuildTargetGroup.Android, AndroidFeatures);
            ConfigureOpenXR(BuildTargetGroup.Standalone, StandaloneFeatures);

            // Player settings (Quest requirements). Linear colour space is mandatory: the Meta XR build step
            // (OVRGradleGeneration) refuses to build an OpenXR Quest app in Gamma.
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Log("Colour space switched from Gamma to Linear (required by the Meta XR SDK with OpenXR)");
            }
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            // Unity 6.3 refuses Android builds with "Both" input handlers: use the Input System only (OpenXR needs it).
            // FlyCamera and LabRoomOverlay have Input System code paths, so the desktop tooling keeps working.
            var playerSettingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (playerSettingsAsset != null)
            {
                var pso = new SerializedObject(playerSettingsAsset);
                var handler = pso.FindProperty("activeInputHandler");
                if (handler != null && handler.intValue != 1)
                {
                    handler.intValue = 1;
                    pso.ApplyModifiedPropertiesWithoutUndo();
                    Log("Active input handling set to Input System only - restart the editor to apply");
                }
            }
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 32)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
            PlayerSettings.gpuSkinning = true;
            Log($"Player: IL2CPP/ARM64, min SDK {PlayerSettings.Android.minSdkVersion}, Android Vulkan+GLES3, Windows D3D11 first, colour space {PlayerSettings.colorSpace}, app id {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}");

            // Meta XR project config: hands + controllers, spatial anchors, passthrough.
            var config = OVRProjectConfig.CachedProjectConfig;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            config.sharedAnchorSupport = OVRProjectConfig.FeatureSupport.Supported; // ready for colocated multi-user later
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Required; // MR Utility Kit requirement (as in the exemplar)
            config.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            OVRProjectConfig.CommitProjectConfig(config);
            Log("OVRProjectConfig: hands+controllers, anchors enabled, shared anchors supported, passthrough supported, scene required, contextual passthrough splash");

            // Android manifest with the Quest permissions/features derived from the config above.
            OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
            AssetDatabase.Refresh();
            Log(File.Exists(Path.Combine(ProjectRoot, ManifestPath)) ? "Android manifest generated: " + ManifestPath : "Android manifest NOT generated");

            EnsureBuildScenes();
            AssetDatabase.SaveAssets();
        }

        static void EnsureOpenXRLoader(BuildTargetGroup group)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (guids.Length > 0) perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (perTarget == null)
            {
                EnsureFolder("Assets", "XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);

            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var manager = perTarget.ManagerSettingsForBuildTarget(group);
            bool assigned = manager.activeLoaders.Any(l => l is OpenXRLoader) ||
                            XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, group);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
            Log($"XR loader {group}: OpenXR {(assigned ? "assigned" : "NOT assigned")} ({string.Join(", ", manager.activeLoaders.Select(l => l.GetType().Name))})");
        }

        static void ConfigureOpenXR(BuildTargetGroup group, string[] featureIds)
        {
            FeatureHelpers.RefreshFeatures(group);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Log($"OpenXR settings for {group} not found");
                return;
            }
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(settings);
            foreach (var id in featureIds)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
                if (feature == null) { Log($"OpenXR {group}: feature {id} not found"); continue; }
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
            // The deprecated Unity "Oculus Quest Support" feature must not run alongside Meta Quest Support.
            var legacy = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, "com.unity.openxr.feature.oculusquest");
            if (legacy != null && legacy.enabled) { legacy.enabled = false; EditorUtility.SetDirty(legacy); }
            var enabled = settings.GetFeatures().Where(f => f.enabled).Select(f => f.GetType().Name);
            Log($"OpenXR {group}: render mode {settings.renderMode}; enabled features: {string.Join(", ", enabled)}");
        }

        static void EnsureBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != MRScenePath && s.path != SourceScenePath).ToList();
            var list = new List<EditorBuildSettingsScene>();
            if (File.Exists(Path.Combine(ProjectRoot, MRScenePath))) list.Add(new EditorBuildSettingsScene(MRScenePath, true));
            list.Add(new EditorBuildSettingsScene(SourceScenePath, true));
            list.AddRange(scenes);
            EditorBuildSettings.scenes = list.ToArray();
            Log("Build scenes: " + string.Join(", ", list.Select(s => s.path)));
        }

        public static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) { Log("Already on Android"); return; }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Android Build Support is not installed for this Unity version (add it in Unity Hub).");
            bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Log("Switch to Android: " + (ok ? "ok" : "failed"));
        }

        public static void BuildApk() => BuildApk(false);

        public static void BuildApk(bool clean)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Switch the build target to Android first.");
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "Builds"));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MRScenePath, SourceScenePath },
                locationPathName = Path.Combine(ProjectRoot, ApkPath),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = clean ? BuildOptions.CleanBuildCache : BuildOptions.None,
                subtarget = (int)MobileTextureSubtarget.ASTC, // best quality/size on Quest; the default (0) would fall back to ETC
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Log($"APK build: {s.result}, {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime}");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Log("  build error: " + m.content);
        }

        // ------------------------------------------------------------------------------------------------ scene

        public static void BuildMRScene()
        {
            if (!File.Exists(Path.Combine(ProjectRoot, SourceScenePath)))
                throw new FileNotFoundException("Original scene not found", SourceScenePath);
            string originalHash = Sha256(SourceScenePath);

            EnsureFolder("Assets", "LabRoomMetaMR");
            EnsureFolder(RootDir, "Materials");
            EnsureFolder(RootDir, "Prefabs");
            var windowMat = EnsureMaterial(WindowMaterialPath, WindowShaderName);
            var guardMat = EnsureMaterial(GuardMaterialPath, GuardShaderName);

            // Never open or save the original: duplicate the file, then work only in the copy.
            var active = SceneManager.GetActiveScene();
            if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Cancelled: the open scene has unsaved changes.");
            if (active.path == MRScenePath || active.path == SourceScenePath)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (File.Exists(Path.Combine(ProjectRoot, MRScenePath)) && !AssetDatabase.DeleteAsset(MRScenePath))
                throw new IOException("Could not replace " + MRScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, MRScenePath))
                throw new IOException($"Could not copy {SourceScenePath} to {MRScenePath}");
            Log($"Copied {SourceScenePath} -> {MRScenePath}");

            var scene = EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var labRoom = roots.Select(r => r.GetComponent<LabRoomOverlay>()).FirstOrDefault(o => o != null);
            if (labRoom == null) throw new InvalidOperationException("LabRoom (LabRoomOverlay) not found in the copied scene.");
            var oldCamera = roots.FirstOrDefault(r => r.name == "Main Camera");

            // --- Meta camera rig, standing where the desktop fly camera stood (floor level, same heading).
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefabPath)
                            ?? throw new FileNotFoundException("OVRCameraRig prefab not found", CameraRigPrefabPath);
            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rigGo.name = RigName;
            var floorY = labRoom.transform.position.y;
            if (oldCamera != null)
            {
                var p = oldCamera.transform.position;
                rigGo.transform.SetPositionAndRotation(new Vector3(p.x, floorY, p.z), Quaternion.Euler(0f, oldCamera.transform.eulerAngles.y, 0f));
                oldCamera.SetActive(false); // kept for reference; the Meta rig provides the camera and audio listener
            }
            var manager = rigGo.GetComponent<OVRManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
            so.FindProperty("isInsightPassthroughEnabled").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var trackingSpace = rigGo.transform.Find("TrackingSpace");
            var centerEye = trackingSpace.Find("CenterEyeAnchor");
            foreach (var cam in rigGo.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // eye-buffer alpha drives the passthrough underlay
                cam.renderingPath = RenderingPath.Forward;
                cam.allowHDR = false;
                cam.allowMSAA = true;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                EditorUtility.SetDirty(cam);
            }

            // --- Passthrough: the single background (underlay) layer that the v85 SDK composites beneath the eye
            // buffer. Its defaults (underlay, automatic reconstruction) are what we need; the window shaders decide
            // where it is visible through the eye-buffer alpha.
            var ptGo = new GameObject(PassthroughName);
            SceneManager.MoveGameObjectToScene(ptGo, scene);
            var layer = ptGo.AddComponent<OVRPassthroughLayer>();
            layer.textureOpacity = 1f;
            layer.hidden = false;
            EditorUtility.SetDirty(layer);

            // --- Alpha guard (full screen) on the centre eye.
            var guardGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            guardGo.name = GuardName;
            UnityEngine.Object.DestroyImmediate(guardGo.GetComponent<Collider>());
            guardGo.transform.SetParent(centerEye, false);
            guardGo.transform.localPosition = new Vector3(0f, 0f, 1f);
            var guardRenderer = guardGo.GetComponent<MeshRenderer>();
            SetupEffectRenderer(guardRenderer, guardMat);

            // --- Passthrough window portals under LabRoom (so they follow the anchor alignment).
            var windowsGo = new GameObject(WindowsName);
            windowsGo.transform.SetParent(labRoom.transform, false);
            var windows = windowsGo.AddComponent<LabRoomPassthroughWindows>();
            windows.passthroughLayer = layer;
            windows.alphaGuard = guardRenderer;
            windows.windowRenderers = CreateWindowPortals(labRoom, windowsGo.transform, windowMat);
            if (windows.windowRenderers.Count == 0) throw new InvalidOperationException("No window glass found in the Hybrid model to build passthrough portals from.");

            // --- Meta Avatars: SDK managers + local avatar in tracking space.
            var avatarSdkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarSdkPrefabPath)
                                  ?? throw new FileNotFoundException("AvatarSDK prefab not found", AvatarSdkPrefabPath);
            var avatarSdk = (GameObject)PrefabUtility.InstantiatePrefab(avatarSdkPrefab, scene);
            avatarSdk.name = AvatarSdkName;
            ConfigureAvatarSdk(avatarSdk);

            var localAvatarGo = new GameObject(LocalAvatarName);
            localAvatarGo.transform.SetParent(trackingSpace, false);
            var localBehaviour = localAvatarGo.AddComponent<LabLocalAvatarBehaviour>();
            var avatarInput = localAvatarGo.AddComponent<LabAvatarInputManager>();
            localAvatarGo.AddComponent<AvatarEntity>();
            ConfigureLocalAvatar(localAvatarGo, rigGo.GetComponent<OVRCameraRig>());

            // --- Alignment, controls, HUD.
            var systems = new GameObject(SystemsName);
            SceneManager.MoveGameObjectToScene(systems, scene);
            var aligner = systems.AddComponent<LabRoomAnchorAligner>();
            aligner.roomRoot = labRoom.transform;
            var hud = systems.AddComponent<LabRoomMRHud>();
            var controls = systems.AddComponent<LabRoomXRControls>();
            var rig = rigGo.GetComponent<OVRCameraRig>();
            controls.overlay = labRoom;
            controls.windows = windows;
            controls.aligner = aligner;
            controls.hud = hud;
            controls.cameraRig = rig;
            hud.cameraRig = rig;
            hud.controls = controls;
            hud.aligner = aligner;
            hud.windows = windows;
            hud.localAvatar = localBehaviour;
            hud.avatarInput = avatarInput;

            CreateRemoteAvatarPrefab();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Saving " + MRScenePath + " failed");
            Log($"MR scene saved: rig at {rigGo.transform.position}, {windows.windowRenderers.Count} window portal(s), local avatar under {trackingSpace.name}");

            EnsureBuildScenes();
            AssetDatabase.SaveAssets();

            string afterHash = Sha256(SourceScenePath);
            Log(afterHash == originalHash ? "Original scene unchanged (sha256 " + afterHash + ")" : "WARNING: original scene hash changed!");
        }

        /// <summary>
        /// Brings the AvatarSDK building-block prefab instance in line with Meta's current (Avatars 28+) block setup:
        /// preset avatar zips registered, Style-2 shader configuration, and the deprecated "Standalone" body-tracking
        /// input manager switched off in favour of LabAvatarInputManager (provider model).
        /// </summary>
        static void ConfigureAvatarSdk(GameObject avatarSdk)
        {
            var manager = avatarSdk.GetComponent<Oculus.Avatar2.OvrAvatarManager>();
            var so = new SerializedObject(manager);
            var zips = so.FindProperty("_preloadZipFiles");
            zips.arraySize = 1;
            zips.GetArrayElementAtIndex(0).stringValue = PresetZipSource;
            so.ApplyModifiedPropertiesWithoutUndo();

            var shaderManager = avatarSdk.GetComponentInChildren<Oculus.Avatar2.OvrAvatarShaderManagerSingle>(true);
            var defaultConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AvatarShaderConfigPath)
                                ?? throw new FileNotFoundException("Avatar shader configuration not found", AvatarShaderConfigPath);
            var fastLoadConfig = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AvatarFastLoadShaderConfigPath)
                                 ?? throw new FileNotFoundException("Avatar shader configuration not found", AvatarFastLoadShaderConfigPath);
            var sso = new SerializedObject(shaderManager);
            sso.FindProperty("DefaultShaderConfigurationInitializer").objectReferenceValue = defaultConfig;
            sso.FindProperty("FastLoadConfigurationInitializer").objectReferenceValue = fastLoadConfig;
            sso.ApplyModifiedPropertiesWithoutUndo();

            foreach (var legacyInput in avatarSdk.GetComponentsInChildren<EntityInputManager>(true))
            {
                legacyInput.enabled = false;
                EditorUtility.SetDirty(legacyInput);
            }
            Log($"AvatarSDK: preset zips '{PresetZipSource}', shader config {defaultConfig.name}, legacy EntityInputManager disabled");
        }

        static readonly Oculus.Avatar2.CAPI.ovrAvatar2JointType[] AvatarCheckJoints =
        {
            Oculus.Avatar2.CAPI.ovrAvatar2JointType.Head,
            Oculus.Avatar2.CAPI.ovrAvatar2JointType.LeftHandWrist,
            Oculus.Avatar2.CAPI.ovrAvatar2JointType.RightHandWrist,
        };

        /// <summary>
        /// Wires the local avatar's tracking: LabAvatarInputManager reads the rig's head/hand anchors, the AvatarEntity
        /// references that input manager explicitly, and the head/wrist joints are exposed as critical joints so the
        /// runtime self-check (HUD) and the play-mode test can compare them with the tracked poses.
        /// </summary>
        static void ConfigureLocalAvatar(GameObject localAvatar, OVRCameraRig rig)
        {
            // (explicit Unity null checks: GetComponent can return a "fake null" object in the editor, which ?? misses)
            var input = localAvatar.GetComponent<LabAvatarInputManager>();
            var entity = localAvatar.GetComponent<AvatarEntity>();
            if (input == null) throw new InvalidOperationException("LocalAvatar has no LabAvatarInputManager");
            if (entity == null) throw new InvalidOperationException("LocalAvatar has no AvatarEntity");
            if (rig == null) throw new InvalidOperationException("No OVRCameraRig for the local avatar");
            input.cameraRig = rig;
            input.enabled = true;
            EditorUtility.SetDirty(input);

            var so = new SerializedObject(entity);
            so.FindProperty("_inputManager").objectReferenceValue = input;
            var joints = so.FindProperty("_criticalJointTypes");
            joints.arraySize = AvatarCheckJoints.Length;
            for (int i = 0; i < AvatarCheckJoints.Length; i++) joints.GetArrayElementAtIndex(i).intValue = (int)AvatarCheckJoints[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            Log($"Local avatar input: {rig.name} anchors -> LabAvatarInputManager -> AvatarEntity; critical joints {string.Join(", ", AvatarCheckJoints)}");
        }

        /// <summary>
        /// Applies the avatar tracking wiring to the existing LabRoom_MetaMR scene in place (no rebuild, so baked
        /// lighting and manual changes are kept). The original LabRoom_Overlay scene is not opened.
        /// </summary>
        public static void PatchAvatarInput()
        {
            string originalHash = Sha256(SourceScenePath);
            if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            var scene = EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            var rig = all.Select(g => g.GetComponent<OVRCameraRig>()).FirstOrDefault(r => r != null) ?? throw new InvalidOperationException("OVRCameraRig not found in " + MRScenePath);
            var local = all.Select(g => g.GetComponent<LabLocalAvatarBehaviour>()).FirstOrDefault(b => b != null) ?? throw new InvalidOperationException("LocalAvatar not found in " + MRScenePath);
            if (local.GetComponent<LabAvatarInputManager>() == null) local.gameObject.AddComponent<LabAvatarInputManager>();
            ConfigureLocalAvatar(local.gameObject, rig);

            foreach (var other in all.SelectMany(g => g.GetComponents<Oculus.Avatar2.OvrAvatarInputManagerBehavior>()))
            {
                if (other is LabAvatarInputManager || !other.enabled) continue;
                other.enabled = false;
                EditorUtility.SetDirty(other);
                Log("Disabled extra avatar input manager " + other.GetType().Name + " on " + other.name);
            }

            var hud = all.Select(g => g.GetComponent<LabRoomMRHud>()).FirstOrDefault(h => h != null);
            if (hud != null)
            {
                hud.avatarInput = local.GetComponent<LabAvatarInputManager>();
                EditorUtility.SetDirty(hud);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Saving " + MRScenePath + " failed");
            string afterHash = Sha256(SourceScenePath);
            Log(afterHash == originalHash ? "Original scene unchanged (sha256 " + afterHash + ")" : "WARNING: original scene hash changed!");
        }

        /// <summary>
        /// Same as the "Collect Window Renderers From Children" context menu on MR_PassthroughWindows: registers every
        /// portal mesh under it (e.g. duplicated portals) so the Y button and strength control all of them.
        /// </summary>
        public static void CollectWindowPortals()
        {
            if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            var scene = EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single);
            var windows = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LabRoomPassthroughWindows>(true)).FirstOrDefault();
            if (windows == null) throw new InvalidOperationException("LabRoomPassthroughWindows not found in " + MRScenePath);
            int before = windows.windowRenderers.Count;
            windows.CollectWindowRenderers();
            EditorUtility.SetDirty(windows);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Saving " + MRScenePath + " failed");
            Log($"Passthrough window portals: {before} -> {windows.windowRenderers.Count} ({string.Join(", ", windows.windowRenderers.Select(r => r.name))})");
        }

        static List<Renderer> CreateWindowPortals(LabRoomOverlay labRoom, Transform parent, Material windowMat)
        {
            var room = labRoom.transform;
            var renderers = new List<Renderer>();
            var glassParts = room.GetComponentsInChildren<MeshFilter>(true)
                .Where(mf => mf.sharedMesh != null && mf.name.StartsWith("Window_Glass", StringComparison.Ordinal))
                .ToList();

            // Room interior centre (for the inward normal).
            var roomBounds = new Bounds();
            bool hasBounds = false;
            foreach (var r in room.GetComponentsInChildren<Renderer>(false))
            {
                if (r.transform.IsChildOf(parent) || !r.enabled) continue;
                if (!hasBounds) { roomBounds = r.bounds; hasBounds = true; }
                else roomBounds.Encapsulate(r.bounds);
            }
            var interior = room.InverseTransformPoint(roomBounds.center);

            foreach (var glass in glassParts)
            {
                var mb = glass.sharedMesh.bounds;
                var min = Vector3.positiveInfinity;
                var max = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = room.InverseTransformPoint(glass.transform.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
                var size = max - min;
                var centre = (min + max) * 0.5f;
                if (size.y < Mathf.Min(size.x, size.z)) { Log($"Skipping {glass.name}: not a vertical pane"); continue; }

                bool thinX = size.x < size.z;
                var normal = thinX ? new Vector3(Mathf.Sign(interior.x - centre.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(interior.z - centre.z));
                float halfThickness = (thinX ? size.x : size.z) * 0.5f;
                float width = thinX ? size.z : size.x;

                var portal = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.DestroyImmediate(portal.GetComponent<Collider>());
                portal.name = "WindowPortal_" + glass.name;
                portal.transform.SetParent(parent, false);
                portal.transform.localPosition = centre + normal * (halfThickness + WindowInset);
                portal.transform.localRotation = Quaternion.LookRotation(-normal, Vector3.up); // quad normal faces the room
                portal.transform.localScale = new Vector3(width, size.y, 1f);
                var renderer = portal.GetComponent<MeshRenderer>();
                SetupEffectRenderer(renderer, windowMat);
                renderers.Add(renderer);
                Log($"Window portal from {glass.name}: centre {portal.transform.localPosition}, size {width:0.00} x {size.y:0.00} m, normal {normal}");
            }
            return renderers;
        }

        static void SetupEffectRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
        }

        static void CreateRemoteAvatarPrefab()
        {
            var go = new GameObject("LabRemoteAvatar");
            try
            {
                go.AddComponent<LabRemoteAvatarBehaviour>();
                go.AddComponent<AvatarEntity>();
                PrefabUtility.SaveAsPrefabAsset(go, RemoteAvatarPrefabPath, out bool ok);
                Log(ok ? "Remote avatar prefab: " + RemoteAvatarPrefabPath : "Remote avatar prefab could not be saved");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static Material EnsureMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName) ?? throw new InvalidOperationException("Shader not found: " + shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static string Sha256(string assetPath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(Path.Combine(ProjectRoot, assetPath));
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        // ------------------------------------------------------------------------------------------------ validation

        public static bool Validate()
        {
            var report = new StringBuilder();
            int fails = 0, warns = 0;
            void Check(bool ok, string what, string detail = null, bool warnOnly = false)
            {
                string tag = ok ? "PASS" : warnOnly ? "WARN" : "FAIL";
                if (!ok) { if (warnOnly) warns++; else fails++; }
                report.AppendLine($"[{tag}] {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
            }

            report.AppendLine($"LabRoom Meta MR validation {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, build target {EditorUserBuildSettings.activeBuildTarget}");

            // Compilation
            Check(!EditorUtility.scriptCompilationFailed, "Scripts compile without errors");

            // Original scene integrity
            var backup = Path.Combine(ProjectRoot, BackupHashFile);
            if (File.Exists(backup))
            {
                var expected = File.ReadAllLines(backup).Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(p => p.Length == 2 && p[1] == SourceScenePath).Select(p => p[0]).FirstOrDefault();
                var actual = Sha256(SourceScenePath);
                Check(expected != null && expected == actual, "Original scene LabRoom_Overlay.unity unchanged", "sha256 " + actual);
            }
            else Check(File.Exists(Path.Combine(ProjectRoot, SourceScenePath)), "Original scene exists", "no backup hash to compare", true);

            // Packages
            var manifest = File.ReadAllText(Path.Combine(ProjectRoot, "Packages/manifest.json"));
            Check(manifest.Contains("\"com.meta.xr.sdk.all\": \"85.0.0\""), "Meta XR All-in-One SDK 85.0.0 in manifest");
            Check(manifest.Contains("\"com.meta.xr.sdk.avatars\""), "Meta Avatars SDK in manifest");
            Check(!manifest.Contains("com.unity.xr.oculus"), "Oculus XR Plugin not installed alongside OpenXR");
            Check(manifest.Contains("\"com.unity.render-pipelines.universal\""), "URP package present (Meta Avatars shaders include URP files)");
            Check(GraphicsSettings.defaultRenderPipeline == null && QualitySettings.renderPipeline == null, "Built-in Render Pipeline still active (no URP asset assigned)");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefabPath) != null, "Meta core package resolved (OVRCameraRig prefab)");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(AvatarSdkPrefabPath) != null, "AvatarSDK building-block prefab resolved");

            // XR management / OpenXR
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                var loaders = general?.Manager?.activeLoaders;
                Check(loaders != null && loaders.Any(l => l is OpenXRLoader), $"OpenXR loader active for {group}",
                    loaders == null ? "no XR settings" : string.Join(", ", loaders.Select(l => l.GetType().Name)));
                var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                Check(xr != null && xr.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced, $"OpenXR {group} render mode single-pass instanced");
                foreach (var id in group == BuildTargetGroup.Android ? AndroidFeatures : StandaloneFeatures)
                {
                    var f = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
                    Check(f != null && f.enabled, $"OpenXR {group} feature {id} enabled");
                }
            }

            // Player / Quest settings
            Check(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP, "Android IL2CPP");
            Check(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "Android ARM64 only");
            Check((int)PlayerSettings.Android.minSdkVersion >= 32, "Android min SDK >= 32", PlayerSettings.Android.minSdkVersion.ToString());
            var androidApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Check(androidApis.Length > 0 && androidApis[0] == GraphicsDeviceType.Vulkan, "Android graphics API Vulkan first", string.Join(", ", androidApis));
            var winApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            Check(winApis.Length > 0 && winApis[0] == GraphicsDeviceType.Direct3D11, "Windows (Link) graphics API D3D11 first", string.Join(", ", winApis), true);
            Check(PlayerSettings.colorSpace == ColorSpace.Linear, "Linear colour space (required for Meta OpenXR builds)");
            var playerSettingsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (playerSettingsObj != null)
            {
                var input = new SerializedObject(playerSettingsObj).FindProperty("activeInputHandler");
                Check(input != null && input.intValue == 1, "Active input handling: Input System only (required for Android builds)", input?.intValue.ToString());
#if !ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
                Check(false, "Editor restarted after the input handling change", "restart Unity so the Input System define is active");
#endif
            }
            Check(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android), "Android Build Support installed");
            Check(EditorUserBuildSettings.androidBuildSubtarget == MobileTextureSubtarget.ASTC, "Android texture compression ASTC", EditorUserBuildSettings.androidBuildSubtarget.ToString());

            // Meta project config + manifest
            var config = OVRProjectConfig.CachedProjectConfig;
            Check(config.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None, "Meta config: passthrough supported");
            Check(config.anchorSupport == OVRProjectConfig.AnchorSupport.Enabled, "Meta config: spatial anchors enabled");
            Check(config.handTrackingSupport == OVRProjectConfig.HandTrackingSupport.ControllersAndHands, "Meta config: controllers and hands");
            Check(config.sceneSupport == OVRProjectConfig.FeatureSupport.Required, "Meta config: scene support (MR Utility Kit requirement)");
            Check(config.systemLoadingScreenBackground == OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough, "Meta config: contextual passthrough splash", null, true);
            var manifestXml = Path.Combine(ProjectRoot, ManifestPath);
            if (File.Exists(manifestXml))
            {
                var xml = File.ReadAllText(manifestXml);
                Check(xml.Contains("USE_ANCHOR_API"), "Android manifest: spatial anchor permission");
                Check(xml.Contains("com.oculus.feature.PASSTHROUGH"), "Android manifest: passthrough feature");
                Check(xml.Contains("oculus.software.handtracking") || xml.Contains("HAND_TRACKING"), "Android manifest: hand tracking");
                Check(xml.Contains("com.oculus.supportedDevices"), "Android manifest: supported Quest devices");
                Check(xml.Contains("com.oculus.intent.category.VR"), "Android manifest: VR launcher category");
            }
            else Check(false, "Android manifest exists", ManifestPath);

            // Build settings
            var buildScenes = EditorBuildSettings.scenes;
            Check(buildScenes.Length > 0 && buildScenes[0].path == MRScenePath && buildScenes[0].enabled, "MR scene is the first build scene");
            Check(buildScenes.Any(s => s.path == SourceScenePath), "Original scene still in build settings");

            // Avatar sample/core assets copied by the Avatars SDK importer
            Check(File.Exists(Path.Combine(ProjectRoot, "Assets/StreamingAssets/Oculus/OvrAvatar2Assets.zip")), "Avatars core assets in StreamingAssets");
            Check(Directory.Exists(Path.Combine(ProjectRoot, "Assets/Oculus/Avatar2_SampleAssets/SampleAssets")), "Avatars preset sample assets imported");

            // Scenes
            Check(File.Exists(Path.Combine(ProjectRoot, MRScenePath)), "MR scene exists", MRScenePath);
            if (File.Exists(Path.Combine(ProjectRoot, MRScenePath)))
            {
                if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                var scene = EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single);
                Check(scene.IsValid() && scene.isLoaded, "MR scene opens");
                ValidateScene(scene, Check);
                // Discard any state changes made by the checks (e.g. view switching) by reloading the saved file.
                EditorSceneManager.OpenScene(MRScenePath, OpenSceneMode.Single);
            }

            report.AppendLine();
            report.AppendLine($"RESULT: {(fails == 0 ? "PASS" : "FAIL")} ({fails} failed, {warns} warnings)");
            Directory.CreateDirectory(Path.GetDirectoryName(ValidationFile));
            File.WriteAllText(ValidationFile, report.ToString());
            Log(report.ToString());
            return fails == 0;
        }

        static void ValidateScene(Scene scene, Action<bool, string, string, bool> check)
        {
            void Check(bool ok, string what, string detail = null, bool warn = false) => check(ok, what, detail, warn);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();

            // Missing scripts / references
            int missingScripts = all.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
            Check(missingScripts == 0, "No missing scripts in MR scene", missingScripts + " missing");
            var missingRefs = new List<string>();
            foreach (var go in all)
            {
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var it = new SerializedObject(c).GetIterator();
                    while (it.NextVisible(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                            missingRefs.Add($"{go.name}/{c.GetType().Name}.{it.propertyPath}");
                }
                if (go.TryGetComponent<Renderer>(out var rend) && rend.sharedMaterials.Any(m => m == null))
                    missingRefs.Add($"{go.name}: renderer with empty material slot");
                if (PrefabUtility.IsPrefabAssetMissing(go)) missingRefs.Add($"{go.name}: missing prefab asset");
            }
            Check(missingRefs.Count == 0, "No missing object/material/prefab references in MR scene",
                missingRefs.Count == 0 ? null : string.Join("; ", missingRefs.Take(15)));

            // Room
            var overlay = all.Select(g => g.GetComponent<LabRoomOverlay>()).FirstOrDefault(o => o != null);
            Check(overlay != null && overlay.scan != null && overlay.blockout != null && overlay.hybrid != null,
                "LabRoom keeps all three representations (scan, blockout, hybrid)");
            if (overlay != null)
            {
                Check(overlay.scan.transform.IsChildOf(overlay.transform) && overlay.blockout.transform.IsChildOf(overlay.transform) && overlay.hybrid.transform.IsChildOf(overlay.transform),
                    "Representations still parented under LabRoom");
                var presets = new[] { (true, false, false), (false, true, false), (true, true, false), (false, false, true), (true, false, true) };
                bool switching = true;
                foreach (var (s, b, h) in presets)
                {
                    overlay.showScan = s; overlay.showBlockout = b; overlay.showHybrid = h;
                    overlay.Apply();
                    switching &= overlay.scan.activeSelf == s && overlay.blockout.activeSelf == b && overlay.hybrid.activeSelf == h;
                }
                Check(switching, "Room view switching (LabRoomOverlay presets 1-5) works");
                int visibleRenderers = overlay.GetComponentsInChildren<Renderer>(false).Count(r => r.enabled && r.sharedMaterial != null);
                Check(visibleRenderers > 0, "Lab renders (active renderers with materials)", visibleRenderers + " renderers");
            }

            // Camera rig
            var rig = all.Select(g => g.GetComponent<OVRCameraRig>()).FirstOrDefault(r => r != null);
            Check(rig != null, "OVRCameraRig present");
            var manager = all.Select(g => g.GetComponent<OVRManager>()).FirstOrDefault(m => m != null);
            Check(manager != null, "OVRManager present");
            if (manager != null)
            {
                var so = new SerializedObject(manager);
                Check(so.FindProperty("_trackingOriginType").intValue == (int)OVRManager.TrackingOrigin.FloorLevel, "Tracking origin: floor level");
                Check(manager.isInsightPassthroughEnabled, "OVRManager: insight passthrough enabled");
            }
            var activeCams = all.Where(g => g.activeInHierarchy).Select(g => g.GetComponent<Camera>()).Where(c => c != null && c.enabled).ToList();
            Check(activeCams.Count == 1 && activeCams[0].CompareTag("MainCamera") && activeCams[0].name == "CenterEyeAnchor",
                "Single active camera is the rig's CenterEyeAnchor (MainCamera)", string.Join(", ", activeCams.Select(c => c.name)));
            var listeners = all.Where(g => g.activeInHierarchy).Select(g => g.GetComponent<AudioListener>()).Count(l => l != null && l.enabled);
            Check(listeners == 1, "Exactly one active AudioListener", listeners.ToString());
            if (activeCams.Count > 0)
            {
                var cam = activeCams[0];
                Check(cam.clearFlags == CameraClearFlags.SolidColor && cam.backgroundColor.a < 0.01f, "Eye camera clears to transparent (passthrough underlay)");
            }

            // Passthrough
            var layers = all.Select(g => g.GetComponent<OVRPassthroughLayer>()).Where(l => l != null).ToList();
            Check(layers.Count == 1, "Exactly one OVRPassthroughLayer", layers.Count.ToString());
            if (layers.Count > 0)
            {
                // Read the (now obsolete) layering fields through serialization to confirm they are at the defaults.
                var lso = new SerializedObject(layers[0]);
                Check(lso.FindProperty("overlayType").intValue == (int)OVROverlay.OverlayType.Underlay,
                    "Passthrough layer is the background underlay (not a full-screen overlay)");
                Check(lso.FindProperty("projectionSurfaceType").intValue == 0, "Passthrough projection: automatic reconstruction");
                Check(!layers[0].hidden && layers[0].textureOpacity > 0.99f, "Passthrough layer visible at full opacity");
            }
            var windows = all.Select(g => g.GetComponent<LabRoomPassthroughWindows>()).FirstOrDefault(w => w != null);
            Check(windows != null, "LabRoomPassthroughWindows present");
            if (windows != null)
            {
                Check(windows.transform.IsChildOf(overlay != null ? overlay.transform : windows.transform.root) && overlay != null,
                    "Window portals are children of LabRoom (follow the anchor alignment)");
                Check(windows.passthroughLayer != null && layers.Contains(windows.passthroughLayer), "Windows reference the passthrough layer");
                Check(windows.windowRenderers.Count > 0 && windows.windowRenderers.All(r => r != null && r.sharedMaterial != null && r.sharedMaterial.shader.name == WindowShaderName),
                    "Window portal meshes use the Passthrough Window material", windows.windowRenderers.Count + " portal(s)");
                Check(windows.alphaGuard != null && windows.alphaGuard.sharedMaterial != null && windows.alphaGuard.sharedMaterial.shader.name == GuardShaderName &&
                      rig != null && windows.alphaGuard.transform.IsChildOf(rig.transform),
                    "Alpha guard on the eye camera restricts passthrough to the portals");
                var windowShader = Shader.Find(WindowShaderName);
                var guardShader = Shader.Find(GuardShaderName);
                Check(windowShader != null && windowShader.isSupported && guardShader != null && guardShader.isSupported, "Passthrough shaders compile");
                var glass = overlay != null ? overlay.GetComponentsInChildren<MeshFilter>(true).Count(mf => mf.name.StartsWith("Window_Glass", StringComparison.Ordinal)) : 0;
                Check(windows.windowRenderers.Count >= glass && glass > 0, "One portal per window pane in the reconstruction", $"{glass} pane(s)");
            }

            // Avatars
            var avatarManager = all.Select(g => g.GetComponent<Oculus.Avatar2.OvrAvatarManager>()).FirstOrDefault(m => m != null);
            Check(avatarManager != null, "OvrAvatarManager (AvatarSDK) present");
            if (avatarManager != null)
            {
                var zips = new SerializedObject(avatarManager).FindProperty("_preloadZipFiles");
                bool registered = false;
                for (int i = 0; i < zips.arraySize; i++) registered |= zips.GetArrayElementAtIndex(i).stringValue == PresetZipSource;
                Check(registered, "Preset avatar zips registered with OvrAvatarManager", PresetZipSource);
                var shaderManager = avatarManager.GetComponentInChildren<Oculus.Avatar2.OvrAvatarShaderManagerSingle>(true);
                var config = shaderManager != null ? new SerializedObject(shaderManager).FindProperty("DefaultShaderConfigurationInitializer").objectReferenceValue : null;
                Check(config != null && !config.name.Contains("Deprecated"), "Avatar shader configuration is current (not deprecated)", config != null ? config.name : "none");
            }
            foreach (var zip in new[] { "PresetAvatars_Quest.zip", "PresetAvatars_Quest_Light.zip", "PresetAvatars_Rift.zip", "PresetAvatars_Rift_Light.zip" })
                Check(File.Exists(Path.Combine(ProjectRoot, PresetZipDir, zip)), "Packaged preset avatars: " + zip);
            var inputManagers = all.Where(g => g.activeInHierarchy).Select(g => g.GetComponent<Oculus.Avatar2.OvrAvatarInputManagerBehavior>())
                .Where(m => m != null && m.enabled).ToList();
            Check(inputManagers.Count == 1 && inputManagers[0] is LabAvatarInputManager,
                "Single active avatar input manager (headset/controller/hand providers)", string.Join(", ", inputManagers.Select(m => m.GetType().Name)));
            Check(all.Any(g => g.GetComponent<Oculus.Avatar2.AvatarLODManager>() != null), "AvatarLODManager present");
            var entities = all.Select(g => g.GetComponent<AvatarEntity>()).Where(e => e != null).ToList();
            Check(entities.Count == 1, "One local AvatarEntity", entities.Count.ToString());
            if (entities.Count == 1)
            {
                var e = entities[0];
                var behaviour = e.GetComponent<LabLocalAvatarBehaviour>();
                Check(behaviour != null && behaviour.HasInputAuthority, "Local avatar has an input-authority IAvatarBehaviour");
                Check(rig != null && e.transform.parent != null && e.transform.parent.name == "TrackingSpace" && e.transform.IsChildOf(rig.transform) &&
                      e.transform.localPosition == Vector3.zero && e.transform.localRotation == Quaternion.identity,
                    "Local avatar sits in tracking space with identity pose");
                var input = e.GetComponent<LabAvatarInputManager>();
                Check(input != null && input.enabled && input.cameraRig == rig, "Avatar input reads the OVRCameraRig head and hand anchors");
                var eso = new SerializedObject(e);
                Check(input != null && eso.FindProperty("_inputManager").objectReferenceValue == input, "AvatarEntity references LabAvatarInputManager");
                var crit = eso.FindProperty("_criticalJointTypes");
                var critJoints = Enumerable.Range(0, crit.arraySize).Select(i => crit.GetArrayElementAtIndex(i).intValue).ToList();
                Check(AvatarCheckJoints.All(j => critJoints.Contains((int)j)), "Avatar head and wrist joints exposed for the tracking self-check",
                    string.Join(", ", critJoints));
                var features = (Oculus.Avatar2.CAPI.ovrAvatar2EntityFeatures)eso.FindProperty("_creationInfo.features").intValue;
                Check((features & Oculus.Avatar2.CAPI.ovrAvatar2EntityFeatures.Animation) != 0 &&
                      (features & Oculus.Avatar2.CAPI.ovrAvatar2EntityFeatures.UseDefaultAnimHierarchy) != 0,
                    "Local avatar entity has animation (tracking-driven pose) enabled", features.ToString());
            }
            var remotePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RemoteAvatarPrefabPath);
            Check(remotePrefab != null && remotePrefab.GetComponent<AvatarEntity>() != null && remotePrefab.GetComponent<LabRemoteAvatarBehaviour>() != null,
                "Remote avatar prefab ready for networking", RemoteAvatarPrefabPath);

            // Anchors / controls
            var aligner = all.Select(g => g.GetComponent<LabRoomAnchorAligner>()).FirstOrDefault(a => a != null);
            Check(aligner != null && overlay != null && aligner.roomRoot == overlay.transform, "Anchor aligner drives the LabRoom root");
            Check(aligner != null && aligner.loadOnStart, "Saved anchor alignment is loaded on start");
            var controls = all.Select(g => g.GetComponent<LabRoomXRControls>()).FirstOrDefault(c => c != null);
            Check(controls != null && controls.overlay == overlay && controls.aligner == aligner && controls.windows == windows && controls.cameraRig == rig,
                "XR controls wired to overlay, windows, aligner and rig");
            var hud = all.Select(g => g.GetComponent<LabRoomMRHud>()).FirstOrDefault(h => h != null);
            Check(hud != null && hud.cameraRig == rig, "Wrist HUD wired");
            Check(hud != null && hud.avatarInput != null && entities.Count == 1 && hud.avatarInput.gameObject == entities[0].gameObject,
                "Wrist HUD shows the avatar tracking self-check");

            // Baked lighting (Lab Room > Meta MR > 6. Bake Lighting). Warnings only until a bake has been run.
            bool hasSettings = Lightmapping.TryGetLightingSettings(out var lighting) && lighting != null && lighting.bakedGI;
            Check(hasSettings && LightmapSettings.lightmaps.Length > 0 && Lightmapping.lightingDataAsset != null,
                "Lighting baked for the MR scene", $"{LightmapSettings.lightmaps.Length} lightmap(s)", true);
            if (overlay != null && overlay.hybrid != null)
            {
                var hybridLights = overlay.hybrid.GetComponentsInChildren<Light>(true);
                Check(hybridLights.All(l => l.lightmapBakeType == LightmapBakeType.Baked),
                    "No realtime lights in the Hybrid (all baked)", $"{hybridLights.Count(l => l.lightmapBakeType != LightmapBakeType.Baked)} realtime of {hybridLights.Length}", true);
                var hybridRenderers = overlay.hybrid.GetComponentsInChildren<MeshRenderer>(true);
                Check(hybridRenderers.All(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) == 0),
                    "Hybrid not batching-static (LabRoom moves with the anchor alignment)");
                int lightmapped = hybridRenderers.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE);
                Check(lightmapped > 0, "Hybrid meshes lightmapped", $"{lightmapped} of {hybridRenderers.Length}", true);
            }
        }
    }
}
