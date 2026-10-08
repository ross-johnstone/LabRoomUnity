Lab Room overlay - Unity 6.3 (6000.3.7f1), Built-in render pipeline

First open: a setup script re-assembles the detailed scan from ImportParts/, copies the
8K scan texture from the 'lab 3d model' folder, imports everything and builds
Assets/Scenes/LabRoom_Overlay.unity (takes a minute or two the first time).

Scene: LabRoom > Scan_Detailed (textured, unlit) and LabRoom > Blockout (white, 50% opacity).
Both share one coordinate frame in metres; floor at y = 0.
Select 'LabRoom' to toggle scan/blockout and change blockout opacity in the Inspector.
Play mode: 1 scan only, 2 blockout only, 3 both, H hide panel; hold right mouse + WASD to fly, Q/E down/up.
Rebuild the scene any time: menu Lab Room > Rebuild Overlay Scene.
