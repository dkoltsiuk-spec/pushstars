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
    /// <summary>
    /// Boss chapters, rewards and map states. Pure rules run on an in-memory case ledger; the map
    /// is rendered in a preview scene at progress 0 / 3 / 5 with the player's saves restored after.
    /// Output: output/boss-map/rewards-*.png and rewards-validation.txt.
    /// </summary>
    public static class BossMapRewardsValidation
    {
        private const string Output = "output/boss-map/";

        [MenuItem("Tools/Push Stars/Validation/Boss Map Rewards")]
        public static void Run()
        {
            ValidateRules();
            ValidateMap();
            File.WriteAllText(Output + "rewards-validation.txt",
                "PASS: forest ladder unchanged (5 bosses, 5-11 reps), ice 12-19 and lava 20-30 perfect reps authored as COMING SOON; " +
                "timeout by HP share; lava hits for 80; cleared count survives the last boss; gem/case receipts pay once and survive old saves; " +
                "map: gem islet after boss 3, bouncing chest after the king, locked/ready/collected states, ice shows five golems without buttons.\n");
            Debug.Log("PASS: boss map rewards");
        }

        private static void ValidateRules()
        {
            var forest = BossCatalog.Chapters[0];
            Require(BossCatalog.Bosses.Count == 5 && BossCatalog.Bosses.SequenceEqual(forest.Bosses), "Only the forest is playable");
            Require(BossCatalog.Chapters.Count(c => c.Id == "ice") == 1 && BossCatalog.FindChapter("ice").Bosses.Count == 5, "Ice has five golems");
            var lava = BossCatalog.FindChapter("lava");
            Require(lava.Bosses.Count >= 5 && lava.Bosses.Count <= 7, "Lava has 5-7 bosses");
            foreach (var boss in lava.Bosses)
            {
                int perfect = Mathf.CeilToInt(boss.MaxHp / 100f);
                Require(perfect >= 20 && perfect <= 30, "Lava bosses need 20-30 perfect reps: " + boss.Id);
                Require(boss.AttackDamage == 80, "Lava bosses hit for 80");
            }
            foreach (var boss in BossCatalog.FindChapter("ice").Bosses)
            {
                int perfect = Mathf.CeilToInt(boss.MaxHp / 100f);
                Require(perfect >= 12 && perfect <= 19, "Ice sits between forest and lava: " + boss.Id);
            }
            var ids = BossCatalog.Chapters.SelectMany(c => c.Bosses).Select(b => b.Id).ToList();
            Require(ids.Distinct().Count() == ids.Count, "Boss IDs are unique");

            var lavaState = new BossCombatState("lava-golem-king");
            int hits = 0;
            while (!lavaState.Knockout) { lavaState.BossAttack(); hits++; }
            Require(hits == 13 && lava.Bosses[0].RepTimes.Count >= 13 && lava.Bosses[0].RepTimes[12] < 55, "Lava KO on hit 13 before time");
            var share = new BossCombatState("lava-golem-king");
            for (int i = 0; i < 20; i++) share.PlayerRep(100); // 1000 of 3000 left, player untouched
            Require(share.PlayerWins && !share.Draw, "Timeout compares HP share, not raw HP");
            var even = new BossCombatState("unknown");
            Require(even.Draw && !even.PlayerWins, "Equal share draws");

            bool had = PlayerPrefs.HasKey("boss_progress");
            int saved = PlayerPrefs.GetInt("boss_progress");
            try
            {
                PlayerPrefs.SetInt("boss_progress", 2);
                Require(!BossCatalog.IsGemIsletReached(forest), "Gems locked before boss 3");
                BossCatalog.ReportResult(true);
                Require(BossCatalog.IsGemIsletReached(forest) && !BossCatalog.IsChapterCleared(forest), "Gems open after boss 3");
                PlayerPrefs.SetInt("boss_progress", 4);
                BossCatalog.ReportResult(true);
                Require(BossCatalog.ClearedCount == 5 && BossCatalog.CurrentIndex == 4 && BossCatalog.IsChapterCleared(forest), "King opens the chest");
                BossCatalog.ReportResult(true);
                Require(BossCatalog.ClearedCount == 5, "Rematches never overflow");
                Require(!BossCatalog.IsGemIsletReached(BossCatalog.FindChapter("ice")), "COMING SOON islands pay nothing");
            }
            finally
            {
                if (had) PlayerPrefs.SetInt("boss_progress", saved); else PlayerPrefs.DeleteKey("boss_progress");
                PlayerPrefs.Save();
            }

            string json = null;
            var ledger = new CaseRewardLedger(null, () => .5, s => json = s);
            Require(ledger.TryCreditGems(forest.GemReceipt, forest.GemReward) && ledger.GemsBalance == 50, "Gem islet pays");
            Require(!ledger.TryCreditGems(forest.GemReceipt, forest.GemReward) && ledger.GemsBalance == 50, "Gem islet pays once");
            Require(ledger.TryGrantCase(forest.ChestReceipt) && ledger.HasCaseReceipt(forest.ChestReceipt) &&
                    !ledger.TryGrantCase(forest.ChestReceipt) && ledger.PendingCount == 1, "Chest grants one case");
            var reloaded = new CaseRewardLedger(json, () => .5, s => json = s);
            Require(reloaded.HasGemReceipt(forest.GemReceipt) && reloaded.GemsBalance == 50 && reloaded.HasCaseReceipt(forest.ChestReceipt),
                "Receipts survive restart");
            var legacy = new CaseRewardLedger("{\"version\":5,\"gems\":7,\"aura\":0,\"auraReceipts\":[],\"avatars\":[],\"awardedWorkouts\":[],\"cases\":[],\"processedWorkoutIds\":[]}",
                () => .5, s => json = s);
            Require(legacy.TryCreditGems(forest.GemReceipt, 50) && legacy.GemsBalance == 57, "Saves without gem receipts load");
        }

        private static void ValidateMap()
        {
            Directory.CreateDirectory(Output);
            bool hadMode = PlayerPrefs.HasKey("selected_game_mode");
            int savedMode = PlayerPrefs.GetInt("selected_game_mode");
            bool hadProgress = PlayerPrefs.HasKey("boss_progress");
            int savedProgress = PlayerPrefs.GetInt("boss_progress");
            var scene = EditorSceneManager.OpenPreviewScene(AuthoredScenes.MainPath);
            RenderTexture texture = null;
            Camera camera = null;
            var previous = RenderTexture.active;
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
                camera = new GameObject("RewardValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                texture = new RenderTexture(780, 1688, 24); camera.targetTexture = texture; canvas.worldCamera = camera;
                SelectedGameMode.Current = GameMode.Boss;
                Canvas.ForceUpdateCanvases(); c.RefreshMode();

                Require(c.Rewards.Length == 2 && c.Rewards.All(r => r.Button && r.Icon && r.Bounce && r.ChapterId == "forest"), "Forest gems and chest are bound");
                var gems = c.Rewards.Single(r => r.Kind == BossMapController.RewardKind.Gems);
                var chest = c.Rewards.Single(r => r.Kind == BossMapController.RewardKind.Chest);
                float Y(Component t) => c.MapContent.InverseTransformPoint(t.transform.position).y;
                Require(Y(c.Nodes[2].Button) < Y(gems.Button) && Y(gems.Button) < Y(c.Nodes[3].Button), "Gem islet sits between bosses 3 and 4");
                Require(Y(c.Nodes[4].Button) < Y(chest.Button), "Chest sits above the main boss");
                Require(gems.Button.onClick.GetPersistentEventCount() == 0 && chest.Button.onClick.GetPersistentEventCount() == 0, "No 'coming later' listeners remain");
                var ice = c.MapContent.Find("IceChapter");
                Require(Enumerable.Range(1, 5).All(i => ice.Find("IceBoss" + i) != null) && ice.GetComponentsInChildren<Button>(true).Length == 0,
                    "Ice previews five golems without interaction");

                // Collected states depend on the player's wallet; this render shows whatever it holds.
                SetMap(c, true);
                foreach (int progress in new[] { 0, 3, 5 })
                {
                    PlayerPrefs.SetInt("boss_progress", progress);
                    c.RefreshProgress();
                    bool gemsCollected = CaseRewards.HasGemReceipt(BossCatalog.Chapters[0].GemReceipt);
                    bool chestCollected = CaseRewards.HasCaseReceipt(BossCatalog.Chapters[0].ChestReceipt);
                    Require(gems.Bounce.Active == (progress >= 3 && !gemsCollected), "Gem islet bounces only when ready");
                    Require(chest.Bounce.Active == (progress >= 5 && !chestCollected), "Chest bounces only when ready");
                    Require(c.Nodes.Count(n => n.Fight.gameObject.activeSelf) == 1, "Exactly one FIGHT, including after the king");
                    Focus(c, Y(gems.Button) + 230);
                    Capture(camera, texture, "rewards-progress-" + progress);
                }
                // Mid-hop pose of the ready chest.
                chest.Bounce.Pose(.32f); gems.Bounce.Pose(.32f);
                Require(((RectTransform)chest.Icon.transform).anchoredPosition.y > -36 + 10, "Chest leaves the ground mid-hop");
                Capture(camera, texture, "rewards-hop");
            }
            finally
            {
                if (hadMode) PlayerPrefs.SetInt("selected_game_mode", savedMode); else PlayerPrefs.DeleteKey("selected_game_mode");
                if (hadProgress) PlayerPrefs.SetInt("boss_progress", savedProgress); else PlayerPrefs.DeleteKey("boss_progress");
                PlayerPrefs.Save();
                RenderTexture.active = previous;
                if (camera != null) camera.targetTexture = null;
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void SetMap(BossMapController c, bool visible)
            => typeof(BossMapController).GetMethod("SetMapVisible", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { visible });

        private static void Focus(BossMapController c, float contentY)
        {
            Canvas.ForceUpdateCanvases();
            float view = c.Scroll.viewport.rect.height, height = c.MapContent.rect.height;
            c.Scroll.verticalNormalizedPosition = Mathf.Clamp01((contentY - view * .5f) / (height - view));
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(Camera camera, RenderTexture texture, string name)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
