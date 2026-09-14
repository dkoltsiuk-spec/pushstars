using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>Authored forest layers are exclusive to the first island's boss battle.</summary>
    public sealed class GoblinForestPresentation : MonoBehaviour
    {
        public GameObject Background, Foreground, Matte;
        public GameObject[] OriginalDecor;
        public RectTransform LeftGrass, RightGrass, Branch, FernStone;
        public ForestParticleEffects Effects;
        private Vector2 _viewport;

        public void ApplyLayout(Vector2 viewport)
        {
            if (Background == null || !Background.activeSelf || viewport == _viewport) return;
            _viewport = viewport;
            ((RectTransform)Background.transform).sizeDelta = viewport;
            ((RectTransform)Foreground.transform).sizeDelta = viewport;
            float halfWidth = viewport.x * .5f, halfHeight = viewport.y * .5f;
            if (LeftGrass) LeftGrass.anchoredPosition = new Vector2(-halfWidth + 45, -halfHeight + 55);
            if (RightGrass) RightGrass.anchoredPosition = new Vector2(halfWidth - 41, -halfHeight + 55);
            if (Branch) Branch.anchoredPosition = new Vector2(halfWidth - 91, halfHeight - 73);
            if (FernStone) FernStone.anchoredPosition = new Vector2(-halfWidth + 54, 54);
        }

        public void Configure(bool active)
        {
            _viewport = Vector2.zero;
            if (Background != null) Background.SetActive(active);
            if (Foreground != null) Foreground.SetActive(active);
            if (Matte != null) Matte.SetActive(active);
            if (OriginalDecor == null) return;
            foreach (var item in OriginalDecor)
                if (item != null) item.SetActive(!active);
        }
    }
}
