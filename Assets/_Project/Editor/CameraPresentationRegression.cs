using System;
using System.IO;
using System.Reflection;
using PushStars.CV;
using PushStars.Fight;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class CameraPresentationRegression
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            CheckPortraitEdges();
            CheckCameraViewport();
            Vector3 reference = Vector3.zero;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                Vector3 final = CheckPlacement(fps);
                if (fps == 30) reference = final;
                else Require(Vector3.Distance(reference, final) < .012f, "Placement depends on render FPS.");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/camera-presentation-regression.txt",
                "PASS: out-of-frame portrait UVs leave transparent space without edge repetition; wholly offscreen crops are empty; " +
                "full camera viewport retained without distortion across four display aspects; close-to-far placement remains bounded, continuous and consistent at 30/60/120 FPS.\n");
        }

        private static void CheckCameraViewport()
        {
            var method = typeof(BossCombatScreen).GetMethod("CameraViewport",
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var display in new[] { new Vector2(390, 844), new Vector2(320, 568),
                new Vector2(844, 390), new Vector2(200, 200) })
            {
                var texture = new Vector2(540, 960);
                var uv = (Rect)method.Invoke(null, new object[] { display, texture });
                Require(uv.xMin <= 0 && uv.yMin <= 0 && uv.xMax >= 1 && uv.yMax >= 1,
                    "Live portrait zoomed into the camera viewport.");
                Require(Vector2.Distance(uv.center, new Vector2(.5f, .5f)) < .0001f,
                    "Live portrait shifted the camera center.");
                float horizontal = display.x / (uv.width * texture.x);
                float vertical = display.y / (uv.height * texture.y);
                Require(Mathf.Abs(horizontal - vertical) < .0001f, "Live portrait stretches the body.");
                // A distant person occupying half the viewport must also occupy half
                // the displayed height, with no body-dependent crop cancelling it.
                float near = .6f / uv.height, far = .3f / uv.height;
                Require(Mathf.Abs(far / near - .5f) < .0001f, "Camera distance was cancelled.");
            }
        }

        private static void CheckPortraitEdges()
        {
            var go = new GameObject("Portrait edge regression", typeof(RectTransform), typeof(PortraitImage));
            try
            {
                var image = go.GetComponent<PortraitImage>();
                image.rectTransform.sizeDelta = new Vector2(200, 200);
                var populate = typeof(PortraitImage).GetMethod("OnPopulateMesh", Private, null, new[] { typeof(VertexHelper) }, null);
                using (var mesh = new VertexHelper())
                {
                    foreach (Rect uv in new[] { new Rect(-.5f, -.5f, 2, 2), new Rect(0, -.6f, 1, 1),
                        new Rect(.7f, .7f, .8f, .8f), new Rect(0, 0, 1, 1) })
                    {
                        image.uvRect = uv; populate.Invoke(image, new object[] { mesh });
                        Require(mesh.currentVertCount == 4, "Visible crop lost its quad.");
                        for (int i = 0; i < mesh.currentVertCount; i++)
                        {
                            UIVertex vertex = default; mesh.PopulateUIVertex(ref vertex, i);
                            Require(vertex.uv0.x >= 0 && vertex.uv0.x <= 1 && vertex.uv0.y >= 0 && vertex.uv0.y <= 1,
                                "Portrait samples outside the render target.");
                            Vector2 reconstructed = new Vector2(uv.x + (vertex.position.x / 200 + .5f) * uv.width,
                                uv.y + (vertex.position.y / 200 + .5f) * uv.height);
                            Require(Vector2.Distance(reconstructed, vertex.uv0) < .0001f,
                                "Clipping stretched the portrait instead of retaining transparent padding.");
                        }
                    }
                    image.uvRect = new Rect(2, 2, 1, 1); populate.Invoke(image, new object[] { mesh });
                    Require(mesh.currentVertCount == 0, "Offscreen crop repeats the last visible pixel.");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static Vector3 CheckPlacement(int fps)
        {
            var go = new GameObject("Placement regression");
            try
            {
                var anchor = go.AddComponent<AvatarMirrorAnchor>();
                var smooth = typeof(AvatarMirrorAnchor).GetMethod("SmoothPlacement", Private);
                Set(anchor, "_shownVp", new Vector2(.5f, .5f)); Set(anchor, "_shownScale", 1f);
                Vector2 previous = new Vector2(.5f, .5f); float previousScale = 1f;
                float dt = 1f / fps;
                for (int i = 0; i < fps * 5; i++)
                {
                    // Approach the phone, walk back, then settle. The detector may change
                    // targets abruptly; display movement still needs bounded speed.
                    bool close = i < fps;
                    Set(anchor, "_targetVp", close ? new Vector2(.8f, .9f) : new Vector2(.5f, .48f));
                    Set(anchor, "_targetScale", close ? 3f : .75f);
                    smooth.Invoke(anchor, new object[] { dt });
                    Vector2 shown = (Vector2)Get(anchor, "_shownVp");
                    float scale = (float)Get(anchor, "_shownScale");
                    Require(Vector2.Distance(previous, shown) <= .651f * dt, "Position jumps during approach.");
                    Require(Mathf.Abs(scale - previousScale) <= .751f * dt, "Scale jumps during approach.");
                    Require(scale >= .74f && scale <= 3.01f, "Placement overshot its target.");
                    previous = shown; previousScale = scale;
                }
                Require(Vector2.Distance(previous, new Vector2(.5f, .48f)) < .01f && Mathf.Abs(previousScale - .75f) < .01f,
                    "Placement did not settle after stepping back.");
                return new Vector3(previous.x, previous.y, previousScale);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static object Get(object owner, string name) => owner.GetType().GetField(name, Private).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
