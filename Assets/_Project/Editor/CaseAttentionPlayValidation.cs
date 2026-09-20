using System;
using System.IO;
using System.Collections.Generic;
using PushStars.Fight;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Checks the actual case idle/tap cycle in preview without changing reward saves.</summary>
    public static class CaseAttentionPlayValidation
    {
        private static RewardScreen _screen;
        private static double _start;
        private static int _step;
        private static Quaternion _rays;
        private static bool _rattled, _feedback;

        [MenuItem("Tools/Push Stars/Rewards/Validate Case Attention In Play Mode")]
        public static void Run()
        {
            _screen = Object.FindFirstObjectByType<RewardScreen>();
            if (!EditorApplication.isPlaying || !FightScreenNavigation.IsPreview ||
                _screen == null || _screen.Screen != FightScreen.CaseOpening || !_screen.CaseUi.TapButton.interactable)
                throw new InvalidOperationException("Run on the ready CaseOpening preview in Play Mode.");
            Require(_screen.CaseUi.Rays != null && _screen.CaseUi.TapHint != null, "Attention references missing.");
            _screen.SendMessage("ResetCaseAttention");
            _start = EditorApplication.timeSinceStartup;
            _step = 0; _rattled = _feedback = false;
            _rays = _screen.CaseUi.Rays.localRotation;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                Require(EditorApplication.isPlaying && _screen != null, "Case preview was interrupted.");
                var ui = _screen.CaseUi;
                double elapsed = EditorApplication.timeSinceStartup - _start;
                if (_step == 0)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(0, ui.Content.localEulerAngles.z)) > .1f) _rattled = true;
                    if (elapsed < 4.9) Require(ui.TapHint.alpha == 0, "Hand appeared before five seconds of inactivity.");
                    if (elapsed < 5.6) return;
                    Require(_rattled, "Waiting case never rattled.");
                    Require(Quaternion.Angle(_rays, ui.Rays.localRotation) > 2, "Background rays stayed static.");
                    Require(ui.TapHint.alpha > .95f, "Hand did not appear after inactivity.");
                    Require(!ui.TapHint.blocksRaycasts && !ui.TapHint.interactable, "Hand blocks the case hit target.");
                    var target = ui.TapButton.gameObject;
                    foreach (var point in new[] { new Vector2(.05f, .95f), new Vector2(.95f, .95f),
                        new Vector2(.05f, .05f), new Vector2(.95f, .05f), new Vector2(.5f, .5f) })
                    {
                        var pointer = new PointerEventData(EventSystem.current)
                        { position = new Vector2(Screen.width * point.x, Screen.height * point.y) };
                        var hits = new List<RaycastResult>();
                        EventSystem.current.RaycastAll(pointer, hits);
                        Require(hits.Count > 0 && hits[0].gameObject == target, "A screen region misses the case tap target: " + point);
                    }
                    int rarity = (int)FightScreenNavigation.PreviewRarity;
                    ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current)
                    { position = new Vector2(Screen.width * .05f, Screen.height * .95f), button = PointerEventData.InputButton.Left },
                        ExecuteEvents.pointerClickHandler);
                    Require((int)FightScreenNavigation.PreviewRarity == Math.Min(3, rarity + 1), "Corner tap did not consume exactly one upgrade.");
                    Require(ui.TapHint.alpha == 0, "Tap did not hide the hand immediately.");
                    _step = 1; _start = EditorApplication.timeSinceStartup;
                }
                else
                {
                    if (ui.Content.localScale.x > 1.06f) _feedback = true;
                    if (elapsed < 4.9) Require(ui.TapHint.alpha == 0, "Hand reappeared too soon after a tap.");
                    if (elapsed < 6.3) return;
                    Require(_feedback, "Idle animation suppressed the tap feedback.");
                    Require(ui.TapButton.interactable && ui.TapHint.alpha > .95f, "Next idle hint did not return.");
                    Finish("PASS: hand hidden for five seconds, then shown, hidden on tap and restored after inactivity; " +
                        "all four screen corners and center hit the same button; corner tap consumes one upgrade; " +
                        "chest rattles, rays rotate, tap feedback is preserved, cue does not block raycasts.");
                }
            }
            catch (Exception exception) { Finish("FAIL: " + exception); Debug.LogException(exception); }
        }

        private static void Finish(string report)
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/case-attention-play-validation.txt", report + "\n");
            Debug.Log(report);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
