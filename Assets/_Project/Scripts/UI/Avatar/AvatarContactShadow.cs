using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Projects the feet onto a hard UI ellipse behind the persistent portrait.</summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(2000)]
    public sealed class AvatarContactShadow : MonoBehaviour
    {
        [SerializeField] private CharacterStage _stage;
        [SerializeField] private RectTransform _shadow;
        private Transform _left, _right;
        public void Bind(CharacterStage stage, RectTransform shadow)
        { _stage = stage; _shadow = shadow; Sync(); }
        private void OnTransformParentChanged() { Sync(); }
        private void LateUpdate() { Sync(); }
        private void Sync()
        {
            if (_stage == null || _shadow == null || transform.parent == null) return;
            if (_left == null)
            {
                var animator = _stage.AvatarRoot.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) return;
                _left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            if (_left == null || _right == null) return;
            var portrait = (RectTransform)transform;
            var camera = _stage.StageCamera;
            var feet = (_left.position + _right.position) * .5f - Vector3.up * .12f;
            var point = camera.WorldToViewportPoint(feet);
            var edge = camera.WorldToViewportPoint(feet + camera.transform.right * .36f);
            _shadow.SetParent(transform.parent, false);
            int index = transform.GetSiblingIndex();
            if (_shadow.GetSiblingIndex() < index) index--;
            _shadow.SetSiblingIndex(index);
            _shadow.anchorMin = _shadow.anchorMax = new Vector2(.5f, .5f);
            _shadow.pivot = new Vector2(.5f, .5f);
            var local = new Vector3((point.x - portrait.pivot.x) * portrait.rect.width,
                (point.y - portrait.pivot.y) * portrait.rect.height, 0);
            _shadow.position = portrait.TransformPoint(local);
            _shadow.localRotation = portrait.localRotation;
            _shadow.localScale = portrait.localScale;
            float width = Mathf.Abs(edge.x - point.x) * portrait.rect.width * 2;
            _shadow.sizeDelta = new Vector2(width, width * .2f);
        }
        private void OnDisable() { if (_shadow != null) _shadow.gameObject.SetActive(false); }
        private void OnEnable() { if (_shadow != null) _shadow.gameObject.SetActive(true); }
    }
}
