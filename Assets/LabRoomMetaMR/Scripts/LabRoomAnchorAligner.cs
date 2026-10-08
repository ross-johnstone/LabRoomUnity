using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LabRoom.MetaMR
{
    /// <summary>
    /// Registers the reconstructed <c>LabRoom</c> to the physical lab with a Meta Spatial Anchor and keeps that
    /// registration between sessions.
    ///
    /// Workflow:
    ///  1. <see cref="BeginAlign"/> and move/rotate the room until the virtual lab lines up with the real one
    ///     (<see cref="LabRoomXRControls"/> maps this to the controllers).
    ///  2. <see cref="SaveAlignment"/> creates a spatial anchor, persists it on the headset, and stores the anchor UUID
    ///     plus the room pose relative to the anchor in PlayerPrefs. Any previous anchor is erased.
    ///  3. On the next launch <see cref="LoadSavedAlignment"/> (automatic when <see cref="loadOnStart"/> is on) loads the
    ///     anchor by UUID, localizes it, binds it to a new <see cref="OVRSpatialAnchor"/> and drives the room from it every
    ///     frame, so the room stays registered even after recentering or tracking corrections.
    ///
    /// Anchor and room poses are treated as upright (yaw only), which keeps the room level even if the anchor reports
    /// a slight tilt.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after OVRSpatialAnchor has updated its transform for the frame
    [DisallowMultipleComponent]
    public class LabRoomAnchorAligner : MonoBehaviour
    {
        public enum AlignState { Unaligned, Loading, Aligned, Aligning, Saving, Failed }

        [Serializable]
        class SavedAlignment
        {
            public int version = 1;
            public string uuid;
            public Vector3 roomPosition; // room position in the anchor's upright frame
            public float roomYaw;        // room yaw relative to the anchor's yaw (degrees)
        }

        [Tooltip("Root of the reconstructed room that gets registered to the real lab.")]
        public Transform roomRoot;

        [Tooltip("Load and apply the saved anchor automatically when the scene starts.")]
        public bool loadOnStart = true;

        [Tooltip("Seconds to wait for the headset to localize a saved anchor (0 = runtime default).")]
        public double localizeTimeoutSeconds = 15;

        [Tooltip("PlayerPrefs key that stores the anchor UUID and the room offset.")]
        public string prefsKey = "LabRoom.MetaMR.Alignment";

        public AlignState State { get; private set; } = AlignState.Unaligned;
        public string Status { get; private set; } = "Not aligned";
        public bool HasSavedAlignment => TryReadSaved(out _);
        public OVRSpatialAnchor Anchor => anchor;
        public event Action<LabRoomAnchorAligner> StateChanged;

        OVRSpatialAnchor anchor;            // anchor the room is currently attached to
        Vector3 roomPosInAnchor;
        float roomYawInAnchor;
        Vector3 posBeforeAlign;
        Quaternion rotBeforeAlign;
        AlignState stateBeforeAlign;

        bool Busy => State == AlignState.Loading || State == AlignState.Saving;

        void Reset()
        {
            var overlay = FindAnyObjectByType<LabRoomOverlay>();
            if (overlay != null) roomRoot = overlay.transform;
        }

        IEnumerator Start()
        {
            if (roomRoot == null) Reset();
            if (!loadOnStart || !TryReadSaved(out _))
            {
                SetState(AlignState.Unaligned, TryReadSaved(out _) ? "Saved alignment not loaded" : "Not aligned: align the room, then save");
                yield break;
            }
            // Give the XR session a moment to start before talking to the spatial-anchor runtime.
            float wait = 0f;
            while (!OVRPlugin.initialized && wait < 10f) { wait += Time.unscaledDeltaTime; yield return null; }
            yield return null;
            LoadSavedAlignment();
        }

        void LateUpdate()
        {
            if (State != AlignState.Aligned || anchor == null || roomRoot == null) return;
            if (!anchor.Localized) return; // keep the last good pose while tracking recovers
            ApplyFromAnchor();
        }

        // ---------------------------------------------------------------- manual alignment

        public void BeginAlign()
        {
            if (Busy || roomRoot == null || State == AlignState.Aligning) return;
            stateBeforeAlign = State;
            posBeforeAlign = roomRoot.position;
            rotBeforeAlign = roomRoot.rotation;
            SetState(AlignState.Aligning, "Aligning: grip to move, sticks to nudge, A to save");
        }

        public void CancelAlign()
        {
            if (State != AlignState.Aligning) return;
            roomRoot.SetPositionAndRotation(posBeforeAlign, rotBeforeAlign);
            var back = stateBeforeAlign == AlignState.Aligned && anchor != null ? AlignState.Aligned : AlignState.Unaligned;
            SetState(back, back == AlignState.Aligned ? "Aligned (changes discarded)" : "Not aligned (changes discarded)");
        }

        public bool IsAligning => State == AlignState.Aligning;

        /// <summary>Moves the room (only while aligning).</summary>
        public void TranslateRoom(Vector3 worldDelta)
        {
            if (!IsAligning) return;
            roomRoot.position += worldDelta;
        }

        /// <summary>Rotates the room about a vertical axis through <paramref name="pivot"/> (only while aligning).</summary>
        public void RotateRoom(Vector3 pivot, float yawDegrees)
        {
            if (!IsAligning) return;
            roomRoot.RotateAround(pivot, Vector3.up, yawDegrees);
            roomRoot.rotation = Upright(roomRoot.rotation);
        }

        /// <summary>Sets the room pose directly (only while aligning). The rotation is levelled.</summary>
        public void SetRoomPose(Vector3 position, Quaternion rotation)
        {
            if (!IsAligning) return;
            roomRoot.SetPositionAndRotation(position, Upright(rotation));
        }

        // ---------------------------------------------------------------- anchors

        /// <summary>Creates and saves a spatial anchor for the current room placement, replacing any previous one.</summary>
        public async void SaveAlignment()
        {
            if (Busy || roomRoot == null) return;
            SetState(AlignState.Saving, "Creating spatial anchor...");

            // Anchor at the room's floor centre (keeps it within a few metres of all content), upright.
            var anchorPos = RoomFloorCentre();
            var anchorRot = Upright(roomRoot.rotation);
            var roomPos = roomRoot.position;
            var roomRot = Upright(roomRoot.rotation);

            var go = new GameObject("LabRoom_SpatialAnchor");
            go.transform.SetPositionAndRotation(anchorPos, anchorRot);
            var newAnchor = go.AddComponent<OVRSpatialAnchor>();

            bool localized = await newAnchor.WhenLocalizedAsync();
            if (this == null) return;
            if (!localized || newAnchor == null)
            {
                if (go != null) Destroy(go);
                Fail("Could not create a spatial anchor (needs a Quest with spatial data enabled)");
                return;
            }

            SetState(AlignState.Saving, "Saving spatial anchor...");
            var saveResult = await newAnchor.SaveAnchorAsync();
            if (this == null) return;
            if (!saveResult.Success)
            {
                Destroy(go);
                Fail($"Saving the anchor failed ({saveResult.Status})");
                return;
            }

            // Room pose in the anchor's upright frame, from the pose the anchor was requested at.
            var inv = Quaternion.Inverse(anchorRot);
            var saved = new SavedAlignment
            {
                uuid = newAnchor.Uuid.ToString(),
                roomPosition = inv * (roomPos - anchorPos),
                roomYaw = Mathf.DeltaAngle(anchorRot.eulerAngles.y, roomRot.eulerAngles.y),
            };

            TryReadSaved(out var previous);
            var previousAnchor = anchor;

            PlayerPrefs.SetString(prefsKey, JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
            UseAnchor(newAnchor, saved);
            SetState(AlignState.Aligned, "Alignment saved to spatial anchor");

            // Erase the anchor this one replaces so stale anchors don't pile up on the headset.
            if (previousAnchor != null && previousAnchor != newAnchor)
            {
                var erase = await previousAnchor.EraseAnchorAsync();
                if (!erase.Success) Debug.LogWarning($"[LabRoom MR] Could not erase previous anchor: {erase.Status}");
                if (previousAnchor != null) Destroy(previousAnchor.gameObject);
            }
            else if (previous != null && Guid.TryParse(previous.uuid, out var oldUuid) && oldUuid != newAnchor.Uuid)
            {
                var erase = await OVRSpatialAnchor.EraseAnchorsAsync(Array.Empty<OVRSpatialAnchor>(), new[] { oldUuid });
                if (!erase.Success) Debug.LogWarning($"[LabRoom MR] Could not erase previous anchor {oldUuid}: {erase.Status}");
            }
        }

        /// <summary>Loads the saved anchor, localizes it and attaches the room to it.</summary>
        public async void LoadSavedAlignment()
        {
            if (Busy || roomRoot == null) return;
            if (State == AlignState.Aligning) CancelAlign();
            if (!TryReadSaved(out var saved) || !Guid.TryParse(saved.uuid, out var uuid))
            {
                SetState(AlignState.Unaligned, "No saved alignment yet");
                return;
            }

            // Already attached to this anchor: just re-apply.
            if (anchor != null && anchor.Uuid == uuid)
            {
                UseAnchor(anchor, saved);
                SetState(AlignState.Aligned, "Aligned from spatial anchor");
                return;
            }

            SetState(AlignState.Loading, "Loading spatial anchor...");
            var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
            var load = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
            if (this == null) return;
            if (!load.Success || unbound.Count == 0)
            {
                Fail(load.Success ? "Saved anchor not found on this headset" : $"Loading the anchor failed ({load.Status})");
                return;
            }

            var unboundAnchor = unbound[0];
            if (!unboundAnchor.Localized)
            {
                SetState(AlignState.Loading, "Localizing spatial anchor (look around the lab)...");
                bool ok = await unboundAnchor.LocalizeAsync(localizeTimeoutSeconds);
                if (this == null) return;
                if (!ok)
                {
                    Fail("Anchor not localized: look around the lab and press Menu to retry");
                    return;
                }
            }

            if (anchor != null) Destroy(anchor.gameObject);
            var go = new GameObject("LabRoom_SpatialAnchor");
            var bound = go.AddComponent<OVRSpatialAnchor>();
            unboundAnchor.BindTo(bound); // must happen in the same frame as AddComponent (before Start)
            UseAnchor(bound, saved);
            SetState(AlignState.Aligned, "Aligned from spatial anchor");
        }

        /// <summary>Erases the saved anchor from the headset and forgets the alignment. The room stays where it is.</summary>
        public async void ClearAlignment()
        {
            if (Busy) return;
            if (State == AlignState.Aligning) CancelAlign();
            TryReadSaved(out var saved);
            var current = anchor;
            anchor = null;
            PlayerPrefs.DeleteKey(prefsKey);
            PlayerPrefs.Save();
            SetState(AlignState.Unaligned, "Alignment cleared");

            if (current != null)
            {
                var erase = await current.EraseAnchorAsync();
                if (!erase.Success) Debug.LogWarning($"[LabRoom MR] Could not erase anchor: {erase.Status}");
                if (current != null) Destroy(current.gameObject);
            }
            else if (saved != null && Guid.TryParse(saved.uuid, out var uuid))
            {
                var erase = await OVRSpatialAnchor.EraseAnchorsAsync(Array.Empty<OVRSpatialAnchor>(), new[] { uuid });
                if (!erase.Success) Debug.LogWarning($"[LabRoom MR] Could not erase anchor {uuid}: {erase.Status}");
            }
        }

        // ---------------------------------------------------------------- helpers

        void UseAnchor(OVRSpatialAnchor a, SavedAlignment saved)
        {
            anchor = a;
            roomPosInAnchor = saved.roomPosition;
            roomYawInAnchor = saved.roomYaw;
            if (a != null && a.Localized) ApplyFromAnchor();
        }

        void ApplyFromAnchor()
        {
            var t = anchor.transform;
            var anchorRot = Upright(t.rotation);
            var pos = t.position + anchorRot * roomPosInAnchor;
            var rot = anchorRot * Quaternion.Euler(0f, roomYawInAnchor, 0f);
            roomRoot.SetPositionAndRotation(pos, rot);
        }

        Vector3 RoomFloorCentre()
        {
            var renderers = roomRoot.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return roomRoot.position;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, roomRoot.position.y, b.center.z);
        }

        static Quaternion Upright(Quaternion q) => Quaternion.Euler(0f, q.eulerAngles.y, 0f);

        bool TryReadSaved(out SavedAlignment saved)
        {
            saved = null;
            var json = PlayerPrefs.GetString(prefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return false;
            try { saved = JsonUtility.FromJson<SavedAlignment>(json); }
            catch (Exception) { saved = null; }
            return saved != null && !string.IsNullOrEmpty(saved.uuid);
        }

        void Fail(string message)
        {
            Debug.LogWarning("[LabRoom MR] " + message);
            SetState(AlignState.Failed, message);
        }

        void SetState(AlignState state, string status)
        {
            State = state;
            Status = status;
            StateChanged?.Invoke(this);
        }
    }
}
