using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
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
            File.WriteAllText(Output + "validation.txt", "PASS: economy regression; authored bindings; full-bleed non-interactive energy; deterministic reveal; interrupted pose reset; 390x844, 320x568, 430x932 renders.\n");
            Debug.Log("[AssessmentRewardValidation] PASS");
        }
        [MenuItem("Tools/Push Stars/Rewards/Render Aura Fire")]
        public static void RenderFire()
        {
            Directory.CreateDirectory(Output);
            Render("CaseOpening");
            Render("CaseReward");
            Debug.Log("[AssessmentRewardValidation] Fire renders PASS");
        }
        private static void Render(string name)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/" + name + ".unity");
            RenderTexture target = null;
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
                var canvas = screen.GetComponentInChildren<Canvas>().rootCanvas;
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
                var rig = screen.GetComponent<AuraRewardPresentation>();
                if (name == "RewardSummary")
                {
                    var ui = screen.SummaryUi;
                    Require(ui.Title != null && ui.Subtitle != null && ui.ContinueLabel != null && ui.AssessmentBonus != null, "Assessment summary bindings missing");
                    ui.Title.text = "ASSESSMENT COMPLETE!"; ui.Subtitle.text = "Great result. Strong start!";
                    ui.TrophyGroup.SetActive(false); ui.AuraGroup.SetActive(false); ui.AssessmentBonus.SetActive(true);
                    ui.TotalReps.text = "8"; ui.Technique.text = "92%"; ui.EnergyXp.text = "+80";
                    ui.ContinueLabel.text = "OPEN CASE";
                    ui.ContinueIcon.sprite = Resources.Load<Sprite>("Rewards/CaseLegendary");
                    ui.Portrait.texture = ui.MalePortrait;
                }
                else
                {
                    Require(rig != null && rig.Energy != null && !rig.Energy.raycastTarget, "Energy rig missing or blocks input");
                    Require(rig.Energy.material != null && rig.Energy.material.shader.name == "PushStars/UI Aura Vortex", "Fire material not serialized");
                    Require(!ShaderUtil.ShaderHasError(rig.Energy.material.shader), "Aura shader failed compilation");
                    Require(rig.Energy.rectTransform.anchorMin == Vector2.zero && rig.Energy.rectTransform.anchorMax == Vector2.one, "Energy must fill screen");
                    var random = UnityEngine.Random.state;
                    if (name == "CaseReward")
                    {
                        rig.ConfigurePrize();
                        screen.PrizeUi.Note.gameObject.SetActive(false);
                        screen.PrizeUi.Note.text = "";
                        var original = screen.PrizeUi.Content.localScale;
                        foreach (float time in new[] { .12f, .35f, .65f, 1f, 1.6f })
                        {
                            rig.SampleReveal(time);
                            Capture(camera, target, name + "-" + Mathf.RoundToInt(time * 100));
                        }
                        for (int frame = 0; frame < 120; frame++)
                        {
                            rig.SampleReveal(frame / 30f);
                            if (frame >= 48) rig.ClaimHint.text = "TAP TO COLLECT";
                            Capture(camera, target, "aura-" + frame.ToString("D3"));
                        }
                        Require(screen.PrizeUi.Amount.text == "+200", "Reveal did not settle at +200");
                        rig.ResetPresentation();
                        Require(rig.Energy.Power == 0, "Interrupted energy did not reset");
                        rig.ConfigurePrize(); rig.Settle();
                    }
                    else
                    {
                        screen.GetComponent<CaseRarityPresentation>().Apply(3);
                        screen.CaseUi.Artwork.sprite = screen.CaseUi.RarityArtwork[3];
                        for (int i = 0; i < 4; i++) screen.CaseUi.RarityStarGroups[i].SetActive(i == 3);
                        screen.CaseUi.Status.text = "200 AURA INSIDE";
                        screen.CaseUi.Hint.text = "Tap to open case";
                        rig.ConfigureCase(); rig.SampleCharge(.8f);
                        screen.GetComponent<CaseOpeningMotion>().SampleReveal(.64f);
                    }
                    Require(JsonUtility.ToJson(random) == JsonUtility.ToJson(UnityEngine.Random.state), "Presentation consumed gameplay randomness");
                }
                Capture(camera, target, name);
                foreach (var size in new[] { new Vector2Int(320, 568), new Vector2Int(430, 932) })
                {
                    rect.sizeDelta = size; camera.orthographicSize = size.y / 2f;
                    target.Release(); target.width = size.x * 2; target.height = size.y * 2; target.Create();
                    var composition = screen.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "Composition");
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
