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
    /// <summary>Renders the boss-map fog on a preview copy of Main at several progress points and
    /// mid-lift. The fog is installed into the preview, so Main itself is never touched.</summary>
    public static class BossMapFogValidation
    {
        private const string Output = "output/boss-map/fog/";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Push Stars/Validation/Boss Map Fog")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            bool hadMode = PlayerPrefs.HasKey("selected_game_mode");
            int savedMode = PlayerPrefs.GetInt("selected_game_mode");
            bool hadProgress = PlayerPrefs.HasKey("boss_progress");
            int savedProgress = PlayerPrefs.GetInt("boss_progress");
            var scene = EditorSceneManager.OpenPreviewScene(AuthoredScenes.MainPath);
            RenderTexture texture = null;
            var previous = RenderTexture.active;
            var log = new System.Text.StringBuilder();
            try
            {
                var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).Single();
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
                var camera = new GameObject("FogValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                texture = new RenderTexture(780, 1688, 24); camera.targetTexture = texture; canvas.worldCamera = camera;

                var fog = BossMapEnvironmentSetup.InstallFog(c, c.Background.GetComponentInChildren<BossMapBackdrop>(true));
                Require(fog.Shader != null && fog.Shader.isSupported, "Fog shader compiles");
                Require(!fog.raycastTarget && fog.transform.parent == c.Scroll.viewport && fog.transform.GetSiblingIndex() > c.MapContent.GetSiblingIndex(),
                    "Fog sits above the chapters inside the viewport mask and never blocks taps");

                SelectedGameMode.Current = GameMode.Boss;
                Canvas.ForceUpdateCanvases(); c.RefreshMode();
                var shots = new[] { (0, 0f, "start"), (2, 0f, "forest-3"), (4, 0f, "forest-king"), (5, -1f, "ice-1"), (10, -1f, "lava-1"), (16, 1f, "all-clear") };
                float last = float.MinValue;
                foreach (var (cleared, scroll, name) in shots)
                {
                    PlayerPrefs.SetInt("boss_progress", cleared);
                    SetMap(c, false); SetMap(c, true);
                    Tick(fog);
                    float line = fog.LineFor(BossCatalog.ClearedCount);
                    Require(line > last, $"Fog line rises with progress ({name})");
                    last = line;
                    log.AppendLine($"{name}: cleared {BossCatalog.ClearedCount}, fog line {line:0}");
                    c.Scroll.verticalNormalizedPosition = scroll >= 0 ? scroll : Focus(c, line);
                    Capture(fog, camera, texture, name);
                }

                // Mid-lift frame: forest boss 2 → 3, half way.
                PlayerPrefs.SetInt("boss_progress", 2);
                SetMap(c, false); SetMap(c, true);
                float from = fog.LineFor(1), to = fog.LineFor(2);
                typeof(BossMapFog).GetField("_from", Private).SetValue(fog, from);
                typeof(BossMapFog).GetField("_to", Private).SetValue(fog, to);
                typeof(BossMapFog).GetField("_pendingOpen", Private).SetValue(fog, false);
                typeof(BossMapFog).GetField("_revealStart", Private).SetValue(fog, Time.unscaledTime - fog.RevealDelay - fog.RevealDuration * .5f);
                Tick(fog);
                c.Scroll.verticalNormalizedPosition = Focus(c, from);
                Capture(fog, camera, texture, "lifting");
                log.AppendLine($"lifting: {from:0} → {to:0}");

                File.WriteAllText(Output + "validation.txt", "PASS\n" + log);
            }
            catch (Exception e)
            { File.WriteAllText(Output + "validation.txt", "FAIL: " + e + "\n" + log); throw; }
            finally
            {
                RenderTexture.active = previous;
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (hadMode) PlayerPrefs.SetInt("selected_game_mode", savedMode); else PlayerPrefs.DeleteKey("selected_game_mode");
                if (hadProgress) PlayerPrefs.SetInt("boss_progress", savedProgress); else PlayerPrefs.DeleteKey("boss_progress");
                PlayerPrefs.Save();
            }
        }

        private static void Tick(BossMapFog fog)
        {
            if (!fog.isActiveAndEnabled) { fog.enabled = false; fog.enabled = true; }
            typeof(BossMapFog).GetMethod("LateUpdate", Private).Invoke(fog, null);
        }

        private static float Focus(BossMapController c, float line)
        {
            float scale = c.MapContent.rect.width / 390f, content = c.MapContent.rect.height, view = c.Scroll.viewport.rect.height;
            return Mathf.Clamp01((line * scale - view * .38f) / (content - view));
        }

        private static void SetMap(BossMapController c, bool visible)
            => typeof(BossMapController).GetMethod("SetMapVisible", Private).Invoke(c, new object[] { visible });

        private static void Capture(BossMapFog fog, Camera camera, RenderTexture texture, string name)
        {
            Canvas.ForceUpdateCanvases();
            Tick(fog);
            foreach (var backdrop in camera.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapBackdrop>(true)))
                backdrop.SetVerticesDirty();
            fog.SetVerticesDirty();
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
