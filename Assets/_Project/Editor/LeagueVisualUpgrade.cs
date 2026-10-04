using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class LeagueVisualUpgrade
    {
        const string ArtPath = "Assets/_Project/UI/Sprites/League/";
        static TMP_FontAsset _bold, _italic;
        static Material _label, _title;

        [MenuItem("Push Stars/UI/Upgrade League Visuals")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main in Edit mode first.");
            foreach (string name in new[] { "cup-bronze", "cup-silver", "cup-gold", "cup-diamond", "season-history", "backdrop" })
            {
                string path = ArtPath + "Illustrated/" + name + ".png";
                if (!File.Exists(path)) throw new FileNotFoundException("Generated league asset missing", path);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                importer.maxTextureSize = name == "backdrop" ? 2048 : 512;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LeagueView>(true)).First();
            Directory.CreateDirectory("Library/LeagueBackup"); EditorSceneManager.SaveScene(scene);
            File.Copy(scene.path, "Library/LeagueBackup/Main-illustrated-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Illustrated league redesign");
            var layout = view.GetComponent<LeagueLayout>(); var art = layout.Art;
            foreach (Transform row in layout.Rows.Cast<Transform>().ToArray())
                if (row.name == "Ranked" || row.name == "Archive" || row.name == "Retry" || row.name == "LoadMore" || row.name.StartsWith("OnlinePlayer_", StringComparison.Ordinal))
                    Undo.DestroyObjectImmediate(row.gameObject);
            var pinned = view.transform.Find("PinnedOwnRank"); if (pinned != null) Undo.DestroyObjectImmediate(pinned.gameObject);
            view.SendMessage("OnDisable", SendMessageOptions.DontRequireReceiver);
            _bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Bold TMP.asset");
            _italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik BoldItalic TMP.asset");
            _label = AssetDatabase.LoadAssetAtPath<Material>(ArtPath + "LeagueLabel.mat");
            _title = AssetDatabase.LoadAssetAtPath<Material>(ArtPath + "LeagueTitle.mat");
            var style = view.GetComponent<LeagueVisualStyle>() ?? Undo.AddComponent<LeagueVisualStyle>(view.gameObject);
            foreach (var surface in view.GetComponentsInChildren<LeagueSurface>(true)) surface.gameObject.SetActive(false);
            foreach (var name in new[] { "LeagueEyebrow", "ProgressPlaque", "ProgressCup", "CurrentTierMarker", "OnlineGlow", "OnlineDot" })
                if (art.Find(name) != null) art.Find(name).gameObject.SetActive(false);
            foreach (Transform child in art)
                if (child.name.StartsWith("TierName") || child.name.StartsWith("TierConnector")) child.gameObject.SetActive(false);
            foreach (var field in view.GetComponentsInChildren<LeagueTrophyField>(true)) field.gameObject.SetActive(false);
            var background = layout.Background.GetComponent<Image>(); background.enabled = true; background.sprite = S("Illustrated/backdrop");
            style.BackdropImage = background;
            style.RankedPlate = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/btn_start.png");
            style.SecondaryPlate = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/type_settings.png");
            style.HistoryIcon = S("Illustrated/season-history");
            Wrap(art, view.Hero.rectTransform, "LeagueHeroEntrance", -10, -8, 410, 378);
            view.Hero.rectTransform.pivot = new Vector2(.5f, .5f);
            view.Hero.preserveAspect = true;
            view.Hero.raycastTarget = true;
            var swipe = view.Hero.GetComponent<LeagueHeroSwipe>() ?? Undo.AddComponent<LeagueHeroSwipe>(view.Hero.gameObject);
            swipe.View = view; swipe.PageScroll = layout.PageScroll;
            Wrap(art, view.Title.rectTransform, "LeagueTitleEntrance", 8, 298, 374, 60);
            view.Title.font = _italic;
            view.Title.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtPath + "LeagueHeading.mat") ?? _title;
            view.Title.UpdateMeshPadding();
            view.Title.fontSize = 43; view.Title.enableAutoSizing = true; view.Title.fontSizeMin = 31; view.Title.fontSizeMax = 43;
            view.Title.characterSpacing = -1.5f; view.Title.rectTransform.localEulerAngles = new Vector3(0, 0, 4);
            style.Heading = view.Title;

            var bar = view.ProgressBar; bar.gameObject.SetActive(true); bar.StyledRail = null;
            Place((RectTransform)bar.transform, 32, 467, 326, 34);
            Pic(bar.transform, "RoadOutline", S("progress-track"), 0, 0, 326, 34, Color.black).transform.SetAsFirstSibling();
            bar.Track.gameObject.SetActive(true); bar.FillImage.gameObject.SetActive(true);
            Place(bar.Track.rectTransform, 3, 3, 320, 28); bar.Track.sprite = S("progress-fill"); bar.Track.color = new Color32(35, 39, 54, 255);
            Place(bar.FillImage.rectTransform, 3, 3, 320, 28); bar.FillImage.sprite = S("progress-fill"); bar.FillImage.color = Color.white;
            bar.FillImage.transform.SetAsLastSibling();
            style.DivisionTicks = new GameObject[3];
            for (int i = 1; i < 4; i++)
            {
                var tick = Pic(bar.transform, "RoadTick" + i, null, 3 + i * 320f / 4, 6, 1.5f, 20, Color.white);
                tick.transform.SetAsLastSibling(); style.DivisionTicks[i - 1] = tick.gameObject;
            }
            style.TierIcons = new Image[5]; style.TierLabels = new TextMeshProUGUI[5];
            style.TrophySprites = new[] { S("Illustrated/cup-bronze"), S("Illustrated/cup-silver"), S("Illustrated/cup-gold"), S("Illustrated/cup-diamond") };
            for (int i = 0; i < 5; i++)
            {
                // Keep numeric subdivisions; only the current and next league get cup icons.
                style.TierIcons[i] = Pic(art, "RoadIcon" + i, style.TrophySprites[0], 15 + i * 80, 426, 40, 38, Color.white);
                style.TierIcons[i].preserveAspect = true;
                OutlineCup(style.TierIcons[i]);
                style.TierLabels[i] = Text(art, "RoadThreshold" + i, "", 7 + i * 80, 504, 56, 19, 13, true);
            }
            style.Minimum = style.TierLabels[0]; style.Maximum = style.TierLabels[4];
            var scoreWrapper = Rect(art, "TrophyScoreEntrance", 55, 350, 280, 78);
            scoreWrapper.pivot = new Vector2(.5f, .5f); scoreWrapper.anchoredPosition += new Vector2(140, -39);
            var marker = Rect(scoreWrapper, "ScoreMarker", 0, 0, 280, 78);
            bar.ScoreMarker = null;
            var crown = marker.Find("ScoreCrown"); if (crown != null) Undo.DestroyObjectImmediate(crown.gameObject);
            style.ScoreCup = Pic(marker, "ScoreCup", style.TrophySprites[0], 0, 0, 82, 76, Color.white);
            style.ScoreCup.preserveAspect = true;
            OutlineCup(style.ScoreCup);
            view.Score.rectTransform.SetParent(marker, false); Place(view.Score.rectTransform, 88, 0, 184, 76);
            view.Score.font = _italic; view.Score.fontSharedMaterial = _title; view.Score.color = Color.white;
            view.Score.fontSize = 60; view.Score.fontSizeMin = 28; view.Score.fontSizeMax = 60; view.Score.enableAutoSizing = true;
            view.Score.alignment = TextAlignmentOptions.Left;
            style.ScoreLabel = view.Score;
            style.Next = Text(art, "NextLeagueGoal", "", 32, 511, 326, 26, 20, true);
            style.Next.gameObject.SetActive(false);
            style.Remaining = null;
            Text(art, "LeaderboardHeading", "LEADERBOARD", 20, 548, 238, 28, 23, true).alignment = TextAlignmentOptions.Left;
            Place(view.Online.rectTransform, 255, 554, 116, 20);
            view.Online.font = _bold; view.Online.fontSharedMaterial = _label; view.Online.color = new Color32(208, 225, 255, 255);
            view.Online.fontSize = 12; view.Online.enableAutoSizing = true; view.Online.fontSizeMin = 8; view.Online.fontSizeMax = 12;
            view.Online.alignment = TextAlignmentOptions.Right;
            Place(view.Season.rectTransform, 22, 578, 346, 18);
            view.Season.font = _italic; view.Season.fontSharedMaterial = _label; view.Season.color = Color.white;
            view.Season.fontSize = 11; view.Season.enableAutoSizing = true; view.Season.fontSizeMin = 8; view.Season.fontSizeMax = 11;
            view.Season.alignment = TextAlignmentOptions.Left;
            Place(layout.Rows, 12, 607, 366, layout.Rows.sizeDelta.y);
            foreach (RectTransform row in layout.Rows)
            {
                var card = row.Find("Card")?.GetComponent<Image>(); if (card == null) continue;
                card.enabled = true; card.sprite = S("row-player"); card.color = Color.white;
                // The card sprite already contains its black lower edge and shadow.
                var depth = row.Find("CardDepth");
                if (depth != null) Undo.DestroyObjectImmediate(depth.gameObject);
                var name = row.Find("PlayerName").GetComponent<TextMeshProUGUI>(); name.fontSize = 18; name.fontSizeMax = 18;
                var trophyIcon = row.Find("TrophyIcon").GetComponent<Image>();
                trophyIcon.sprite = S("Illustrated/cup-bronze");
                OutlineCup(trophyIcon);
                var score = row.Find("Trophies").GetComponent<TextMeshProUGUI>(); score.enableAutoSizing = true; score.fontSizeMin = 12; score.fontSizeMax = 27;
            }
            view.Refresh(); layout.Fit(); bar.Refresh(); layout.PageScroll.verticalNormalizedPosition = 1;
            EditorUtility.SetDirty(style); EditorUtility.SetDirty(view); EditorUtility.SetDirty(bar);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log("[League] Illustrated trophy road and large centered hero saved.");
        }
        static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
        static void OutlineCup(Image image)
        {
            var outline = image.GetComponent<Outline>() ?? Undo.AddComponent<Outline>(image.gameObject);
            outline.effectColor = new Color32(30, 18, 12, 230);
            outline.effectDistance = new Vector2(.8f, .8f);
            outline.useGraphicAlpha = true;
        }
        static void Wrap(Transform root, RectTransform child, string name, float x, float y, float w, float h)
        {
            var wrapper = Rect(root, name, x, y, w, h); wrapper.pivot = new Vector2(.5f, .5f); wrapper.anchoredPosition += new Vector2(w / 2, -h / 2);
            child.SetParent(wrapper, false); child.anchorMin = child.anchorMax = new Vector2(.5f, .5f); child.pivot = new Vector2(0, 1);
            child.anchoredPosition = new Vector2(-w / 2, h / 2); child.sizeDelta = new Vector2(w, h); child.localScale = Vector3.one;
        }
        static void Place(RectTransform r, float x, float y, float w, float h)
        { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); }
        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = parent.Find(name) as RectTransform;
            if (r == null) { var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; Undo.RegisterCreatedObjectUndo(go, "Add illustrated league element"); r = (RectTransform)go.transform; r.SetParent(parent, false); }
            r.gameObject.SetActive(true); Place(r, x, y, w, h); return r;
        }
        static Image Pic(Transform parent, string name, Sprite sprite, float x, float y, float w, float h, Color color)
        {
            var r = Rect(parent, name, x, y, w, h); var image = r.GetComponent<Image>() ?? r.gameObject.AddComponent<Image>();
            image.enabled = true; image.sprite = sprite; image.color = color; image.raycastTarget = false; return image;
        }
        static TextMeshProUGUI Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, bool italic)
        {
            var r = Rect(parent, name, x, y, w, h); var text = r.GetComponent<TextMeshProUGUI>() ?? r.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = italic ? _italic : _bold; text.fontSharedMaterial = italic ? _title : _label; text.text = value;
            text.fontSize = size; text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; return text;
        }
    }
}
