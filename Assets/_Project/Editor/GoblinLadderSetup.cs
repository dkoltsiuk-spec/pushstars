using System;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class GoblinLadderSetup
    {
        public static void ExpandMap(BossMapController c)
        {
            var nodes = c.Nodes.ToList();
            var template = nodes[0];
            var progress = template.ProgressPlate.transform.parent;
            while (nodes.Count < BossCatalog.Bosses.Count)
            {
                int index = nodes.Count;
                var root = Object.Instantiate(template.Button.gameObject, template.Button.transform.parent).transform;
                root.name = "Boss" + (index + 1);
                ((RectTransform)root).anchoredPosition = new Vector2(index % 2 == 0 ? -73 : 72, 420 + index * 135);
                var plate = Object.Instantiate(template.ProgressPlate, progress);
                plate.name = "BossProgress" + (index + 1);
                var node = new BossMapController.Node {
                    BossIndex = index, Button = root.GetComponent<Button>(),
                    Platform = root.Find("Platform").GetComponent<Image>(),
                    Disc = root.Find("Disc").GetComponent<Image>(), Face = root.Find("Goblin").GetComponent<Image>(),
                    Fight = root.Find("Fight").GetComponent<Button>(), ProgressPlate = plate,
                    ProgressFace = plate.transform.Find("Goblin").GetComponent<Image>()
                };
                var pulse = root.GetComponentInChildren<BossPulseRings>(true);
                if (pulse != null) pulse.ActiveMarker = node.Fight.gameObject;
                nodes.Add(node);
            }
            c.Nodes = nodes.ToArray();
            c.FinalGoblin = AssetDatabase.LoadAssetAtPath<Sprite>(GoblinKingSetup.PortraitPath);
            c.FinalLockedGoblin = c.FinalGoblin;
            for (int i = 0; i < c.Nodes.Length; i++)
                c.Nodes[i].ProgressPlate.rectTransform.anchoredPosition = new Vector2(-112 + i * 56, 0);
            foreach (string name in new[] { "Milestone", "RewardStep", "ChestStep" })
            {
                var obsolete = progress.Find(name);
                if (obsolete != null) Object.DestroyImmediate(obsolete.gameObject);
            }
            var bubble = progress.Find("RewardBubble") as RectTransform;
            if (bubble != null) bubble.anchoredPosition = new Vector2(112, 57);
            c.RefreshProgress();
            EditorUtility.SetDirty(c);
        }

        public static void Validate()
        {
            if (BossCatalog.Bosses.Count != 5) throw new Exception("Expected five goblins");
            for (int damage = 70; damage <= 100; damage++)
            {
                int previous = 0;
                for (int i = 0; i < BossCatalog.Bosses.Count; i++)
                {
                    var profile = BossCatalog.Bosses[i];
                    int reps = Mathf.CeilToInt(profile.MaxHp / (float)damage);
                    if (i > 0 && (reps - previous < (i == 4 ? 2 : 1) || reps - previous > (i == 4 ? 3 : 2)))
                        throw new Exception("Rep increment outside requested bounds");
                    if (Resources.Load<GameObject>("Bosses/" + profile.PrefabId) == null)
                        throw new Exception("Missing boss prefab " + profile.PrefabId);
                    previous = reps;
                }
            }
            bool hadProgress = PlayerPrefs.HasKey("boss_progress");
            int savedProgress = PlayerPrefs.GetInt("boss_progress");
            try
            {
                PlayerPrefs.SetInt("boss_progress", 0);
                for (int i = 0; i < 5; i++)
                {
                    if (BossCatalog.CurrentIndex != i) throw new Exception("Incorrect unlocked stage");
                    var state = new BossCombatState(BossCatalog.Current.Id);
                    int reps = 0;
                    while (!state.Knockout) { state.PlayerRep(100); reps++; }
                    if (!state.PlayerWins || reps != Mathf.CeilToInt(BossCatalog.Current.MaxHp / 100f))
                        throw new Exception("Incorrect knockout threshold");
                    BossCatalog.ReportResult(false);
                    if (BossCatalog.CurrentIndex != i) throw new Exception("Loss advanced ladder");
                    BossCatalog.ReportResult(true);
                    if (BossCatalog.CurrentIndex != Mathf.Min(i + 1, 4)) throw new Exception("Win progression failed");
                }
            }
            finally
            {
                if (hadProgress) PlayerPrefs.SetInt("boss_progress", savedProgress);
                else PlayerPrefs.DeleteKey("boss_progress");
                PlayerPrefs.Save();
            }
            Debug.Log("PASS: five goblins, all prefabs, +1–2 / +2–3 reps at every damage value 70–100.");
        }
    }
}
