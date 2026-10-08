// Play-mode check that the user's tracking reaches the local Meta Avatar in LabRoom_MetaMR.
//
// Menu: Lab Room > Meta MR > Test Avatar Tracking (Play Mode)   (automation command: avatar-playtest)
// Enters play mode in the MR scene, waits for the preset avatar to load, then puts the headset at two different
// poses through OVRManager's head-pose emulation (the same path the editor uses without a headset; the rig's
// CenterEyeAnchor is also set directly in case OVRManager is not running) and checks that the avatar's head joint
// follows the CenterEyeAnchor. With a headset on Link the live head pose is measured instead. Hands cannot be
// exercised without a headset; their source and offset are shown on the wrist HUD on device.
// Results: Logs/LabRoomMetaMR_AvatarPlayTest.txt. Play mode is left automatically; the scene is not modified.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Oculus.Avatar2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LabRoom.MetaMR.EditorTools
{
    [InitializeOnLoad]
    public static class LabRoomMetaMRAvatarPlayTest
    {
        const string ActiveKey = "LabRoomMetaMR.AvatarPlayTest.Active";
        const double LoadTimeout = 120.0;
        const double SettleSeconds = 2.5;
        const float MaxHeadError = 0.25f; // avatar head joint vs CenterEyeAnchor (the joint sits behind the eyes)

        static readonly (Vector3 position, float yaw)[] Poses =
        {
            (new Vector3(0.45f, 1.55f, 0.35f), 60f),
            (new Vector3(-0.50f, 1.25f, -0.40f), -45f),
        };

        static string ResultFile => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "LabRoomMetaMR_AvatarPlayTest.txt");

        static int phase;
        static double phaseStart, testStart, lastProgress;
        static int poseIndex;
        static bool liveHeadset;
        static int errorCount;
        static readonly StringBuilder report = new StringBuilder();
        static readonly Vector3[] headJoint = new Vector3[2];
        static readonly Vector3[] headAnchor = new Vector3[2];
        static readonly float[] headError = new float[2];

        static LabRoomMetaMRAvatarPlayTest()
        {
            // Entering play mode reloads the script domain; pick the test up again on the other side.
            if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlayingOrWillChangePlaymode)
                Subscribe();
            else if (SessionState.GetBool(ActiveKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                SessionState.EraseBool(ActiveKey); // left over from an interrupted run
        }

        [MenuItem("Lab Room/Meta MR/Test Avatar Tracking (Play Mode)", priority = 41)]
        public static void BeginMenu() => LabRoomMetaMRSetup.Run("Avatar tracking play-mode test", Begin);

        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave play mode first.");
            if (SceneManager.GetActiveScene().path != LabRoomMetaMRSetup.MRScenePath)
            {
                if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                EditorSceneManager.OpenScene(LabRoomMetaMRSetup.MRScenePath, OpenSceneMode.Single);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ResultFile));
            File.WriteAllText(ResultFile, $"Avatar tracking play-mode test started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            SessionState.SetBool(ActiveKey, true);
            Subscribe();
            EditorApplication.isPlaying = true;
            LabRoomMetaMRSetup.Log("Entering play mode for the avatar tracking test; results in " + ResultFile);
        }

        static void Subscribe()
        {
            phase = 0;
            poseIndex = 0;
            errorCount = 0;
            testStart = phaseStart = lastProgress = EditorApplication.timeSinceStartup;
            report.Clear();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errorCount++;
            if (errorCount <= 20) report.AppendLine($"  console {type}: {condition.Split('\n')[0]}");
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.timeSinceStartup - testStart > 30.0 && phase == 0) Finish(false, "play mode did not start");
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            var input = UnityEngine.Object.FindAnyObjectByType<LabAvatarInputManager>();
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
            var entity = input != null ? input.GetComponent<OvrAvatarEntity>() : null;
            if (input == null || rig == null || entity == null)
            {
                if (now - testStart > 20.0) Finish(false, $"scene objects missing (input manager {input != null}, rig {rig != null}, entity {entity != null})");
                return;
            }

            switch (phase)
            {
                case 0: // wait for the avatar skeleton
                    if (now - lastProgress > 15.0)
                    {
                        lastProgress = now;
                        report.AppendLine($"  {now - testStart:0}s: entity created {entity.IsCreated}, state {entity.CurrentState}, " +
                                          $"Unity joints {entity.SkeletonJointCount}, SDK input reads {input.InputReads}, OvrAvatarManager initialized {OvrAvatarManager.initialized}");
                    }
                    if (input.JointError(CAPI.ovrAvatar2JointType.Head, rig.centerEyeAnchor.position) >= 0f)
                    {
                        liveHeadset = OVRNodeStateProperties.IsHmdPresent();
                        report.AppendLine($"Avatar loaded after {now - testStart:0.0}s, state {entity.CurrentState}, input manager in use: " +
                                          (entity.InputManager != null ? entity.InputManager.GetType().Name : "none") +
                                          $", hand tracking service {(input.HandTrackingServiceAvailable ? "available" : "unavailable (expected without a headset)")}" +
                                          $", headset {(liveHeadset ? "connected (live pose measured)" : "not connected (emulated head poses)")}");
                        phase = 1;
                        phaseStart = now;
                        ApplyPose(rig, 0);
                    }
                    else if (now - testStart > LoadTimeout)
                        Finish(false, $"avatar head joint not available within {LoadTimeout}s (state {entity.CurrentState}, created {entity.IsCreated}, " +
                                      $"critical joints {string.Join(", ", entity.GetCriticalJoints())}, SDK input reads {input.InputReads})");
                    break;

                case 1: // hold a pose, then measure
                    ApplyPose(rig, poseIndex);
                    if (now - phaseStart < SettleSeconds) break;
                    headAnchor[poseIndex] = rig.centerEyeAnchor.position;
                    headError[poseIndex] = input.JointError(CAPI.ovrAvatar2JointType.Head, headAnchor[poseIndex]);
                    var headTx = entity.GetSkeletonTransform(CAPI.ovrAvatar2JointType.Head);
                    headJoint[poseIndex] = headTx != null ? headTx.position : Vector3.zero;
                    report.AppendLine($"Pose {poseIndex + 1}: CenterEyeAnchor {headAnchor[poseIndex]:F2}, avatar head {headJoint[poseIndex]:F2}, " +
                                      $"offset {headError[poseIndex] * 100f:0} cm, SDK input reads so far {input.InputReads} ({input.InputReadsPerSecond:0}/s)");
                    poseIndex++;
                    phaseStart = now;
                    if (poseIndex >= Poses.Length) Evaluate(input);
                    break;
            }
        }

        static void ApplyPose(OVRCameraRig rig, int index)
        {
            if (liveHeadset) return;
            var (position, yaw) = Poses[index];
            var manager = OVRManager.instance;
            if (manager != null)
            {
                manager.headPoseRelativeOffsetTranslation = position;
                manager.headPoseRelativeOffsetRotation = new Vector3(0f, -yaw, 0f); // OVRCameraRig negates x/y
            }
            rig.centerEyeAnchor.localPosition = position;
            rig.centerEyeAnchor.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        static void Evaluate(LabAvatarInputManager input)
        {
            bool read = input.InputReads > 0;
            bool close = headError.All(e => e >= 0f && e <= MaxHeadError);
            float anchorMove = Vector3.Distance(headAnchor[0], headAnchor[1]);
            float jointMove = Vector3.Distance(headJoint[0], headJoint[1]);
            bool follows = liveHeadset || (anchorMove > 0.5f && jointMove > 0.6f * anchorMove);
            report.AppendLine($"[{(read ? "PASS" : "FAIL")}] Avatars SDK reads LabAvatarInputManager's tracking input ({input.InputReads} reads)");
            report.AppendLine($"[{(close ? "PASS" : "FAIL")}] Avatar head joint within {MaxHeadError * 100f:0} cm of the tracked head at every pose");
            report.AppendLine($"[{(follows ? "PASS" : "FAIL")}] Avatar head follows the headset when it moves (headset moved {anchorMove:0.00} m, avatar head {jointMove:0.00} m)");
            Finish(read && close && follows, null);
        }

        static void Finish(bool pass, string failure)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SessionState.EraseBool(ActiveKey);
            if (failure != null) report.AppendLine("[FAIL] " + failure);
            report.AppendLine($"Console errors during the test: {errorCount}");
            report.AppendLine($"RESULT: {(pass ? "PASS" : "FAIL")}");
            File.AppendAllText(ResultFile, report.ToString());
            LabRoomMetaMRSetup.Log("Avatar tracking play-mode test" + Environment.NewLine + report);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }
    }
}
