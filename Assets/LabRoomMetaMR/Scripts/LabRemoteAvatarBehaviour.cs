using Meta.XR.MultiplayerBlocks.Shared;
using UnityEngine;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// <see cref="IAvatarBehaviour"/> for an avatar that is driven by another user's pose stream.
    ///
    /// Use the LabRemoteAvatar prefab (Assets/LabRoomMetaMR/Prefabs): instantiate it when a peer joins, call
    /// <see cref="Configure"/> in the same frame (before its Start runs), then pass every pose frame received from
    /// the network to <see cref="ApplyStreamData"/>. The frames are the byte arrays published by
    /// <see cref="LabLocalAvatarBehaviour.StreamDataRecorded"/> on the sender. Parent the instance to the shared
    /// <c>LabRoom</c> (or another common reference such as a shared spatial anchor) so both users agree on space.
    /// </summary>
    [DisallowMultipleComponent]
    public class LabRemoteAvatarBehaviour : MonoBehaviour, IAvatarBehaviour
    {
        [Tooltip("Meta user id of the remote user (0 = preset avatar).")]
        [SerializeField] ulong oculusUserId;

        [Tooltip("Preset avatar index used when the remote user has no Meta avatar.")]
        [SerializeField, Range(0, 32)] int localAvatarIndex = 1;

        public ulong OculusId => oculusUserId;
        public int LocalAvatarIndex => localAvatarIndex;
        public bool HasInputAuthority => false;

        public AvatarEntity Entity => GetComponent<AvatarEntity>();

        /// <summary>Sets who this avatar represents. Call right after instantiating, before the first frame.</summary>
        public void Configure(ulong userId, int presetIndex)
        {
            oculusUserId = userId;
            localAvatarIndex = Mathf.Clamp(presetIndex, 0, 32);
        }

        /// <summary>Applies a pose frame received from the remote user.</summary>
        public void ApplyStreamData(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            var entity = Entity;
            if (entity != null) entity.SetStreamData(bytes);
        }

        /// <summary>Remote avatars never record their own stream.</summary>
        public void ReceiveStreamData(byte[] bytes) { }
    }
}
