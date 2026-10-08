using System.Collections.Generic;
using UnityEngine;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// Window-only Meta passthrough for the reconstructed lab.
    ///
    /// Setup (built by Lab Room > Meta MR > Create or Rebuild MR Scene):
    ///  * an underlay <see cref="OVRPassthroughLayer"/> on the camera rig (OVRManager.isInsightPassthroughEnabled = true);
    ///  * the rig cameras clear to transparent black, so the eye buffer alpha decides where passthrough is visible;
    ///  * portal meshes (children of this object) use the "LabRoom/MR/Passthrough Window" material. They are depth-tested
    ///    against the room, punch colour/alpha to 0 and mark a stencil bit;
    ///  * a full-screen "Passthrough Alpha Guard" (child of the centre eye) forces alpha back to 1 everywhere else.
    ///
    /// To change the windows later, move/scale the portal objects, add new meshes with the Passthrough Window material
    /// (then use "Collect Window Renderers From Children"), or paint a mask texture into the material.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabRoomPassthroughWindows : MonoBehaviour
    {
        [Tooltip("Underlay passthrough layer on the camera rig.")]
        public OVRPassthroughLayer passthroughLayer;

        [Tooltip("Portal meshes. Anything rendered with the Passthrough Window material shows the real world.")]
        public List<Renderer> windowRenderers = new List<Renderer>();

        [Tooltip("Full-screen alpha guard renderer (child of the centre eye camera).")]
        public Renderer alphaGuard;

        [Tooltip("Show the real world through the windows.")]
        [SerializeField] bool windowsEnabled = true;

        [Tooltip("When the windows are switched off, draw the scene's skybox behind them like the original scene.")]
        public bool skyboxWhenDisabled = true;

        Camera[] rigCameras = new Camera[0];

        public bool WindowsEnabled => windowsEnabled;

        /// <summary>True when the passthrough service is running and the layer is visible.</summary>
        public bool PassthroughVisible =>
            windowsEnabled && passthroughLayer != null && passthroughLayer.isActiveAndEnabled &&
            !passthroughLayer.hidden && OVRManager.IsInsightPassthroughInitialized();

        public string Status
        {
            get
            {
                if (!windowsEnabled) return "Windows: virtual";
                if (passthroughLayer == null) return "Windows: no passthrough layer";
                if (!OVRManager.IsInsightPassthroughInitialized()) return "Windows: passthrough starting";
                return "Windows: passthrough";
            }
        }

        void Start()
        {
            // The portal shader writes premultiplied (0,0,0,0); make the compositor treat the eye buffer that way.
            OVRManager.eyeFovPremultipliedAlphaModeEnabled = true;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null) rigCameras = rig.GetComponentsInChildren<Camera>(true);
            Apply();
        }

        public void SetWindowsEnabled(bool on)
        {
            windowsEnabled = on;
            Apply();
        }

        public void ToggleWindows() => SetWindowsEnabled(!windowsEnabled);

        public void Apply()
        {
            foreach (var r in windowRenderers)
                if (r != null) r.enabled = windowsEnabled;
            if (alphaGuard != null) alphaGuard.enabled = windowsEnabled;
            if (passthroughLayer != null) passthroughLayer.hidden = !windowsEnabled;

            foreach (var cam in rigCameras)
            {
                if (cam == null) continue;
                if (windowsEnabled || !skyboxWhenDisabled)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                }
                else
                {
                    cam.clearFlags = CameraClearFlags.Skybox;
                }
            }
        }

        [ContextMenu("Collect Window Renderers From Children")]
        public void CollectWindowRenderers()
        {
            windowRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != alphaGuard) windowRenderers.Add(r);
        }

        void Reset() => CollectWindowRenderers();

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }
#endif
    }
}
