using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class PushupFramingRegression
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Push Stars/Validate Pushup Framing")]
        public static void Run()
        {
            Directory.CreateDirectory("output/pushup-framing");
            int frames = 0;
            foreach (CharacterGender gender in new[] { CharacterGender.Male, CharacterGender.Female })
            {
                var scene = EditorSceneManager.NewPreviewScene();
                var holder = new GameObject("Pushup framing regression");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
                var texture = new RenderTexture(390, 422, 24);
                var snapshots = new List<PushupPosePreview.PreviewSkin>();
                try
                {
                    var root = new GameObject("Body root").transform;
                    root.SetParent(holder.transform, false);
                    var camera = new GameObject("Camera").AddComponent<Camera>();
                    camera.transform.SetParent(holder.transform, false);
                    camera.scene = scene;
                    camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                    camera.enabled = false;
                    camera.targetTexture = texture;
                    camera.fieldOfView = 40f;
                    camera.nearClipPlane = .03f;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.035f, .06f, .085f);
                    var image = new GameObject("Display", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                    image.transform.SetParent(holder.transform, false);
                    image.texture = texture;
                    var stage = holder.AddComponent<CharacterStage>();
                    Set(stage, "_stageCamera", camera);
                    Set(stage, "_targetImage", image);
                    Set(stage, "_avatarRoot", root);
                    var driver = holder.AddComponent<PushupAvatarDriver>();
                    var avatar = holder.AddComponent<FightAvatar>();
                    Set(avatar, "_driverBehaviour", driver);
                    Set(avatar, "_avatarRoot", root);
                    Set(avatar, "_stageCamera", camera);
                    var prefab = MainCharacterSetup.LoadCharacterPrefab(gender);
                    Set(avatar, "_malePrefab", prefab);
                    Set(avatar, "_femalePrefab", prefab);
                    Set(avatar, "_fightController", AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/_Project/Art/Characters/Mixamo/AvatarOverlayTest.controller"));
                    Invoke(avatar, "Build");
                    var animator = avatar.Character.GetComponentInChildren<Animator>();
                    var correction = animator.GetComponent<PushupPoseCorrection>();
                    Require(correction != null, "No authored pose correction");
                    var skins = avatar.Character.GetComponentsInChildren<SkinnedMeshRenderer>();
                    AddLight(holder, new Vector3(30, 145, 0), 1.3f);
                    AddLight(holder, new Vector3(20, -100, 0), .8f);

                    // Cache building must preserve ALL pose transforms, not only the hips.
                    correction.Apply(.37f);
                    var bones = animator.GetComponentsInChildren<Transform>();
                    var positions = Array.ConvertAll(bones, b => b.localPosition);
                    var rotations = Array.ConvertAll(bones, b => b.localRotation);
                    Invoke(avatar, "CachePushupSilhouette");
                    var cached = (Vector3[])Get(avatar, "_pushupSilhouette");
                    var cacheBounds = new Bounds(cached[0], Vector3.zero);
                    foreach (var p in cached) cacheBounds.Encapsulate(p);
                    for (int i = 0; i < bones.Length; i++)
                        Require(Vector3.Distance(positions[i], bones[i].localPosition) < .00001f
                            && Quaternion.Angle(rotations[i], bones[i].localRotation) < .05f, "Framing cache altered the pose");
                    foreach (var skin in skins) snapshots.Add(new PushupPosePreview.PreviewSkin(skin));
                    var worldVertices = new List<Vector3>();

                    foreach (var size in new[] { new Vector2(390, 422), new Vector2(320, 284),
                        new Vector2(768, 512), new Vector2(390, 720) })
                    foreach (var uv in new[] { new Rect(0, 0, 1, 1), new Rect(.1f, 0, .8f, .8f) })
                    foreach (float scale in new[] { .65f, 1.8f })
                    {
                        image.rectTransform.sizeDelta = size;
                        image.uvRect = uv;
                        animator.transform.localScale = Vector3.one * scale;
                        animator.transform.localPosition = new Vector3(1.7f, -.6f, .8f);
                        correction.SetDepth(0f, true, 0f);
                        Vector3 cameraPosition = default;
                        Quaternion cameraRotation = default;
                        Vector2 motionMin = Vector2.one * float.PositiveInfinity;
                        Vector2 motionMax = Vector2.one * float.NegativeInfinity;
                        for (int sample = 0; sample <= 20; sample++)
                        {
                            correction.Apply(sample / 20f);
                            Invoke(avatar, "LateUpdate");
                            if (sample == 0) { cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation; }
                            Require(Vector3.Distance(cameraPosition, camera.transform.position) < .00001f
                                && Quaternion.Angle(cameraRotation, camera.transform.rotation) < .01f,
                                "Camera follows rep depth");
                            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
                            worldVertices.Clear();
                            foreach (var snapshot in snapshots)
                            {
                                snapshot.UpdateGeometry();
                                snapshot.CollectWorldVertices(worldVertices);
                            }
                            foreach (var v in worldVertices)
                            {
                                    Vector3 p = camera.WorldToViewportPoint(v);
                                    Require(p.z > camera.nearClipPlane, "Body behind camera");
                                    var display = new Vector2((p.x - uv.x) / uv.width, (p.y - uv.y) / uv.height);
                                    min = Vector2.Min(min, display); max = Vector2.Max(max, display);
                            }
                            Require(min.x >= .06f && min.y >= .06f && max.x <= .94f && max.y <= .94f,
                                $"Clipped {gender}/{size}/{scale}/depth={sample / 20f}: {min} .. {max}");
                            Require(Mathf.Abs((min.x + max.x) * .5f - .5f) < .025f,
                                "Avatar is not horizontally centered");
                            motionMin = Vector2.Min(motionMin, min);
                            motionMax = Vector2.Max(motionMax, max);
                            frames++;
                        }
                        // A deeper rep compresses the silhouette at the bottom. The fixed
                        // camera must fill the frame with the whole motion, without zooming
                        // on each depth; clipping and centering are still checked per pose.
                        Require(Mathf.Max(motionMax.x - motionMin.x, motionMax.y - motionMin.y) > .60f,
                            $"Avatar motion too small: {gender}/{size}/{uv}/{scale}: {motionMin} .. {motionMax}; cache={cacheBounds}; camera={camera.transform.position}; bodyScale={animator.transform.lossyScale}");
                    }

                    // Preparation may move the camera while the pushup driver remains active.
                    avatar.SetPreparationPresentation(true);
                    Invoke(avatar, "LateUpdate");
                    avatar.SetPreparationPresentation(false);
                    correction.SetDepth(0f, true, 0f);
                    correction.Apply(0f);
                    Invoke(avatar, "LateUpdate");
                    Require((bool)Get(avatar, "_pushupFramed"), "Preparation did not release its shot");

                    image.rectTransform.sizeDelta = new Vector2(390, 422);
                    image.uvRect = new Rect(.1f, 0, .8f, .8f);
                    animator.transform.localScale = Vector3.one;
                    animator.transform.localPosition = Vector3.zero;
                    for (int phase = 0; phase < 3; phase++)
                    {
                        correction.Apply(phase / 2f);
                        Invoke(avatar, "LateUpdate");
                        foreach (var snapshot in snapshots) snapshot.UpdateGeometry();
                        camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = texture;
                        var crop = image.uvRect;
                        var pixels = new Rect(Mathf.RoundToInt(crop.x * texture.width), Mathf.RoundToInt(crop.y * texture.height),
                            Mathf.RoundToInt(crop.width * texture.width), Mathf.RoundToInt(crop.height * texture.height));
                        var shot = new Texture2D((int)pixels.width, (int)pixels.height, TextureFormat.RGB24, false);
                        try
                        {
                            shot.ReadPixels(pixels, 0, 0);
                            shot.Apply();
                            File.WriteAllBytes($"output/pushup-framing/{gender}-{phase}.png", shot.EncodeToPNG());
                        }
                        finally { RenderTexture.active = previous; Object.DestroyImmediate(shot); }
                    }
                }
                finally
                {
                    foreach (var snapshot in snapshots) snapshot.Dispose();
                    EditorSceneManager.ClosePreviewScene(scene);
                    texture.Release(); Object.DestroyImmediate(texture);
                }
            }
            string report = $"PASS: {frames} posed frames, male/female, 4 display aspects, full/duel UV crop, 2 mirror scales; fixed camera, centered silhouette, full-body margins, pose restoration and preparation handoff.";
            File.WriteAllText("output/pushup-framing/validation.txt", report);
            Debug.Log(report);
        }

        private static void AddLight(GameObject holder, Vector3 euler, float intensity)
        {
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(holder.transform, false);
            light.transform.rotation = Quaternion.Euler(euler);
            light.type = LightType.Directional; light.intensity = intensity;
        }
        private static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        private static void Invoke(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
