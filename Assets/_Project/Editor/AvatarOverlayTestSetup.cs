using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    /// The body on the stage is the owner's own character (<see cref="MainCharacterSetup"/>) —
    /// the clips stay Mixamo and reach it through Humanoid retargeting, so the push-up scrub and
    /// the live mirror are unchanged by the swap.
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

        /// <summary>The owner's target flow (HYBRID): the character live-mirrors the user's limbs
        /// while they get into position, then — the moment the plank ARMS — softly blends into the
        /// canned push-up animation (depth-scrubbed by <see cref="PushupAvatarDriver"/>) and does
        /// the reps "в рамках анимации". Disarm blends back to the live mirror.</summary>
        [MenuItem("Tools/Push Stars/CV/Build Avatar Hybrid Test (mirror → pushup animation)", priority = 312)]
        public static void BuildRetarget()
        {
            AssetDatabase.Refresh();

            if (!ConfigureModelImport(PushupFbx, PushupState, loop: false) ||
                !ConfigureModelImport(IdleFbx, IdleState, loop: true) ||
                !ConfigureModelImport(RestFbx, RestState, loop: true))
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Hybrid Test",
                    $"Mixamo FBX files not found under:\n{MixamoDir}", "OK");
                return;
            }

            var controller = BuildController();
            if (controller == null) return;

            DestroyIfExists("CVTest");
            DestroyIfExists("AvatarStage");
            DestroyIfExists("DisplayClearCamera");
            DestroyIfExists("AvatarStageCamera");
            DestroyIfExists("Ground");
            DestroyIfExists("Ch36_Body (URP)");

            var cvTest = CvTestSceneSetup.CreateCvTestObject();
            if (cvTest == null)
            {
                EditorUtility.DisplayDialog("Push Stars — Avatar Hybrid Test",
                    "MediaPipePoseSource not found — enable the plugin first.", "OK");
                return;
            }

            ApplyEditorWebcamDefaults(cvTest);

            var stageCamera = BuildStage(controller, out Animator animator);

            // Use a frontal presentation; retarget directions themselves are camera-relative.
            animator.transform.localRotation = Quaternion.identity;

            var session = cvTest.GetComponent<PushupSession>();

            // Armed phase: the depth-scrubbed push-up clip owns the body.
            var driver = cvTest.AddComponent<PushupAvatarDriver>();
            var dSo = new SerializedObject(driver);
            dSo.FindProperty("_session").objectReferenceValue = session;
            dSo.FindProperty("_animator").objectReferenceValue = animator;
            dSo.ApplyModifiedPropertiesWithoutUndo();

            // Setup phase: live mirror; blends out over ~0.35s when the armer fires.
            var retargeter = cvTest.AddComponent<PoseMirrorRetargeter>();
            var rSo = new SerializedObject(retargeter);
            rSo.FindProperty("_session").objectReferenceValue = session;
            rSo.FindProperty("_animator").objectReferenceValue = animator;
            rSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            rSo.ApplyModifiedPropertiesWithoutUndo();

            var anchor = cvTest.AddComponent<AvatarMirrorAnchor>();
            var aSo = new SerializedObject(anchor);
            aSo.FindProperty("_session").objectReferenceValue = session;
            aSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            aSo.FindProperty("_characterRoot").objectReferenceValue = animator.transform;
            // Armed = the animation owns the body; the anchor freezes at the locked spot.
            aSo.FindProperty("_followWhileArmed").boolValue = false;
            aSo.FindProperty("_mirrorX").boolValue = true;
            aSo.FindProperty("_hipsBone").objectReferenceValue =
                animator.GetBoneTransform(HumanBodyBones.Hips);
            ApplyRigProportions(aSo, animator);
            aSo.ApplyModifiedPropertiesWithoutUndo();
            WirePreviewMirror(cvTest, anchor);

            var preview = cvTest.AddComponent<AvatarStagePreview>();
            var pSo = new SerializedObject(preview);
            pSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            pSo.FindProperty("_driver").objectReferenceValue = driver;
            pSo.FindProperty("_anchor").objectReferenceValue = anchor;
            pSo.FindProperty("_fullScreenOverlay").boolValue = true;
            pSo.ApplyModifiedPropertiesWithoutUndo();

            MarkSceneDirty();
            Selection.activeGameObject = cvTest;
            EditorGUIUtility.PingObject(cvTest);
            Debug.Log("[AvatarHybridTest] Built hybrid stand. Play: live mirror while you get into " +
                      "position → plank arms → soft blend into the depth-scrubbed push-up clip → " +
                      "reps run on the animation; disarm blends back to the mirror.");
        }

        private static void WirePreviewMirror(GameObject cvTest, AvatarMirrorAnchor anchor)
        {
            foreach (var component in cvTest.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "WebCamPreview") continue;
                var so = new SerializedObject(component);
                so.FindProperty("_anchor").objectReferenceValue = anchor;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
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
            ApplyRigProportions(aSo, animator);
            aSo.ApplyModifiedPropertiesWithoutUndo();
            WirePreviewMirror(cvTest, anchor);

            var preview = cvTest.AddComponent<AvatarStagePreview>();
            var pSo = new SerializedObject(preview);
            pSo.FindProperty("_stageCamera").objectReferenceValue = stageCamera;
            pSo.FindProperty("_driver").objectReferenceValue = driver;
            pSo.FindProperty("_anchor").objectReferenceValue = anchor;
            pSo.FindProperty("_fullScreenOverlay").boolValue = true;
            pSo.ApplyModifiedPropertiesWithoutUndo();

            MarkSceneDirty();
            Selection.activeGameObject = cvTest;
            EditorGUIUtility.PingObject(cvTest);
            Debug.Log("[AvatarOverlayTest] Built CVTest + AvatarStage (mirror mode). Press Play: " +
                      "the character stands center-screen until tracking locks, then glides after " +
                      "you (hip-mid anchor, filtered); plank arms the push-up scrub; " +
                      "rest/set-complete switches to the sitting clip.");
        }

        /// <summary>The anchor scales the character to the user by comparing the rig's real
        /// torso against the CV torso, so those two numbers have to come from the rig actually on
        /// the stage — they used to be hard-coded to the Mixamo Ch36 body and are wrong for any
        /// other character. Measured off the bind pose; leaves the defaults if a bone is missing.</summary>
        private static void ApplyRigProportions(SerializedObject anchorSo, Animator animator)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var lUp  = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rUp  = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (hips == null || lUp == null || rUp == null) return;

            Vector3 shoulderMid = (lUp.position + rUp.position) * 0.5f;
            anchorSo.FindProperty("_rigTorsoMeters").floatValue =
                Vector3.Distance(shoulderMid, hips.position);
            anchorSo.FindProperty("_rigHipHeightMeters").floatValue =
                hips.position.y - animator.transform.position.y;
        }

        /// <summary>Building the stand creates and destroys objects without going through Undo,
        /// which leaves Unity believing the scene is untouched — Ctrl+S then does nothing and the
        /// whole stand disappears the next time the scene is loaded.</summary>
        private static void MarkSceneDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
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

        /// <summary>Use the sensor metadata on both desktop and mobile, rather than baking a
        /// platform guess into a stand that may later run on another camera. Components are found
        /// by type name because the MediaPipe assembly is define-gated.</summary>
        private static void ApplyEditorWebcamDefaults(GameObject cvTest)
        {
            foreach (var component in cvTest.GetComponents<Component>())
            {
                string type = component.GetType().Name;
                if (type == "MediaPipePoseSource")
                {
                    var so = new SerializedObject(component);
                    so.FindProperty("_landmarkRotationDeg").intValue = -1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else if (type == "WebCamPreview")
                {
                    var so = new SerializedObject(component);
                    so.FindProperty("_rotationOverride").intValue = -1;
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

        /// <summary>Configures the Mixamo clip imports and (re)builds the shared push-up
        /// controller, so anything that needs a rig able to do a push-up — this stand, the fight
        /// screen — asks for it here instead of assembling its own copy. Null when the FBX files
        /// are missing.</summary>
        public static AnimatorController EnsurePushupController()
        {
            if (!ConfigureModelImport(PushupFbx, PushupState, loop: false) ||
                !ConfigureModelImport(IdleFbx, IdleState, loop: true) ||
                !ConfigureModelImport(RestFbx, RestState, loop: true))
            {
                Debug.LogError($"[AvatarOverlayTest] Mixamo FBX files not found under {MixamoDir}.");
                return null;
            }
            return BuildController();
        }

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

            // Rewritten in place, never deleted and recreated: a fresh asset means a fresh GUID,
            // and every reference already pointing at the old one goes Missing.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            var sm = controller.layers[0].stateMachine;
            foreach (var existing in sm.states) sm.RemoveState(existing.state);

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

            // The owner's own character carries the stand now; the Mixamo Ch36 body is only the
            // fallback for a checkout where main_man hasn't been imported yet. Either way the
            // clips below are Humanoid, so Mecanim retargets them onto whichever rig shows up.
            var model = MainCharacterSetup.LoadCharacterPrefab();
            if (model == null) model = AssetDatabase.LoadMainAssetAtPath(PushupFbx) as GameObject;

            var character = (GameObject)PrefabUtility.InstantiatePrefab(model);
            character.name = "AvatarOverlayCharacter";
            character.transform.SetParent(stage.transform, false);
            character.transform.localRotation = Quaternion.Euler(0f, 20f, 0f); // slight 3/4 to camera

            animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            FixMaterialsForPipeline(character);

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

        /// <summary>A material whose shader belongs to the other render pipeline draws magenta.
        /// Mixamo FBXs import on built-in shaders ("Standard", legacy Phong variants), which is
        /// wrong under URP and right under the built-in pipeline — so the test is the project's
        /// actual pipeline, not the shader name. Mismatches are swapped for a clone on the correct
        /// shader that keeps the original texture; the replacement lives only on the scene
        /// instance. The main character arrives already correct and is left alone.</summary>
        private static void FixMaterialsForPipeline(GameObject character)
        {
            var shader = MainCharacterSetup.LitShader();
            if (shader == null) return;

            foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (MainCharacterSetup.RendersInThisPipeline(m)) continue;

                    var fixedMat = new Material(shader) { name = (m != null ? m.name : "Avatar") + " (fixed)" };
                    MainCharacterSetup.ApplyCharacterSurface(fixedMat, m != null ? m.mainTexture : null);
                    mats[i] = fixedMat;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }
    }
}
