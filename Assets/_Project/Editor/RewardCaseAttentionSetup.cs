using System;
using System.Linq;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class RewardCaseAttentionSetup
    {
        private const string HandPath = "Assets/_Project/Resources/Rewards/TapHand.png";

        [MenuItem("Tools/Push Stars/Rewards/Configure Case Attention")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits first.");
            try
            {
                var scene = EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(FightScreen.CaseOpening));
                Attach(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single());
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        public static void Attach(RewardScreen screen)
        {
            var importer = AssetImporter.GetAtPath(HandPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing supplied TapHand.png.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();

            var chest = (RectTransform)screen.CaseUi.Content.parent;
            var hand = Child(chest, "TapHand", new Vector2(225, -232), new Vector2(94, 99));
            var oldHand = hand.GetComponent<CaseTapGraphic>();
            if (oldHand != null) Object.DestroyImmediate(oldHand);
            var image = hand.GetComponent<Image>();
            if (image == null) image = hand.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(HandPath);
            image.preserveAspect = true; image.raycastTarget = false;
            var group = hand.GetComponent<CanvasGroup>();
            if (group == null) group = hand.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            screen.CaseUi.TapHint = group;

            var rays = chest.Find("Rays") as RectTransform;
            if (rays == null)
            {
                rays = Child(chest, "Rays", new Vector2(chest.rect.width / 2, -chest.rect.height / 2), new Vector2(390, 470));
                var graphic = rays.gameObject.AddComponent<CaseTapGraphic>();
                graphic.Rays = true; graphic.raycastTarget = false;
                rays.SetAsFirstSibling();
            }
            screen.CaseUi.Rays = rays;
            rays.sizeDelta = new Vector2(440, 440);
            screen.CaseUi.Glow.rectTransform.sizeDelta = new Vector2(380, 380);
            screen.CaseUi.Glow.preserveAspect = true;

            // A single click target above the composition covers even the screen corners.
            // Reuse the normal button event so a tap cannot hit both the chest and backdrop.
            var canvas = chest.GetComponentInParent<Canvas>().rootCanvas;
            var surface = Child((RectTransform)canvas.transform, "CaseScreenTap", Vector2.zero, Vector2.zero);
            surface.anchorMin = Vector2.zero; surface.anchorMax = Vector2.one;
            surface.offsetMin = surface.offsetMax = Vector2.zero;
            surface.SetAsLastSibling();
            var hit = surface.GetComponent<Image>();
            if (hit == null) hit = surface.gameObject.AddComponent<Image>();
            hit.color = Color.clear; hit.raycastTarget = true;
            var button = surface.GetComponent<Button>();
            if (button == null) button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = hit; button.transition = Selectable.Transition.None;
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, screen.TapCase);
            if (screen.CaseUi.TapButton != null && screen.CaseUi.TapButton != button)
                screen.CaseUi.TapButton.enabled = false;
            screen.CaseUi.TapButton = button;
            CaseRaritySceneSetup.Configure(screen);
            EditorUtility.SetDirty(screen);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        }

        private static RectTransform Child(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null)
            {
                rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(parent, false);
            }
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }
    }
}
