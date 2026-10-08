# Lab Room MR

A Unity 6 reconstruction of our lab, viewable on desktop and in mixed reality on Meta Quest.

The room exists in three overlaid representations that share one coordinate frame (metres, floor at y = 0):

| Representation | What it is |
| --- | --- |
| **Scan_Detailed** | Photogrammetry scan of the real room (8K texture, unlit) |
| **Blockout** | Simple white geometry of walls, furniture and windows (adjustable opacity) |
| **Hybrid** | Photoreal rebuild: procedural furniture, scan-derived textures, baked lighting |

The Quest scene adds:

- **Passthrough windows.** You see the real world only through the room's window panes.
- **Spatial-anchor alignment.** Line the virtual room up with the real lab once; it stays aligned on every launch.
- **Meta Avatar.** Driven by your headset, controllers and hand tracking.
- **Baked lighting** for Quest frame rates.

## Requirements

- **Unity 6000.3.7f1** with **Android Build Support** (including *OpenJDK* and *Android SDK & NDK Tools*). Unity Hub offers to install this exact version when you add the project.
- An internet connection on first open. Unity's Package Manager downloads Meta XR SDK 85, Meta Avatars SDK 40.0.1, OpenXR and the Input System from Unity's registry.
- To run on device: a Meta Quest 2, Pro, 3 or 3S in developer mode.

## Getting started

1. Clone the repository:
   ```
   git clone https://github.com/<your-account>/lab-room-mr.git
   ```
2. In Unity Hub: **Add > Add project from disk**, then select the cloned folder. Setting the platform to **Android** in Hub before opening saves a second import.
3. The first open takes a while:
   - Packages download and everything imports.
   - The Meta Avatars SDK unpacks and re-packages its preset avatars (about 1 GB in `Assets/Oculus/Avatar2_SampleAssets`). That folder is not in git because it is over GitHub's 100 MB file limit, and the SDK recreates it automatically.
   - Wait until all progress bars have finished.
4. Open a scene:
   - `Assets/Scenes/LabRoom_MetaMR.unity` is the Quest mixed-reality scene (the first build scene).
   - `Assets/Scenes/LabRoom_Overlay.unity` is the original desktop viewer.

## Building for Quest

Use the **Lab Room > Meta MR** menu:

1. **4. Switch Build Target to Android (Quest)**.
2. **3. Validate** runs about 100 checks and should end in `RESULT: PASS`. The report is written to `Logs/LabRoomMetaMR_Validation.txt`. If a project-setting check fails on a fresh machine, run **1. Configure Project for Quest** and validate again.
3. **5. Build Quest APK** writes `Builds/LabRoomMetaMR.apk`.
4. Install the APK with Meta Quest Developer Hub, or run `adb install -r Builds/LabRoomMetaMR.apk`.

Other tools in the same menu:

- **6. Bake Lighting (MR scene only)** and **7. Apply Quest Quality Settings**.
- **Test Avatar Tracking (Play Mode)** checks in the editor that the avatar's head follows the headset.
- **Open MR Scene**.

Avoid **2. Create or Rebuild MR Scene** unless you mean it. It recreates `LabRoom_MetaMR.unity` from the overlay scene, so the bake and any manual changes are lost.

## Controls

### On Quest (LabRoom_MetaMR)

| Input | Action |
| --- | --- |
| X | Next room view (scan / blockout / scan+blockout / hybrid / hybrid+scan) |
| Y | Turn passthrough through the windows on or off |
| Left stick up/down | Blockout opacity |
| Left stick click | Show or hide the wrist status panel |
| B | Align mode |
| Menu (left) | Reload the saved alignment |

In align mode:

- Hold the right grip to move or turn the room.
- The right stick turns the room or changes its height; the left stick slides it.
- Hold the right trigger for fine moves.
- **A** saves a spatial anchor, and the alignment loads automatically on every later launch.
- **B** cancels.
- Holding the left stick click for 2 s erases the saved alignment.

The wrist panel also shows a live avatar-tracking check: how often the avatar reads input, each hand's source, and how far the avatar's head and wrists are from your real ones.

### Desktop (LabRoom_Overlay, play mode)

| Input | Action |
| --- | --- |
| 1 / 2 / 3 | Scan only / blockout only / both |
| 4 / 5 | Hybrid only / hybrid + scan |
| H | Hide the panel |
| Hold right mouse + WASD | Fly (Q/E: down/up) |

## Repository layout

| Path | Contents |
| --- | --- |
| `Assets/Scenes/` | `LabRoom_Overlay` (original), `LabRoom_MetaMR` (Quest) and its baked lightmaps |
| `Assets/Models/` | Scan, blockout and hybrid models and textures |
| `Assets/LabRoomMetaMR/` | Quest MR scripts, shaders, editor tooling and `README_MetaMR.txt` (full technical notes) |
| `Assets/Editor`, `Assets/Scripts` | Original overlay setup, fly camera and view toggles |
| `ImportParts/` | The scan FBX split into two parts, used by the original setup script if the FBX is missing |
| `_Backup_BeforeMetaMR/` | Pre-integration backups plus the original scene's SHA-256, which Validate uses to prove the original scene is untouched |

Not in git (see `.gitignore`):

- `Library/`, `Temp/`, `Logs/`, `UserSettings/` and `Builds/`. Unity regenerates these.
- The Meta preset-avatar folder described above.

`.gitattributes` turns off line-ending conversion so files check out byte-for-byte on Windows too.

## Credits

Third-party models in the Hybrid room (see `Assets/Models/Hybrid/CREDITS.txt`):

- Office chair and aloe plant: Sara Vieira via pmndrs/market (CC0).
- Glass vase with flowers and water bottle: Khronos glTF Sample Assets (CC0).
- Potted plants: Khronos glTF Sample Assets, *Diffuse Transmission Plant* (**CC BY 4.0**, credit required).

The Meta XR and Meta Avatars SDKs are not part of this repository. Unity's Package Manager downloads them under Meta's own licence terms.
