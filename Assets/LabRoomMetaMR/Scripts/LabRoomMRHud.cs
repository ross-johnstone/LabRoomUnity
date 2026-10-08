using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// Small world-space status panel above the left controller: current room view, passthrough window state,
    /// spatial-anchor alignment state, avatar tracking self-check and the controller cheat sheet.
    /// The canvas is created at runtime under OVRCameraRig/LeftControllerAnchor, so the scene holds no UI assets.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabRoomMRHud : MonoBehaviour
    {
        public OVRCameraRig cameraRig;
        public LabRoomXRControls controls;
        public LabRoomAnchorAligner aligner;
        public LabRoomPassthroughWindows windows;
        public LabLocalAvatarBehaviour localAvatar;
        public LabAvatarInputManager avatarInput;

        [Tooltip("Show the panel when the scene starts (left stick click toggles it).")]
        public bool visible = true;
        [Tooltip("Panel position relative to the left controller anchor (metres).")]
        public Vector3 localOffset = new Vector3(0f, 0.1f, 0.03f);
        [Tooltip("Panel rotation relative to the left controller anchor.")]
        public Vector3 localEuler = new Vector3(20f, 0f, 0f);
        [Tooltip("World size of one canvas pixel (metres).")]
        public float pixelSize = 0.00035f;

        Canvas canvas;
        Text label;
        string flashMessage;
        float flashUntil;
        float nextRefresh;
        readonly StringBuilder sb = new StringBuilder(512);

        void Start()
        {
            if (cameraRig == null) cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (controls == null) controls = FindAnyObjectByType<LabRoomXRControls>();
            if (aligner == null) aligner = FindAnyObjectByType<LabRoomAnchorAligner>();
            if (windows == null) windows = FindAnyObjectByType<LabRoomPassthroughWindows>();
            if (localAvatar == null) localAvatar = FindAnyObjectByType<LabLocalAvatarBehaviour>();
            if (avatarInput == null) avatarInput = FindAnyObjectByType<LabAvatarInputManager>();
            Build();
            if (aligner != null) aligner.StateChanged += a => Flash(a.Status);
        }

        void Build()
        {
            Transform parent = cameraRig != null && cameraRig.leftControllerAnchor != null ? cameraRig.leftControllerAnchor : transform;

            var root = new GameObject("MR_HUD_Canvas", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localOffset;
            root.transform.localRotation = Quaternion.Euler(localEuler);
            root.transform.localScale = Vector3.one * pixelSize;
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(560f, 390f);

            var bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(root.transform, false);
            Stretch((RectTransform)bg.transform, 0f);
            bg.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f, 0.85f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            Stretch((RectTransform)textGo.transform, 14f);
            label = textGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 20;
            label.lineSpacing = 1.05f;
            label.color = Color.white;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = true;

            canvas.enabled = visible;
            Refresh();
        }

        static void Stretch(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        public void ToggleVisible()
        {
            visible = !visible;
            if (canvas != null) canvas.enabled = visible;
        }

        public void Flash(string message)
        {
            flashMessage = message;
            flashUntil = Time.unscaledTime + 3f;
            nextRefresh = 0f;
        }

        void Update()
        {
            if (label == null || !visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void Refresh()
        {
            if (label == null) return;
            bool aligning = aligner != null && aligner.IsAligning;
            sb.Clear();
            sb.Append("<b>LAB ROOM MR</b>");
            if (aligning) sb.Append("   <color=#ffc84a><b>ALIGN MODE</b></color>");
            sb.Append('\n');
            sb.Append("View: ").Append(controls != null ? controls.CurrentViewName : "-").Append('\n');
            sb.Append(windows != null ? windows.Status : "Windows: -").Append('\n');
            sb.Append("Anchor: ").Append(aligner != null ? aligner.Status : "-").Append('\n');
            if (avatarInput != null) sb.Append(avatarInput.Status).Append('\n');
            else sb.Append("Avatar: ").Append(localAvatar == null ? "-" : localAvatar.LatestStreamData != null ? "streaming" : "loading").Append('\n');
            if (flashMessage != null && Time.unscaledTime < flashUntil)
                sb.Append("<color=#7fd7ff>").Append(flashMessage).Append("</color>\n");
            sb.Append("<color=#a8b0bc>");
            if (aligning)
                sb.Append("R grip: grab room   R stick: turn / height\nL stick: slide   hold R trigger: fine\nA: save anchor   B: cancel   hold L stick 2s: erase");
            else
                sb.Append("X: next view   Y: windows   B: align room\nMenu: reload anchor   L stick: blockout opacity\nL stick click: hide this panel");
            sb.Append("</color>");
            label.text = sb.ToString();
        }
    }
}
