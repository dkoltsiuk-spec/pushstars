using PushStars.Core;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>An island's PvP arena split behind the boss battle, with a ledge on the seam and calm ambient
    /// particles: the ice temple with snow on the second island, the lava forge with embers on the third.</summary>
    public sealed class ArenaIslandPresentation : MonoBehaviour
    {
        public string ArenaId;
        public GameObject Background, Foreground, Matte;
        public GameObject[] OriginalDecor;
        public SplitArenaGraphic Backdrop;
        public ForestParticleEffects Effects;
        private Vector2 _viewport;

        public void ApplyLayout(Vector2 viewport)
        {
            if (Background == null || !Background.activeSelf || viewport == _viewport) return;
            _viewport = viewport;
            ((RectTransform)Background.transform).sizeDelta = viewport;
            ((RectTransform)Foreground.transform).sizeDelta = viewport;
        }

        public void Configure(bool active)
        {
            _viewport = Vector2.zero;
            // The arena sprite is resolved on demand so PvP and other islands' fights never hold the texture.
            if (Backdrop != null) { Backdrop.Sprite = active ? ArenaCatalog.Get(ArenaId)?.Home : null; Backdrop.SetVerticesDirty(); Backdrop.SetMaterialDirty(); }
            if (Background != null) Background.SetActive(active);
            if (Foreground != null) Foreground.SetActive(active);
            if (Matte != null) Matte.SetActive(active);
            if (OriginalDecor == null) return;
            foreach (var item in OriginalDecor)
                if (item != null) item.SetActive(!active);
        }
    }
}
