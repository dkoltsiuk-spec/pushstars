using System;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// One-click setup for the phase-08 CV test: creates a "CVTest" GameObject with
    /// MediaPipePoseSource + PushupSession + PushupDebugHud and wires their references, so you don't
    /// have to hunt through the plugin's many components in Add Component. Components are resolved by
    /// reflection so this editor script compiles whether or not PUSHSTARS_MEDIAPIPE is set.
    /// Menu: Tools → Push Stars → CV → Build CV Test object.
    /// </summary>
    public static class CvTestSceneSetup
    {
        [MenuItem("Tools/Push Stars/CV/Build CV Test object (camera)", priority = 310)]
        public static void Build()
        {
            var go = CreateCvTestObject();
            if (go == null)
            {
                EditorUtility.DisplayDialog("Push Stars — CV Test",
                    "MediaPipePoseSource not found.\n\nEnable the plugin first:\n" +
                    "Tools → Push Stars → MediaPipe → Enable, wait for recompile, then run this again.",
                    "OK");
                return;
            }
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            Debug.Log("[CVTest] Built 'CVTest' with MediaPipePoseSource + PushupSession + PushupDebugHud. Press Play.");
        }

        /// <summary>One-click scene cleanup for CV testing. Running "Build CV Test object" several
        /// times used to STACK CVTest copies (three of them were found live in testCV.unity — every
        /// HUD drew on top of the others), and the avatar-overlay stand shares the same scene. This
        /// removes the duplicates and deactivates (not deletes) the avatar-stand objects, then
        /// builds one fresh, fully-wired CVTest.</summary>
        [MenuItem("Tools/Push Stars/CV/Reset CV Test scene (fix duplicates + hide avatar stand)", priority = 311)]
        public static void ResetScene()
        {
            RemoveExistingCvTestObjects();

            // The avatar stand belongs to its own experiment — hide it from the CV test instead of
            // destroying the user's work. Reactivate manually (or via its own build tool) any time.
            string[] avatarStandNames =
                { "AvatarStage", "AvatarStageCamera", "DisplayClearCamera", "Ground", "Ch36_Body (URP)" };
            foreach (var name in avatarStandNames)
            {
                var obj = GameObject.Find(name); // finds active only
                if (obj != null)
                {
                    obj.SetActive(false);
                    Debug.Log($"[CVTest] Deactivated avatar-stand object '{name}'.");
                }
            }

            Build();
        }

        private static void RemoveExistingCvTestObjects()
        {
            int removed = 0;
            // Resources.FindObjectsOfTypeAll also finds inactive scene objects.
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || go.name != "CVTest") continue;
                if (!go.scene.IsValid()) continue; // skip prefab assets
                UnityEngine.Object.DestroyImmediate(go);
                removed++;
            }
            if (removed > 0)
                Debug.Log($"[CVTest] Removed {removed} existing CVTest object(s).");
        }

        /// <summary>Creates and fully wires a "CVTest" GameObject in the active scene. Returns null if
        /// the MediaPipe adapter isn't compiled (PUSHSTARS_MEDIAPIPE off). Used by the menu and by the
        /// CI build script (so the built scene never depends on stale serialized references).</summary>
        public static GameObject CreateCvTestObject()
        {
            var poseType    = Type.GetType("PushStars.CV.MediaPipePoseSource, PushStars.CV.MediaPipe");
            var sessionType = Type.GetType("PushStars.CV.PushupSession, PushStars.CV");
            var hudType     = Type.GetType("PushStars.CV.PushupDebugHud, PushStars.CV");
            if (poseType == null || sessionType == null || hudType == null)
            {
                Debug.LogError("[CVTest] Required types not found (MediaPipe define off or PushStars.CV not compiled).");
                return null;
            }

            // Idempotency: never stack a second CVTest — duplicate HUDs draw over each other and
            // duplicate sessions fight for the camera.
            RemoveExistingCvTestObjects();

            var previewType = Type.GetType("PushStars.CV.WebCamPreview, PushStars.CV.MediaPipe");

            var go = new GameObject("CVTest");

            var pose    = go.AddComponent(poseType);
            var session = go.AddComponent(sessionType);
            var preview = previewType != null ? go.AddComponent(previewType) : null;
            var hud     = go.AddComponent(hudType);

            // Wire WebCamPreview._source = the MediaPipePoseSource (full-screen camera feed behind HUD).
            if (preview != null)
            {
                var pSo = new SerializedObject(preview);
                pSo.FindProperty("_source").objectReferenceValue = pose;
                pSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // Wire PushupSession._poseSourceBehaviour = the MediaPipePoseSource, and _logReps = true.
            var sSo = new SerializedObject(session);
            sSo.FindProperty("_poseSourceBehaviour").objectReferenceValue = pose;
            var logReps = sSo.FindProperty("_logReps");
            if (logReps != null) logReps.boolValue = true;
            sSo.ApplyModifiedPropertiesWithoutUndo();

            // Wire PushupDebugHud._session = the PushupSession.
            var hSo = new SerializedObject(hud);
            hSo.FindProperty("_session").objectReferenceValue = session;
            hSo.ApplyModifiedPropertiesWithoutUndo();

            return go;
        }
    }
}
