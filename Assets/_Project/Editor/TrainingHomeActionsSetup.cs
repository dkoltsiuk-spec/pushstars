using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Wire training artwork into the existing action buttons without rebuilding Main.</summary>
    public static class TrainingHomeActionsSetup
    {
        public static void Install(Scene scene)
        {
            const string art = "Assets/_Project/UI/Sprites/ModeSelection/";
            const string iconPath = art + "training-settings-icon.png";
            const string defaultBackgroundPath = "Assets/_Project/UI/Sprites/IMG_0916.PNG";
            const string trainingBackgroundPath = "Assets/_Project/UI/Sprites/bg_training_home.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var roots = scene.GetRootGameObjects();
            var controller = roots.SelectMany(r => r.GetComponentsInChildren<ModeSelectionController>(true)).Single();
            var buttons = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).ToArray();
            var defaultBackground = SpriteImporter.Load(defaultBackgroundPath);
            var trainingBackground = SpriteImporter.Load(trainingBackgroundPath);
            var homeBackground = roots.SelectMany(r => r.GetComponentsInChildren<Image>(true))
                .FirstOrDefault(i => i.sprite == defaultBackground)
                ?? roots.SelectMany(r => r.GetComponentsInChildren<Image>(true))
                    .FirstOrDefault(i => i.name == "Background" && i.rectTransform.anchorMin == Vector2.zero
                        && i.rectTransform.anchorMax == Vector2.one);
            var right = buttons.Single(b => b.name == "PushupButton");
            var action = buttons.Single(b => b.name == "BattleButton");
            var label = right.GetComponentInChildren<TextMeshProUGUI>(true);
            label.enableAutoSizing = true;
            label.fontSizeMin = 8;
            label.fontSizeMax = 12;
            label.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(art + "ModeOutline.mat");
            var so = new SerializedObject(controller);
            so.FindProperty("_actionLabel").objectReferenceValue = action.GetComponentInChildren<TextMeshProUGUI>(true);
            so.FindProperty("_settingsLabel").objectReferenceValue = label;
            so.FindProperty("_settingsIcon").objectReferenceValue = right.transform.Find("Icon").GetComponent<Image>();
            so.FindProperty("_trainingSettingsIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            so.FindProperty("_homeBackground").objectReferenceValue = homeBackground;
            so.FindProperty("_trainingBackground").objectReferenceValue = trainingBackground;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
