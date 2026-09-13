using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.UI.Layout
{
    /// <summary>Authored positions shared by builds. Device overrides live separately in PlayerPrefs.</summary>
    [CreateAssetMenu(menuName = "Push Stars/Screen layouts", fileName = "ScreenLayouts")]
    public sealed class ScreenLayoutCatalog : ScriptableObject
    {
        public const string ResourcePath = "UI/ScreenLayouts";
        [SerializeField] private List<ScreenLayoutData> _screens = new List<ScreenLayoutData>();

        public ScreenLayoutData Find(string screenId) => _screens.Find(item => item != null && item.screenId == screenId);

        public void Set(ScreenLayoutData data)
        {
            if (data == null || string.IsNullOrEmpty(data.screenId)) return;
            _screens.RemoveAll(item => item == null || item.screenId == data.screenId);
            _screens.Add(data);
        }

        public void Remove(string screenId) => _screens.RemoveAll(item => item != null && item.screenId == screenId);
    }

    [Serializable]
    public sealed class ScreenLayoutData
    {
        public int version = 1;
        public string screenId;
        public List<ScreenLayoutElementState> elements = new List<ScreenLayoutElementState>();
    }

    [Serializable]
    public sealed class ScreenLayoutElementState
    {
        public string id;
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 pivot;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;

        public static ScreenLayoutElementState Capture(string id, RectTransform rect) => new ScreenLayoutElementState
        {
            id = id,
            anchorMin = rect.anchorMin,
            anchorMax = rect.anchorMax,
            pivot = rect.pivot,
            anchoredPosition = rect.anchoredPosition,
            sizeDelta = rect.sizeDelta
        };

        public bool IsValid => !string.IsNullOrEmpty(id) && Finite(anchorMin) && Finite(anchorMax) &&
                               Finite(pivot) && Finite(anchoredPosition) && Finite(sizeDelta);

        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && Mathf.Abs(value.x) <= 100000f && Mathf.Abs(value.y) <= 100000f;

        public void Apply(RectTransform rect)
        {
            if (rect == null || !IsValid) return;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
            // Scale and rotation belong to authored presentation/animation, never a layout save.
        }
    }
}
