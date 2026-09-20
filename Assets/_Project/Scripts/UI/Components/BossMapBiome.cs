using UnityEngine;

namespace PushStars.UI
{
    /// <summary>A chapter's environment. Add one to each island chapter to extend the map.</summary>
    public sealed class BossMapBiome : MonoBehaviour
    {
        public Color Background = new Color(.065f, .105f, .045f);
        public Color Mist = new Color(.20f, .27f, .085f);
        public Color Light = new Color(.62f, .58f, .12f);
        [Min(1)] public float TransitionHeight = 620;
        public RectTransform[] LightAnchors;
    }
}
