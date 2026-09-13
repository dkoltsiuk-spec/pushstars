using System;
using System.Collections.Generic;
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
    public static class LeaguePresentationSetup
    {
        [MenuItem("Push Stars/UI/Add League Entrance and Ten Players")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open Main first.");
            var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LeagueView>(true)).First();
            var previous = view.GetComponent<LeagueEntrance>();
            if (previous != null)
            {
                var root = view.GetComponent<LeagueLayout>().Art;
                var targets = new List<RectTransform> {
                    (RectTransform)root.Find("LeagueHeroEntrance"), (RectTransform)view.Title.transform.parent,
                    (RectTransform)view.Score.transform.parent, (RectTransform)root.Find("ProgressPlaque"),
                    (RectTransform)root.Find("TrophyProgress"), (RectTransform)root.Find("ProgressCup"),
                    view.Season.rectTransform, view.Online.rectTransform,
                    (RectTransform)root.Find("OnlineGlow"), (RectTransform)root.Find("OnlineDot") };
                targets.AddRange(previous.Leaderboard.content.Cast<RectTransform>());
                Undo.RecordObject(previous, "Repair entrance group bindings");
                for (int i = 0; i < previous.Beats.Length; i++) previous.Beats[i].Group = Group(targets[i]);
                EditorUtility.SetDirty(previous); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                return;
            }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("League entrance and scrollable top ten");
            var art = view.GetComponent<LeagueLayout>().Art;
            var existing = new[] { "RankRow1", "RankRow2", "RankRow3", "CurrentPlayerRow" }
                .Select(n => (RectTransform)art.Find(n)).ToArray();
            if (existing.Any(r => r == null)) throw new InvalidOperationException("Expected the four authored league rows.");
            Undo.RecordObject(view, "Expand mock leaderboard");
            var players = view.Players.ToList();
            string[] names = { "IRON_MAX", "LUNA_FIT", "PUSH_KING", "BEAST_07", "KIRA", "TITAN_X" };
            int[] scores = { 512, 487, 453, 421, 398, 365 };
            for (int i = players.Count; i < 10; i++) players.Add(new LeagueView.Entry { Name = names[i - 4], Trophies = scores[i - 4] });
            view.Players = players.ToArray();
            var viewport = Rect(art, "LeaderboardViewport", 12, 471, 366, 229);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "LeaderboardContent", 0, 0, 366, 61 + 9 * 69);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true; scroll.decelerationRate = .135f; scroll.scrollSensitivity = 28;
            var rows = new List<RectTransform>();
            for (int i = 0; i < 10; i++)
            {
                RectTransform row;
                if (i < 4) { row = existing[i]; Undo.SetTransformParent(row, content, "Move row into leaderboard"); }
                else
                {
                    row = Object.Instantiate(existing[2], content);
                    Undo.RegisterCreatedObjectUndo(row.gameObject, "Add mock player row");
                    row.name = "RankRow" + (i + 1);
                    var card = row.Find("Card").GetComponent<Image>();
                    card.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/League/row-player.png");
                    card.color = new Color(.44f, .5f, .58f, 1);
                    row.Find("Rank").GetComponent<TextMeshProUGUI>().color = Color.white;
                }
                row.anchorMin = row.anchorMax = row.pivot = new Vector2(0, 1);
                row.anchoredPosition = new Vector2(0, -i * 69); row.localScale = Vector3.one;
                row.Find("Rank").GetComponent<TextMeshProUGUI>().text = (i + 1).ToString();
                if (i == 9) row.Find("Rank").GetComponent<TextMeshProUGUI>().fontSize = 30;
                rows.Add(row);
            }
            view.Names = rows.Select(r => r.Find("PlayerName").GetComponent<TextMeshProUGUI>()).ToArray();
            view.Scores = rows.Select(r => r.Find("Trophies").GetComponent<TextMeshProUGUI>()).ToArray();
            var entrance = Undo.AddComponent<LeagueEntrance>(view.gameObject);
            entrance.View = view; entrance.Leaderboard = scroll;
            var beats = new List<LeagueEntrance.Beat>();
            beats.Add(Beat(Wrap((RectTransform)art.Find("LeagueHero")), 0, .4f, .12f, true));
            beats.Add(Beat(Wrap(view.Title.rectTransform), .24f, .32f, .65f, true));
            beats.Add(Beat(Wrap(view.Score.rectTransform), .46f, .3f, .6f, true));
            foreach (var name in new[] { "ProgressPlaque", "TrophyProgress", "ProgressCup" })
                beats.Add(Beat(Group((RectTransform)art.Find(name)), .46f, .2f));
            beats.Add(Beat(Group(view.Season.rectTransform), .82f, .22f));
            foreach (var name in new[] { "OnlinePlayers", "OnlineGlow", "OnlineDot" })
                beats.Add(Beat(Group((RectTransform)art.Find(name)), .87f, .22f));
            for (int i = 0; i < rows.Count; i++)
            {
                var beat = Beat(Group(rows[i]), .96f + i * .045f, .26f);
                beat.Rise = 16; beats.Add(beat);
            }
            entrance.Beats = beats.ToArray();
            view.Refresh(); scroll.verticalNormalizedPosition = 1;
            EditorUtility.SetDirty(view); EditorUtility.SetDirty(entrance);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = view.gameObject;
            Debug.Log("[League] Entrance timeline and 10-player leaderboard saved. Row pitch 69, viewport 229, content 682.");
        }
        static LeagueEntrance.Beat Beat(CanvasGroup group, float delay, float duration, float scale = 1, bool spring = false)
            => new LeagueEntrance.Beat { Group = group, Delay = delay, Duration = duration, FromScale = scale, Spring = spring };
        static CanvasGroup Group(RectTransform rect)
        {
            var group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = Undo.AddComponent<CanvasGroup>(rect.gameObject);
            return group;
        }
        static CanvasGroup Wrap(RectTransform target)
        {
            var wrapper = Rect(target.parent, target.name + "Entrance", 0, 0, target.sizeDelta.x, target.sizeDelta.y);
            wrapper.SetSiblingIndex(target.GetSiblingIndex());
            wrapper.anchorMin = target.anchorMin; wrapper.anchorMax = target.anchorMax;
            wrapper.pivot = new Vector2(.5f, .5f);
            var delta = Vector2.Scale(wrapper.pivot - target.pivot, target.rect.size);
            wrapper.anchoredPosition = target.anchoredPosition + delta;
            var scale = target.localScale; var rotation = target.localRotation; var size = target.sizeDelta;
            Undo.SetTransformParent(target, wrapper, "Center entrance animation pivot");
            target.anchorMin = target.anchorMax = new Vector2(.5f, .5f);
            target.anchoredPosition = -delta; target.sizeDelta = size;
            target.localScale = scale; target.localRotation = rotation;
            return Group(wrapper);
        }
        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            Undo.RegisterCreatedObjectUndo(go, "Create league presentation element");
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
    }
}
