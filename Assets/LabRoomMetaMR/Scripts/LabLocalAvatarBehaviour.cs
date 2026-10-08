using System;
using Meta.XR.MultiplayerBlocks.Shared;
using UnityEngine;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// <see cref="IAvatarBehaviour"/> for the local user's Meta Avatar.
    ///
    /// The Meta <see cref="AvatarEntity"/> on the same GameObject reads this to decide that it is the local avatar
    /// (input authority), which preset or user avatar to load, and where to send its serialized pose stream.
    /// Tracking comes from <see cref="LabAvatarInputManager"/> on the same GameObject (headset and controller poses
    /// from the OVRCameraRig anchors, hand tracking from the Avatars SDK service). It sits under
    /// OVRCameraRig/TrackingSpace with an identity local transform so the avatar stands where the user stands.
    ///
    /// Networking hook: every few frames the AvatarEntity records a compact pose stream and hands it to
    /// <see cref="ReceiveStreamData"/>; subscribe to <see cref="StreamDataRecorded"/> to send it to other clients,
    /// and feed it into a <see cref="LabRemoteAvatarBehaviour"/> on the receiving side.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabLocalAvatarBehaviour : MonoBehaviour, IAvatarBehaviour
    {
        [Tooltip("Meta user id to load a personal avatar for (needs Platform SDK entitlement). 0 = use the preset below.")]
        [SerializeField] ulong oculusUserId;

        [Tooltip("Preset avatar from the Meta Avatars sample assets (0-32) used when no user avatar is loaded.")]
        [SerializeField, Range(0, 32)] int localAvatarIndex = 0;

        /// <summary>Raised with each serialized avatar pose frame recorded for this local avatar.</summary>
        public event Action<byte[]> StreamDataRecorded;

        /// <summary>The most recent pose frame (null until the avatar has loaded).</summary>
        public byte[] LatestStreamData { get; private set; }

        public ulong OculusId => oculusUserId;
        public int LocalAvatarIndex => localAvatarIndex;
        public bool HasInputAuthority => true;

        public AvatarEntity Entity => GetComponent<AvatarEntity>();

        public void ReceiveStreamData(byte[] bytes)
        {
            LatestStreamData = bytes;
            StreamDataRecorded?.Invoke(bytes);
        }

        /// <summary>Switches the preset (or user) avatar at runtime.</summary>
        public void SetAvatar(int presetIndex, ulong userId = 0)
        {
            localAvatarIndex = Mathf.Clamp(presetIndex, 0, 32);
            oculusUserId = userId;
            var entity = Entity;
            if (entity != null) entity.ReloadAvatarManually();
        }
    }
}
