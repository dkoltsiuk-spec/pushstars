using System;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class GemRewardSceneSetup
    {
        [MenuItem("Tools/Push Stars/Rewards/Align Gems To Mockup")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits first.");
            try
            {
                var scene = EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(FightScreen.CaseReward));
                Configure(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single());
                foreach (var root in scene.GetRootGameObjects()) FightPresentationSceneBuilder.PersistTextMaterials(root);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        public static void Configure(RewardScreen screen)
        {
            var ui = screen.PrizeUi;
            var canvas = ui.Content.GetComponentInParent<Canvas>().rootCanvas;
            ConfigureLightning(canvas);
            var composition = ui.Content.parent.parent as RectTransform;
            if (composition.GetComponent<RewardCompositionFitter>() == null)
                composition.gameObject.AddComponent<RewardCompositionFitter>();
            foreach (string name in new[] { "rarity", "claim-button", "home-button" })
                composition.Find(name).gameObject.SetActive(false);
            // Keep a hidden, usable message slot for save errors; normal rewards have no extra copy.
            ui.Note.gameObject.SetActive(false);
            Place((RectTransform)ui.Note.transform.parent, 195, 710, 340, 50);
            Place((RectTransform)composition.Find("header"), 195, 195, 340, 45);
            var title = composition.Find("header").GetComponentInChildren<TextMeshProUGUI>(true);
            title.text = "GEMS"; title.fontSize = title.fontSizeMax = 30;
            title.enableAutoSizing = false; title.fontStyle = FontStyles.Normal;
            title.fontSharedMaterial = new Material(title.font.material) { name = "Gem Reward Clean Title" };
            title.fontSharedMaterial.SetFloat("_OutlineWidth", 0);
            title.fontSharedMaterial.SetFloat("_FaceDilate", 0);
            title.fontSharedMaterial.DisableKeyword("UNDERLAY_ON");
            title.color = Color.white;
            Place((RectTransform)ui.Content.parent, 195, 428, 192, 182);
            Place((RectTransform)ui.Amount.transform.parent, 195, 514, 240, 38);
            ui.Amount.fontSize = ui.Amount.fontSizeMax = 22;
            ui.Amount.fontSizeMin = 18;
            ui.Amount.text = "×100";
            ui.Glow.rectTransform.sizeDelta = new Vector2(310, 310);
            ui.Glow.color = new Color(.65f, 1f, .14f, .38f);
            var rays = ui.Content.parent.Find("PrizeRays") as RectTransform;
            if (rays == null)
            {
                rays = new GameObject("PrizeRays", typeof(RectTransform), typeof(CaseTapGraphic)).GetComponent<RectTransform>();
                rays.SetParent(ui.Content.parent, false);
            }
            rays.anchorMin = rays.anchorMax = rays.pivot = new Vector2(.5f, .5f);
            rays.anchoredPosition = Vector2.zero; rays.sizeDelta = new Vector2(380, 380);
            rays.SetAsFirstSibling();
            var graphic = rays.GetComponent<CaseTapGraphic>();
            graphic.Rays = true; graphic.RayTint = new Color(.74f, 1f, .22f, .7f); graphic.raycastTarget = false;
            ui.Rays = rays;

            var surface = canvas.transform.Find("PrizeScreenTap") as RectTransform;
            if (surface == null)
            {
                surface = new GameObject("PrizeScreenTap", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
                surface.SetParent(canvas.transform, false);
            }
            surface.anchorMin = Vector2.zero; surface.anchorMax = Vector2.one;
            surface.offsetMin = surface.offsetMax = Vector2.zero; surface.SetAsLastSibling();
            var hit = surface.GetComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            var button = surface.GetComponent<Button>(); button.targetGraphic = hit; button.transition = Selectable.Transition.None;
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, screen.ClaimPrize);
            ui.ClaimButton = button;
            if (!screen.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                .Any(c => c.enabled && c.targetTexture == null && c.cullingMask == 0))
            {
                var camera = new GameObject("DisplayClearCamera", typeof(Camera)).GetComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject, screen.gameObject.scene);
                camera.cullingMask = 0; camera.depth = -100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(39, 148, 0, 255);
            }
            EditorUtility.SetDirty(screen);
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        }

        public static void ConfigureLightning(Canvas canvas)
        {
            var field = canvas.GetComponentInChildren<LightningField>(true);
            if (field == null)
                field = LightningField.Build((RectTransform)canvas.transform,
                    Resources.Load<PushStarsTheme>("PushStarsTheme").IconLightningBG);
            field.gameObject.SetActive(true);
            field.enabled = true;
            field.transform.SetSiblingIndex(1);
            // The supplied bolt sprite already carries its soft transparency. Keep the
            // pattern visible across the whole green field, including the screen edges.
            var data = new SerializedObject(field);
            data.FindProperty("_edgeStart").floatValue = .35f;
            data.FindProperty("_edgeFade").floatValue = .35f;
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (var icon in field.GetComponentsInChildren<Image>(true))
            {
                Vector2 point = icon.rectTransform.anchoredPosition;
                float edge = Mathf.Max(Mathf.Abs(point.x) / 195f, Mathf.Abs(point.y) / 422f);
                icon.color = new Color(1, 1, 1, 1 - .35f * Mathf.SmoothStep(.35f, 1, edge));
                icon.raycastTarget = false;
                EditorUtility.SetDirty(icon);
            }
            EditorUtility.SetDirty(field);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
    }
}
