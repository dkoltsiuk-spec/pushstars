using PushStars.Core;
using PushStars.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PreparationArenaBackdrop : MonoBehaviour
    {
        [SerializeField] private string _playerId = ArenaCatalog.DefaultId, _opponentId = ArenaCatalog.DefaultId;
        private RawImage _player, _opponent;
        private RawImage _divider;
        private RectTransform _medal;
        private float _seam = .51f;
        private Vector2 _size;
        private bool _built;
        public void SetMaps(string player, string opponent)
        {
            _playerId = ArenaCatalog.Normalize(player); _opponentId = ArenaCatalog.Normalize(opponent);
            Build(); Refresh();
        }
        private void OnEnable() { Build(); }
        private void LateUpdate()
        {
            var size = ((RectTransform)transform).rect.size;
            if (size != _size || !Mathf.Approximately(MedalSeam(), _seam)) Refresh();
        }
        private void Build()
        {
            if (_built && _player != null && _opponent != null && _divider != null) return;
            var legacy = GetComponent<ReadyScreenGraphic>(); if (legacy != null) legacy.enabled = false;
            foreach (Transform child in transform)
                if (child.name != "PlayerArena" && child.name != "OpponentArena" && child.name != "TeamDivider") child.gameObject.SetActive(false);
            if (_medal == null && transform.parent != null)
                foreach (var rect in transform.parent.GetComponentsInChildren<RectTransform>(true))
                    if (rect.name == "VsMedal") { _medal = rect; break; }
            // Stretch the radial artwork's central strip across the screen so its
            // transparent side margins never leave the left/right edges untinted.
            _opponent = Half("OpponentArena", _seam, 1f, "RedOverlay", new Rect(.49f, 0f, .02f, .5f));
            _player = Half("PlayerArena", 0f, _seam, "BlueOverlay", new Rect(.49f, .5f, .02f, .5f));
            _divider = Artwork(transform, "TeamDivider", "Divider");
            var dividerRect = _divider.rectTransform;
            dividerRect.anchorMin = new Vector2(0f, _seam);
            dividerRect.anchorMax = new Vector2(1f, _seam);
            dividerRect.pivot = new Vector2(.5f, .5f);
            dividerRect.anchoredPosition = Vector2.zero;
            dividerRect.SetAsLastSibling();
            _built = true;
        }
        private RawImage Half(string name, float bottom, float top, string artwork, Rect uv)
        {
            var rect = transform.Find(name) as RectTransform;
            if (rect == null) rect = ArenaUi.Rect(transform, name, Vector2.zero, Vector2.zero);
            rect.anchorMin = new Vector2(0,bottom); rect.anchorMax = new Vector2(1,top); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = rect.GetComponent<RawImage>() ?? rect.gameObject.AddComponent<RawImage>(); image.raycastTarget = false;
            var oldOverlay = rect.Find("TeamOverlay");
            if (oldOverlay != null) oldOverlay.gameObject.SetActive(false);
            var overlay = Artwork(rect, "TeamGradient", artwork);
            ArenaUi.Stretch(overlay.rectTransform);
            // Only half of each radial PNG is visible: red is strongest at the top edge,
            // blue at the bottom edge, and both fade towards the divider.
            overlay.uvRect = uv;
            return image;
        }
        private static RawImage Artwork(Transform parent, string name, string resource)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null) rect = ArenaUi.Rect(parent, name, Vector2.zero, Vector2.zero);
            var image = rect.GetComponent<RawImage>() ?? rect.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>("MatchFound/" + resource);
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }
        private void Refresh()
        {
            if (_player == null || _opponent == null) return;
            _size = ((RectTransform)transform).rect.size;
            _seam = MedalSeam();
            _opponent.rectTransform.anchorMin = new Vector2(0f, _seam);
            _player.rectTransform.anchorMax = new Vector2(1f, _seam);
            if (_divider != null)
            {
                _divider.rectTransform.anchorMin = new Vector2(0f, _seam);
                _divider.rectTransform.anchorMax = new Vector2(1f, _seam);
            }
            SetArt(_player, ArenaCatalog.Get(_playerId)?.Home);
            SetArt(_opponent, ArenaCatalog.Get(_opponentId)?.Home);
            if (_divider != null && _divider.texture != null)
                _divider.rectTransform.sizeDelta = new Vector2(0f, _size.x * _divider.texture.height / _divider.texture.width);
        }
        private float MedalSeam()
        {
            var bounds = ((RectTransform)transform).rect;
            if (_medal == null || bounds.height <= 0f) return _seam;
            // Follow the stationary medal box, not the animated crown inside it.
            float y = transform.InverseTransformPoint(_medal.TransformPoint(_medal.rect.center)).y;
            return Mathf.Clamp01((y - bounds.yMin) / bounds.height);
        }
        private static void SetArt(RawImage image, Sprite sprite)
        {
            if (sprite == null) return;
            image.texture = sprite.texture;
            var r = sprite.textureRect; var size = image.rectTransform.rect.size;
            float h = Mathf.Min(r.height, r.width * size.y / Mathf.Max(1,size.x));
            float w = Mathf.Min(r.width, r.height * size.x / Mathf.Max(1,size.y));
            image.uvRect = new Rect((r.x+(r.width-w)*.5f)/sprite.texture.width,
                (r.y+(r.height-h)*.34f)/sprite.texture.height,w/sprite.texture.width,h/sprite.texture.height);
        }
        public static void Apply(GameObject root)
        {
            var target = root.transform.Find("PreparationBackdrop"); if (target == null) return;
            var background = target.GetComponent<PreparationArenaBackdrop>() ?? target.gameObject.AddComponent<PreparationArenaBackdrop>();
            background.SetMaps(ArenaMatch.HasChoices ? ArenaMatch.PlayerId : ArenaProfile.SelectedId,
                ArenaMatch.HasChoices ? ArenaMatch.OpponentId : GhostStore.Load()?.arenaId);
        }
    }
}
