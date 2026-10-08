// Baked lighting and Quest render settings for the MR scene (LabRoom_MetaMR.unity only - the original
// LabRoom_Overlay scene keeps its realtime lights and is never opened or saved by this code).
//
// Why: the Hybrid uses ~15 realtime lights (per-pixel point lights, several with soft shadows). In the Built-in
// forward renderer every per-pixel light adds another draw of every object it touches, and each shadowed point light
// renders 6 shadow-map faces per frame - far beyond a Quest's budget. Baking turns all of that into lightmaps.
//
// Notes:
//  * Hybrid lights are switched to "Baked" in the MR scene, so at runtime they cost nothing.
//  * Opaque Hybrid meshes are marked Contribute GI (lightmapped) but NOT batching-static: LabRoom is moved at runtime
//    by the spatial-anchor alignment, and lightmapped renderers move correctly with it as long as they are not
//    statically batched.
//  * The Hybrid FBX gets "Generate Lightmap UVs" (adds a UV2 channel; the original scene looks the same).
//  * The scene gets its own Lighting Settings asset so no shared settings change.
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LabRoom.MetaMR.EditorTools
{
    public static class LabRoomMetaMRLighting
    {
        const string HybridModelPath = "Assets/Models/Hybrid/lab_room_hybrid.fbx";
        const string LightingDir = "Assets/LabRoomMetaMR/Lighting";
        const string LightingSettingsPath = LightingDir + "/LabRoom_MetaMR_Lighting.lighting";

        // Bake quality (tuned for a ~6 x 6.5 m room on Quest).
        const float TexelsPerUnit = 60f;
        const int MaxLightmapSize = 2048;

        static double bakeStartTime;

        [MenuItem("Lab Room/Meta MR/6. Bake Lighting (MR scene only)", priority = 6)]
        public static void BakeMenu() => LabRoomMetaMRSetup.Run("Bake lighting", BakeLighting);

        [MenuItem("Lab Room/Meta MR/7. Apply Quest Quality Settings", priority = 7)]
        public static void QualityMenu() => LabRoomMetaMRSetup.Run("Quest quality settings", ApplyQuestQuality);

        [MenuItem("Lab Room/Meta MR/Rendering Stats (MR scene)", priority = 41)]
        public static void StatsMenu() => LabRoomMetaMRSetup.Run("Rendering stats", ReportStats);

        // ------------------------------------------------------------------------------------------------ bake

        public static void BakeLighting()
        {
            if (Lightmapping.isRunning) throw new InvalidOperationException("A lightmap bake is already running.");
            var scene = OpenMRScene();

            // 1) Lightmap UVs for the Hybrid model.
            var importer = AssetImporter.GetAtPath(HybridModelPath) as ModelImporter
                           ?? throw new FileNotFoundException("Hybrid model importer not found", HybridModelPath);
            if (!importer.generateSecondaryUV)
            {
                importer.generateSecondaryUV = true;
                importer.secondaryUVPackMargin = 8f;
                importer.SaveAndReimport();
                LabRoomMetaMRSetup.Log("Hybrid model: lightmap UVs generated");
                scene = SceneManager.GetActiveScene();
            }

            var overlay = Find<LabRoomOverlay>(scene) ?? throw new InvalidOperationException("LabRoomOverlay not found in the MR scene.");
            var hybrid = overlay.hybrid ?? throw new InvalidOperationException("LabRoomOverlay has no Hybrid assigned.");

            // 2) Lighting settings asset owned by this scene.
            if (!AssetDatabase.IsValidFolder(LightingDir)) AssetDatabase.CreateFolder("Assets/LabRoomMetaMR", "Lighting");
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "LabRoom_MetaMR_Lighting" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = TexelsPerUnit;
            settings.lightmapPadding = 4;
            settings.lightmapMaxSize = MaxLightmapSize;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.ao = true;
            settings.aoMaxDistance = 0.6f;
            settings.aoExponentIndirect = 1f;
            settings.aoExponentDirect = 0f;
            settings.maxBounces = 3;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 256;
            settings.environmentSampleCount = 256;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            // 3) Hybrid lights -> baked (soft baked shadows cost nothing at runtime).
            int lights = 0;
            foreach (var light in hybrid.GetComponentsInChildren<Light>(true))
            {
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.shadows = LightShadows.Soft;
                if (light.type == LightType.Point || light.type == LightType.Spot) light.shadowRadius = 0.05f;
                if (light.type == LightType.Directional) light.shadowAngle = 0.5f;
                EditorUtility.SetDirty(light);
                lights++;
            }

            // 4) Opaque Hybrid meshes are lightmapped; transparent ones (glass, bottles) stay dynamic.
            int lit = 0, transparent = 0;
            const StaticEditorFlags giFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
            const StaticEditorFlags neverFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
            foreach (var r in hybrid.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isTransparent = r.sharedMaterials.Any(m => m == null || m.renderQueue > (int)RenderQueue.GeometryLast);
                var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject) & ~(neverFlags | giFlags);
                if (isTransparent) transparent++;
                else
                {
                    flags |= giFlags;
                    r.receiveGI = ReceiveGI.Lightmaps;
                    lit++;
                }
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
            }

            // Everything outside the Hybrid (scan, blockout, MR portals, avatar) must not take part in the bake.
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            {
                if (r.transform.IsChildOf(hybrid.transform)) continue;
                var f = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((f & (StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic)) != 0)
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, f & ~(StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic));
            }

            // 5) Bake with only the Hybrid visible (inactive scan/blockout are ignored by the lightmapper).
            overlay.showScan = false;
            overlay.showBlockout = false;
            overlay.showHybrid = true;
            overlay.Apply();
            foreach (var probe in hybrid.GetComponentsInChildren<ReflectionProbe>(true))
            {
                probe.mode = ReflectionProbeMode.Baked;
                EditorUtility.SetDirty(probe);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            bakeStartTime = EditorApplication.timeSinceStartup;
            if (!Lightmapping.BakeAsync())
            {
                Lightmapping.bakeCompleted -= OnBakeCompleted;
                throw new InvalidOperationException("Unity refused to start the lightmap bake.");
            }
            LabRoomMetaMRSetup.Log($"Bake started: {lights} lights set to Baked, {lit} lightmapped meshes, {transparent} transparent meshes left dynamic, " +
                                   $"{TexelsPerUnit} texels/m, max {MaxLightmapSize}px, GPU progressive lightmapper");
        }

        static void OnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            var scene = SceneManager.GetActiveScene();
            double seconds = EditorApplication.timeSinceStartup - bakeStartTime;
            if (scene.path != LabRoomMetaMRSetup.MRScenePath)
            {
                LabRoomMetaMRSetup.Log("Bake finished, but the MR scene is no longer the active scene; it was not saved.");
                return;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            var maps = LightmapSettings.lightmaps;
            var sizes = string.Join(", ", maps.Select(m => m.lightmapColor != null ? $"{m.lightmapColor.width}x{m.lightmapColor.height}" : "?"));
            var data = Lightmapping.lightingDataAsset != null ? AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset) : "none";
            LabRoomMetaMRSetup.Log($"Bake finished in {seconds:0}s: {maps.Length} lightmap(s) [{sizes}], lighting data {data}");
        }

        // ------------------------------------------------------------------------------------------------ quality

        /// <summary>Sets the quality level used by Android (Quest) for an all-baked scene. Desktop levels are untouched.</summary>
        public static void ApplyQuestQuality()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault()
                        ?? throw new FileNotFoundException("QualitySettings.asset not found");
            var so = new SerializedObject(asset);
            int index = AndroidQualityIndex(so);
            var level = so.FindProperty("m_QualitySettings").GetArrayElementAtIndex(index);
            string name = level.FindPropertyRelative("name").stringValue;
            level.FindPropertyRelative("antiAliasing").intValue = 4;            // 4x MSAA (cheap on Quest's tiled GPU)
            level.FindPropertyRelative("shadows").intValue = 0;                 // no realtime shadow maps: lighting is baked
            level.FindPropertyRelative("pixelLightCount").intValue = 1;
            level.FindPropertyRelative("globalTextureMipmapLimit").intValue = 0; // full-resolution textures
            level.FindPropertyRelative("anisotropicTextures").intValue = 1;     // per-texture
            level.FindPropertyRelative("realtimeReflectionProbes").boolValue = false;
            level.FindPropertyRelative("softParticles").boolValue = false;
            level.FindPropertyRelative("lodBias").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            LabRoomMetaMRSetup.Log($"Android quality level '{name}' (#{index}): 4x MSAA, no realtime shadows, full-res textures");
        }

        static int AndroidQualityIndex(SerializedObject so)
        {
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; map != null && i < map.arraySize; i++)
            {
                var entry = map.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first")?.stringValue == "Android")
                    return entry.FindPropertyRelative("second").intValue;
            }
            throw new InvalidOperationException("No default quality level is set for Android.");
        }

        // ------------------------------------------------------------------------------------------------ stats

        public static void ReportStats()
        {
            var scene = OpenMRScene();
            var sb = new StringBuilder();
            var overlay = Find<LabRoomOverlay>(scene);
            foreach (var (label, root) in new[] { ("Scan", overlay?.scan), ("Blockout", overlay?.blockout), ("Hybrid", overlay?.hybrid) })
            {
                if (root == null) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                long tris = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) tris += Triangles(mf.sharedMesh);
                int materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
                int lightmapped = renderers.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE);
                sb.AppendLine($"{label}: active={root.activeSelf}, {renderers.Length} renderers, {tris:N0} triangles, {materials} materials, {lightmapped} lightmapped");
            }
            foreach (var l in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)))
                sb.AppendLine($"Light {l.name}: {l.type}, mode {l.lightmapBakeType}, shadows {l.shadows}, render {l.renderMode}, range {l.range:0.0}, intensity {l.intensity:0.00}, active {l.gameObject.activeInHierarchy}");
            foreach (var p in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ReflectionProbe>(true)))
                sb.AppendLine($"ReflectionProbe {p.name}: {p.mode}, refresh {p.refreshMode}, resolution {p.resolution}");
            sb.AppendLine($"Lightmaps: {LightmapSettings.lightmaps.Length}, lighting data: {(Lightmapping.lightingDataAsset != null ? AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset) : "none")}");

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (asset != null)
            {
                var so = new SerializedObject(asset);
                var level = so.FindProperty("m_QualitySettings").GetArrayElementAtIndex(AndroidQualityIndex(so));
                sb.AppendLine($"Android quality '{level.FindPropertyRelative("name").stringValue}': MSAA {level.FindPropertyRelative("antiAliasing").intValue}, " +
                              $"shadows {level.FindPropertyRelative("shadows").intValue}, pixel lights {level.FindPropertyRelative("pixelLightCount").intValue}, " +
                              $"texture mip limit {level.FindPropertyRelative("globalTextureMipmapLimit").intValue}");
            }
            LabRoomMetaMRSetup.Log(sb.ToString());
        }

        static long Triangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
            return count;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        static Scene OpenMRScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == LabRoomMetaMRSetup.MRScenePath) return active;
            if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Cancelled: the open scene has unsaved changes.");
            return EditorSceneManager.OpenScene(LabRoomMetaMRSetup.MRScenePath, OpenSceneMode.Single);
        }

        static T Find<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<T>(true)).FirstOrDefault(c => c != null);
    }
}
