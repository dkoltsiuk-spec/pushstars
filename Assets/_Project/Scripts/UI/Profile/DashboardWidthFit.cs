using UnityEngine;

namespace PushStars.UI
{
    // Design coordinates stay editable; the scroll area handles shorter phones.
    [ExecuteAlways]
    public sealed class DashboardWidthFit : MonoBehaviour
    {
        public RectTransform Art;
        public float DesignHeight = 690;
        private void LateUpdate()
        {
            if (Art == null) return;
            var rect = (RectTransform)transform;
            float scale = Mathf.Max(1, rect.rect.width) / 390f;
            Art.localScale = Vector3.one * scale;
            Art.sizeDelta = new Vector2(390, DesignHeight);
            var size = rect.sizeDelta;
            float height = DesignHeight * scale;
            if (Mathf.Abs(size.y - height) > .1f) rect.sizeDelta = new Vector2(size.x, height);
        }
    }
}
