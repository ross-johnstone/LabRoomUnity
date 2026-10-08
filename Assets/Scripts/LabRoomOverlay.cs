using UnityEngine;

// Switches between the detailed scan, the white blockout and the photoreal hybrid.
// Play mode keys: 1 scan, 2 blockout, 3 scan + blockout, 4 hybrid, 5 hybrid + scan, H hide/show panel.
[ExecuteAlways]
public class LabRoomOverlay : MonoBehaviour
{
    public GameObject scan;
    public GameObject blockout;
    public GameObject hybrid;
    public bool showScan = true;
    public bool showBlockout = true;
    public bool showHybrid = false;
    [Range(0f, 1f)] public float blockoutOpacity = 0.5f;
    public bool showPanel = true;

    MaterialPropertyBlock block;

    void OnEnable() { Apply(); }

#if UNITY_EDITOR
    void OnValidate() { UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); }; }
#endif

    public void Apply()
    {
        if (scan != null) scan.SetActive(showScan);
        if (hybrid != null) hybrid.SetActive(showHybrid);
        if (blockout == null) return;
        blockout.SetActive(showBlockout);
        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(1f, 1f, 1f, blockoutOpacity));
        foreach (var r in blockout.GetComponentsInChildren<Renderer>(true)) r.SetPropertyBlock(block);
    }

    void Set(bool s, bool b, bool h) { showScan = s; showBlockout = b; showHybrid = h; Apply(); }

    void Update()
    {
        if (!Application.isPlaying) return;
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Alpha1)) Set(true, false, false);
        if (Input.GetKeyDown(KeyCode.Alpha2)) Set(false, true, false);
        if (Input.GetKeyDown(KeyCode.Alpha3)) Set(true, true, false);
        if (Input.GetKeyDown(KeyCode.Alpha4)) Set(false, false, true);
        if (Input.GetKeyDown(KeyCode.Alpha5)) Set(true, false, true);
        if (Input.GetKeyDown(KeyCode.H)) showPanel = !showPanel;
#elif ENABLE_INPUT_SYSTEM
        // Same shortcuts through the Input System (used when the project's active input handling is the new system).
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) Set(true, false, false);
        if (kb.digit2Key.wasPressedThisFrame) Set(false, true, false);
        if (kb.digit3Key.wasPressedThisFrame) Set(true, true, false);
        if (kb.digit4Key.wasPressedThisFrame) Set(false, false, true);
        if (kb.digit5Key.wasPressedThisFrame) Set(true, false, true);
        if (kb.hKey.wasPressedThisFrame) showPanel = !showPanel;
#endif
    }

    void OnGUI()
    {
        if (!Application.isPlaying || !showPanel) return;
        GUILayout.BeginArea(new Rect(10, 10, 300, 215), GUI.skin.box);
        GUILayout.Label("Lab room overlay");
        bool s = GUILayout.Toggle(showScan, " Detailed scan");
        bool b = GUILayout.Toggle(showBlockout, " Blockout");
        bool h = GUILayout.Toggle(showHybrid, " Hybrid (photoreal)");
        GUILayout.Label($"Blockout opacity: {blockoutOpacity:0.00}");
        float o = GUILayout.HorizontalSlider(blockoutOpacity, 0f, 1f);
        GUILayout.Space(8);
        GUILayout.Label("Keys: 1 scan · 2 blockout · 3 both · 4 hybrid · 5 hybrid+scan · H panel");
        GUILayout.Label("Fly: hold right mouse + WASD, Q/E down/up, Shift fast");
        GUILayout.EndArea();
        if (s != showScan || b != showBlockout || h != showHybrid || !Mathf.Approximately(o, blockoutOpacity))
        {
            showScan = s; showBlockout = b; showHybrid = h; blockoutOpacity = o; Apply();
        }
    }
}
