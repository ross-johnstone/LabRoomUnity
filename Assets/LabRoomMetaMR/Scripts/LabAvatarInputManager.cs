using System.Text;
using Meta.XR.MultiplayerBlocks.Shared;
using Oculus.Avatar2;
using UnityEngine;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// Drives the local Meta Avatar from the user's own tracking (Avatars SDK 40 provider model, body tracking mode
    /// "None" plus explicit providers; replaces the deprecated "Standalone" mode of the AvatarSDK building block):
    ///  * head and controllers: the OVRCameraRig anchors the user is seeing through, converted into the avatar's own
    ///    space every frame (same source as Meta's building-block InputTrackingDelegate, but sampled on the main
    ///    thread and handed to the SDK through <see cref="LabAvatarTrackingDelegate"/>);
    ///  * hand tracking (wrist pose and finger articulation): the Avatars SDK's OVRPlugin hand-tracking service. If a
    ///    hand is tracked by the headset but that service reports nothing for it, the hand anchor's pose is sent as a
    ///    controller pose instead so the avatar's arm still follows the hand;
    ///  * finger curl from controller buttons/triggers: Meta's building-block InputControlDelegate.
    /// AvatarEntity picks up the first enabled input manager in the scene, so keep only one enabled.
    /// <see cref="Status"/> is a live self-check (shown on the wrist HUD): avatar state, how often the SDK reads the
    /// input, the source of each hand and how far the avatar's head and wrists are from the tracked poses.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabAvatarInputManager : OvrAvatarInputManager
    {
        public enum HandSource { None, Controller, HandTracking, HandAnchorFallback }

        [Tooltip("Rig whose head and hand anchors drive the avatar. Found automatically (parent, then scene) when empty.")]
        public OVRCameraRig cameraRig;

        [Tooltip("Seconds a hand may be tracked by the headset while the avatar hand-tracking service reports no data " +
                 "for it before the hand anchor pose is sent as a controller pose instead.")]
        [Min(0f)] public float handFallbackDelay = 0.5f;

        readonly LabAvatarTrackingDelegate trackingDelegate = new LabAvatarTrackingDelegate();
        readonly StringBuilder sb = new StringBuilder(256);
        OvrAvatarInputTrackingDelegatedProvider ownTrackingProvider;
        OvrAvatarInputControlDelegatedProvider ownControlProvider;
        OvrAvatarTrackingHandsState handProbe;
        OvrAvatarEntity entity;
        bool nativeLeft, nativeRight;
        float leftUntrackedSince = -1f, rightUntrackedSince = -1f;
        float nextProbe, nextStatus, skeletonSince = -1f;
        long readsAtLastStatus;
        float lastStatusTime;
        HandSource loggedLeft = (HandSource)(-1), loggedRight = (HandSource)(-1);

        public HandSource LeftSource { get; private set; }
        public HandSource RightSource { get; private set; }
        /// <summary>True when the Avatars SDK OVRPlugin hand-tracking service exists (finger articulation).</summary>
        public bool HandTrackingServiceAvailable => _handTrackingProvider != null;
        /// <summary>Input reads by the Avatars SDK per second (0 means the avatar is not consuming this input).</summary>
        public float InputReadsPerSecond { get; private set; }
        public long InputReads => trackingDelegate.Reads;
        public string Status { get; private set; } = "Avatar: starting";

        protected override void OnTrackingInitialized()
        {
            ResolveRig();
            if (_inputTrackingProvider == null)
            {
                ownTrackingProvider = new OvrAvatarInputTrackingDelegatedProvider(trackingDelegate);
                _inputTrackingProvider = ownTrackingProvider;
            }
            if (_inputControlProvider == null)
            {
                ownControlProvider = new OvrAvatarInputControlDelegatedProvider(new InputControlDelegate());
                _inputControlProvider = ownControlProvider;
            }
            var hands = HandTrackingProvider; // creates the OVRPlugin hand-tracking provider through the base class
            Debug.Log("[LabAvatar] avatar input: head/controllers from " + (cameraRig != null ? cameraRig.name : "NO OVRCameraRig") +
                      " anchors; hand tracking service " + (hands != null ? "available" : "unavailable (hand anchors drive the arms instead)"));
            Capture();
        }

        void ResolveRig()
        {
            if (cameraRig == null) cameraRig = GetComponentInParent<OVRCameraRig>();
            if (cameraRig == null) cameraRig = FindAnyObjectByType<OVRCameraRig>();
        }

        void OnEnable() => Application.onBeforeRender += Capture;

        void OnDisable() => Application.onBeforeRender -= Capture;

        void Update()
        {
            float now = Time.unscaledTime;
            if (_handTrackingProvider != null && now >= nextProbe)
            {
                nextProbe = now + 0.1f;
                if (handProbe == null) handProbe = new OvrAvatarTrackingHandsState();
                bool ok = _handTrackingProvider.GetHandData(handProbe);
                nativeLeft = ok && handProbe.isTrackedLeft;
                nativeRight = ok && handProbe.isTrackedRight;
            }
            if (now >= nextStatus)
            {
                nextStatus = now + 0.25f;
                UpdateStatus(now);
            }
        }

        // OVRCameraRig moves its anchors in Update and again just before rendering; sample after both.
        void LateUpdate() => Capture();

        void Capture()
        {
            if (cameraRig == null || cameraRig.centerEyeAnchor == null) return;
            var root = transform;
            float now = Time.unscaledTime;
            bool controllersInUse = OVRInput.GetActiveController() != OVRInput.Controller.Hands;
            LeftSource = SourceFor(controllersInUse, OVRInput.Controller.LTouch, OVRInput.Controller.LHand, nativeLeft, ref leftUntrackedSince, now);
            RightSource = SourceFor(controllersInUse, OVRInput.Controller.RTouch, OVRInput.Controller.RHand, nativeRight, ref rightUntrackedSince, now);

            var state = new OvrAvatarInputTrackingState
            {
                headsetActive = true,
                headset = RelativePose(root, cameraRig.centerEyeAnchor),
                leftControllerActive = LeftSource == HandSource.Controller || LeftSource == HandSource.HandAnchorFallback,
                rightControllerActive = RightSource == HandSource.Controller || RightSource == HandSource.HandAnchorFallback,
                leftControllerVisible = false,
                rightControllerVisible = false,
                leftController = RelativePose(root, cameraRig.leftHandAnchor),
                rightController = RelativePose(root, cameraRig.rightHandAnchor),
            };
            trackingDelegate.Publish(state);

            if (LeftSource != loggedLeft || RightSource != loggedRight)
            {
                loggedLeft = LeftSource;
                loggedRight = RightSource;
                Debug.Log($"[LabAvatar] hand sources: left {LeftSource}, right {RightSource}");
            }
        }

        HandSource SourceFor(bool controllersInUse, OVRInput.Controller touch, OVRInput.Controller hand, bool nativeTracked,
            ref float untrackedSince, float now)
        {
            if (controllersInUse && OVRInput.GetControllerOrientationTracked(touch))
            {
                untrackedSince = -1f;
                return HandSource.Controller;
            }
            if (!OVRInput.GetControllerPositionValid(hand))
            {
                untrackedSince = -1f;
                return HandSource.None;
            }
            if (nativeTracked)
            {
                untrackedSince = -1f;
                return HandSource.HandTracking;
            }
            if (untrackedSince < 0f) untrackedSince = now;
            return now - untrackedSince >= handFallbackDelay ? HandSource.HandAnchorFallback : HandSource.HandTracking;
        }

        static CAPI.ovrAvatar2Transform RelativePose(Transform root, Transform anchor)
        {
            if (anchor == null) return new CAPI.ovrAvatar2Transform(Vector3.zero, Quaternion.identity, Vector3.one);
            var position = root.InverseTransformPoint(anchor.position);
            var rotation = Quaternion.Inverse(root.rotation) * anchor.rotation;
            return new CAPI.ovrAvatar2Transform(position, rotation, Vector3.one);
        }

        /// <summary>True once the avatar skeleton has been loaded and posed for a moment (joint queries are valid).</summary>
        public bool SkeletonReady
        {
            get
            {
                if (entity == null) entity = GetComponent<OvrAvatarEntity>();
                if (entity == null || !entity.IsCreated || entity.CurrentState < OvrAvatarEntity.AvatarState.Skeleton)
                {
                    skeletonSince = -1f;
                    return false;
                }
                if (skeletonSince < 0f) skeletonSince = Time.unscaledTime;
                return Time.unscaledTime - skeletonSince > 1f;
            }
        }

        /// <summary>Distance (metres) between an avatar joint and a world position, or -1 if the joint is unavailable.</summary>
        public float JointError(CAPI.ovrAvatar2JointType joint, Vector3 worldPosition)
        {
            if (!SkeletonReady || !entity.HasCriticalJoint(joint)) return -1f;
            try
            {
                // With GPU skinning the SDK keeps critical joints in its joint monitor (no Unity skeleton hierarchy).
                var t = entity.GetSkeletonTransform(joint);
                return t != null ? Vector3.Distance(t.position, worldPosition) : -1f;
            }
            catch (System.IndexOutOfRangeException)
            {
                return -1f; // joint data not valid yet on this frame
            }
        }

        void UpdateStatus(float now)
        {
            long reads = trackingDelegate.Reads;
            if (lastStatusTime > 0f) InputReadsPerSecond = (reads - readsAtLastStatus) / Mathf.Max(0.001f, now - lastStatusTime);
            readsAtLastStatus = reads;
            lastStatusTime = now;

            if (entity == null) entity = GetComponent<OvrAvatarEntity>();
            sb.Clear();
            sb.Append("Avatar: ").Append(entity != null ? entity.CurrentState.ToString() : "no entity")
              .Append("   input ").Append(Mathf.RoundToInt(InputReadsPerSecond)).Append("/s");
            if (!HandTrackingServiceAvailable) sb.Append("   <color=#ffc84a>no hand service</color>");
            sb.Append('\n');
            if (cameraRig != null)
            {
                sb.Append("  head ").Append(Cm(JointError(CAPI.ovrAvatar2JointType.Head, cameraRig.centerEyeAnchor.position)));
                sb.Append("   L ").Append(Label(LeftSource)).Append(' ')
                  .Append(LeftSource == HandSource.None ? "" : Cm(JointError(CAPI.ovrAvatar2JointType.LeftHandWrist, cameraRig.leftHandAnchor.position)));
                sb.Append("   R ").Append(Label(RightSource)).Append(' ')
                  .Append(RightSource == HandSource.None ? "" : Cm(JointError(CAPI.ovrAvatar2JointType.RightHandWrist, cameraRig.rightHandAnchor.position)));
            }
            else sb.Append("  <color=#ff6a6a>no OVRCameraRig</color>");
            Status = sb.ToString();
        }

        static string Label(HandSource s) => s switch
        {
            HandSource.Controller => "ctrl",
            HandSource.HandTracking => "hand",
            HandSource.HandAnchorFallback => "hand*",
            _ => "-",
        };

        static string Cm(float metres) => metres < 0f ? "?" : Mathf.RoundToInt(metres * 100f) + "cm";

        protected override void OnDestroyCalled()
        {
            if (ReferenceEquals(_inputTrackingProvider, ownTrackingProvider)) _inputTrackingProvider = null;
            if (ReferenceEquals(_inputControlProvider, ownControlProvider)) _inputControlProvider = null;
            ownTrackingProvider?.Dispose();
            ownControlProvider?.Dispose();
            ownTrackingProvider = null;
            ownControlProvider = null;
            base.OnDestroyCalled();
        }
    }
}
