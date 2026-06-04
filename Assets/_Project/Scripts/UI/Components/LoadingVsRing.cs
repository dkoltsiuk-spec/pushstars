using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Animated rotating ring shown at the centre of the matchmaking screen (VS logo).
    /// Phase 14 will drive state transitions (searching → found → countdown).
    /// The ring is a dashed arc created via Image.Type.Filled + Image.FillMethod.Radial360.
    /// </summary>
    public class LoadingVsRing : MonoBehaviour
    {
        [SerializeField] private RectTransform   _ring;
        [SerializeField] private Image           _ringImage;
        [SerializeField] private TextMeshProUGUI _vsLabel;

        [SerializeField] private float _rotationSpeed = 90f; // degrees per second

        private void Update()
        {
            if (_ring != null)
                _ring.Rotate(0f, 0f, -_rotationSpeed * Time.deltaTime, Space.Self);
        }

        public void SetSearching(bool searching)
        {
            if (_ringImage != null)
                _ringImage.color = searching ? AppColors.AccentYellow : AppColors.AccentLime;
        }

        public void SetVisible(bool visible) => gameObject.SetActive(visible);
    }
}
