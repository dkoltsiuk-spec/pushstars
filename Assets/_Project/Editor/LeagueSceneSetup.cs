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
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class LeagueSceneSetup
    {
        const string ArtPath = "Assets/_Project/UI/Sprites/League/";
        static TMP_FontAsset _bold, _italic;
        static Material _regularOutline, _titleOutline;
        [MenuItem("Push Stars/UI/Install Floating League Trophies")]
        public static void InstallFloatingTrophies()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open Main first.");
            var layout = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LeagueLayout>(true)).First();
            foreach (Transform child in layout.Art.Cast<Transform>().ToArray())
                if (child.name.StartsWith("BackdropCup", StringComparison.Ordinal)) Undo.DestroyObjectImmediate(child.gameObject);
            var field = layout.Background.GetComponentInChildren<LeagueTrophyField>(true);
            if (field == null)
            {
                field = LeagueTrophyField.Build(layout.Background, AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "cup-pattern.png"));
                Undo.RegisterCreatedObjectUndo(field.gameObject, "Add floating league trophies");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Push Stars/UI/Use Exported League Progress")]
        public static void UseExportedProgress()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open Main first.");
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LeagueView>(true)).First();
            foreach (var name in new[] { "progress-track", "progress-fill" })
            {
                var path = ArtPath + name + ".png";
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            var bar = view.ProgressBar;
            Undo.RecordObject(bar, "Use exported progress artwork");
            var rect = (RectTransform)bar.transform;
            Undo.RecordObject(rect, "Match reference progress dimensions");
            rect.anchoredPosition = new Vector2(72, -318);
            rect.sizeDelta = new Vector2(276, 50);
            // Remove the old custom Graphic's transient mesh after migrating to sprite children.
            var oldRenderer = bar.GetComponent<CanvasRenderer>();
            if (oldRenderer != null) oldRenderer.Clear();
            if (bar.Track == null) bar.Track = Pic(rect, "Track", "progress-track", 0, 0, 276, 50);
            if (bar.FillImage == null) bar.FillImage = Pic(rect, "Fill", "progress-fill", 0, 0, 276, 50);
            view.Refresh();
            EditorUtility.SetDirty(bar);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = bar.gameObject;
            Debug.Log("[League] Exported progress sprites installed.");
        }
        [MenuItem("Push Stars/UI/Build League Mock Screen")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/Main.unity")
                throw new InvalidOperationException("Open Main in Edit mode first.");
            var shell = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MainShellView>(true)).First();
            var so = new SerializedObject(shell);
            var panel = (GameObject)so.FindProperty("_leaguePanel").objectReferenceValue;
            if (panel.GetComponent<LeagueLayout>() != null) throw new InvalidOperationException("League is already authored. Edit the existing objects.");
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Library/LeagueBackup");
            File.Copy(scene.path, "Library/LeagueBackup/Main-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build league mock screen");
            foreach (var path in Directory.GetFiles(ArtPath, "*.png"))
            {
                var normalized = path.Replace('\\', '/');
                AssetDatabase.ImportAsset(normalized);
                var importer = (TextureImporter)AssetImporter.GetAtPath(normalized);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = normalized.EndsWith("background.png") ? 4096 : 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            _bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Bold TMP.asset");
            _italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik BoldItalic TMP.asset");
            _regularOutline = Material(_bold, "LeagueLabel", .17f);
            _titleOutline = Material(_italic, "LeagueTitle", .25f);
            var legacy = Rect(panel.transform, "LegacyLeaguePlaceholder", 0, 0, 390, 700);
            foreach (var child in panel.transform.Cast<Transform>().Where(t => t != legacy).ToArray())
                Undo.SetTransformParent(child, legacy, "Preserve placeholder");
            legacy.gameObject.SetActive(false);
            var baseImage = Pic(panel.transform, "LeagueBase", null, 0, 0, 390, 844);
            baseImage.color = Color.black;
            baseImage.rectTransform.anchorMin = Vector2.zero; baseImage.rectTransform.anchorMax = Vector2.one;
            baseImage.rectTransform.offsetMin = baseImage.rectTransform.offsetMax = Vector2.zero;
            var backdrop = Pic(panel.transform, "LeagueBackground", "background", 0, 0, 390, 844);
            backdrop.rectTransform.anchorMin = backdrop.rectTransform.anchorMax = backdrop.rectTransform.pivot = new Vector2(.5f, .5f);
            var art = Rect(panel.transform, "LeagueArt", 0, 0, 390, 700);
            art.anchorMin = art.anchorMax = new Vector2(.5f, 1);
            art.pivot = new Vector2(.5f, 1);
            var layout = Undo.AddComponent<LeagueLayout>(panel);
            layout.Art = art; layout.Background = backdrop.rectTransform;
            var view = panel.GetComponent<LeagueView>() ?? Undo.AddComponent<LeagueView>(panel);
            LeagueTrophyField.Build(backdrop.rectTransform, AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "cup-pattern.png"));
            Pic(art, "LeagueHero", "hero", 0, -8, 390, 360).preserveAspect = true;
            Pic(art, "ProgressPlaque", "progress-plaque", 41, 276, 340, 119);
            view.ProgressBar = Rect(art, "TrophyProgress", 72, 318, 276, 50).gameObject.AddComponent<LeagueProgressGraphic>();
            view.ProgressBar.Track = Pic(view.ProgressBar.transform, "Track", "progress-track", 0, 0, 276, 50);
            view.ProgressBar.FillImage = Pic(view.ProgressBar.transform, "Fill", "progress-fill", 0, 0, 276, 50);
            Pic(art, "ProgressCup", "cup-progress", -2, 307, 111, 104).preserveAspect = true;
            view.Title = Text(art, "LeagueTitle", view.LeagueName, 23, 241, 349, 55, 40, true, new Color32(255, 155, 0, 255));
            view.Title.rectTransform.localEulerAngles = new Vector3(0, 0, 5);
            view.Title.characterSpacing = -1;
            view.Score = Text(art, "TrophyScore", "955", 135, 283, 140, 62, 54, true, new Color32(255, 153, 0, 255));
            view.Season = Text(art, "SeasonCountdown", "", 40, 400, 310, 27, 17, true, Color.white);
            var glow = Pic(art, "OnlineGlow", null, 255, 439, 17, 17);
            glow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/glow_radial.png");
            glow.color = new Color(0.25f, 1, 0, .55f);
            var dot = Pic(art, "OnlineDot", null, 260, 444, 7, 7);
            dot.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/circle_128.png");
            dot.color = new Color32(72, 255, 0, 255);
            view.Online = Text(art, "OnlinePlayers", "126 online", 275, 435, 105, 26, 17, false, new Color32(72, 255, 0, 255));
            view.Names = new TextMeshProUGUI[4]; view.Scores = new TextMeshProUGUI[4];
            string[] cards = { "row-first", "row-second", "row-third", "row-player" };
            Color32[] colors = { new Color32(255,204,0,255), new Color32(214,213,194,255), new Color32(255,159,0,255), new Color32(255,159,0,255) };
            for (int i = 0; i < 4; i++)
            {
                var row = Rect(art, i == 3 ? "CurrentPlayerRow" : "RankRow" + (i + 1), 12, 471 + i * 58, 366, 61);
                Pic(row, "Card", cards[i], 0, 0, 366, 61);
                Text(row, "Rank", (i + 1).ToString(), 12, 5, 42, 48, 36, true, colors[i]);
                view.Names[i] = Text(row, "PlayerName", view.Players[i].Name, 60, 13, 181, 31, 18, false, Color.white);
                view.Names[i].alignment = TextAlignmentOptions.MidlineLeft;
                Pic(row, "TrophyIcon", "cup-small", 254, 16, 32, 31).preserveAspect = true;
                view.Scores[i] = Text(row, "Trophies", view.Players[i].Trophies.ToString(), 289, 9, 61, 40, 27, false, Color.white);
            }
            var tab = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TabButton>(true)).First(t => t.TabId == TabId.League);
            var tabSo = new SerializedObject(tab);
            var active = (Image)tabSo.FindProperty("_activeIndicator").objectReferenceValue;
            Undo.RecordObject(active, "Set active league icon"); active.sprite = S("nav-active"); active.color = Color.white;
            view.Refresh(); layout.Fit();
            ((GameObject)so.FindProperty("_duelPanel").objectReferenceValue).SetActive(false);
            ((GameObject)so.FindProperty("_profilePanel").objectReferenceValue).SetActive(false);
            panel.SetActive(true);
            foreach (var button in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TabButton>(true))) button.SetActive(button.TabId == TabId.League);
            EditorUtility.SetDirty(view); EditorUtility.SetDirty(layout);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            LeaguePresentationSetup.Install();
            Selection.activeGameObject = panel;
            Debug.Log("[League] Mock screen authored and saved in Main. Fixture values are editable on LeagueView.");
        }
        static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
        static Material Material(TMP_FontAsset font, string name, float width)
        {
            var mat = new Material(font.material) { name = name };
            mat.SetColor("_FaceColor", Color.white); mat.SetColor("_OutlineColor", Color.black);
            mat.SetFloat("_OutlineWidth", width); mat.SetFloat("_FaceDilate", width);
            mat.SetFloat("_WeightNormal", 0); mat.SetFloat("_WeightBold", 0);
            mat.SetColor("_UnderlayColor", Color.black); mat.SetFloat("_UnderlayOffsetY", -.35f);
            mat.SetFloat("_UnderlayDilate", .05f); mat.SetFloat("_UnderlaySoftness", 0);
            mat.EnableKeyword("UNDERLAY_ON"); mat.DisableKeyword("UNDERLAY_INNER");
            ShaderUtilities.UpdateShaderRatios(mat);
            AssetDatabase.CreateAsset(mat, ArtPath + name + ".mat"); return mat;
        }
        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create league element"); go.layer = 5;
            var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h); return r;
        }
        static Image Pic(Transform parent, string name, string sprite, float x, float y, float w, float h)
        {
            var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite == null ? null : S(sprite); image.raycastTarget = false; return image;
        }
        static TextMeshProUGUI Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, bool italic, Color color)
        {
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = italic ? _italic : _bold; text.fontSharedMaterial = italic ? _titleOutline : _regularOutline;
            text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
            text.fontSize = size; text.text = value; text.color = color;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
            text.UpdateMeshPadding(); return text;
        }
    }
}
