using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>References the existing training/settings artwork for runtime account sheets.</summary>
    public sealed class AuthDialogStyle : ScriptableObject
    {
        public Sprite Sheet, Pattern, Primary, Secondary, Field;
        public TMP_FontAsset Bold, Regular;
        public Material Outline;
    }
}
