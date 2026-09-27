using UnityEngine;

namespace PushStars.UI
{
    public static class AvatarFraming
    {
        // Preserve height-driven framing. Horizontal overscan never changes the crop's zoom.
        public static Rect FitPortrait(Rect body, float displayAspect, float textureAspect,
            float height, float footShare = .5f)
        {
            height = Mathf.Min(1f, height);
            float width = Mathf.Min(1f, height * displayAspect / textureAspect);
            height = Mathf.Min(height, width * textureAspect / displayAspect);
            return new Rect(Mathf.Clamp(body.center.x - width * .5f, 0f, 1f - width),
                Mathf.Clamp(body.yMin - (height - body.height) * footShare, 0f, 1f - height), width, height);
        }
    }
}