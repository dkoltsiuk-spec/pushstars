using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class BossMapValidation
    {
        private const string Output = "output/boss-map/";
        [MenuItem("Tools/Push Stars/Validate Boss Map")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            bool hadMode = PlayerPrefs.HasKey("selected_game_mode");
            int savedMode = PlayerPrefs.GetInt("selected_game_mode");
            var scene = EditorSceneManager.OpenPreviewScene(AuthoredScenes.MainPath);
            RenderTexture texture = null;
            var previous = RenderTexture.active;
            try
            {
                var roots = scene.GetRootGameObjects();
                var c = roots.SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).Single();
                var panel = c.transform.parent;
                foreach (Transform sibling in panel.parent)
                    if (sibling.name == "LeaguePanel" || sibling.name == "ProfilePanel") sibling.gameObject.SetActive(false);
                panel.gameObject.SetActive(true);
                var canvas = c.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                foreach (var mirror in canvas.GetComponentsInChildren<DeviceSimulatorMirrorFix>(true))
                { mirror.enabled = false; mirror.transform.localRotation = Quaternion.identity; }
                canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
                var rect = (RectTransform)canvas.transform;
                rect.position = Vector3.zero; rect.localScale = Vector3.one; rect.sizeDelta = new Vector2(390, 844);
                var camera = new GameObject("BossValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                texture = new RenderTexture(780, 1688, 24); camera.targetTexture = texture; canvas.worldCamera = camera;
                SelectedGameMode.Current = GameMode.Boss;
                Canvas.ForceUpdateCanvases(); c.RefreshMode();
                Require(c.Home.activeSelf && !c.Map.activeSelf && c.Background.activeSelf, "Boss opens island home");
                Require(c.CharacterDecor.All(go => !go.activeSelf), "Character, glow, wardrobe and friend entry hidden");
                Require(c.Nodes.Count(n => n.Fight.gameObject.activeSelf) == 1, "Exactly one active boss");
                Require(c.Nodes.Length == BossCatalog.Bosses.Count && c.Nodes.Select(n => n.BossIndex).SequenceEqual(Enumerable.Range(0, 5)), "Five ordered boss nodes");
                Require(c.Nodes.All(n => n.Button && n.Platform.sprite && n.Disc.sprite && n.Face.sprite && n.ProgressFace.sprite), "All boss nodes have art and interaction");
                var actionLabel = panel.Find("ActionRow/BattleButton/Label").GetComponent<TMPro.TextMeshProUGUI>();
                actionLabel.text = "FIGHT";
                var modeLabel = panel.Find("ActionRow/PvpButton/Label").GetComponent<TMPro.TextMeshProUGUI>();
                modeLabel.text = "BOSS";
                panel.Find("ActionRow/PvpButton/Icon").GetComponent<Image>().sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/UI/Sprites/ModeSelection/boss-icon.png").OfType<Sprite>().First();
                Capture(camera, texture, "home");
                SetMap(c, true);
                Require(c.IsMapOpen && !c.Home.activeSelf && !c.BottomNav.activeSelf, "Map hides home actions and nav");
                Require(c.MapContent.rect.height > c.Scroll.viewport.rect.height, "Map has real vertical scroll range");
                ValidateEffects(c);
                Capture(camera, texture, "map-bottom");
                c.Scroll.verticalNormalizedPosition = 1;
                Capture(camera, texture, "map-top");
                ValidateBiomes(c, camera, texture);
                SetMap(c, false);
                Require(c.Home.activeSelf && c.BottomNav.activeSelf && c.HomeOnly.All(go => go.activeSelf), "OK restores home controls");
                rect.sizeDelta = new Vector2(320, 568); camera.orthographicSize = 284;
                camera.targetTexture = null; RenderTexture.active = null; texture.Release(); Object.DestroyImmediate(texture);
                texture = new RenderTexture(640, 1136, 24); camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
                typeof(BossMapController).GetMethod("Fit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, null);
                Capture(camera, texture, "home-small");
                SetMap(c, true); Capture(camera, texture, "map-small");
                SelectedGameMode.Current = GameMode.Pvp; c.RefreshMode();
                Require(!c.Home.activeSelf && !c.Map.activeSelf && !c.Background.activeSelf, "PVP removes boss presentation");
                Require(panel.Find("CharacterArea").gameObject.activeSelf, "PVP restores the character");
                File.WriteAllText(Output + "validation.txt", "PASS: home, map, real scroll range, five nodes, active boss, enlarged island, active-only expanding/fading rings, masked OK shine with 3s repeat, return, small portrait, PVP restoration; procedural forest/ice backdrop, light-blue winter, continuous biome boundary, supplied ice art, preview without combat.\n");
            }
            catch (Exception e)
            { File.WriteAllText(Output + "validation.txt", "FAIL: " + e); throw; }
            finally
            {
                RenderTexture.active = previous;
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (hadMode) PlayerPrefs.SetInt("selected_game_mode", savedMode); else PlayerPrefs.DeleteKey("selected_game_mode");
                PlayerPrefs.Save();
            }
        }
        private static void SetMap(BossMapController c, bool visible)
            => typeof(BossMapController).GetMethod("SetMapVisible", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { visible });
        private static void ValidateBiomes(BossMapController c, Camera camera, RenderTexture texture)
        {
            var backdrop = c.Background.GetComponentInChildren<BossMapBackdrop>(true);
            Require(backdrop != null && !backdrop.raycastTarget && !c.Background.GetComponent<Image>().enabled,
                "Procedural atmosphere replaces the static background and does not block input");
            Require(backdrop.Biomes.Length == 2, "Forest and ice biomes are configured");
            var ice = (RectTransform)c.MapContent.Find("IceChapter");
            Require(ice != null && ice.GetComponentsInChildren<Button>().Length == 0, "Ice is a preview until combat is supplied");
            Require(ice.GetComponentsInChildren<Image>().All(i => i.sprite != null), "Ice preview has supplied art");
            float boundary = c.MapContent.InverseTransformPoint(ice.position).y;
            var forestColor = backdrop.Sample(new Vector2(0, 200), .5f);
            var winterColor = backdrop.Sample(new Vector2(0, boundary + 500), .5f);
            Require(winterColor.b > forestColor.b + .25f && winterColor.g > forestColor.g + .15f, "Winter becomes light blue");
            var before = backdrop.Sample(new Vector2(0, boundary - .1f), .5f);
            var after = backdrop.Sample(new Vector2(0, boundary + .1f), .5f);
            Require(Mathf.Abs(after.b - before.b) < .01f, "No seam at the biome boundary");
            c.Scroll.verticalNormalizedPosition = .7f;
            Capture(camera, texture, "map-transition");
            c.Scroll.verticalNormalizedPosition = 0;
            Capture(camera, texture, "map-return-bottom");
        }
        private static void ValidateEffects(BossMapController c)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var island = (RectTransform)c.MapContent.Find("ForestChapter/ForestIsland");
            Require(island.sizeDelta == new Vector2(325, 367.5f), "Map island enlarged by 25%");
            foreach (var node in c.Nodes)
            {
                var rings = node.Button.GetComponentInChildren<BossPulseRings>(true);
                Require(rings != null && !rings.raycastTarget && rings.ActiveMarker == node.Fight.gameObject, "Rings bind to boss state without blocking input");
                Require(rings.transform.GetSiblingIndex() < node.Disc.transform.GetSiblingIndex(), "Rings stay behind boss disc");
                using (var mesh = new VertexHelper())
                {
                    var populate = typeof(BossPulseRings).GetMethod("OnPopulateMesh", flags | BindingFlags.DeclaredOnly);
                    var elapsed = typeof(BossPulseRings).GetField("_elapsed", flags);
                    elapsed.SetValue(rings, .2f); populate.Invoke(rings, new object[] { mesh });
                    Require(mesh.currentVertCount == (node.Fight.gameObject.activeSelf ? 512 : 0), "Only active boss has ring geometry");
                    if (mesh.currentVertCount == 0) continue;
                    var before = new UIVertex(); mesh.PopulateUIVertex(ref before, 0);
                    elapsed.SetValue(rings, .6f); populate.Invoke(rings, new object[] { mesh });
                    var after = new UIVertex(); mesh.PopulateUIVertex(ref after, 0);
                    Require(after.position.x > before.position.x && after.color.a < before.color.a, "Rings expand and fade");
                    rings.SetVerticesDirty();
                }
            }
            var shine = c.Close.GetComponentInChildren<ButtonShineSweep>(true);
            Require(shine != null && shine.GetComponent<RectMask2D>() != null && !shine.Stripes.raycastTarget, "OK shine is masked and non-interactive");
            Require(shine.Interval == 3 && shine.Duration == .6f, "OK shine timing");
            var clock = typeof(ButtonShineSweep).GetField("_elapsed", flags);
            var update = typeof(ButtonShineSweep).GetMethod("Update", flags);
            Action<float> sample = time => { clock.SetValue(shine, time - Time.unscaledDeltaTime); update.Invoke(shine, null); };
            sample(.15f); float left = shine.Stripes.rectTransform.anchoredPosition.x;
            sample(.45f); Require(shine.Stripes.enabled && shine.Stripes.rectTransform.anchoredPosition.x > left, "Shine travels left to right");
            sample(1); Require(!shine.Stripes.enabled, "Shine hidden between sweeps");
            sample(3.15f); Require(shine.Stripes.enabled && Mathf.Abs(shine.Stripes.rectTransform.anchoredPosition.x - left) < .01f, "Shine repeats after exactly 3 seconds");
            sample(.3f);
        }
        private static void Capture(Camera camera, RenderTexture texture, string name)
        {
            foreach (var backdrop in camera.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapBackdrop>(true)))
                backdrop.SetVerticesDirty();
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
