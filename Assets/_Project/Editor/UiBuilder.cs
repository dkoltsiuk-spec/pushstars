using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEditor;

namespace PushStars.Editor
{
    /// <summary>
    /// The UGUI idioms every scene-building tool in this project repeats: a stretched rect, a solid
    /// image, a Rubik label, a flat button. Extracted when the boot and onboarding tools needed the
    /// same twelve helpers <see cref="FightSceneSetup"/> already had privately.
    ///
    /// <para>Deliberately thin — it builds primitives, never layouts. Each screen's composition
    /// stays in its own tool where it can be read as a description of that screen.</para>
    /// </summary>
    internal static class UiBuilder
    {
        /// <summary>iPhone-portrait reference the whole project's canvases are authored against.</summary>
        public const float RefWidth = 390f;
        public const float RefHeight = 844f;

        public static Canvas Canvas(string name, out RectTransform root)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            root = (RectTransform)go.transform;
            return canvas;
        }

        /// <summary>A camera that clears to black and draws nothing. Every screen here is UI, but a
        /// scene with no camera renders the previous frame's garbage on device.</summary>
        public static Camera ClearCamera(string name = "DisplayClearCamera", bool audioListener = true)
        {
            var go = audioListener
                ? new GameObject(name, typeof(Camera), typeof(AudioListener))
                : new GameObject(name, typeof(Camera));
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.depth = -100f;
            return cam;
        }

        public static void EventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static RectTransform Rect(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Image(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>An un-textured RawImage placeholder. Its texture is assigned at runtime — a
        /// stage's render target, a mirrored copy of one — so nothing here can wire it; the scene
        /// stores the empty component and a script fills it in once the game is actually running.</summary>
        public static RawImage RawImage(RectTransform parent, string name, Color tint)
        {
            // Fully qualified inside the body on purpose: this method's own name shadows the type
            // name it needs, and a bare `RawImage` here would ask the compiler to guess between the
            // two rather than settle it.
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<UnityEngine.UI.RawImage>();
            img.color = tint;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(RectTransform parent, string name, Color color, string text,
                                           float size, FontStyles style = FontStyles.Bold,
                                           TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;

            var rubik = FontSetup.Resolve(style, out var remaining);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = remaining; }
            FontSetup.ApplyOutlineFor(tmp, size);
            return tmp;
        }

        public static Button Button(RectTransform parent, string name, string label, Color bg, Color fg,
                                    float fontSize, out TextMeshProUGUI text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;

            text = Text((RectTransform)go.transform, "Label", fg, label, fontSize);
            Stretch(text.rectTransform, 6, 4, 6, 4);
            return go.GetComponent<Button>();
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
        }

        /// <summary>Anchors a fixed-size element to a point, positioned from that point.</summary>
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            Anchor(rt, anchor, anchor, anchor);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>Full-width element pinned at a vertical anchor, inset by <paramref name="margin"/>.</summary>
        public static void PlaceWide(RectTransform rt, float anchorY, float y, float height, float margin = 24f)
        {
            Anchor(rt, new Vector2(0f, anchorY), new Vector2(1f, anchorY), new Vector2(0.5f, anchorY));
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-margin * 2f, height);
        }

        public static void Set(SerializedObject so, string property, Object value)
            => so.FindProperty(property).objectReferenceValue = value;

        public static void SetArray(SerializedObject so, string property, Object[] items)
        {
            var arr = so.FindProperty(property);
            arr.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        public static void SetStringArray(SerializedObject so, string property, string[] items)
        {
            var arr = so.FindProperty(property);
            arr.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
                arr.GetArrayElementAtIndex(i).stringValue = items[i];
        }

        /// <summary>Index of a user layer by name, creating it in the first free slot. The 3D
        /// character has to live on a layer only its stage camera renders, or it draws twice —
        /// once into the render texture and once straight over the UI.</summary>
        public static int EnsureLayer(string layerName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning($"[UiBuilder] TagManager not found; '{layerName}' falls back to Default.");
                return 0;
            }

            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;

            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(el.stringValue)) continue;
                el.stringValue = layerName;
                so.ApplyModifiedProperties();
                Debug.Log($"[UiBuilder] Created layer '{layerName}' at index {i}.");
                return i;
            }

            Debug.LogWarning($"[UiBuilder] No free user layer; '{layerName}' falls back to Default.");
            return 0;
        }

        public static void EnsureSceneInBuildSettings(string scenePath)
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path == scenePath) return;

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(scenePath, true),
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
