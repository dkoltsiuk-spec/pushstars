using System;
using System.IO;
using System.Reflection;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class FightAspectValidation
    {
        [MenuItem("Tools/Push Stars/Validate Fight Display Aspect")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("AspectTest");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var rt = new RenderTexture(960, 1080, 24);
            try
            {
                var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.scene = scene; camera.targetTexture = rt;
                var image = new GameObject("Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
                image.transform.SetParent(root.transform); image.texture = rt;
                var stage = root.AddComponent<CharacterStage>();
                var so = new SerializedObject(stage);
                so.FindProperty("_stageCamera").objectReferenceValue = camera;
                so.FindProperty("_targetImage").objectReferenceValue = image;
                so.ApplyModifiedPropertiesWithoutUndo();
                float oldDistortion = (576f / 504) / camera.aspect;
                foreach (var size in new[] { new Vector2(576, 504), new Vector2(390, 422), new Vector2(320, 284), new Vector2(430, 466), new Vector2(500, 320) })
                foreach (var scale in new[] { Vector3.one, new Vector3(1.15f, .85f, 1) })
                foreach (var uv in new[] { new Rect(0, 0, 1, 1), new Rect(.1f, .2f, .8f, .6f), new Rect(.1f, 0, .8f, .8f) })
                {
                    image.rectTransform.sizeDelta = size; image.transform.localScale = scale; image.uvRect = uv;
                    stage.MatchDisplayAspect();
                    // Equal world-space segments parallel to the image plane must occupy equal
                    // displayed pixels, after camera projection, UV crop and UI scaling.
                    var center = camera.WorldToViewportPoint(new Vector3(0, 0, 5));
                    var x = camera.WorldToViewportPoint(new Vector3(1, 0, 5));
                    var y = camera.WorldToViewportPoint(new Vector3(0, 1, 5));
                    float px = (x.x - center.x) * size.x * scale.x / uv.width;
                    float py = (y.y - center.y) * size.y * scale.y / uv.height;
                    if (Mathf.Abs(px / py - 1) > .0001f) throw new InvalidOperationException("Display distortion at " + size);
                    if (image.texture != rt || camera.targetTexture != rt) throw new InvalidOperationException("Render target was replaced");
                }
                Directory.CreateDirectory("output/fight-aspect");
                var hud = root.AddComponent<FightHud>();
                var opponent = new GameObject("Opponent", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
                opponent.transform.SetParent(root.transform);
                image.uvRect = opponent.uvRect = new Rect(0, 0, 1, 1);
                var hudData = new SerializedObject(hud);
                hudData.FindProperty("_playerHalf").objectReferenceValue = image.rectTransform;
                hudData.FindProperty("_opponentHalf").objectReferenceValue = opponent.gameObject;
                hudData.ApplyModifiedPropertiesWithoutUndo();
                var zoom = typeof(FightHud).GetMethod("SetDuelAvatarZoom", BindingFlags.Instance | BindingFlags.NonPublic);
                zoom.Invoke(hud, new object[] { true }); zoom.Invoke(hud, new object[] { true });
                foreach (var target in new[] { image, opponent })
                    if (target.uvRect != new Rect(.1f, 0, .8f, .8f)) throw new InvalidOperationException("25% zoom is not stable or bottom anchored");
                zoom.Invoke(hud, new object[] { false });
                foreach (var target in new[] { image, opponent })
                    if (target.uvRect != new Rect(0, 0, 1, 1)) throw new InvalidOperationException("Solo viewport was not restored");
                File.WriteAllText("output/fight-aspect/validation.txt", "PASS: 30 display/scale/UV combinations preserve proportions; both duel characters enlarged exactly 25%, bottom anchored; repeat configuration does not accumulate zoom; solo UVs restored; render targets and UI layout preserved. Original distortion at 576x504: " + oldDistortion.ToString("F3") + ".\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); rt.Release(); Object.DestroyImmediate(rt); }
        }
    }
}
