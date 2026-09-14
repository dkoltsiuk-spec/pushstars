using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class GoblinForestValidation
    {
        public static void Check(BossCombatScreen screen, Camera camera, RenderTexture target,
            Action<Camera, RenderTexture, string> capture)
        {
            var forest = screen.Forest;
            Require(forest.Effects && forest.Effects.Leaves.Length == 4 && forest.Effects.Petals.Length == 3, "Leaf and petal assets wired");
            forest.Effects.ResetSimulation();
            for (int frame = 0; frame < 40; frame++) forest.Effects.Advance(.05f);
            var props = forest.Foreground.GetComponentsInChildren<ForestWindGraphic>(true);
            Require(props.Length == 4, "Four foreground props");
            Require(forest.Foreground.transform.GetSiblingIndex() < screen.Action.transform.GetSiblingIndex(), "Controls above foreground");
            Require(forest.Foreground.GetComponent<RectMask2D>(), "Foreground clipped to viewport");
            CheckLayout(screen);
            foreach (var prop in props)
            {
                Require(prop.Texture && !prop.raycastTarget, "Texture present and input passes through " + prop.name);
                var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(prop.Texture));
                Require(importer.DoesSourceTextureHaveAlpha(), "Source alpha: " + prop.name);
                Require(prop.material != null && prop.material.shader.name == "PushStars/UI Forest Defringe" &&
                    prop.material.GetFloat("_FringePixels") > 1, "One-pixel fringe cleanup enabled: " + prop.name);
                {
                    prop.SetWindTime(0); Canvas.ForceUpdateCanvases();
                    var before = prop.canvasRenderer.GetMesh().vertices;
                    Require(before.Length > 100, "Subdivided visible mesh: " + prop.name);
                    prop.SetWindTime(1.7f); Canvas.ForceUpdateCanvases();
                    var after = prop.canvasRenderer.GetMesh().vertices;
                    Require(before.Length == after.Length, "Stable topology");
                    float motion = 0;
                    for (int i = 0; i < before.Length; i++) motion = Mathf.Max(motion, Vector3.Distance(before[i], after[i]));
                    Require(motion > .05f, "Wind changes rendered vertices: " + prop.name);
                    // Bottom grid row is the root of grass/fern; last column anchors the branch.
                    for (int i = prop.PinRight ? 24 : 0; i < (prop.PinRight ? before.Length : 25); i += prop.PinRight ? 25 : 1)
                        Require(Vector3.Distance(before[i], after[i]) < .0001f, "Root stays fixed: " + prop.name);
                }
            }
            foreach (float time in new[] {0f, 1.7f})
            {
                foreach (var prop in props) prop.SetWindTime(time);
                capture(camera, target, time == 0 ? "GoblinForest-wind-a" : "GoblinForest-wind-b");
            }
            forest.Configure(false);
            Require(!forest.Foreground.activeSelf && !forest.Background.activeSelf && !forest.Matte.activeSelf && forest.OriginalDecor.All(o => o.activeSelf), "Other bosses retain original decor");
            forest.Configure(true);
            Require(forest.Foreground.activeSelf && forest.OriginalDecor.All(o => !o.activeSelf), "Goblin decor restores");
            File.WriteAllText("output/goblin-forest/validation.txt", "PASS: four alpha textures; deformed rendered meshes at two times; anchored roots; viewport clipping; UI above non-raycast props; original decor restored outside goblin theme.\n");
        }

        public static void CheckLayout(BossCombatScreen screen)
        {
            var forest = screen.Forest;
            Rect area = ((RectTransform)screen.Root.transform).rect;
            Vector2 drawn = ((RectTransform)forest.Background.transform).rect.size * screen.Content.localScale.x;
            Require(Vector2.Distance(drawn, area.size) < .1f, "Forest fills both viewport dimensions");
            float half = ((RectTransform)forest.Foreground.transform).rect.width * .5f;
            Require(Mathf.Abs(forest.LeftGrass.anchoredPosition.x + half - 45) < .1f, "Left grass follows viewport edge");
            Require(Mathf.Abs(forest.Branch.anchoredPosition.x - half + 91) < .1f, "Branch follows right viewport edge");
            var backdrop = forest.Background.GetComponent<ForestBackdropGraphic>();
            Require(backdrop && backdrop.OpaqueRows.Length == 65, "Rounded export corners replaced with opaque edge samples");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
