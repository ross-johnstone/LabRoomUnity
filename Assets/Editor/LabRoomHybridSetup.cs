// Adds the photoreal "Hybrid" version of the room as LabRoom > Hybrid in the overlay scene.
// Runs once automatically; re-run any time from: Lab Room > Rebuild Hybrid
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class LabRoomHybridSetup
{
    const string HybridPath = "Assets/Models/Hybrid/lab_room_hybrid.fbx";
    const string MatJson = "Assets/Models/Hybrid/hybrid_materials.json";
    const string TexDir = "Assets/Models/Hybrid/Textures/";
    const string MatDir = "Assets/Materials/Hybrid";
    const string ScenePath = "Assets/Scenes/LabRoom_Overlay.unity";
    const string Marker = "Assets/Models/Hybrid/.hybrid_built";

    [Serializable] public class MatSpec
    {
        public string name; public float[] color; public float metallic; public float smoothness;
        public string albedo; public string normal; public float normalScale = 1f; public string metallicGloss; public string occlusion;
        public string emission; public float[] emissionColor; public float emissionIntensity; public string mode; public float cutoff = 0.5f;
        public float[] tiling;
    }
    [Serializable] public class MatList { public MatSpec[] materials; }

    static LabRoomHybridSetup()
    {
        if (File.Exists(Marker) || !File.Exists(HybridPath) || !File.Exists(ScenePath)) return;
        EditorApplication.delayCall += () => { if (!File.Exists(Marker)) Build(); };
    }

    static Texture2D Tex(string file) =>
        string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + file);

    static void SetMode(Material m, string mode, float cutoff)
    {
        m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        switch (mode)
        {
            case "Cutout":
                m.SetFloat("_Mode", 1); m.SetOverrideTag("RenderType", "TransparentCutout");
                m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.Zero); m.SetInt("_ZWrite", 1);
                m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", cutoff); m.renderQueue = (int)RenderQueue.AlphaTest; break;
            case "Fade":
                m.SetFloat("_Mode", 2); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = (int)RenderQueue.Transparent; break;
            case "Transparent":
                m.SetFloat("_Mode", 3); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.renderQueue = (int)RenderQueue.Transparent; break;
            default:
                m.SetFloat("_Mode", 0); m.SetOverrideTag("RenderType", "");
                m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.Zero); m.SetInt("_ZWrite", 1);
                m.renderQueue = -1; break;
        }
    }

    static Material MakeMaterial(MatSpec s)
    {
        string path = $"{MatDir}/{s.name.Replace('.', '_')}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Standard");
        var c = s.color != null && s.color.Length >= 3 ? new Color(s.color[0], s.color[1], s.color[2], s.color.Length > 3 ? s.color[3] : 1f) : Color.white;
        m.color = c;
        m.SetFloat("_Metallic", s.metallic);
        m.SetFloat("_Glossiness", s.smoothness);
        m.SetFloat("_GlossMapScale", s.smoothness > 0 ? Mathf.Clamp01(s.smoothness * 1.4f) : 1f);
        m.mainTexture = Tex(s.albedo);
        var n = Tex(s.normal);
        m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", s.normalScale);
        if (n != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        var ms = Tex(s.metallicGloss);
        m.SetTexture("_MetallicGlossMap", ms);
        if (ms != null) m.EnableKeyword("_METALLICGLOSSMAP"); else m.DisableKeyword("_METALLICGLOSSMAP");
        m.SetTexture("_OcclusionMap", Tex(s.occlusion));
        var e = Tex(s.emission);
        bool emissive = e != null || s.emissionIntensity > 0;
        if (emissive)
        {
            var ec = s.emissionColor != null && s.emissionColor.Length >= 3 ? new Color(s.emissionColor[0], s.emissionColor[1], s.emissionColor[2]) : Color.white;
            m.SetColor("_EmissionColor", ec * s.emissionIntensity);
            m.SetTexture("_EmissionMap", e);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
        SetMode(m, s.mode, s.cutoff);
        EditorUtility.SetDirty(m);
        return m;
    }

    [MenuItem("Lab Room/Rebuild Hybrid")]
    public static void Build()
    {
        try
        {
            if (File.Exists(HybridPath))   // re-import so the current import rules (material slot names) are applied
                AssetDatabase.ImportAsset(HybridPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HybridPath);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(MatJson);
            if (prefab == null || json == null)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HybridPath);
                json = AssetDatabase.LoadAssetAtPath<TextAsset>(MatJson);
            }
            if (prefab == null || json == null) { Debug.LogError("[LabRoom] Hybrid model or material list missing."); return; }

            // 1) materials
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials", "Hybrid");
            var list = JsonUtility.FromJson<MatList>(json.text);
            var mats = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var s in list.materials) mats[s.name] = MakeMaterial(s);
            AssetDatabase.SaveAssets();

            // 2) scene
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath);
            }
            var root = GameObject.Find("LabRoom");
            if (root == null) { Debug.LogError("[LabRoom] 'LabRoom' object not found in the scene."); return; }
            var old = root.transform.Find("Hybrid");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var hybrid = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            hybrid.name = "Hybrid";
            hybrid.transform.SetParent(root.transform, false);

            // 3) assign materials by name
            int missing = 0;
            foreach (var r in hybrid.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    string key = sm[i] != null ? sm[i].name : "";
                    if (mats.TryGetValue(key, out var mm) || mats.TryGetValue(key.Replace(" (Instance)", ""), out mm)) sm[i] = mm;
                    else missing++;
                }
                r.sharedMaterials = sm;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            if (missing > 0) Debug.LogWarning($"[LabRoom] {missing} hybrid material slots had no match in {MatJson}.");

            // 4) lights from the LIGHT_* markers
            Transform sunTarget = null, probeT = null;
            foreach (var t in hybrid.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "LIGHT_sun_target") sunTarget = t;
                if (t.name == "LIGHT_probe") probeT = t;
            }
            foreach (var t in hybrid.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("LIGHT_")) continue;
                if (t.name.StartsWith("LIGHT_point_"))
                {
                    var l = t.gameObject.AddComponent<Light>();
                    l.type = LightType.Point; l.range = 4.5f; l.intensity = 0.55f; l.color = new Color(1f, 0.96f, 0.90f);
                    l.shadows = (t.name.EndsWith("11") || t.name.EndsWith("21")) ? LightShadows.Soft : LightShadows.None;
                    l.renderMode = LightRenderMode.ForcePixel;
                }
                else if (t.name == "LIGHT_lamp_mug")
                {
                    var l = t.gameObject.AddComponent<Light>();
                    l.type = LightType.Point; l.range = 2.0f; l.intensity = 1.0f; l.color = new Color(1f, 0.80f, 0.55f);
                    l.shadows = LightShadows.Soft; l.renderMode = LightRenderMode.ForcePixel;
                }
                else if (t.name == "LIGHT_sun")
                {
                    var l = t.gameObject.AddComponent<Light>();
                    l.type = LightType.Directional; l.intensity = 0.9f; l.color = new Color(1f, 0.97f, 0.90f); l.shadows = LightShadows.Soft;
                    if (sunTarget != null) t.LookAt(sunTarget);
                }
            }

            // turn off the scene's default directional light while the hybrid has its own sun
            var defaultSun = GameObject.Find("Directional Light");
            if (defaultSun != null) defaultSun.SetActive(false);

            // 5) ambient light, camera
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.50f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.25f);
            var cam = GameObject.Find("Main Camera");
            if (cam != null) { var c = cam.GetComponent<Camera>(); c.renderingPath = RenderingPath.DeferredShading; c.allowHDR = true; }

            // 6) visibility: show the hybrid, hide scan + blockout while baking reflections
            var overlay = root.GetComponent<LabRoomOverlay>();
            if (overlay != null) { overlay.hybrid = hybrid; overlay.showScan = false; overlay.showBlockout = false; overlay.showHybrid = true; overlay.Apply(); }

            // 7) reflection probe (baked, box-projected to the room)
            var b = new Bounds(hybrid.transform.position, Vector3.zero); bool first = true;
            foreach (var r in hybrid.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("Floor") || r.name.StartsWith("Ceiling"))
                { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var pgo = new GameObject("ReflectionProbe"); pgo.transform.SetParent(hybrid.transform, false);
            pgo.transform.position = probeT != null ? probeT.position : b.center;
            var probe = pgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked; probe.boxProjection = true; probe.resolution = 256;
            probe.size = b.size + new Vector3(0.1f, 0.1f, 0.1f); probe.center = b.center - pgo.transform.position;
            string cube = "Assets/Materials/Hybrid/HybridReflection.exr";
            Lightmapping.BakeReflectionProbe(probe, cube);
            probe.bakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(cube);

            EditorSceneManager.MarkSceneDirty(hybrid.scene);
            EditorSceneManager.SaveScene(hybrid.scene);
            File.WriteAllText(Marker, DateTime.Now.ToString("s"));
            Selection.activeGameObject = hybrid;
            Debug.Log("[LabRoom] Hybrid added under LabRoom. Keys in Play mode: 1 scan, 2 blockout, 3 scan+blockout, 4 hybrid, 5 hybrid+scan.");
        }
        catch (Exception e) { Debug.LogException(e); }
    }
}
