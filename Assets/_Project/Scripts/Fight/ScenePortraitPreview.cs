using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Shows a baked character in Scene view; the same editable rect uses the live stage in Play.</summary>
    [ExecuteAlways, RequireComponent(typeof(RawImage))]
    public sealed class ScenePortraitPreview : MonoBehaviour
    {
        [SerializeField] private Texture _runtimeTexture;
        [SerializeField] private Texture2D _editorPortrait;
        [SerializeField] private AspectRatioFitter _aspectFitter;

        public void Configure(Texture runtimeTexture, Texture2D editorPortrait)
        {
            _runtimeTexture = runtimeTexture;
            _editorPortrait = editorPortrait;
            Apply();
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void LateUpdate() => UpdateAspect();

        private void UpdateAspect()
        {
            if (_aspectFitter == null) return;
            var texture = GetComponent<RawImage>().texture;
            if (texture != null && texture.height > 0)
                _aspectFitter.aspectRatio = (float)texture.width / texture.height;
        }

        private void Apply()
        {
            var image = GetComponent<RawImage>();
            if (image == null) return;
            if (!Application.isPlaying)
            {
                if (_editorPortrait != null) image.texture = _editorPortrait;
            }
            else if (_editorPortrait != null && image.texture == _editorPortrait)
            {
                // CharacterStage may already have bound its live target in Awake.
                // Only remove our baked preview; never replace that live render texture.
                image.texture = _runtimeTexture;
            }
            UpdateAspect();
        }
    }
}
