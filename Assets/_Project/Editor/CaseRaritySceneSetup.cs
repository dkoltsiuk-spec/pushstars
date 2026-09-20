using System;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class CaseRaritySceneSetup
    {
        private const string Folder = "Assets/_Project/Resources/Rewards/";

        [MenuItem("Tools/Push Stars/Rewards/Apply Supplied Case Artwork")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits first.");
            try
            {
                var scene = EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(FightScreen.CaseOpening));
                Configure(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single());
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        public static void Configure(RewardScreen screen)
        {
            foreach (string name in new[] { "CaseBlueBackground", "CaseGoldBackground", "CasePurpleBackground", "CaseDarkLightning", "CaseUpgradeStar" })
            {
                var importer = AssetImporter.GetAtPath(Folder + name + ".png") as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing supplied artwork: " + name);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = name.Contains("Background") ? 2048 : 512;
                importer.SaveAndReimport();
            }
            var canvas = screen.CaseUi.Content.GetComponentInParent<Canvas>().rootCanvas;
            var presentation = screen.GetComponent<CaseRarityPresentation>();
            if (presentation == null) presentation = screen.gameObject.AddComponent<CaseRarityPresentation>();
            var rect = canvas.transform.Find("CaseRarityBackground") as RectTransform;
            if (rect == null)
            {
                rect = new GameObject("CaseRarityBackground", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
                rect.SetParent(canvas.transform, false);
            }
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.SetSiblingIndex(1);
            presentation.Background = rect.GetComponent<RawImage>();
            presentation.Background.raycastTarget = false;
            presentation.Backgrounds = new[] { (Texture2D)null,
                Resources.Load<Texture2D>("Rewards/CaseBlueBackground"),
                Resources.Load<Texture2D>("Rewards/CaseGoldBackground"),
                Resources.Load<Texture2D>("Rewards/CasePurpleBackground") };
            var field = canvas.GetComponentInChildren<LightningField>(true);
            field.transform.SetSiblingIndex(2);
            var data = new SerializedObject(field);
            data.FindProperty("_edgeFade").floatValue = .5f;
            data.ApplyModifiedPropertiesWithoutUndo();
            presentation.Lightning = field.GetComponentsInChildren<Image>(true);
            foreach (var bolt in presentation.Lightning)
            {
                Vector2 point = bolt.rectTransform.anchoredPosition;
                float edge = Mathf.Max(Mathf.Abs(point.x) / 195f, Mathf.Abs(point.y) / 422f);
                bolt.color = new Color(1, 1, 1, 1 - .5f * Mathf.SmoothStep(.15f, 1, edge));
            }
            presentation.DarkLightning = Resources.Load<Sprite>("Rewards/CaseDarkLightning");
            presentation.LightLightning = Resources.Load<PushStarsTheme>("PushStarsTheme").IconLightningBG;
            presentation.Rays = screen.CaseUi.Rays != null ? screen.CaseUi.Rays.GetComponent<CaseTapGraphic>() : null;
            foreach (var star in canvas.GetComponentsInChildren<RewardStarGraphic>(true))
            {
                star.Artwork = Resources.Load<Texture2D>("Rewards/CaseUpgradeStar");
                star.SetAllDirty(); EditorUtility.SetDirty(star);
            }
            presentation.Apply(0);
            EditorUtility.SetDirty(presentation);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        }
    }
}
