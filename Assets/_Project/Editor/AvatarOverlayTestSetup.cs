using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using PushStars.CV;

namespace PushStars.Editor
{
    /// <summary>
    /// One-click stand for the avatar-overlay experiment (variant A: canned push-up clip scrubbed
    /// by the CV depth signal). Builds on top of <see cref="CvTestSceneSetup"/>:
    ///
    /// 1. Configures the Mixamo FBX imports (Humanoid rig, friendly clip names, loop on idles).
    /// 2. (Re)creates the AnimatorController with the three states the driver plays by name.
    /// 3. Builds the CVTest object (MediaPipe source + session + HUD), an off-screen AvatarStage
    ///    (character + camera + light), and wires <see cref="PushupAvatarDriver"/> +
    ///    <see cref="AvatarStagePreview"/>.
    ///
    /// Menu: Tools → Push Stars → CV → Build Avatar Overlay Test.
    /// </summary>
    public static class AvatarOverlayTestSetup
    {
        private const string MixamoDir = "Assets/_Project/Art/Characters/Mixamo";
        private const string PushupFbx = MixamoDir + "/Ch36_nonPBR@Push Up.fbx";
        private const string IdleFbx   = MixamoDir + "/Ch36_nonPBR@Warrior Idle.fbx";
        private const string RestFbx   = MixamoDir + "/Ch36_nonPBR@Sitting Idle.fbx";
        private const string ControllerPath = MixamoDir + "/AvatarOverlayTest.controller";

        private const string PushupState = "PushUp";
        private const string IdleState   = "WarriorIdle";
        private const string RestState   = "SittingIdle";

        /// <summary>Variant B (owner's pick after seeing variant A): NO canned animation — the
        /// character live-mirrors the user's limbs from the world landmarks. Same stand, but the
        /// Animator gets no controller (rest pose) and <see cref="PoseMirrorRetargeter"/> +
        /// an always-following <see cref="AvatarMirrorAnchor"/> drive the character.</summary>
        [MenuItem("Tools/Push Stars/CV/Build Avatar Mirror Test (retarget, no animation)", priority = 312)]
        public static void BuildRetarget()
        {
            AssetDatabase.Refresh();

            // Only the model itself is needed (Humanoid rig for GetBoneTransform) — no clips.
            if (!ConfigureModelImport(PushupFbx, PushupState, loop: false))
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Mirror Test",
                    $"Mixamo FBX not found:\n{PushupFbx}", "OK");
                return;
            }

            DestroyIfExists("CVTest");
            DestroyIfExists("AvatarStage");
            DestroyIfExists("DisplayClearCamera");
            DestroyIfExists("AvatarStageCamera");
            DestroyIfExists("Ground");
            DestroyIfExists("Ch36_Body (URP)");

            var cvTest = CvTestSceneSetup.CreateCvTestObject();
            if (cvTest == null)
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Mirror Test",
                    "MediaPipePoseSource not found — enable the plugin first.", "OK");
                return;
            }

            ApplyEditorWebcamDefaults(cvTest);

            var stageCamera = BuildStage(controller: null, out Animator animator); // rest pose, no clips

            var session = cvTest.GetComponent<PushupSession>();

            var retargeter = cvTest.AddComponent<PoseMirrorRetargeter>();
            var rSo = new SerializedObject(retargeter);
            rSo.FindProperty("_session").objectReferenceValue = session;
            rSo.FindProperty("_animator").objectReferenceValue = animator;
            rSo.ApplyModifiedPropertiesWithoutUndo();

            var anchor = cvTest.AddComponent<AvatarMirrorAnchor>();
            var aSo = new SerializedObject(anchor);
            aSo.FindProperty("_session").objectReferenceValue = session;
            aSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            aSo.FindProperty("_characterRoot").objectReferenceValue = animator.transform;
            aSo.FindProperty("_followWhileArmed").boolValue = true;
            aSo.ApplyModifiedPropertiesWithoutUndo();

            var preview = cvTest.AddComponent<AvatarStagePreview>();
            var pSo = new SerializedObject(preview);
            pSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            pSo.FindProperty("_anchor").objectReferenceValue = anchor;
            pSo.FindProperty("_fullScreenOverlay").boolValue = true;
            pSo.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = cvTest;
            EditorGUIUtility.PingObject(cvTest);
            Debug.Log("[AvatarMirrorTest] Built retarget stand. Press Play: the character LIVE-" +
                      "MIRRORS your limbs (world landmarks). If a limb moves the wrong way, toggle " +
                      "Flip X / Flip Z on PoseMirrorRetargeter in the inspector during Play.");
        }

        [MenuItem("Tools/Push Stars/CV/Build Avatar Overlay Test (camera)", priority = 311)]
        public static void Build()
        {
            AssetDatabase.Refresh();

            if (!ConfigureModelImport(PushupFbx, PushupState, loop: false) ||
                !ConfigureModelImport(IdleFbx, IdleState, loop: true) ||
                !ConfigureModelImport(RestFbx, RestState, loop: true))
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Overlay Test",
                    $"Mixamo FBX files not found under:\n{MixamoDir}\n\nExpected:\n" +
                    "Ch36_nonPBR@Push Up.fbx\nCh36_nonPBR@Warrior Idle.fbx\nCh36_nonPBR@Sitting Idle.fbx",
                    "OK");
                return;
            }

            var controller = BuildController();
            if (controller == null) return;

            // Re-running the menu rebuilds the stand instead of stacking duplicates. The extra
            // names are leftovers from earlier stand iterations found live in testCV.unity.
            DestroyIfExists("CVTest");
            DestroyIfExists("AvatarStage");
            DestroyIfExists("DisplayClearCamera");
            DestroyIfExists("AvatarStageCamera");   // pre-parenting iteration left it at root
            DestroyIfExists("Ground");
            DestroyIfExists("Ch36_Body (URP)");     // old character copy with the broken material

            var cvTest = CvTestSceneSetup.CreateCvTestObject();
            if (cvTest == null)
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Overlay Test",
                    "MediaPipePoseSource not found.\n\nEnable the plugin first:\n" +
                    "Tools → Push Stars → MediaPipe → Enable, wait for recompile, then run this again.",
                    "OK");
                return;
            }

            ApplyEditorWebcamDefaults(cvTest);

            var stageCamera = BuildStage(controller, out Animator animator);

            var session = cvTest.GetComponent<PushupSession>();

            var driver = cvTest.AddComponent<PushupAvatarDriver>();
            var dSo = new SerializedObject(driver);
            dSo.FindProperty("_session").objectReferenceValue = session;
            dSo.FindProperty("_animator").objectReferenceValue = animator;
            dSo.ApplyModifiedPropertiesWithoutUndo();

            var anchor = cvTest.AddComponent<AvatarMirrorAnchor>();
            var aSo = new SerializedObject(anchor);
            aSo.FindProperty("_session").objectReferenceValue = session;
            aSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            aSo.FindProperty("_characterRoot").objectReferenceValue = animator.transform;
            aSo.ApplyModifiedPropertiesWithoutUndo();

            var preview = cvTest.AddComponent<AvatarStagePreview>();
            var pSo = new SerializedObject(preview);
            pSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            pSo.FindProperty("_driver").objectReferenceValue = driver;
            pSo.FindProperty("_anchor").objectReferenceValue = anchor;
            pSo.FindProperty("_fullScreenOverlay").boolValue = true;
            pSo.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = cvTest;
            EditorGUIUtility.PingObject(cvTest);
            Debug.Log("[AvatarOverlayTest] Built CVTest + AvatarStage (mirror mode). Press Play: " +
                      "the character stands center-screen until tracking locks, then glides after " +
                      "you (hip-mid anchor, filtered); plank arms the push-up scrub; " +
                      "rest/set-complete switches to the sitting clip.");
        }

        private static void DestroyIfExists(string name)
        {
            // FindObjectsOfType(true) also catches INACTIVE copies — earlier stand iterations (and
            // the CV-reset tool that deactivated instead of deleting) left disabled duplicates that
            // GameObject.Find never saw, so re-running the build stacked new ones on top.
            foreach (var go in Object.FindObjectsOfType<GameObject>(true))
            {
                if (go == null || go.name != name) continue;
                if (!go.scene.IsValid()) continue;
                if (go.transform.parent != null) continue; // roots only — children die with parents
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>The CVTest defaults are tuned for the iPhone (landmark rotation 90, portrait
        /// display). A PC webcam is already upright — zero the rotations and turn the live
        /// orientation buttons on so any leftover flip can be fixed without a recompile. Components
        /// are found by type name: the MediaPipe assembly is define-gated and not referenced here.</summary>
        private static void ApplyEditorWebcamDefaults(GameObject cvTest)
        {
            foreach (var component in cvTest.GetComponents<Component>())
            {
                string type = component.GetType().Name;
                if (type == "MediaPipePoseSource")
                {
                    var so = new SerializedObject(component);
                    so.FindProperty("_landmarkRotationDeg").intValue = 0;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else if (type == "WebCamPreview")
                {
                    var so = new SerializedObject(component);
                    so.FindProperty("_rotationOverride").intValue = 0;
                    so.FindProperty("_showControls").boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        /// <summary>Humanoid rig + a friendly clip name (the controller references clips by these
        /// names) + loop for the idle clips. Returns false when the FBX is missing.</summary>
        private static bool ConfigureModelImport(string path, string clipName, bool loop)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return false;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = clipName;
                clip.loopTime = loop;
            }
            if (clips.Length > 0) importer.clipAnimations = clips;

            importer.SaveAndReimport();
            return true;
        }

        private static AnimationClip LoadClip(string fbxPath)
            => AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview"));

        private static AnimatorController BuildController()
        {
            var pushClip = LoadClip(PushupFbx);
            var idleClip = LoadClip(IdleFbx);
            var restClip = LoadClip(RestFbx);
            if (pushClip == null || idleClip == null || restClip == null)
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Overlay Test",
                    "Animation clips not found inside the Mixamo FBX files after reimport.", "OK");
                return null;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = controller.layers[0].stateMachine;

            var idle = sm.AddState(IdleState);
            idle.motion = idleClip;
            var rest = sm.AddState(RestState);
            rest.motion = restClip;
            var push = sm.AddState(PushupState);
            push.motion = pushClip;

            sm.defaultState = idle; // no transitions — the driver plays states by name
            return controller;
        }

        /// <summary>Character + camera + light + ground, parked away from the scene origin. The
        /// stage camera renders into the RT owned by <see cref="AvatarStagePreview"/> at runtime.</summary>
        private static Camera BuildStage(AnimatorController controller, out Animator animator)
        {
            var stage = new GameObject("AvatarStage");
            stage.transform.position = new Vector3(0f, 0f, 100f);

            var model = AssetDatabase.LoadMainAssetAtPath(PushupFbx) as GameObject;
            var character = (GameObject)PrefabUtility.InstantiatePrefab(model);
            character.name = "AvatarOverlayCharacter";
            character.transform.SetParent(stage.transform, false);
            character.transform.localRotation = Quaternion.Euler(0f, 20f, 0f); // slight 3/4 to camera

            animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            FixMaterialsForUrp(character);

            // No ground plane: mirror mode draws the stage as a transparent full-screen overlay,
            // so anything but the character would occlude the camera feed.

            // Blank scenes usually carry a directional light already; only add one if not.
            bool hasLight = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Any(l => l.type == LightType.Directional && l.enabled);
            if (!hasLight)
            {
                var lightGo = new GameObject("Stage Light");
                lightGo.transform.SetParent(stage.transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(45f, -30f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
            }

            var camGo = new GameObject("AvatarStageCamera");
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.35f, 3.6f);
            camGo.transform.LookAt(stage.transform.position + new Vector3(0f, 0.75f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Alpha 0: the overlay shows the camera feed everywhere the character isn't.
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            EnsureDisplayCamera(cam);
            return cam;
        }

        /// <summary>The stage camera renders into a RenderTexture at runtime, so a scene without
        /// its own Main Camera shows the "No cameras rendering" watermark under the IMGUI stand.
        /// Add a black clear-only camera when no other display camera exists.</summary>
        private static void EnsureDisplayCamera(Camera stageCamera)
        {
            bool hasDisplayCamera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Any(c => c != stageCamera && c.enabled && c.targetTexture == null);
            if (hasDisplayCamera) return;

            var go = new GameObject("DisplayClearCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.depth = -100f;
        }

        /// <summary>Mixamo FBX materials import on built-in shaders ("Standard", "Standard
        /// (Specular setup)", legacy Phong variants) — all of them render pink under URP. Anything
        /// that is not already a URP shader is replaced with a URP/Lit clone that keeps the
        /// original main texture (the replacement lives only on the scene instance).</summary>
        private static void FixMaterialsForUrp(GameObject character)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;

            foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && m.shader != null
                        && m.shader.name.StartsWith("Universal Render Pipeline")) continue;

                    var fixedMat = new Material(lit) { name = (m != null ? m.name : "Avatar") + " (URP)" };
                    var tex = m != null ? m.mainTexture : null;
                    if (tex != null) fixedMat.SetTexture("_BaseMap", tex);
                    else fixedMat.SetColor("_BaseColor", new Color(0.75f, 0.75f, 0.78f));
                    mats[i] = fixedMat;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }
    }
}
