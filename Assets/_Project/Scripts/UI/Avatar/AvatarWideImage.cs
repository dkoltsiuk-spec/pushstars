using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Displays the extra horizontal camera pixels outside the original layout box.</summary>
    [DisallowMultipleComponent]
    public sealed class AvatarWideImage : BaseMeshEffect
    {
        public static void Configure(RawImage image, Camera camera)
        {
            if (image == null) return;
            bool wide = camera != null && camera.TryGetComponent<AvatarWideCamera>(out var lens) && lens.enabled;
            var effect = image.GetComponent<AvatarWideImage>();
            if (effect == null && wide) effect = image.gameObject.AddComponent<AvatarWideImage>();
            if (effect != null && effect.enabled != wide) effect.enabled = wide;
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            float center = graphic.rectTransform.rect.center.x;
            var vertex = new UIVertex();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.position.x = center + (vertex.position.x - center) * AvatarWideCamera.WidthMultiplier;
                mesh.SetUIVertex(vertex, i);
            }
        }
    }
}
