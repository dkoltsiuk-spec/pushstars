using System;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PushStars.Editor
{
    /// <summary>Engine-backed regression checks. Uses only disposable objects and unique preference keys.</summary>
    public static class ScreenLayoutRegression
    {
        private const string PreferencePrefix = "PushStars.ScreenLayout.v1.";

        public static void Run() => RunBatch();

        [MenuItem("Tools/Push Stars/Run Screen Layout Regression", priority = 91)]
        public static void RunBatch()
        {
            int passed = 0;
            Run("capture / apply preserves geometry and animation", CheckCaptureApply); passed++;
            Run("registration rejects foreign canvases and replaces duplicate ids", CheckRegistration); passed++;
            Run("save survives reapply; cancel restores edit snapshot", CheckSaveCancel); passed++;
            Run("reset is provisional, cancelable, and clears overrides when saved", CheckReset); passed++;
            Run("accepting authored defaults closes editing and clears stale overrides", CheckAcceptDefaults); passed++;
            Run("malformed and mismatched saves cannot change the screen", CheckInvalidData); passed++;
            Run("mouse and touch drag through scaled parents persists correctly", CheckPointerDrag); passed++;
            Debug.Log($"Screen layout regression passed: {passed}/7.");
        }

        private static void Run(string name, Action<Fixture> check)
        {
            using (var fixture = new Fixture()) check(fixture);
            Debug.Log("PASS layout: " + name);
        }

        private static void CheckCaptureApply(Fixture fixture)
        {
            var rect = fixture.Target;
            rect.anchorMin = new Vector2(0.2f, 0.3f);
            rect.anchorMax = new Vector2(0.7f, 0.8f);
            rect.pivot = new Vector2(0.25f, 0.75f);
            rect.anchoredPosition = new Vector2(34f, -67f);
            rect.sizeDelta = new Vector2(-18f, 140f);
            var snapshot = fixture.Root.CaptureLayout();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one;
            rect.localScale = new Vector3(1.8f, 1.2f, 1f);
            rect.localRotation = Quaternion.Euler(0f, 0f, 29f);
            fixture.Root.ApplyLayout(snapshot);
            Equal(rect.anchorMin, new Vector2(0.2f, 0.3f), "minimum anchor");
            Equal(rect.anchorMax, new Vector2(0.7f, 0.8f), "maximum anchor");
            Equal(rect.pivot, new Vector2(0.25f, 0.75f), "pivot");
            Equal(rect.anchoredPosition, new Vector2(34f, -67f), "position");
            Equal(rect.sizeDelta, new Vector2(-18f, 140f), "size");
            Require(Vector3.Distance(rect.localScale, new Vector3(1.8f, 1.2f, 1f)) < 0.001f, "Layout overwrote animated scale.");
            Require(Quaternion.Angle(rect.localRotation, Quaternion.Euler(0f, 0f, 29f)) < 0.001f, "Layout overwrote animated rotation.");
        }

        private static void CheckRegistration(Fixture fixture)
        {
            fixture.Root.Register("target", fixture.Target);
            Require(fixture.Root.Targets.Count == 1, "Registering a target twice duplicated it.");
            var replacement = fixture.CreateTarget(fixture.Canvas.transform, "Replacement");
            fixture.Root.Register("replacement-before-rename", replacement);
            Require(fixture.Root.Targets.Count == 2, "Distinct targets were not registered independently.");
            fixture.Root.Register("target", replacement);
            Require(fixture.Root.Targets.Count == 1 && fixture.Root.Targets[0].rect == replacement, "A stable id was not replaced.");
            fixture.Root.Register("renamed", replacement);
            Require(fixture.Root.Targets.Count == 1 && fixture.Root.Targets[0].id == "renamed", "Renaming a target duplicated it.");

            var siblingRoot = new GameObject("SiblingRoot", typeof(RectTransform), typeof(ScreenLayoutRoot));
            siblingRoot.transform.SetParent(fixture.Canvas.transform, false);
            var siblingLayout = siblingRoot.GetComponent<ScreenLayoutRoot>();
            siblingLayout.Configure(fixture.ScreenId + "-sibling", false);
            siblingLayout.ShowEditButton = false;
            siblingLayout.Register("external-sibling", replacement);
            Require(siblingLayout.Targets.Count == 1, "Same-canvas sibling registration was rejected.");

            var foreignCanvas = fixture.CreateExtraCanvas();
            var foreignTarget = fixture.CreateTarget(foreignCanvas.transform, "ForeignTarget");
            fixture.Root.Register("foreign", foreignTarget);
            Require(fixture.Root.Targets.Count == 1, "A foreign canvas target was accepted.");
        }

        private static void CheckSaveCancel(Fixture fixture)
        {
            fixture.Root.BeginEditing();
            Require(fixture.Root.IsEditing, "Editing did not begin.");
            fixture.Target.anchoredPosition = new Vector2(81f, -23f);
            fixture.Target.sizeDelta = new Vector2(144f, 66f);
            fixture.Root.SaveLayout();
            Require(!fixture.Root.IsEditing && !ScreenLayoutRoot.IsAnyEditing, "Save did not finish editing.");
            Require(PlayerPrefs.HasKey(fixture.PreferenceKey), "Save did not create its isolated preference.");
            fixture.Target.anchoredPosition = Vector2.zero;
            fixture.Root.ApplySavedLayout();
            Equal(fixture.Target.anchoredPosition, new Vector2(81f, -23f), "reloaded saved position");
            Equal(fixture.Target.sizeDelta, new Vector2(144f, 66f), "reloaded saved size");
            string saved = PlayerPrefs.GetString(fixture.PreferenceKey);
            fixture.Root.BeginEditing();
            fixture.Target.anchoredPosition = new Vector2(-170f, 211f);
            fixture.Root.CancelEditing();
            Equal(fixture.Target.anchoredPosition, new Vector2(81f, -23f), "cancel position");
            Require(PlayerPrefs.GetString(fixture.PreferenceKey) == saved, "Cancel changed the saved preference.");
        }

        private static void CheckReset(Fixture fixture)
        {
            fixture.Root.BeginEditing();
            fixture.Target.anchoredPosition = new Vector2(109f, 44f);
            fixture.Root.SaveLayout();
            string saved = PlayerPrefs.GetString(fixture.PreferenceKey);
            fixture.Root.BeginEditing();
            fixture.Root.ResetToDefaults();
            Equal(fixture.Target.anchoredPosition, Fixture.OriginalPosition, "reset position");
            Require(PlayerPrefs.GetString(fixture.PreferenceKey) == saved, "Provisional reset wrote preferences before Save.");
            fixture.Root.CancelEditing();
            Equal(fixture.Target.anchoredPosition, new Vector2(109f, 44f), "canceled reset position");
            fixture.Root.BeginEditing();
            fixture.Root.ResetToDefaults();
            fixture.Root.SaveLayout();
            Require(!PlayerPrefs.HasKey(fixture.PreferenceKey), "Saving an unchanged reset retained the override.");
            Equal(fixture.Target.anchoredPosition, Fixture.OriginalPosition, "saved default position");

            fixture.Root.BeginEditing();
            fixture.Root.ResetToDefaults();
            fixture.Target.anchoredPosition = new Vector2(13f, 19f);
            fixture.Root.SaveLayout();
            Require(PlayerPrefs.HasKey(fixture.PreferenceKey), "Moving after reset failed to create the new override.");
            fixture.Target.anchoredPosition = Vector2.zero;
            fixture.Root.ApplySavedLayout();
            Equal(fixture.Target.anchoredPosition, new Vector2(13f, 19f), "edited-after-reset position");
        }

        private static void CheckAcceptDefaults(Fixture fixture)
        {
            fixture.Root.BeginEditing();
            fixture.Target.anchoredPosition = new Vector2(60f, 80f);
            fixture.Root.SaveLayout();
            fixture.Root.BeginEditing();
            fixture.Target.anchoredPosition = new Vector2(90f, 120f);
            // The actual editor writes its catalog before this call. A unique test screen has
            // no catalog entry, so this also checks accepting does not restore the edit snapshot.
            fixture.Root.AcceptSavedDefaults();
            Require(!fixture.Root.IsEditing, "Accepting authored defaults left the edit open.");
            Require(!PlayerPrefs.HasKey(fixture.PreferenceKey), "A stale device override would mask the catalog.");
            Equal(fixture.Target.anchoredPosition, new Vector2(90f, 120f), "accepted position");
            fixture.Root.CancelEditing();
            Equal(fixture.Target.anchoredPosition, new Vector2(90f, 120f), "position after preview-switch cancel");
        }

        private static void CheckInvalidData(Fixture fixture)
        {
            var data = fixture.Root.CaptureLayout();
            data.screenId = "some-other-screen";
            data.elements[0].anchoredPosition = Vector2.one * 400f;
            fixture.Root.ApplyLayout(data);
            Equal(fixture.Target.anchoredPosition, Fixture.OriginalPosition, "mismatched screen data");
            data.screenId = fixture.ScreenId;
            data.elements[0].anchoredPosition = new Vector2(float.NaN, float.PositiveInfinity);
            fixture.Root.ApplyLayout(data);
            Equal(fixture.Target.anchoredPosition, Fixture.OriginalPosition, "non-finite data");
            PlayerPrefs.SetString(fixture.PreferenceKey, "{ malformed");
            fixture.Root.ApplySavedLayout();
            Equal(fixture.Target.anchoredPosition, Fixture.OriginalPosition, "malformed saved data");
        }

        private static void CheckPointerDrag(Fixture fixture)
        {
            var parent = fixture.CreateTarget(fixture.Canvas.transform, "ScaledParent");
            parent.anchorMin = parent.anchorMax = new Vector2(0.5f, 0.5f);
            parent.sizeDelta = new Vector2(300f, 500f);
            parent.anchoredPosition = new Vector2(17f, -29f);
            parent.localScale = new Vector3(0.625f, 0.8f, 1f);
            fixture.Target.SetParent(parent, false);
            fixture.Target.anchoredPosition = Fixture.OriginalPosition;
            fixture.Canvas.GetComponent<Canvas>().scaleFactor = 1.75f;
            fixture.Root.BeginEditing();
            Canvas.ForceUpdateCanvases();
            var overlay = fixture.Canvas.GetComponentInChildren<ScreenLayoutOverlay>(true);
            Require(overlay != null, "Beginning an edit did not create the drag overlay.");

            var expected = Fixture.OriginalPosition;
            var delta = new Vector2(40f, 35f);
            foreach (int pointerId in new[] { -1, 3 }) // Mouse, then a touch finger.
            {
                Vector3 startWorld = fixture.Target.TransformPoint(fixture.Target.rect.center);
                Vector3 startLocal = parent.InverseTransformPoint(startWorld);
                Vector3 endWorld = parent.TransformPoint(startLocal + (Vector3)delta);
                var pointer = new PointerEventData(EventSystem.current)
                {
                    pointerId = pointerId,
                    position = RectTransformUtility.WorldToScreenPoint(null, startWorld)
                };
                overlay.OnPointerDown(pointer);
                overlay.OnBeginDrag(pointer);
                var otherFinger = new PointerEventData(EventSystem.current)
                {
                    pointerId = 99,
                    position = RectTransformUtility.WorldToScreenPoint(null, endWorld)
                };
                overlay.OnDrag(otherFinger);
                Equal(fixture.Target.anchoredPosition, expected, "another finger must not steal the drag");
                pointer.position = RectTransformUtility.WorldToScreenPoint(null, endWorld);
                overlay.OnDrag(pointer);
                expected += delta;
                Equal(fixture.Target.anchoredPosition, expected, "scaled parent drag for pointer " + pointerId);
                overlay.OnEndDrag(pointer);
            }

            fixture.Root.SaveLayout();
            Require(PlayerPrefs.HasKey(fixture.PreferenceKey), "Dragging then saving did not persist the isolated layout.");
            fixture.Target.anchoredPosition = Vector2.zero;
            fixture.Root.ApplySavedLayout();
            Equal(fixture.Target.anchoredPosition, expected, "saved mouse/touch drag after reload");
        }

        private static void Equal(Vector2 actual, Vector2 expected, string label)
        {
            Require(Vector2.Distance(actual, expected) < 0.01f, label + ": expected " + expected + ", got " + actual);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Screen layout regression: " + message);
        }

        private sealed class Fixture : IDisposable
        {
            public static readonly Vector2 OriginalPosition = new Vector2(12f, -36f);
            public readonly string ScreenId = "layout-regression-" + Guid.NewGuid().ToString("N");
            public string PreferenceKey => PreferencePrefix + ScreenId;
            public readonly GameObject Canvas;
            public readonly ScreenLayoutRoot Root;
            public readonly RectTransform Target;
            private GameObject _extraCanvas;

            public Fixture()
            {
                if (ScreenLayoutRoot.IsAnyEditing)
                    throw new InvalidOperationException("Close the active layout edit before running the regression.");
                Canvas = new GameObject("__LayoutRegression", typeof(RectTransform), typeof(Canvas), typeof(ScreenLayoutRoot));
                Canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                Root = Canvas.GetComponent<ScreenLayoutRoot>();
                Root.Configure(ScreenId, false);
                Root.ShowEditButton = false;
                Target = CreateTarget(Canvas.transform, "Movable");
                Target.anchoredPosition = OriginalPosition;
                Target.sizeDelta = new Vector2(120f, 64f);
                Root.Register("target", Target);
            }

            public RectTransform CreateTarget(Transform parent, string name)
            {
                var item = new GameObject(name, typeof(RectTransform));
                item.transform.SetParent(parent, false);
                return (RectTransform)item.transform;
            }

            public GameObject CreateExtraCanvas()
            {
                _extraCanvas = new GameObject("__LayoutRegressionForeign", typeof(RectTransform), typeof(Canvas));
                return _extraCanvas;
            }

            public void Dispose()
            {
                try
                {
                    if (Root != null) Root.CancelEditing();
                    if (Canvas != null) UnityEngine.Object.DestroyImmediate(Canvas);
                    if (_extraCanvas != null) UnityEngine.Object.DestroyImmediate(_extraCanvas);
                }
                finally
                {
                    PlayerPrefs.DeleteKey(PreferenceKey);
                    PlayerPrefs.Save();
                }
            }
        }
    }
}
