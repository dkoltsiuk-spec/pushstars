using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Checks the ice and lava islands' split-arena battle layers installed by <see cref="ArenaIslandSetup"/>.</summary>
    public static class ArenaIslandValidation
    {
        public static string Output(ArenaIslandSetup.Island island) => "output/" + island.Chapter + "-island/";

        /// <summary>The island whose battle layers belong to the current boss, or null for the forest.</summary>
        public static ArenaIslandSetup.Island Current(BossCombatScreen screen)
        {
            string chapter = BossCatalog.ChapterOf(BossCatalog.Current.Id)?.Id;
            var island = ArenaIslandSetup.Islands.FirstOrDefault(i => i.Chapter == chapter);
            return island != null && ArenaIslandSetup.Presentation(screen, island) != null ? island : null;
        }

        public static void Check(BossCombatScreen screen, ArenaIslandSetup.Island island, Camera camera, RenderTexture target,
            Action<Camera, RenderTexture, string> capture)
        {
            Directory.CreateDirectory(Output(island));
            var shown = ArenaIslandSetup.Presentation(screen, island);
            Require(shown.Background.activeSelf && shown.Foreground.activeSelf && shown.Matte.activeSelf &&
                shown.OriginalDecor.All(o => !o.activeSelf), island.Name + " layers replace the PvP decor");
            var forest = screen.Forest;
            Require(forest == null || (!forest.Background.activeSelf && !forest.Foreground.activeSelf && !forest.Matte.activeSelf),
                "Forest layers stay off on the " + island.Chapter + " island");
            foreach (var other in ArenaIslandSetup.Islands.Where(i => i != island).Select(i => ArenaIslandSetup.Presentation(screen, i)).Where(p => p != null))
                Require(!other.Background.activeSelf && !other.Foreground.activeSelf && !other.Matte.activeSelf && other.Backdrop.Sprite == null,
                    other.ArenaId + " layers stay off on the " + island.Chapter + " island");
            Require(shown.ArenaId == island.ArenaId && shown.Backdrop != null && shown.Backdrop.Sprite != null &&
                shown.Backdrop.Sprite == ArenaCatalog.Get(island.ArenaId).Home, island.ArenaId + " arena art behind the boss");
            var ledge = shown.Background.GetComponentInChildren<IslandLedgeGraphic>(true);
            Require(ledge != null && !ledge.raycastTarget && !shown.Backdrop.raycastTarget, "Ledge present and input passes through");
            var fx = shown.Effects;
            Require(fx && fx.Leaves.Length == island.Ambient.Length && fx.Petals.Length == island.Bursts.Length &&
                fx.Leaves.Concat(fx.Petals).All(t => t != null && t.name.Length > 0 && UnityEditor.AssetDatabase.GetAssetPath(t).StartsWith(island.Folder)),
                island.Name + " ambient and burst textures wired");
            foreach (var texture in fx.Leaves.Concat(fx.Petals).Distinct())
            {
                var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(texture));
                Require(importer.DoesSourceTextureHaveAlpha() && importer.alphaIsTransparency, "Source alpha: " + texture.name);
            }
            Require(shown.Foreground.transform.GetSiblingIndex() < screen.Action.transform.GetSiblingIndex(), "Controls above foreground");
            Require(shown.Foreground.GetComponent<RectMask2D>(), "Foreground clipped to viewport");
            CheckLayout(screen, island);
            fx.ResetSimulation();
            for (int frame = 0; frame < 40; frame++) fx.Advance(.05f);
            var live = fx.transform.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToArray();
            Require(live.Length >= 6 && live.Length <= fx.AmbientMax && fx.ActivePetals == 0, "Calm ambience: a handful of particles, no bursts");
            var before = live.ToDictionary(t => t, t => ((RectTransform)t).anchoredPosition.y);
            fx.Advance(.05f);
            int rising = live.Count(t => t.gameObject.activeSelf && ((RectTransform)t).anchoredPosition.y > before[t]);
            Require(fx.AmbientRise ? rising == live.Count(t => t.gameObject.activeSelf) : rising == 0,
                fx.AmbientRise ? "Embers drift up" : "Snow falls");
            capture(camera, target, island.Name + "Island-ambient");
            shown.Configure(false);
            Require(!shown.Foreground.activeSelf && !shown.Background.activeSelf && !shown.Matte.activeSelf &&
                shown.Backdrop.Sprite == null && shown.OriginalDecor.All(o => o.activeSelf), "Other bosses retain original decor");
            shown.Configure(true);
            Require(shown.Foreground.activeSelf && shown.Backdrop.Sprite != null && shown.OriginalDecor.All(o => !o.activeSelf), island.Name + " decor restores");
            File.WriteAllText(Output(island) + "validation.txt", "PASS: " + island.ArenaId + " arena split into boss/player halves; ledge on the seam; " +
                "forest and other island layers off; " + live.Length + " ambient particles after 2 s; alpha textures; viewport clipping; UI above non-raycast layers; original decor restored outside the " + island.Chapter + " island.\n");
        }

        public static void CheckLayout(BossCombatScreen screen, ArenaIslandSetup.Island island)
        {
            var shown = ArenaIslandSetup.Presentation(screen, island);
            Rect area = ((RectTransform)screen.Root.transform).rect;
            var background = (RectTransform)shown.Background.transform;
            Vector2 drawn = background.rect.size * screen.Content.localScale.x;
            Require(Vector2.Distance(drawn, area.size) < .1f, island.Name + " backdrop fills both viewport dimensions");
            var ledge = (RectTransform)shown.Background.GetComponentInChildren<IslandLedgeGraphic>(true).transform;
            Require(Mathf.Abs(ledge.rect.width - background.rect.width) < .1f, "Ledge spans the viewport");
            Require(Mathf.Abs(((RectTransform)shown.Foreground.transform).rect.width - background.rect.width) < .1f, "Particles cover the viewport");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
