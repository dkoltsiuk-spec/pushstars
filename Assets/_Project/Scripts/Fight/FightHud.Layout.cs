using UnityEngine;
using UnityEngine.UI;
using PushStars.UI.Layout;

namespace PushStars.Fight
{
    public sealed partial class FightHud
    {
        [SerializeField] private ScreenLayoutRoot _duelEditor;
        [SerializeField] private ScreenLayoutRoot _soloEditor;
        private ScreenLayoutElementState _originalPlayerHalf;
        private ScreenLayoutElementState _originalPlayerAvatar;
        private ScreenLayoutElementState _originalBanner;
        private ScreenLayoutElementState _originalCountdown;
        private RawImage _duelPlayerImage, _duelOpponentImage;
        private Rect _duelPlayerUv, _duelOpponentUv;
        private bool _duelZoomCaptured;

        private void SetDuelAvatarZoom(bool enabled)
        {
            if (!_duelZoomCaptured)
            {
                _duelPlayerImage = _playerHalf != null ? _playerHalf.GetComponentInChildren<RawImage>(true) : null;
                _duelOpponentImage = _opponentHalf != null ? _opponentHalf.GetComponentInChildren<RawImage>(true) : null;
                if (_duelPlayerImage != null) _duelPlayerUv = _duelPlayerImage.uvRect;
                if (_duelOpponentImage != null) _duelOpponentUv = _duelOpponentImage.uvRect;
                _duelZoomCaptured = true;
            }
            // Crop the live render equally on both axes: 1 / .8 = 125% apparent size.
            // Keep the lower edge fixed so the feet stay inside their respective halves.
            // Always derive from the original UVs, never from an already zoomed viewport.
            if (_duelPlayerImage != null) _duelPlayerImage.uvRect = enabled ? ZoomFromBottom(_duelPlayerUv) : _duelPlayerUv;
            if (_duelOpponentImage != null) _duelOpponentImage.uvRect = enabled ? ZoomFromBottom(_duelOpponentUv) : _duelOpponentUv;
        }

        private static Rect ZoomFromBottom(Rect uv)
            => new Rect(uv.x + uv.width * .1f, uv.y, uv.width * .8f, uv.height * .8f);

        private void CaptureHudDefaults()
        {
            if (_bannerRoot != null) _originalBanner = ScreenLayoutElementState.Capture("banner", (RectTransform)_bannerRoot.transform);
            if (_countdown != null) _originalCountdown = ScreenLayoutElementState.Capture("countdown", _countdown.rectTransform);
            if (_playerHalf == null) return;
            _originalPlayerHalf = ScreenLayoutElementState.Capture("half", _playerHalf);
            var image = _playerHalf.GetComponentInChildren<RawImage>(true);
            if (image != null) _originalPlayerAvatar = ScreenLayoutElementState.Capture("avatar", image.rectTransform);
        }

        private void RestoreSharedHudDefaults()
        {
            if (_bannerRoot != null) _originalBanner?.Apply((RectTransform)_bannerRoot.transform);
            if (_countdown != null) _originalCountdown?.Apply(_countdown.rectTransform);
            if (_playerHalf == null) return;
            _originalPlayerHalf?.Apply(_playerHalf);
            var image = _playerHalf.GetComponentInChildren<RawImage>(true);
            if (image != null) _originalPlayerAvatar?.Apply(image.rectTransform);
        }

        private void ConfigureEditableHud(bool solo)
        {
            if (_duelEditor == null) _duelEditor = GetComponent<ScreenLayoutRoot>();
            if (_soloEditor == null && _soloPanel != null) _soloEditor = _soloPanel.GetComponent<ScreenLayoutRoot>();
            if (_duelEditor == null)
            {
                _duelEditor = gameObject.AddComponent<ScreenLayoutRoot>();
                _duelEditor.Configure("battle", false);
                RegisterChildren(_duelEditor, _opponentPanel, "opponent");
                RegisterChildren(_duelEditor, _playerPanel, "player");
                RegisterAvatar(_duelEditor, _playerHalf, "player-avatar");
                RegisterAvatar(_duelEditor, _opponentHalf != null ? _opponentHalf.transform : null, "opponent-avatar");
                RegisterObject(_duelEditor, _timerPlate, "clock");
                RegisterObject(_duelEditor, _bannerRoot, "guidance");
                if (_countdown != null) _duelEditor.Register("countdown", _countdown.rectTransform);
                var safe = transform.Find("SafeArea");
                if (safe != null)
                {
                    RegisterObject(_duelEditor, safe.Find("ExitFight")?.gameObject, "finish-button");
                    RegisterObject(_duelEditor, safe.Find("Debug")?.gameObject, "debug-button");
                }
            }
            if (_soloEditor == null && _soloPanel != null)
            {
                _soloEditor = _soloPanel.AddComponent<ScreenLayoutRoot>();
                _soloEditor.Configure("battle-solo", false);
                RegisterChildren(_soloEditor, _soloPanel, "solo");
                RegisterAvatar(_soloEditor, _playerHalf, "player-avatar");
                RegisterObject(_soloEditor, _bannerRoot, "guidance");
                if (_countdown != null) _soloEditor.Register("countdown", _countdown.rectTransform);
            }
            _duelEditor.SetAvailable(!solo);
            if (_soloEditor != null) _soloEditor.SetAvailable(solo);
            (solo ? _soloEditor : _duelEditor)?.ApplySavedLayout();
        }

        public void SetLayoutAvailable(bool available)
        {
            if (_duelEditor != null) _duelEditor.SetAvailable(available && !_solo);
            if (_soloEditor != null) _soloEditor.SetAvailable(available && _solo);
        }

        private static void RegisterChildren(ScreenLayoutRoot layout, GameObject parent, string prefix)
        {
            if (parent == null) return;
            foreach (Transform child in parent.transform)
            {
                if (child.name == "PauseOverlay") continue;
                if (child is RectTransform rect) layout.Register(prefix + "/" + child.name, rect);
            }
        }

        private static void RegisterAvatar(ScreenLayoutRoot layout, Transform parent, string id)
        {
            if (parent == null) return;
            var image = parent.GetComponentInChildren<RawImage>(true);
            if (image != null) layout.Register(id, image.rectTransform);
        }

        private static void RegisterObject(ScreenLayoutRoot layout, GameObject target, string id)
        {
            if (target != null && target.transform is RectTransform rect) layout.Register(id, rect);
        }
    }
}
