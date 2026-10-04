using System.Collections.Generic;
using PushStars.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PushStars.UI
{
    /// <summary>
    /// Renders the player's own hero frozen in an emote's key pose as a flat silhouette — the shop
    /// sells the move, not the hero, so the card shows a shadow of the hero the player already has.
    ///
    /// <para>The hero is cloned from the live model on screen (whatever the player has selected),
    /// under an inactive holder so none of its scripts wake up, posed by a one-frame playable, shot
    /// by an orthographic camera far below the scene and read back as white-on-transparent. The
    /// UI tints it (black in the shop, white in the picker). Results are cached per hero.</para>
    /// </summary>
    public static class EmoteThumbnails
    {
        private const int StudioLayer = 31;
        private static readonly Vector3 StudioPosition = new Vector3(0f, -2000f, 0f);
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>The hero model currently shown on a stage (its first child), or null.</summary>
        public static GameObject ModelOn(CharacterStage stage)
            => stage != null && stage.AvatarRoot != null && stage.AvatarRoot.childCount > 0
                ? stage.AvatarRoot.GetChild(stage.AvatarRoot.childCount - 1).gameObject : null;

        public static Texture2D Get(GameObject model, EmoteDef emote, int width = 160, int height = 200)
        {
            if (model == null || emote == null || emote.Clip == null) return null;
            string key = model.name.Replace("(Clone)", "") + "/" + emote.Id + "/" + width + "x" + height;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var texture = Render(model, emote.Clip, emote.PoseSeconds, width, height, true);
            if (texture != null) Cache[key] = texture;
            return texture;
        }

        public static Texture2D Render(GameObject model, AnimationClip clip, float seconds, int width, int height,
            bool silhouette, float yaw = 12f, bool lit = false, bool fixedFraming = false)
        {
            var holder = new GameObject("EmoteThumbnailStudio") { hideFlags = HideFlags.HideAndDontSave };
            holder.SetActive(false);
            holder.transform.position = StudioPosition;
            var clone = Object.Instantiate(model, holder.transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                Object.DestroyImmediate(behaviour);
            SetLayer(clone.transform);

            var cameraGo = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
            cameraGo.transform.SetParent(holder.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.cullingMask = 1 << StudioLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 20f;
            camera.allowHDR = false;
            camera.allowMSAA = true;

            if (lit)
            {
                // Review renders only; silhouettes need no light.
                var lightGo = new GameObject("Key") { hideFlags = HideFlags.HideAndDontSave };
                lightGo.transform.SetParent(holder.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
                var key = lightGo.AddComponent<Light>();
                key.type = LightType.Directional; key.intensity = 1.1f; key.cullingMask = 1 << StudioLayer;
            }

            PlayableGraph graph = default;
            RenderTexture rt = null;
            try
            {
                holder.SetActive(true);
                var animator = clone.GetComponentInChildren<Animator>();
                if (animator == null || !animator.isHuman) return null;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // Standing reference first, so every card of one hero shares a scale.
                Bounds standing = BoneBounds(animator);
                graph = PlayableGraph.Create("EmoteThumbnail");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                output.SetSourcePlayable(playable);
                playable.SetTime(Mathf.Clamp(seconds, 0f, clip.length));
                playable.SetTime(Mathf.Clamp(seconds, 0f, clip.length));
                graph.Evaluate(0f);
                Bounds posed = BoneBounds(animator);

                float aspect = (float)width / height;
                float half = Mathf.Max(standing.size.y * .56f, posed.size.y * .56f, posed.size.x * .56f / aspect);
                var center = posed.center;
                center.y = Mathf.Max(center.y, standing.center.y - (half - standing.size.y * .56f));
                if (fixedFraming)
                {
                    // Frame on the standing body so consecutive frames of a clip line up.
                    half = standing.size.y * .6f;
                    center = standing.center + Vector3.up * standing.size.y * .06f;
                }
                camera.orthographicSize = half;
                camera.aspect = aspect;
                cameraGo.transform.position = center + Vector3.forward * 6f;
                cameraGo.transform.rotation = Quaternion.LookRotation(Vector3.back);

                rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                camera.targetTexture = rt;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "EmoteThumbnail", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = previous;
                if (silhouette)
                {
                    var pixels = texture.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32(255, 255, 255, pixels[i].a > 8 ? (byte)255 : pixels[i].a);
                    texture.SetPixels32(pixels);
                }
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                camera.targetTexture = null;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>Bone positions, padded for the head and hands the bones stop short of.</summary>
        private static Bounds BoneBounds(Animator animator)
        {
            bool any = false;
            var bounds = new Bounds();
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                var t = animator.GetBoneTransform(bone);
                if (t == null) continue;
                if (!any) { bounds = new Bounds(t.position, Vector3.zero); any = true; }
                else bounds.Encapsulate(t.position);
            }
            // Scale-free padding: the hero may be scaled anywhere between the model and the stage.
            float body = Mathf.Max(.01f, Vector3.Distance(Bone(animator, HumanBodyBones.Hips), Bone(animator, HumanBodyBones.Head)));
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) bounds.Encapsulate(head.position + Vector3.up * body * .45f);
            bounds.Expand(new Vector3(body * .35f, body * .1f, body * .35f));
            return bounds;
        }

        private static Vector3 Bone(Animator animator, HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            return t != null ? t.position : animator.transform.position;
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = StudioLayer;
            foreach (Transform child in t) SetLayer(child);
        }
    }
}
