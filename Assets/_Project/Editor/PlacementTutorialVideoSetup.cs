using System;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PushStars.Editor
{
    public static class PlacementTutorialVideoSetup
    {
        public const string ClipPath = "Assets/_Project/UI/Video/phone-placement.mp4";
        private const string PosterPath = "Assets/_Project/UI/Video/phone-placement-poster.jpg";
        private const string Art = "Assets/_Project/UI/Sprites/Onboarding/";

        [MenuItem("Tools/Push Stars/Onboarding/Apply Placement Video")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode first.");
            AssetDatabase.Refresh();
            ImportSprite(Art + "coach-placement-bubble.png");
            ImportSprite(Art + "placement-video-frame.png");
            var scene = SceneManager.GetSceneByPath(OnboardingSceneSetup.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(OnboardingSceneSetup.ScenePath, OpenSceneMode.Additive);
            var guide = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GuidedOnboardingController>(true)).Single();
            var guideSettings = new SerializedObject(guide);
            var panel = (GameObject)guideSettings.FindProperty("_placement").objectReferenceValue;
            guideSettings.FindProperty("_placementBubbleSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "coach-placement-bubble.png");
            guideSettings.ApplyModifiedPropertiesWithoutUndo();
            Configure((RectTransform)panel.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Onboarding] Approved placement video connected.");
        }

        public static void Configure(RectTransform panel)
        {
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(ClipPath);
            var poster = AssetDatabase.LoadAssetAtPath<Texture2D>(PosterPath);
            if (clip == null || poster == null) throw new InvalidOperationException("Missing approved placement video or poster.");
            UiBuilder.Place(panel, new Vector2(.5f, 1), new Vector2(0, -44), new Vector2(306, 306f * 9 / 16));
            foreach (var name in new[] { "BluePanel", "PushupPosition", "Phone", "Distance" })
            {
                var old = panel.Find(name);
                if (old != null) old.gameObject.SetActive(false);
            }
            var hint = panel.Find("PlacementHint").GetComponent<TextMeshProUGUI>();
            UiBuilder.Place(hint.rectTransform, new Vector2(.5f, 1), new Vector2(0, -178), new Vector2(314, 28));
            hint.text = "Prop it upright, facing you.\nStep back 1.5 - 2 m. Keep your whole body in view.";
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 10;
            hint.fontSizeMax = 12;
            var existing = panel.Find("TutorialVideo");
            var display = existing != null ? existing.GetComponent<RawImage>() : UiBuilder.RawImage(panel, "TutorialVideo", Color.white);
            UiBuilder.Stretch(display.rectTransform);
            display.raycastTarget = false;
            display.texture = poster;
            if (display.GetComponent<PlacementVideoFrame>() == null) display.gameObject.AddComponent<PlacementVideoFrame>();
            var frameObject = panel.Find("VideoFrame");
            var frame = frameObject != null ? frameObject.GetComponent<Image>() : UiBuilder.Image(panel, "VideoFrame", Color.white);
            frame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "placement-video-frame.png");
            frame.type = Image.Type.Simple;
            frame.raycastTarget = false;
            UiBuilder.Stretch(frame.rectTransform);
            frame.transform.SetAsLastSibling();
            var video = display.GetComponent<PlacementTutorialVideo>();
            if (video == null) video = display.gameObject.AddComponent<PlacementTutorialVideo>();
            var so = new SerializedObject(video);
            so.FindProperty("_clip").objectReferenceValue = clip;
            so.FindProperty("_poster").objectReferenceValue = poster;
            so.FindProperty("_display").objectReferenceValue = display;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ImportSprite(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }
}
