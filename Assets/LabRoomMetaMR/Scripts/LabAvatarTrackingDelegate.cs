using System.Threading;
using Oculus.Avatar2;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// Headset and controller (or hand-anchor) poses for the local Meta Avatar.
    ///
    /// The Avatars SDK can call <see cref="GetRawInputTrackingState"/> from inside its native update, which is not
    /// guaranteed to run on Unity's main thread, so this class never touches the Unity API. Instead
    /// <see cref="LabAvatarInputManager"/> samples the OVRCameraRig anchors on the main thread every frame and
    /// <see cref="Publish"/>es a snapshot; the SDK always receives the latest one.
    /// </summary>
    public sealed class LabAvatarTrackingDelegate : OvrAvatarInputTrackingDelegate
    {
        readonly object gate = new object();
        OvrAvatarInputTrackingState snapshot;
        bool hasSnapshot;
        long reads;

        /// <summary>How many times the Avatars SDK has asked for input (shows that the avatar is consuming it).</summary>
        public long Reads => Interlocked.Read(ref reads);

        /// <summary>Stores the pose snapshot the SDK will receive on its next read (main thread).</summary>
        public void Publish(in OvrAvatarInputTrackingState state)
        {
            lock (gate)
            {
                snapshot = state;
                hasSnapshot = true;
            }
        }

        public override bool GetRawInputTrackingState(out OvrAvatarInputTrackingState inputTrackingState)
        {
            Interlocked.Increment(ref reads);
            lock (gate)
            {
                inputTrackingState = snapshot;
                return hasSnapshot;
            }
        }
    }
}
