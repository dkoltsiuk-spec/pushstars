using UnityEngine;

namespace PushStars.UI
{
    /// <summary>Horizontal overscan only. Keeps the authored lens, distance and vertical framing.</summary>
    [ExecuteAlways, DefaultExecutionOrder(400), RequireComponent(typeof(Camera))]
    public sealed class AvatarWideCamera : MonoBehaviour
    {
        public const float WidthMultiplier = 3f;
        private Camera _camera;

        public static float TextureAspect(Camera camera, Texture texture)
        {
            if (texture == null) return camera != null ? camera.aspect : 1f;
            var stage = camera != null ? camera.GetComponentInParent<CharacterStage>() : null;
            return stage != null && stage.RenderTarget == texture
                ? stage.RenderAspect : (float)texture.width / texture.height;
        }

        public static void Configure(Camera camera, bool wide)
        {
            if (camera == null) return;
            var component = camera.GetComponent<AvatarWideCamera>();
            if (component == null && wide) component = camera.gameObject.AddComponent<AvatarWideCamera>();
            if (component == null) return;
            component.enabled = wide;
            if (wide) component.Apply();
        }

        public void Apply()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            _camera.ResetProjectionMatrix();
            var projection = _camera.projectionMatrix;
            projection.m00 /= WidthMultiplier;
            projection.m02 /= WidthMultiplier;
            _camera.projectionMatrix = projection;
        }

        private void LateUpdate() => Apply();
        private void OnPreCull() => Apply();
        private void OnDisable()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera != null) _camera.ResetProjectionMatrix();
        }
    }
}
