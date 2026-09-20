using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Authored artwork follows the same rarity as the chest and stars.</summary>
    public sealed class CaseRarityPresentation : MonoBehaviour
    {
        public RawImage Background;
        public Texture2D[] Backgrounds = new Texture2D[4];
        public Image[] Lightning;
        public Sprite DarkLightning, LightLightning;
        public CaseTapGraphic Rays;

        public void Apply(int rarity)
        {
            rarity = Mathf.Clamp(rarity, 0, 3);
            if (Rays != null)
            {
                Rays.RayTint = rarity == 0 ? new Color(.8f, .84f, 1, .16f)
                    : new Color(1, 1, 1, rarity == 2 ? .7f : .45f);
                Rays.SetVerticesDirty();
            }
            if (Background != null)
            {
                Background.texture = Backgrounds[rarity];
                Background.enabled = Background.texture != null;
            }
            if (Lightning == null) return;
            foreach (var bolt in Lightning)
                if (bolt != null) bolt.sprite = rarity <= 1 ? DarkLightning : LightLightning;
        }
    }
}
