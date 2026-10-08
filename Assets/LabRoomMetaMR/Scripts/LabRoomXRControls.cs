using UnityEngine;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// Quest controller mapping for the MR lab scene. Drives the existing <see cref="LabRoomOverlay"/> (room views and
    /// blockout opacity), the passthrough windows and the spatial-anchor alignment.
    ///
    /// Normal mode
    ///   X            next room view (scan / blockout / scan+blockout / hybrid / hybrid+scan)
    ///   Y            passthrough windows on/off
    ///   Left stick   up/down: blockout opacity (when the blockout is shown)
    ///   Left stick click   show/hide the wrist HUD
    ///   B            enter align mode
    ///   Menu         reload the saved anchor alignment
    /// Align mode
    ///   Right grip   grab the room and move/turn it with the controller (stays level and on the floor)
    ///   Right stick  left/right: turn the room around you, up/down: raise/lower the room
    ///   Left stick   slide the room (relative to where you look)
    ///   Right trigger (hold)   fine adjustment
    ///   A            save the alignment to a spatial anchor
    ///   B            cancel (restore the previous placement)
    ///   Left stick click (hold 2 s)   erase the saved anchor
    /// The keyboard shortcuts of LabRoomOverlay (1-5, H) keep working in the editor.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabRoomXRControls : MonoBehaviour
    {
        public LabRoomOverlay overlay;
        public LabRoomPassthroughWindows windows;
        public LabRoomAnchorAligner aligner;
        public LabRoomMRHud hud;
        public OVRCameraRig cameraRig;

        [Header("Alignment speeds")]
        [Tooltip("Metres per second at full stick deflection.")]
        public float slideSpeed = 0.3f;
        [Tooltip("Degrees per second at full stick deflection.")]
        public float turnSpeed = 25f;
        [Tooltip("Metres per second at full stick deflection.")]
        public float heightSpeed = 0.15f;
        [Tooltip("Speed multiplier while the right trigger is held.")]
        public float fineFactor = 0.15f;
        public float stickDeadzone = 0.2f;

        [Header("Room view")]
        [Tooltip("Blockout opacity change per second at full stick deflection.")]
        public float opacitySpeed = 0.6f;
        [Tooltip("The overlay's on-screen IMGUI panel can't be seen in a headset, so hide it in this scene.")]
        public bool hideOverlayPanel = true;

        static readonly (bool scan, bool blockout, bool hybrid, string name)[] Views =
        {
            (true, false, false, "Detailed scan"),
            (false, true, false, "Blockout"),
            (true, true, false, "Scan + blockout"),
            (false, false, true, "Hybrid"),
            (true, false, true, "Hybrid + scan"),
        };

        bool grabbing;
        float grabSpeed = 1f;
        Vector3 grabControllerStart;
        float grabYawStart;
        Vector3 grabRoomStart;
        Quaternion grabRoomRotStart;
        float clearHoldTime;

        public string CurrentViewName
        {
            get
            {
                if (overlay == null) return "-";
                int i = MatchView();
                return i >= 0 ? Views[i].name : "Custom";
            }
        }

        void Start()
        {
            if (overlay == null) overlay = FindAnyObjectByType<LabRoomOverlay>();
            if (windows == null) windows = FindAnyObjectByType<LabRoomPassthroughWindows>();
            if (aligner == null) aligner = FindAnyObjectByType<LabRoomAnchorAligner>();
            if (hud == null) hud = FindAnyObjectByType<LabRoomMRHud>();
            if (cameraRig == null) cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (overlay != null && hideOverlayPanel) overlay.showPanel = false;
        }

        void Update()
        {
            if (aligner != null && aligner.IsAligning) UpdateAlignMode();
            else UpdateNormalMode();
        }

        void UpdateNormalMode()
        {
            grabbing = false;
            clearHoldTime = 0f;

            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)) NextView();
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch) && windows != null)
            {
                windows.ToggleWindows();
                Flash(windows.WindowsEnabled ? "Passthrough windows on" : "Passthrough windows off");
            }
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) && hud != null)
                hud.ToggleVisible();
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) && aligner != null)
                aligner.BeginAlign();
            if (OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch) && aligner != null)
                aligner.LoadSavedAlignment();

            var left = Stick(OVRInput.Controller.LTouch);
            if (overlay != null && overlay.showBlockout && Mathf.Abs(left.y) > 0f)
            {
                overlay.blockoutOpacity = Mathf.Clamp01(overlay.blockoutOpacity + left.y * opacitySpeed * Time.deltaTime);
                overlay.Apply();
            }
        }

        void UpdateAlignMode()
        {
            float speed = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch) > 0.5f ? fineFactor : 1f;
            float dt = Time.deltaTime;

            // Grab-and-drag with the right grip.
            float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            var hand = cameraRig != null ? cameraRig.rightControllerAnchor : null;
            if (hand != null)
            {
                bool startGrab = !grabbing && grip > 0.6f;
                bool fineChanged = grabbing && !Mathf.Approximately(speed, grabSpeed);
                if (startGrab || fineChanged)
                {
                    // (Re)start the grab from the current state so switching fine mode never makes the room jump.
                    grabbing = true;
                    grabSpeed = speed;
                    grabControllerStart = hand.position;
                    grabYawStart = Yaw(hand.rotation);
                    grabRoomStart = aligner.roomRoot.position;
                    grabRoomRotStart = aligner.roomRoot.rotation;
                }
                else if (grabbing && grip < 0.4f)
                {
                    grabbing = false;
                }

                if (grabbing)
                {
                    // Rigid grab about the controller, restricted to yaw; fine mode scales both motion and turn.
                    var turn = Quaternion.Euler(0f, Mathf.DeltaAngle(grabYawStart, Yaw(hand.rotation)) * speed, 0f);
                    var pivot = grabControllerStart + (hand.position - grabControllerStart) * speed;
                    var target = pivot + turn * (grabRoomStart - grabControllerStart);
                    target.y = grabRoomStart.y; // height only changes with the stick
                    aligner.SetRoomPose(target, turn * grabRoomRotStart);
                }
            }

            if (!grabbing)
            {
                var head = cameraRig != null ? cameraRig.centerEyeAnchor : null;
                var right = Stick(OVRInput.Controller.RTouch);
                var left = Stick(OVRInput.Controller.LTouch);

                if (Mathf.Abs(right.x) > 0f)
                {
                    var pivot = head != null ? head.position : aligner.roomRoot.position;
                    aligner.RotateRoom(pivot, right.x * turnSpeed * speed * dt);
                }
                if (Mathf.Abs(right.y) > 0f)
                    aligner.TranslateRoom(Vector3.up * right.y * heightSpeed * speed * dt);
                if (left.sqrMagnitude > 0f)
                {
                    var yaw = Quaternion.Euler(0f, head != null ? Yaw(head.rotation) : 0f, 0f);
                    var move = yaw * new Vector3(left.x, 0f, left.y);
                    aligner.TranslateRoom(move * slideSpeed * speed * dt);
                }
            }

            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                grabbing = false;
                aligner.SaveAlignment();
            }
            else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            {
                grabbing = false;
                aligner.CancelAlign();
            }

            if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch))
            {
                clearHoldTime += dt;
                if (clearHoldTime >= 2f)
                {
                    clearHoldTime = float.NegativeInfinity; // fire once per press
                    aligner.ClearAlignment();
                }
            }
            else clearHoldTime = 0f;
        }

        public void NextView()
        {
            if (overlay == null) return;
            int i = (MatchView() + 1) % Views.Length;
            overlay.showScan = Views[i].scan;
            overlay.showBlockout = Views[i].blockout;
            overlay.showHybrid = Views[i].hybrid;
            overlay.Apply();
            Flash("View: " + Views[i].name);
        }

        int MatchView()
        {
            for (int i = 0; i < Views.Length; i++)
                if (Views[i].scan == overlay.showScan && Views[i].blockout == overlay.showBlockout && Views[i].hybrid == overlay.showHybrid)
                    return i;
            return -1;
        }

        void Flash(string message)
        {
            if (hud != null) hud.Flash(message);
        }

        Vector2 Stick(OVRInput.Controller controller)
        {
            var v = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller);
            return v.magnitude < stickDeadzone ? Vector2.zero : v;
        }

        static float Yaw(Quaternion rotation)
        {
            var f = rotation * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.04f) f = Vector3.ProjectOnPlane(rotation * Vector3.up, Vector3.up); // pointing straight up/down
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}
