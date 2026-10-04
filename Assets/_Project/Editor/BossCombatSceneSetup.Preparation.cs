using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static partial class BossCombatSceneSetup
    {
        [MenuItem("Tools/Push Stars/Boss/Update Preparation Design")]
        public static void RunPreparationUpgrade()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            const string path = "Assets/_Project/Scenes/FightPreparation.unity";
            Directory.CreateDirectory("output/boss-preparation");
            File.Copy(path, "output/boss-preparation/before-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".unity");
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                ApplyPreparationPolish(Find<BossCombatScreen>(scene), Find<DuelReadyPanel>(scene));
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ApplyPreparationPolish(BossCombatScreen screen, DuelReadyPanel pvp)
        {
            var content = screen.Content;
            var root = (RectTransform)screen.Root.transform;
            var safe = Rect(root, "SafeArea", 0, 0, 0, 0);
            Stretch(safe); Get<SafeAreaFitter>(safe);
            screen.PreparationSafeBounds = safe;
            content.SetParent(safe, false);
            content.anchoredPosition = Vector2.zero;
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            var labels = pvp.Root.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text Reference(string name) => labels.First(t => t.name == name);
            void Place(RectTransform rect, float x, float y, float w, float h)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(x + w * .5f - 195f, 422f - y - h * .5f);
                rect.sizeDelta = new Vector2(w, h);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
            }
            void Style(TMP_Text target, TMP_Text source)
            {
                target.font = source.font;
                target.fontSharedMaterial = source.fontSharedMaterial;
                target.fontStyle = source.fontStyle;
                target.fontSize = source.fontSize;
                target.color = source.color;
                target.enableAutoSizing = false;
                target.textWrappingMode = TextWrappingModes.NoWrap;
                target.overflowMode = TextOverflowModes.Ellipsis;
                target.UpdateMeshPadding();
            }
            void Name(TMP_Text target, string source, float x, float y, float w, bool right)
            {
                Style(target, Reference(source));
                Place(target.rectTransform, x, y, w, 112f);
                target.rectTransform.localRotation = Quaternion.Euler(0, 0, 4);
                target.textWrappingMode = TextWrappingModes.Normal;
                target.alignment = right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
                target.fontSize = 38f;
                target.enableAutoSizing = !right;
                target.fontSizeMin = 26f; target.fontSizeMax = 38f;
            }
            void Stat(TMP_Text value, string captionName, string reference, float y, bool bonus)
            {
                Style(value, Reference(reference));
                Place(value.rectTransform, 246f, y + 13f, 128f, 38f);
                value.fontSize = 30f;
                value.alignment = TextAlignmentOptions.TopRight;
                value.enableAutoSizing = true; value.fontSizeMin = 22f; value.fontSizeMax = 30f;
                value.color = bonus ? new Color32(255, 211, 0, 255) : Color.white;
                var caption = content.Find(captionName).GetComponent<TMP_Text>();
                Style(caption, Reference("PlayerBestCaption"));
                Place(caption.rectTransform, 216f, y, 158f, 13f);
                caption.fontSize = 9f; caption.alignment = TextAlignmentOptions.TopRight;
            }

            foreach (Transform child in content)
                if (child.name.StartsWith("Pattern", StringComparison.Ordinal) ||
                    new[] { "VsDivider", "VS", "BossIcon", "PlayerIcon", "PushupIcon" }.Contains(child.name))
                    child.gameObject.SetActive(false);

            var oldBackdrop = root.Find("Backdrop");
            if (oldBackdrop != null) oldBackdrop.gameObject.SetActive(false);
            var background = Rect(root, "PreparationBackdrop", 0, 0, 0, 0);
            Stretch(background); background.SetAsFirstSibling();

            // The stationary medal box is also the backdrop's seam anchor and the impact's target.
            var medal = Get<Image>(Rect(content, "VsMedal", 0, 8, 98, 98));
            medal.sprite = Resources.Load<Sprite>("MatchFound/VsCrown");
            medal.color = Color.white; medal.preserveAspect = true; medal.raycastTarget = false;
            medal.transform.SetAsLastSibling();

            Get<PreparationArenaBackdrop>(background).SetMaps("jungle", "jungle");
            Place(screen.BossPortrait.rectTransform, 192, 8, 180, 368);
            Place(screen.PlayerPortrait.rectTransform, 16, 364, 182, 368);
            foreach (var entry in new[] { ("OpponentGroundShadow", screen.BossPortrait), ("PlayerGroundShadow", screen.PlayerPortrait) })
            {
                var shadow = Get<Image>(Rect(content, entry.Item1, 0, 0, 140, 32));
                shadow.sprite = theme.CircleShape; shadow.color = new Color(0, 0, 0, .34f);
                shadow.raycastTarget = false;
                shadow.transform.SetSiblingIndex(entry.Item2.transform.GetSiblingIndex());
            }
            Name(screen.BossName, "OpponentName", 18, 88, 222, false);
            Name(screen.PlayerName, "PlayerName", 132, 445, 244, true);
            Place(screen.BossHpFill.rectTransform, 18, 220, 188, 28);
            Place(screen.BossHpText.rectTransform, 8, 2, 172, 24);
            screen.BossHpText.rectTransform.anchoredPosition = Vector2.zero;
            Place(screen.Stars.rectTransform, 18, 266, 142, 28);
            Place(screen.PlayerHpFill.rectTransform, 216, 561, 158, 28);
            Place(screen.PlayerHpText.rectTransform, 8, 2, 142, 24);
            screen.PlayerHpText.rectTransform.anchoredPosition = Vector2.zero;
            Stat(screen.Best, "BestCaption", "PlayerBest", 611, false);
            Stat(screen.Reward, "RewardCaption", "PlayerWinRate", 670, true);
            var rewardIcon = content.Find("RewardIcon") as RectTransform;
            Place(rewardIcon, 265, 684, 23, 28);

            Place(screen.Action.GetComponent<RectTransform>(), 133, 748, 124, 54);
            var pvpReady = (Button)Ref(pvp, "_readyButton");
            Style(screen.ActionLabel, pvpReady.GetComponentInChildren<TMP_Text>(true));
            screen.ActionLabel.fontSize = 20f;
            screen.ActionLabel.alignment = TextAlignmentOptions.Center;
            var actionRect = screen.ActionLabel.rectTransform;
            actionRect.anchorMin = Vector2.zero; actionRect.anchorMax = Vector2.one;
            actionRect.offsetMin = new Vector2(8, 54f * .09f); actionRect.offsetMax = new Vector2(-8, 0);
            Place(screen.Home.GetComponent<RectTransform>(), 303, 12, 69, 25);
            EditorUtility.SetDirty(screen);
        }
    }
}
