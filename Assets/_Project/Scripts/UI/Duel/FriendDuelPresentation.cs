using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Independent opponent avatar rendering and reversible two-person home layout.</summary>
    public sealed class FriendDuelPresentation : MonoBehaviour
    {
        public RawImage PlayerImage, FriendImage;
        public Camera SourceCamera, FriendCamera;
        public Transform FriendRoot;
        public Transform PlayerRoot;
        public Image PlayerShadow, FriendShadow;
        public Light RimLight;
        public GameObject MalePrefab, FemalePrefab;
        public RectTransform BattleTitle, BattleCaption;
        public float PlayerOffset = 0f, FriendOffset = -108f, PairScale = 1f;
        public float FriendScale = .82f, FriendDepth = 30f;
        public Vector2 PlayerShadowOffset = new Vector2(-10f, 5f);
        public Vector2 PlayerShadowSize = new Vector2(168f, 40f);
        private Vector2 _playerPosition, _titlePosition;
        private Vector3 _playerScale;
        private Vector3 _shadowPosition;
        private Vector2 _shadowSize;
        private bool _cached, _present;
        private float _blend;
        private string _avatarId;
        private GameObject _avatar;
        private RenderTexture _texture;
        public GameObject LoadedAvatar => _avatar;
        public float Blend => _blend;

        private void Cache()
        {
            if (_cached || PlayerImage == null) return;
            _cached = true;
            _playerPosition = PlayerImage.rectTransform.anchoredPosition;
            _playerScale = PlayerImage.rectTransform.localScale;
            _titlePosition = BattleTitle.anchoredPosition;
            if (PlayerShadow != null)
            { _shadowPosition = PlayerShadow.rectTransform.localPosition; _shadowSize = PlayerShadow.rectTransform.sizeDelta; }
        }

        public void Refresh(FriendDuelSession session)
        {
            Cache();
            if (!_cached) return;
            _present = session.HasRoom && session.HasFriend;
            BattleTitle.anchoredPosition = _titlePosition + (session.HasRoom ? Vector2.up * 11 : Vector2.zero);
            BattleCaption.gameObject.SetActive(session.HasRoom);
            if (_present && (_avatar == null || _avatarId != session.FriendAvatarId)) LoadAvatar(session.FriendAvatarId);
            if (!Application.isPlaying) Snap();
        }

        private void Update()
        {
            if (!_cached) return;
            _blend = Mathf.MoveTowards(_blend, _present ? 1 : 0, Time.unscaledDeltaTime / .32f);
            ApplyPose();
            if (!_present && _blend == 0) ReleaseAvatar();
        }

        /// <summary>Deterministic final pose for editor previews and returning from another tab.</summary>
        public void Snap()
        {
            _blend = _present ? 1 : 0; ApplyPose();
            if (!_present) ReleaseAvatar();
        }

        private void ApplyPose()
        {
            if (!_cached) return;
            float t = Mathf.SmoothStep(0, 1, _blend);
            float scale = Mathf.Lerp(1, PairScale, t);
            // Keep the feet on the same ground line as the portrait shrinks slightly.
            float down = PlayerImage.rectTransform.rect.height * _playerScale.y * .5f * (1 - scale);
            PlayerImage.rectTransform.anchoredPosition = _playerPosition + new Vector2(PlayerOffset * t, -down);
            PlayerImage.rectTransform.localScale = _playerScale * scale;
            float friendScale = Mathf.Lerp(PairScale, FriendScale, t);
            float friendDown = FriendImage.rectTransform.rect.height * _playerScale.y * .5f * (1 - friendScale);
            FriendImage.rectTransform.anchoredPosition = _playerPosition + new Vector2(FriendOffset - 18 * (1 - t), FriendDepth * t - friendDown);
            FriendImage.rectTransform.localScale = _playerScale * friendScale;
            FriendImage.color = new Color(1, 1, 1, t);
            FriendImage.gameObject.SetActive(_avatar != null && (_present || _blend > 0));
            FriendCamera.enabled = Application.isPlaying && FriendImage.gameObject.activeInHierarchy;
            PositionShadow(PlayerShadow, PlayerImage, SourceCamera, PlayerRoot, scale, .5f);
            if (PlayerShadow != null)
            {
                PlayerShadow.rectTransform.localPosition += (Vector3)(PlayerShadowOffset * scale);
                PlayerShadow.rectTransform.sizeDelta = PlayerShadowSize * scale;
            }
            bool showFriend = FriendImage.gameObject.activeInHierarchy;
            if (FriendShadow != null)
            {
                FriendShadow.gameObject.SetActive(showFriend);
                if (showFriend) PositionShadow(FriendShadow, FriendImage, FriendCamera, FriendRoot, friendScale, .5f * t);
            }
        }

        private void PositionShadow(Image shadow, RawImage portrait, Camera camera, Transform ground, float scale, float alpha)
        {
            if (shadow == null || camera == null || ground == null) return;
            Vector3 viewport = camera.WorldToViewportPoint(ground.position);
            var rect = portrait.rectTransform.rect;
            Vector3 feet = portrait.rectTransform.TransformPoint(new Vector3(rect.xMin + rect.width * viewport.x, rect.yMin + rect.height * viewport.y, 0));
            var point = shadow.rectTransform.parent.InverseTransformPoint(feet);
            shadow.rectTransform.localPosition = new Vector3(point.x, point.y - 3f * scale, _shadowPosition.z);
            shadow.rectTransform.sizeDelta = _shadowSize * scale;
            shadow.color = new Color(0, 0, 0, alpha);
        }

        private void LoadAvatar(string avatarId)
        {
            var prefab = avatarId == "female" ? FemalePrefab : avatarId == "male" ? MalePrefab : null;
            ReleaseAvatar();
            _avatarId = avatarId;
            if (prefab == null) return; // Do not substitute the owner's character for unknown appearance data.
            _avatar = Instantiate(prefab, FriendRoot, false);
            _avatar.name = "FriendAvatar_" + avatarId;
            foreach (Transform child in _avatar.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = FriendRoot.gameObject.layer;
            var animator = _avatar.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Play("Idle", 0, .37f); animator.Update(0);
            }
            if (RimLight != null)
            {
                var block = new MaterialPropertyBlock();
                foreach (var renderer in _avatar.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_RimColor", RimLight.color * RimLight.intensity);
                    block.SetFloat("_RimPower", 3.2f); block.SetFloat("_RimStrength", .34f);
                    var direction = -RimLight.transform.forward;
                    block.SetVector("_RimLightDirection", new Vector4(direction.x, direction.y, direction.z, 1));
                    renderer.SetPropertyBlock(block); block.Clear();
                }
            }
            _texture = new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32)
            { name = "FriendAvatarRT", antiAliasing = 2 };
            _texture.Create();
            var cameraPosition = FriendCamera.transform.position;
            var cameraRotation = FriendCamera.transform.rotation;
            FriendCamera.CopyFrom(SourceCamera);
            FriendCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            // CopyFrom also carries camera matrices; the remote stage must use its own transform.
            FriendCamera.ResetWorldToCameraMatrix();
            FriendCamera.ResetProjectionMatrix();
            FriendCamera.ResetCullingMatrix();
            FriendCamera.targetTexture = _texture;
            FriendCamera.clearFlags = CameraClearFlags.SolidColor;
            FriendCamera.backgroundColor = Color.clear;
            FriendCamera.enabled = Application.isPlaying;
            FriendImage.texture = _texture;
            RenderPreview();
        }

        public void RenderPreview()
        {
            if (_texture == null || _avatar == null) return;
            if (!Application.isPlaying) FriendCamera.scene = gameObject.scene;
            FriendCamera.Render();
        }

        private void ReleaseAvatar()
        {
            if (FriendCamera != null) { FriendCamera.enabled = false; FriendCamera.targetTexture = null; }
            if (FriendImage != null) { FriendImage.texture = null; FriendImage.gameObject.SetActive(false); }
            if (_avatar != null) { _avatar.SetActive(false); Dispose(_avatar); _avatar = null; }
            if (_texture != null) { _texture.Release(); Dispose(_texture); _texture = null; }
        }

        private static void Dispose(Object obj)
        { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }

        private void OnDisable()
        {
            _present = false; _blend = 0;
            ReleaseAvatar();
            if (_cached) ApplyPose();
            if (FriendShadow != null) FriendShadow.gameObject.SetActive(false);
        }
        private void OnDestroy() => ReleaseAvatar();
    }
}
