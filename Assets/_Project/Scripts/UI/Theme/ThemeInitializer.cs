using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Bootstrap component: loads PushStarsTheme from Resources and applies it to AppColors.
    /// Attach to any persistent GameObject in Boot or Main scene (runs before other Awakes
    /// if placed early in the Script Execution Order).
    /// The theme asset must live at: Assets/_Project/UI/Theme/Resources/PushStarsTheme.asset
    /// </summary>
    public class ThemeInitializer : MonoBehaviour
    {
        private const string ResourcePath = "PushStarsTheme";

        private void Awake()
        {
            var theme = Resources.Load<PushStarsTheme>(ResourcePath);
            if (theme != null)
            {
                AppColors.Apply(theme);
            }
            else
            {
                Debug.LogWarning(
                    $"[ThemeInitializer] Theme asset not found at Resources/{ResourcePath}. " +
                    "Using built-in defaults.");
            }
        }
    }
}
