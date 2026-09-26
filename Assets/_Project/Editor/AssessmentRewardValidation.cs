using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Edit-mode renders and binding checks for the post-fight reward scenes: summary
    /// without Aura, an ordinary level-1 case, its prize, and the standalone Aura stamp.</summary>
    public static class AssessmentRewardValidation
    {
        private const string Output = "output/assessment-rewards/";
        [MenuItem("Tools/Push Stars/Rewards/Validate Assessment Aura")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            CaseRewardsRegression.Run();
            Render("RewardSummary");
            Render("CaseOpening");
            Render("CaseReward");
            Render("AuraReward");
            File.WriteAllText(Output + "validation.txt", "PASS: economy regression; summary without Aura; level-1 case without Aura rig; gem prize without stamp; deterministic +200 AURA stamp and skull; interrupted stamp reset; 390x844, 320x568, 430x932 renders.\n");
            Debug.Log("[AssessmentRewardValidation] PASS");
        }
        /// <summary>Frames of the full stamp → skull → idle → collect sequence at 60 fps, for review
        /// (tools/audio mixes the matching sound track onto them).</summary>
        [MenuItem("Tools/Push Stars/Rewards/Render Aura Stamp Movie")]
        public static void RenderStampMovie() => Render("AuraReward", movie: true);
        private static void Render(string name, bool movie = false)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/" + name + ".unity");
            RenderTexture target = null;
            try
            {
                var roots = scene.GetRootGameObjects();
                var owner = (Component)roots.SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).SingleOrDefault()
                    ?? roots.SelectMany(r => r.GetComponentsInChildren<AuraRewardScreen>(true)).Single();
                var canvas = owner.GetComponentInChildren<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)canvas.transform;
                rect.position = Vector3.zero; rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(390, 844);
                var camera = new GameObject("AssessmentValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .02f, .1f);
                target = new RenderTexture(780, 1688, 24); camera.targetTexture = target; canvas.worldCamera = camera;
                Canvas.ForceUpdateCanvases();
                bool auraLeft = owner.GetComponents<MonoBehaviour>().Any(b => b == null || (b is AuraStampPresentation && name != "AuraReward"))
                    || owner.GetComponentsInChildren<Transform>(true).Any(t => t.name == "AuraEnergy" || (t.name == "AuraStamp" && name != "AuraReward"));
                Require(!auraLeft, name + " still carries an Aura rig");

                if (owner is AuraRewardScreen aura)
                {
                    var stamp = aura.Stamp;
                    if (movie) { RenderMovie(stamp, camera, target); return; }
                    Require(stamp != null && stamp.Fx != null && !stamp.Fx.raycastTarget && !stamp.Flash.raycastTarget, "Stamp rig missing or blocks input");
                    Require(!stamp.Body.blocksRaycasts && !stamp.SkullGroup.blocksRaycasts, "Stamp blocks the full-screen collect tap");
                    Require(stamp.Number.fontSharedMaterial != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(stamp.Number.fontSharedMaterial)), "Stamp text material not persisted");
                    Require(stamp.SkullSlices.Length > 0 && stamp.SkullSlices.All(s => s.texture != null), "Skull art not imported");
                    var tap = canvas.GetComponentsInChildren<Button>(true).Single(b => b.name == "CollectTap");
                    Require(tap.onClick.GetPersistentEventCount() == 1 && tap.onClick.GetPersistentTarget(0) == aura &&
                        tap.onClick.GetPersistentMethodName(0) == nameof(AuraRewardScreen.Collect), "Collect tap is not wired");
                    var random = UnityEngine.Random.state;
                    var home = stamp.Stamp.anchoredPosition;
                    stamp.Configure(CaseRewards.AssessmentAura);
                    Require(stamp.Number.text == "+200" && stamp.Word.text == "AURA", "Stamp shows wrong amount");
                    foreach (float time in new[] { .06f, .14f, .17f, .22f, .32f, .5f, .95f, 1.1f, 1.3f, 1.54f })
                    {
                        stamp.Sample(time);
                        Capture(camera, target, name + "-" + Mathf.RoundToInt(time * 100));
                    }
                    Require(JsonUtility.ToJson(random) == JsonUtility.ToJson(UnityEngine.Random.state), "Presentation consumed gameplay randomness");
                    stamp.Settle();
                    foreach (float claim in new[] { .1f, .25f, .4f, .6f })
                    {
                        stamp.SampleClaim(claim);
                        Capture(camera, target, name + "-claim-" + Mathf.RoundToInt(claim * 100));
                    }
                    stamp.ResetPresentation();
                    Require(stamp.Stamp.localScale == Vector3.one && stamp.Shaker.anchoredPosition == Vector2.zero, "Interrupted stamp did not reset");
                    Require(stamp.Stamp.anchoredPosition == home && stamp.Body.alpha == 1, "Stamp lost its authored position");
                    stamp.Configure(CaseRewards.AssessmentAura); stamp.Settle();
                }
                else
                {
                    var screen = (RewardScreen)owner;
                    if (name == "RewardSummary")
                    {
                        var ui = screen.SummaryUi;
                        Require(ui.Title != null && ui.Subtitle != null && ui.ContinueLabel != null, "Assessment summary bindings missing");
                        Require(!screen.GetComponentsInChildren<Transform>(true).Any(t => t.name == "AssessmentBonus"), "Summary still announces Aura");
                        ui.Title.text = "ASSESSMENT COMPLETE!"; ui.Subtitle.text = "Great result. Strong start!";
                        ui.TrophyGroup.SetActive(false); ui.AuraGroup.SetActive(false);
                        ui.TotalReps.text = "8"; ui.Technique.text = "92%"; ui.EnergyXp.text = "+80";
                        ui.ContinueLabel.text = "CONTINUE";
                        ui.ContinueIcon.sprite = Resources.Load<PushStarsTheme>("PushStarsTheme").IconAura;
                        ui.Portrait.texture = ui.MalePortrait;
                    }
                    else if (name == "CaseOpening")
                    {
                        // The welcome case is an ordinary level-1 case on its own rarity background.
                        screen.GetComponent<CaseRarityPresentation>().Apply(0);
                        screen.CaseUi.Artwork.sprite = screen.CaseUi.RarityArtwork[0];
                        for (int i = 0; i < 4; i++) screen.CaseUi.RarityStarGroups[i].SetActive(i == 0);
                        screen.CaseUi.Status.text = "";
                        screen.CaseUi.Hint.text = "Tap to upgrade · 3 chances left";
                    }
                }
                Capture(camera, target, name);
                foreach (var size in new[] { new Vector2Int(320, 568), new Vector2Int(430, 932) })
                {
                    rect.sizeDelta = size; camera.orthographicSize = size.y / 2f;
                    target.Release(); target.width = size.x * 2; target.height = size.y * 2; target.Create();
                    var composition = owner.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Composition");
                    composition.localScale = Vector3.one * Mathf.Min(size.x / 390f, size.y / 844f);
                    Capture(camera, target, name + "-" + size.x + "x" + size.y);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
        private static void RenderMovie(AuraStampPresentation stamp, Camera camera, RenderTexture target)
        {
            const string folder = "output/aura-stamp/movie/";
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            int frame = 0;
            void Shot() => Capture(camera, target, "../aura-stamp/movie/f" + (frame++).ToString("D4"));
            stamp.Configure(CaseRewards.AssessmentAura);
            for (float t = 0; t < AuraStampPresentation.RevealSeconds; t += 1 / 60f) { stamp.Sample(t); Shot(); }
            stamp.Settle();
            for (float t = AuraStampPresentation.RevealSeconds; t < 3.4f; t += 1 / 60f) { stamp.SampleIdle(t); Shot(); }
            for (float t = 0; t <= AuraStampPresentation.ClaimSeconds + .25f; t += 1 / 60f) { stamp.SampleClaim(t); Shot(); }
        }
        private static void Capture(Camera camera, RenderTexture target, string name)
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
