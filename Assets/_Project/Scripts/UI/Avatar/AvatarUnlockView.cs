using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>The unlock bar on a hero or arena card. Aura unlocks fill with peak Aura against the
    /// goal, e.g. "32K / 50K", next to the Aura icon; it only ever fills, since losing Aura never takes
    /// progress back. Island unlocks count the island's defeated bosses next to the island's badge.</summary>
    public sealed class AvatarUnlockView : MonoBehaviour
    {
        public GameObject ProgressRoot;
        public AvatarShardBar Bar;
        public TextMeshProUGUI Count;
        public Image Head;

        private static Sprite _auraIcon;
        private Vector3 _headScale = Vector3.zero;

        /// <summary>The Aura skull, made into a sprite once (the texture is imported for the stamp).</summary>
        public static Sprite AuraIcon
        {
            get
            {
                if (_auraIcon != null) return _auraIcon;
                var texture = Resources.Load<Texture2D>("Rewards/AuraSkull");
                if (texture != null) _auraIcon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
                return _auraIcon;
            }
        }

        /// <summary>"forest" → AvatarCollection/ForestIslandBadge.</summary>
        public static Sprite IslandBadge(BossChapter island)
            => island == null || string.IsNullOrEmpty(island.Id) ? null
                : Resources.Load<Sprite>("AvatarCollection/" + char.ToUpperInvariant(island.Id[0]) + island.Id.Substring(1) + "IslandBadge");

        public void Refresh(AvatarOffer offer)
        {
            var island = offer != null && offer.UnlocksByIsland ? BossCatalog.FindChapter(offer.Island) : null;
            bool show = offer != null && !CaseRewards.OwnsAvatar(offer.Id) &&
                (offer.UnlocksByAura && offer.AuraGoal > 0 || island != null);
            ProgressRoot.SetActive(show);
            if (!show) return;
            if (island != null) ShowIsland(island);
            else ShowAura(CaseRewards.AvatarUnlockProgress(offer.Id), offer.AuraGoal);
        }

        public void Refresh(ArenaDefinition arena)
        {
            var island = arena != null && arena.Access == ArenaAccess.Island ? BossCatalog.FindChapter(arena.Island) : null;
            bool show = arena != null && !ArenaProfile.Owns(arena.Id) &&
                (arena.Access == ArenaAccess.Aura && arena.Price > 0 || island != null);
            ProgressRoot.SetActive(show);
            if (!show) return;
            if (island != null) ShowIsland(island);
            else ShowAura(System.Math.Min(CaseRewards.AuraPeak, arena.Price), arena.Price);
        }

        private void ShowIsland(BossChapter island)
        {
            int beaten = BossCatalog.ClearedInChapter(island);
            Bar.SetProgress((float)beaten / Mathf.Max(1, island.Bosses.Count));
            Count.text = $"{beaten} / {island.Bosses.Count}";
            SetIcon(IslandBadge(island), 1f);
        }

        private void ShowAura(long progress, long goal)
        {
            Bar.SetProgress((float)((double)progress / goal));
            Count.text = $"{AuraFormat.Short(progress)} / {AuraFormat.Short(goal)}";
            // The head slot overhangs the bar for hero heads; the skull reads better tucked in.
            SetIcon(AuraIcon, .6f);
        }

        private void SetIcon(Sprite sprite, float scale)
        {
            Head.sprite = sprite;
            if (_headScale == Vector3.zero) _headScale = Head.rectTransform.localScale;
            Head.rectTransform.localScale = new Vector3(_headScale.x * scale, _headScale.y * scale, _headScale.z);
            Head.preserveAspect = true;
            Head.enabled = sprite != null;
        }
    }
}
