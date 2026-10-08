LAB ROOM - META QUEST MIXED REALITY
===================================

Scene: Assets/Scenes/LabRoom_MetaMR.unity (a copy of LabRoom_Overlay.unity; the original is never modified).
Menu:  Lab Room > Meta MR  (1 Configure Project, 2 Create or Rebuild MR Scene, 3 Validate, 4 Switch to Android,
       5 Build APK, 6 Bake Lighting, 7 Apply Quest Quality Settings)

Packages: Meta XR All-in-One SDK 85.0.0 (core, interaction, MRUK, platform, audio, voice, haptics),
Meta Avatars SDK 40.0.1 (+ sample preset avatars), Unity OpenXR 1.16.1 with the Meta XR feature, Input System 1.18.0.
Render pipeline: Built-in (unchanged; the URP 17.3 package is installed but inactive, only because the Meta Avatars
shaders #include URP files and otherwise fail Quest builds). Colour space: Linear (switched from Gamma: the Meta XR SDK refuses OpenXR Quest builds in Gamma).

WHAT IS IN THE MR SCENE
  LabRoom (unchanged: Scan_Detailed, Blockout, Hybrid, LabRoomOverlay toggles/opacity)
    MR_PassthroughWindows        LabRoomPassthroughWindows + one portal quad per window pane
  OVRCameraRig                   Meta rig (floor-level tracking, passthrough enabled), placed where the fly camera was
    TrackingSpace/LocalAvatar    Meta AvatarEntity + LabLocalAvatarBehaviour + LabAvatarInputManager
                                 (your head, controllers and tracked hands drive the avatar - see below)
    CenterEyeAnchor/MR_PassthroughAlphaGuard
  MR_Passthrough                 OVRPassthroughLayer (underlay, reconstructed projection)
  AvatarSDK                      Meta Avatars managers (OvrAvatarManager with preset zips, LOD, GPU skinning, Style-2
                                 shaders); its legacy EntityInputManager ("Standalone" mode, deprecated in Avatars 40)
                                 is disabled in favour of LabAvatarInputManager
  MR_Systems                     LabRoomAnchorAligner, LabRoomXRControls, LabRoomMRHud
  Main Camera                    original fly camera, disabled (kept for reference)

BAKED LIGHTING (Quest performance)
  Lab Room > Meta MR > 6. Bake Lighting bakes the Hybrid's 14 lights (12 per-pixel point lights, sun, lamp; several
  with realtime shadows) into lightmaps for LabRoom_MetaMR only. Realtime, those lights cost one extra draw of every
  object per light plus cube shadow maps - the cause of single-digit frame rates on Quest. After baking:
  - lights are "Baked" (no runtime cost); opaque Hybrid meshes are lightmapped (Contribute GI) but NOT batching-static,
    because LabRoom is moved by the spatial-anchor alignment; glass/bottles stay dynamic;
  - settings live in Assets/LabRoomMetaMR/Lighting/LabRoom_MetaMR_Lighting.lighting; output in Assets/Scenes/LabRoom_MetaMR/;
  - the original LabRoom_Overlay scene keeps its realtime lights. Re-run the bake after "Create or Rebuild MR Scene".
  7. Apply Quest Quality Settings sets the Android quality level: 4x MSAA, no realtime shadows, full-res textures.
  Avatars and other moving objects get ambient lighting only (no realtime lights remain).

PASSTHROUGH ONLY THROUGH THE WINDOWS
  The passthrough layer is an underlay; the eye buffer's alpha decides where the real world shows.
  - Portal meshes use Materials/PassthroughWindow.mat (shader "LabRoom/MR/Passthrough Window"). They draw after the
    opaque room, are depth-tested (mullions, furniture and hands in front still occlude them), clear colour/alpha to 0
    and set a stencil bit.
  - MR_PassthroughAlphaGuard (shader "LabRoom/MR/Passthrough Alpha Guard") runs after all transparent objects and sets
    alpha back to 1 everywhere the stencil bit is not set, so faded blockout/glass can't leak passthrough elsewhere.
  To change the windows: move/scale the portal quads, or add any mesh with PassthroughWindow.mat under
  MR_PassthroughWindows and use the component's "Collect Window Renderers From Children" context menu. The material's
  mask texture (red channel, white = passthrough) can feather or shape the opening; _Strength fades it.

SPATIAL ANCHOR ALIGNMENT (persists between sessions)
  First time in the lab:
    1. Press B (right controller) to enter align mode.
    2. Hold the right grip and move/turn the room until it matches the real lab (it stays level).
       Right stick: turn around you / raise-lower. Left stick: slide. Hold right trigger for fine moves.
    3. Press A: a spatial anchor is created at the room's floor centre, saved on the headset, and its UUID plus the
       room's offset from it are stored in PlayerPrefs (key LabRoom.MetaMR.Alignment).
  Every later launch: the anchor is loaded, localized and the room is attached to it automatically.
  Menu (left) reloads it; in align mode hold the left stick click for 2 s to erase it; B in align mode cancels.

OTHER CONTROLS
  X: next room view (scan / blockout / scan+blockout / hybrid / hybrid+scan)   Y: windows passthrough on/off
  Left stick up/down: blockout opacity   Left stick click: show/hide the wrist HUD
  Keyboard 1-5 / H from LabRoomOverlay and the FlyCamera still work in the editor (now through the Input System:
  active input handling is "Input System Package" because Unity 6.3 refuses Android builds with "Both").

AVATAR TRACKING (your head and hands drive the avatar)
  LabAvatarInputManager feeds the local avatar every frame:
  - head and controllers: the OVRCameraRig CenterEye/LeftHand/RightHand anchors (exactly what you see through),
    sampled on the main thread and passed to the Avatars SDK as poses relative to the avatar;
  - hand tracking: the Avatars SDK hand-tracking service gives wrist pose and finger articulation. If the headset
    tracks a hand but that service reports nothing for it, the hand anchor is sent as a controller pose ("hand*")
    so the arm still follows;
  - controller buttons/triggers curl the avatar's fingers (Meta building-block InputControlDelegate).
  The avatar is in first-person view: its head is hidden so it does not block your eyes; you see its body and hands.
  The wrist HUD shows a live self-check, e.g.  "Avatar: UserAvatar  input 72/s"  and  "head 9cm  L hand 3cm  R ctrl 6cm":
  avatar state, how often the SDK reads the input, each hand's source (ctrl / hand / hand* / -) and how far the
  avatar's head and wrists are from the tracked poses. "input 0/s" or large offsets mean the avatar is not following.
  Logcat lines tagged [LabAvatar] report the input sources. Editor check: Lab Room > Meta MR > Test Avatar Tracking
  (Play Mode) moves an emulated headset and verifies the avatar head follows (Logs/LabRoomMetaMR_AvatarPlayTest.txt).

AVATARS AND NETWORKING
  LocalAvatar loads Meta preset avatar #0 (change "Local Avatar Index" on LabLocalAvatarBehaviour) and is driven by
  LabAvatarInputManager. LabLocalAvatarBehaviour.StreamDataRecorded publishes the compressed avatar pose stream;
  send it over your network layer and feed it to LabRemoteAvatarBehaviour.ApplyStreamData on an instance of
  Prefabs/LabRemoteAvatar.prefab (call Configure(userId, presetIndex) right after instantiating). For co-located users,
  shared spatial anchors are already enabled in the Meta project config.

DIAGNOSTICS
  Logs/LabRoomMetaMR.log (setup steps), Logs/LabRoomMetaMR_Validation.txt (checklist),
  Logs/LabRoomMetaMR_Compile.log (compiler/console errors). Editor/Diagnostics also runs commands written to
  Library/LabRoomMetaMR.command (configure, build-scene, validate, android, apk, avatar-input, avatar-playtest,
  refresh) for automation.
