using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>One gold gradient across the complete count, rather than repeated per digit.</summary>
    [ExecuteAlways, RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class TrophyCountGradient : MonoBehaviour
    {
        private TextMeshProUGUI _text;

        private void OnEnable()
        {
            _text = GetComponent<TextMeshProUGUI>();
            _text.OnPreRenderText += Apply;
            _text.SetVerticesDirty();
        }

        private void OnDisable()
        {
            if (_text == null) return;
            _text.OnPreRenderText -= Apply;
            _text.SetVerticesDirty();
        }

        private void Apply(TMP_TextInfo info)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                min = Mathf.Min(min, c.bottomLeft.x);
                max = Mathf.Max(max, c.topRight.x);
            }
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                var mesh = info.meshInfo[c.materialReferenceIndex];
                for (int j = 0; j < 4; j++)
                {
                    int v = c.vertexIndex + j;
                    Color32 tint = Color.Lerp(new Color32(255, 224, 0, 255),
                        new Color32(255, 147, 0, 255), Mathf.InverseLerp(min, max, mesh.vertices[v].x));
                    tint.a = mesh.colors32[v].a;
                    mesh.colors32[v] = tint;
                }
            }
        }
    }
}
