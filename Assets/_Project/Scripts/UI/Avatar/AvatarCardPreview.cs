using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Live, isolated portrait using the same materials and scene lighting as the home stage.</summary>
    public sealed class AvatarCardPreview : MonoBehaviour
    {
        public GameObject Prefab;
        public RawImage Image;
        public CharacterStage HomeStage;
        public int Slot;
        public int PreviewLayer;
        public Quaternion Facing = Quaternion.Euler(0, 180, 0);
        public bool FullBody;
        public Color Tint = Color.white;
        [Tooltip("Horizontal subject correction as a fraction of model width. Positive moves the character right in frame.")]
        public float SubjectOffsetX;
        private GameObject _world;
        private Camera _camera;
        private RenderTexture _texture;
        public Animator Animator { get; private set; }
        public bool IsRendering => _world != null && _world.activeSelf && _camera.enabled;

        private void OnEnable()
        {
            if (_world == null) Build();
            if (_world != null) _world.SetActive(true);
        }

        public void SetPrefab(GameObject prefab)
        {
            if (Prefab == prefab && _world != null) return;
            Release();
            Prefab = prefab;
            if (isActiveAndEnabled) Build();
        }

        private void Build()
        {
            if (Prefab == null || Image == null) return;
            _world = new GameObject("CardStage_" + Slot);
            _world.transform.position = new Vector3(2000 + Slot * 15, 0, 0);
            var model = Instantiate(Prefab, _world.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.rotation = Facing;
            foreach (var child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            Animator = model.GetComponentInChildren<Animator>();
            if (Animator != null)
            {
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.Play("Idle", 0, Slot * .17f);
                Animator.Update(0);
                if (model.GetComponent<SonicIdleBehaviour>() == null)
                {
                    var accent = model.GetComponent<CharacterIdleAccent>() ?? model.AddComponent<CharacterIdleAccent>();
                    accent.Configure(Animator, "Idle", "WarriorIdle");
                }
            }

            var renderers = model.GetComponentsInChildren<Renderer>();
            // The home stage writes its directional rim into a property block, not shared materials.
            var properties = new MaterialPropertyBlock();
            if (HomeStage != null && HomeStage.AvatarRoot != null)
            {
                var homeRenderer = HomeStage.AvatarRoot.GetComponentInChildren<Renderer>();
                if (homeRenderer != null) homeRenderer.GetPropertyBlock(properties);
            }
            foreach (var renderer in renderers) renderer.SetPropertyBlock(properties);

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var cameraObject = new GameObject("PortraitCamera");
            cameraObject.transform.SetParent(_world.transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.orthographic = true;
            _camera.orthographicSize = bounds.size.y * (FullBody ? .48f : .285f);
            _camera.transform.position = new Vector3(bounds.center.x - bounds.size.x * SubjectOffsetX,
                FullBody ? bounds.center.y + bounds.size.y * .03f : bounds.max.y - bounds.size.y * .265f,
                _world.transform.position.z - 5);
            _camera.transform.rotation = Quaternion.identity;
            _camera.nearClipPlane = .05f;
            _camera.farClipPlane = 10;
            _camera.useOcclusionCulling = false;
            _camera.allowHDR = false;
            _camera.depth = -5;
            _texture = new RenderTexture((FullBody ? 560 : 512) * (int)AvatarWideCamera.WidthMultiplier, FullBody ? 800 : 512, 24, RenderTextureFormat.ARGB32)
            { name = "AvatarCard_" + Slot, antiAliasing = 2 };
            _texture.Create();
            _camera.targetTexture = _texture;
            _camera.aspect = FullBody ? 560f / 800f : 1f;
            AvatarWideCamera.Configure(_camera, true);
            AvatarWideImage.Configure(Image, _camera);
            Image.texture = _texture;
            Image.color = Tint;
            _camera.Render();
        }

        private void OnDisable() { if (_world != null) _world.SetActive(false); }

        private void OnDestroy() => Release();

        private void Release()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_world != null) { _world.SetActive(false); Destroy(_world); }
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
            if (Image != null) Image.texture = null;
            _world = null; _camera = null; _texture = null; Animator = null;
        }
    }
}
