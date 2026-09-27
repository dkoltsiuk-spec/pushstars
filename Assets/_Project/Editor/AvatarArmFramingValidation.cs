using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class AvatarArmFramingValidation
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static AvatarArmFramingValidation() { EditorApplication.update += RunPending; }
        private static void RunPending()
        {
            const string request = "Temp/validate-arm-framing.request";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            if (File.GetLastWriteTimeUtc("Assets/_Project/Scripts/UI/Avatar/AvatarFraming.cs") > File.GetLastWriteTimeUtc(typeof(AvatarFraming).Assembly.Location)
                || File.GetLastWriteTimeUtc("Assets/_Project/Editor/AvatarArmFramingValidation.cs") > File.GetLastWriteTimeUtc(typeof(AvatarArmFramingValidation).Assembly.Location)) return;
            File.Delete(request);
            Directory.CreateDirectory("output/avatar-arm-framing");
            try
            {
                Run();
                PreparationStanceRegression.Run();
                CameraPresentationRegression.Run();
                File.WriteAllText("output/avatar-arm-framing/run.result", "PASS " + DateTime.Now);
            }
            catch (Exception e) { File.WriteAllText("output/avatar-arm-framing/run.result", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Tools/Push Stars/Validate Avatar Arm Framing")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Main.unity");
            int poses = 0;
            try
            {
                var stage = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true))
                    .First(s => s.GetComponent<CharacterRoster>() != null);
                var roster = new SerializedObject(stage.GetComponent<CharacterRoster>());
                foreach (Transform child in stage.AvatarRoot) child.gameObject.SetActive(false);
                typeof(CharacterStage).GetMethod("Awake", Private).Invoke(stage, null);
                var camera = stage.StageCamera;
                camera.scene = scene;
                typeof(CharacterStage).GetMethod("CreateRenderTexture", Private).Invoke(stage, null);
                var texture = stage.RenderTarget;
                if (texture.width != 2160 || texture.height != 1280) throw new Exception("Overscan lost source pixel density.");
                Directory.CreateDirectory("output/avatar-arm-framing");
                try
                {
                    foreach (string field in new[] { "_malePrefab", "_femalePrefab", "_gladiatorPrefab", "_robotPrefab" })
                    {
                        var prefab = roster.FindProperty(field)?.objectReferenceValue as GameObject;
                        if (prefab == null) continue;
                        var model = Object.Instantiate(prefab, stage.AvatarRoot);
                        model.transform.localPosition = Vector3.zero;
                        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                            child.gameObject.layer = stage.AvatarRoot.gameObject.layer;
                        var animator = model.GetComponentInChildren<Animator>();
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.applyRootMotion = false;
                        animator.Play("Idle", 0, 0); animator.Update(0);
                        AvatarWideCamera.Configure(camera, false);
                        var originalPosition = camera.transform.position;
                        float originalFov = camera.fieldOfView;
                        var originalSize = stage.TargetImage.rectTransform.rect.size;
                        stage.FitAvatarFraming();
                        if (camera.transform.position != originalPosition || camera.fieldOfView != originalFov
                            || stage.TargetImage.rectTransform.rect.size != originalSize)
                            throw new Exception("Overscan changed the authored camera or UI scale.");
                        Vector3 cameraPosition = camera.transform.position;
                        var mesh = new Mesh();
                        try
                        {
                            foreach (string state in new[] { "Idle", "WarriorIdle" })
                            {
                                if (!animator.HasState(0, Animator.StringToHash(state))) continue;
                                for (int frame = 0; frame <= 20; frame++)
                                {
                                    int layer = state == "WarriorIdle" ? animator.GetLayerIndex("Accent") : 0;
                                    if (layer < 0) layer = 0;
                                    if (layer > 0)
                                    {
                                        animator.Play("Idle", 0, frame / 20f);
                                        animator.SetLayerWeight(layer, 1f);
                                    }
                                    animator.Play(state, layer, frame / 20f); animator.Update(0);
                                    CheckScale(camera, animator);
                                    try { CheckArms(camera, model, mesh, stage.DisplayUv, prefab.name + " " + state + " " + frame); }
                                    catch { camera.Render(); Capture(texture, "output/avatar-arm-framing/failing-pose.png"); throw; }
                                    if (camera.transform.position != cameraPosition) throw new Exception("Framing moves during idle.");
                                    poses++;
                                    if (state == "WarriorIdle" && frame == 10)
                                    {
                                        var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>()
                                            .Where(s => s.enabled && s.sharedMesh != null)
                                            .Select(s => new PushupPosePreview.PreviewSkin(s)).ToArray();
                                        try
                                        {
                                            foreach (var skin in skins) skin.UpdateGeometry();
                                            camera.Render();
                                            Capture(texture, "output/avatar-arm-framing/" + prefab.name + "-arms.png");
                                        }
                                        finally { foreach (var skin in skins) skin.Dispose(); }
                                    }
                                }
                            }
                        }
                        finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(model); }
                    }
                }
                finally { camera.targetTexture = null; }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            if (poses < 80) throw new Exception("Too few character poses checked: " + poses);
            File.WriteAllText("output/avatar-arm-framing/validation.txt",
                "PASS: " + poses + " animated poses; baked mesh stays inside the home camera horizontally; original visible scale and camera preserved; 2160x1280 overscan retains original pixel density.\n");
        }

        private static void CheckScale(Camera camera, Animator animator)
        {
            var lens = camera.GetComponent<AvatarWideCamera>();
            foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                var joint = animator.GetBoneTransform(bone);
                if (joint == null) continue;
                camera.ResetProjectionMatrix();
                var before = camera.WorldToViewportPoint(joint.position);
                lens.Apply();
                var after = camera.WorldToViewportPoint(joint.position);
                after.x = .5f + (after.x - .5f) * AvatarWideCamera.WidthMultiplier;
                if (Vector2.Distance(before, after) > .0001f)
                    throw new Exception("Overscan changed displayed character scale or position: " + bone);
            }
        }

        private static void CheckArms(Camera camera, GameObject model, Mesh mesh, Rect uv, string context)
        {
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || skin.sharedMesh == null) continue;
                skin.BakeMesh(mesh);
                foreach (Vector3 vertex in mesh.vertices)
                {
                    var p = camera.WorldToViewportPoint(skin.transform.TransformPoint(vertex));
                    if (p.z <= camera.nearClipPlane || p.x < uv.xMin + .005f || p.x > uv.xMax - .005f)
                        throw new Exception(context + " clips mesh at viewport " + p);
                }
            }
        }

        private static void Capture(RenderTexture texture, string path)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
    }
}
